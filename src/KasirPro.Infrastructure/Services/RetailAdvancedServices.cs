using System.Data;
using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Exchange (tukar barang): old items back to stock, new items out, cash delta settled.</summary>
public class ExchangeService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly CashService _cash;

    public ExchangeService(Db db, AuditService audit, CashService cash)
    { _db = db; _audit = audit; _cash = cash; }

    /// <summary>
    /// Creates an exchange against an original sale. Old items (resellable) return to stock,
    /// new items leave stock, cash difference optionally flows into the open shift.
    /// </summary>
    public long Create(long originalSaleId, List<(long SaleItemId, decimal Qty)> oldItems,
        List<(long ProductId, decimal Qty, decimal Price)> newItems,
        string reason, long userId, string username, long cashSessionId = 0, bool resellable = true)
    {
        var oldSubtotal = _db.With(c => c.Query<decimal>(@"
            SELECT {su} FROM sale_items WHERE id IN (@ids)"
                .Replace("{su}", DbEx.MoneyColumn("subtotal")).Replace("@ids", "0")
            , new { }).FirstOrDefault()); // placeholder, real calc below
        return _db.Transaction(c =>
        {
            var now = DbEx.Iso(DateTime.Now);
            decimal oldValue = 0, newValue = 0;

            var retLines = new List<(long ProductId, decimal Qty, decimal Price)>();
            foreach (var (saleItemId, qty) in oldItems)
            {
                if (qty <= 0) continue;
                var si = c.QueryFirstOrDefault<(long ProductId, string Name, decimal Qty, decimal Price)>(
                    @"SELECT product_id AS ProductId, product_name AS Name, qty AS Qty,
                      {pr} AS Price FROM sale_items WHERE id=@id".Replace("{pr}", DbEx.MoneyColumn("price")),
                    new { id = saleItemId });
                if (si.ProductId == 0) continue;
                var already = c.ExecuteScalar<decimal>(
                    "SELECT COALESCE(SUM(qty),0) FROM sale_return_items WHERE sale_item_id=@id", new { id = saleItemId });
                if (qty > si.Qty - already)
                    throw new InvalidOperationException($"Qty tukar melebihi sisa untuk '{si.Name}'");
                oldValue += Money.Round(qty * si.Price);
                retLines.Add((si.ProductId, qty, si.Price));

                // old item back to stock (resellable) or to damaged via notes
                InventoryService.ApplyMovement(c, si.ProductId, StockRef.SaleReturn, originalSaleId,
                    StockDirection.In, qty, resellable ? $"Exchange in: {reason}" : $"Exchange DAMAGED: {reason}", userId, now);
            }

            var outLines = new List<(long ProductId, decimal Qty, decimal Price)>();
            foreach (var (productId, qty, price) in newItems)
            {
                if (qty <= 0) continue;
                newValue += Money.Round(qty * price);
                outLines.Add((productId, qty, price));
                InventoryService.ApplyMovement(c, productId, StockRef.Sale, originalSaleId,
                    StockDirection.Out, qty, $"Exchange out: {reason}", userId, now);
            }

            var delta = Money.Round(newValue - oldValue);
            var cashPaid = delta > 0 ? delta : 0;
            var cashRefund = delta < 0 ? -delta : 0;

            if (cashPaid > 0 && cashSessionId > 0)
                _cash.RecordSaleCash(c, cashSessionId, cashPaid, originalSaleId, userId, now);

            var no = InventoryService.NextNo(_db, "EXC", "exchanges", "exchange_no");
            var id = c.ExecuteScalar<long>(@"INSERT INTO exchanges
                (exchange_no, exchange_date, original_sale_id, old_value, new_value, cash_paid, cash_refund,
                 user_id, reason, created_at, updated_at)
                VALUES (@no, @d, @sid, @ov, @nv, @cp, @cr, @uid, @reason, @t, @t2); SELECT last_insert_rowid();",
                new
                {
                    no, d = now, sid = originalSaleId, ov = DbEx.MoneyParam(oldValue),
                    nv = DbEx.MoneyParam(newValue), cp = DbEx.MoneyParam(cashPaid),
                    cr = DbEx.MoneyParam(cashRefund), uid = userId, reason, t = now, t2 = now
                });

            // record as a return for stock/return-tracking purposes (resellable only)
            if (resellable && retLines.Count > 0)
            {
                var returnNo = InventoryService.NextNo(_db, "RTN", "sale_returns", "return_no");
                var total = Money.Round(retLines.Sum(r => Money.Round(r.Qty * r.Price)));
                var returnId = c.ExecuteScalar<long>(@"INSERT INTO sale_returns
                    (return_no, sale_id, return_date, user_id, total, reason, created_at, updated_at)
                    VALUES (@no, @sid, @d, @uid, @tot, @reason, @t, @t2); SELECT last_insert_rowid();",
                    new { no = returnNo, sid = originalSaleId, d = now, uid = userId, tot = DbEx.MoneyParam(total), reason = "EXCHANGE: " + reason, t = now, t2 = now });
                foreach (var r in retLines)
                {
                    c.Execute(@"INSERT INTO sale_return_items (sale_return_id, sale_item_id, product_id, product_name, qty, price, subtotal)
                        SELECT @rid, si.id, si.product_id, si.product_name, @q, si.price, @sub
                        FROM sale_items si WHERE si.sale_id=@sid AND si.product_id=@pid LIMIT 1",
                        new { rid = returnId, q = (double)r.Qty, sub = DbEx.MoneyParam(Money.Round(r.Qty * r.Price)), sid = originalSaleId, pid = r.ProductId });
                }
            }

            _audit.InTx(c, userId, username, "EXCHANGE", "exchange", id,
                $"{no}: old {Money.Format(oldValue)} -> new {Money.Format(newValue)}, cash paid {Money.Format(cashPaid)}, refund {Money.Format(cashRefund)}");
            return id;
        });
    }
}

/// <summary>Batch (lot/expiry) management, FEFO helpers, near-expiry/expired queries.</summary>
public class BatchService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public BatchService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public long Receive(long productId, long warehouseId, string batchNo, DateTime? expiry,
        decimal qty, decimal cost, long userId, string username, long? supplierId = null)
    {
        if (qty <= 0) throw new InvalidOperationException("Qty harus lebih dari 0");
        var now = DbEx.Iso(DateTime.Now);
        var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO inventory_batches
            (product_id, warehouse_id, batch_no, expiry_date, qty, purchase_cost, received_date, supplier_id, created_at)
            VALUES (@p, @w, @b, @e, @q, @c, @r, @s, @t); SELECT last_insert_rowid();",
            new
            {
                p = productId, w = warehouseId, b = batchNo,
                e = expiry.HasValue ? DbEx.Iso(expiry.Value) : (string?)null,
                q = (double)qty, c = DbEx.MoneyParam(cost), r = now,
                s = supplierId > 0 ? (long?)supplierId : null, t = now
            }));
        _audit.Log(userId, username, "BATCH_RECEIVE", "inventory_batch", id, $"batch {batchNo} qty {qty}");
        return id;
    }

    /// <summary>FEFO consume: reduces from earliest-expiring batches first. Call inside a sale transaction.</summary>
    public static void ConsumeFefo(IDbConnection c, long productId, long warehouseId, decimal qty, string now)
    {
        var batches = c.Query<(long Id, decimal Qty)>(
            @"SELECT id AS Id, qty AS Qty FROM inventory_batches
              WHERE product_id=@p AND warehouse_id=@w AND qty > 0
              ORDER BY (CASE WHEN expiry_date IS NULL THEN '9999-12-31' ELSE expiry_date END), id",
            new { p = productId, w = warehouseId }).ToList();
        var remaining = qty;
        foreach (var b in batches)
        {
            if (remaining <= 0) break;
            var take = Math.Min(b.Qty, remaining);
            c.Execute("UPDATE inventory_batches SET qty = qty - @t WHERE id=@id",
                new { t = (double)take, id = b.Id });
            remaining -= take;
        }
        // if no batches tracked, stock ledger still handles the overall qty
    }

    public List<(long Id, string Batch, string Product, DateTime? Expiry, decimal Qty, decimal Cost)> NearExpiry(int withinDays)
    {
        return _db.With(c => c.Query<(long, string, string, DateTime?, decimal, decimal)>(@"
            SELECT b.id AS Id, b.batch_no AS Batch, p.name AS Product, b.expiry_date AS Expiry,
            b.qty AS Qty, {pc} AS Cost
            FROM inventory_batches b JOIN products p ON p.id=b.product_id
            WHERE b.qty > 0 AND b.expiry_date IS NOT NULL
              AND date(b.expiry_date) <= date('now','localtime', @d)
            ORDER BY b.expiry_date"
                .Replace("{pc}", DbEx.MoneyColumn("b.purchase_cost")),
            new { d = $"+{withinDays} days" }).ToList());
    }

    public List<(long Id, string Batch, string Product, DateTime? Expiry, decimal Qty, decimal Cost)> Expired() =>
        _db.With(c => c.Query<(long, string, string, DateTime?, decimal, decimal)>(@"
            SELECT b.id AS Id, b.batch_no AS Batch, p.name AS Product, b.expiry_date AS Expiry,
            b.qty AS Qty, {pc} AS Cost
            FROM inventory_batches b JOIN products p ON p.id=b.product_id
            WHERE b.qty > 0 AND b.expiry_date IS NOT NULL AND date(b.expiry_date) < date('now','localtime')
            ORDER BY b.expiry_date"
                .Replace("{pc}", DbEx.MoneyColumn("b.purchase_cost"))).ToList());

    /// <summary>Writes off expired batches: stock OUT with EXPIRED reference.</summary>
    public void WriteOffExpired(long userId, string username, long warehouseId = 1)
    {
        var expired = Expired();
        var now = DbEx.Iso(DateTime.Now);
        _db.Transaction(c =>
        {
            foreach (var b in expired.Where(x => x.Expiry.HasValue && x.Qty > 0))
            {
                InventoryService.ApplyMovement(c, c.ExecuteScalar<long>(
                    "SELECT product_id FROM inventory_batches WHERE id=@id", new { id = b.Id }),
                    "EXPIRED", b.Id, StockDirection.Out, b.Qty, $"Write-off expired batch {b.Batch}", userId, now);
                c.Execute("UPDATE inventory_batches SET qty=0 WHERE id=@id", new { id = b.Id });
            }
        });
        _audit.Log(userId, username, "BATCH_WRITEOFF", "inventory_batch", 0, $"{expired.Count} batch expired");
    }
}

/// <summary>Serial/IMEI registry: receive, pick at sale, return, damage.</summary>
public class SerialService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public SerialService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public long Add(long productId, long warehouseId, string serialNo, string imei1, string imei2,
        long userId, string username, long? purchaseId = null)
    {
        if (string.IsNullOrWhiteSpace(serialNo)) throw new InvalidOperationException("Serial tidak boleh kosong");
        var exists = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM product_serials WHERE product_id=@p AND serial_no=@s",
            new { p = productId, s = serialNo.Trim() }));
        if (exists > 0) throw new InvalidOperationException($"Serial '{serialNo}' sudah terdaftar untuk produk ini");
        var now = DbEx.Iso(DateTime.Now);
        var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO product_serials
            (product_id, warehouse_id, serial_no, imei_1, imei_2, status, purchase_id, created_at, updated_at)
            VALUES (@p, @w, @s, @i1, @i2, 'AVAILABLE', @pid, @t, @t2); SELECT last_insert_rowid();",
            new
            {
                p = productId, w = warehouseId, s = serialNo.Trim(), i1 = imei1 ?? "", i2 = imei2 ?? "",
                pid = purchaseId > 0 ? (long?)purchaseId : null, t = now, t2 = now
            }));
        _audit.Log(userId, username, "SERIAL_ADD", "product_serial", id, serialNo);
        return id;
    }

    public List<(long Id, string Serial, string Imei1, string Status)> Available(long productId, long warehouseId = 1) =>
        _db.With(c => c.Query<(long, string, string, string)>(@"
            SELECT id, serial_no, imei_1, status FROM product_serials
            WHERE product_id=@p AND warehouse_id=@w AND status='AVAILABLE' ORDER BY serial_no",
            new { p = productId, w = warehouseId }).ToList());

    /// <summary>Marks serials sold and links them to the sale (saleId must exist). Call inside sale transaction.</summary>
    public static void MarkSold(IDbConnection c, long productId, IEnumerable<string> serialNos, long? saleId, string now)
    {
        foreach (var s in serialNos)
        {
            var updated = c.Execute(@"UPDATE product_serials SET status='SOLD', sale_id=@sid, updated_at=@t
                WHERE product_id=@p AND serial_no=@s AND status='AVAILABLE'",
                new { sid = saleId > 0 ? (long?)saleId : null, t = now, p = productId, s = s.Trim() });
            if (updated == 0)
                throw new InvalidOperationException($"Serial '{s}' tidak tersedia untuk produk ini");
        }
    }

    public void MarkReturned(string serialNo, long userId, string username)
    {
        var n = _db.With(c => c.Execute(@"UPDATE product_serials SET status='RETURNED', sale_id=NULL, updated_at=@t
            WHERE serial_no=@s AND status='SOLD'", new { t = DbEx.Iso(DateTime.Now), s = serialNo.Trim() }));
        if (n == 0) throw new InvalidOperationException("Serial tidak ditemukan dalam status SOLD");
        _audit.Log(userId, username, "SERIAL_RETURN", "product_serial", 0, serialNo);
    }
}

/// <summary>Fast/slow/dead stock analytics (indexed queries).</summary>
public class MovementAnalyticsService
{
    private readonly Db _db;
    public MovementAnalyticsService(Db db) { _db = db; }

    public List<(string Code, string Name, decimal QtySold, decimal Revenue)> FastMoving(DateTime from, DateTime to, int limit = 20) =>
        _db.With(c => c.Query<(string, string, decimal, decimal)>(@"
            SELECT si.product_code AS Code, si.product_name AS Name,
            CAST(ROUND(SUM(si.qty),2) AS REAL) AS QtySold,
            CAST(ROUND(SUM(si.subtotal)/100.0,2) AS REAL) AS Revenue
            FROM sale_items si JOIN sales s ON s.id=si.sale_id
            WHERE s.status='COMPLETED' AND s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY si.product_id ORDER BY QtySold DESC LIMIT @l",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)), l = limit }).ToList());

    public List<(string Code, string Name, decimal Stock, string LastSale)> SlowOrDead(int days, bool deadOnly)
    {
        var extra = deadOnly
            ? "AND NOT EXISTS (SELECT 1 FROM sale_items si2 JOIN sales s2 ON s2.id=si2.sale_id WHERE si2.product_id=p.id AND s2.status='COMPLETED')"
            : "";
        var sql = @"
            SELECT p.code AS Code, p.name AS Name, p.stock AS Stock,
            COALESCE((SELECT MAX(date(s.sale_date)) FROM sale_items si
                JOIN sales s ON s.id=si.sale_id WHERE si.product_id=p.id),'-') AS LastSale
            FROM products p
            WHERE p.is_active=1 AND p.stock > 0
              AND NOT EXISTS (
                SELECT 1 FROM sale_items si JOIN sales s ON s.id=si.sale_id
                WHERE si.product_id=p.id AND s.status='COMPLETED'
                  AND s.sale_date >= datetime('now','localtime', @d))
              " + extra + @"
            ORDER BY p.name LIMIT 500";
        return _db.With(c => c.Query<(string, string, decimal, string)>(
            sql, new { d = $"-{days} days" }).ToList());
    }
}
