using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Multi-warehouse stock + inter-warehouse transfers.</summary>
public class WarehouseService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public WarehouseService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public List<Warehouse> GetAll() =>
        _db.With(c => c.Query<Warehouse>("SELECT id AS Id, name AS Name, is_default AS IsDefault FROM warehouses ORDER BY id").ToList());

    public Warehouse GetDefault() =>
        _db.With(c => c.QueryFirstOrDefault<Warehouse>("SELECT id AS Id, name AS Name, is_default AS IsDefault FROM warehouses ORDER BY is_default DESC, id LIMIT 1"))
        ?? new Warehouse { Id = 1, Name = "TOKO", IsDefault = true };

    /// <summary>Moves stock between warehouses inside one transaction (ledger notes the transfer).</summary>
    public long Transfer(long fromWarehouse, long toWarehouse, List<(long ProductId, decimal Qty)> items,
        string notes, long userId, string username)
    {
        if (fromWarehouse == toWarehouse) throw new InvalidOperationException("Gudang asal dan tujuan sama");
        if (items.Count == 0) throw new InvalidOperationException("Pilih minimal 1 produk");
        var now = DbEx.Iso(DateTime.Now);

        return _db.Transaction(c =>
        {
            var no = InventoryService.NextNo(_db, "TRF", "stock_transfers", "transfer_no");
            var id = c.ExecuteScalar<long>(@"INSERT INTO stock_transfers (transfer_no, transfer_date, from_warehouse_id, to_warehouse_id, user_id, notes, created_at, updated_at)
                VALUES (@no, @d, @fw, @tw, @uid, @n, @t, @t2); SELECT last_insert_rowid();",
                new { no, d = now, fw = fromWarehouse, tw = toWarehouse, uid = userId, n = notes, t = now, t2 = now });

            foreach (var (productId, qty) in items)
            {
                if (qty <= 0) continue;
                // ensure stock exists at source
                var available = c.ExecuteScalar<decimal>(
                    "SELECT COALESCE((SELECT qty FROM warehouse_stock WHERE warehouse_id=@w AND product_id=@p),0)",
                    new { w = fromWarehouse, p = productId });
                if (available < qty)
                    throw new InvalidOperationException($"Stok gudang asal tidak cukup (tersedia {available:0.##})");

                c.Execute(@"INSERT INTO warehouse_stock (warehouse_id, product_id, qty) VALUES (@w, @p, @q)
                    ON CONFLICT(warehouse_id, product_id) DO UPDATE SET qty = qty + @q",
                    new { w = toWarehouse, p = productId, q = (double)qty });
                c.Execute(@"UPDATE warehouse_stock SET qty = qty - @q WHERE warehouse_id=@w AND product_id=@p",
                    new { q = (double)qty, w = fromWarehouse, p = productId });
                c.Execute(@"INSERT INTO stock_transfer_items (transfer_id, product_id, qty) VALUES (@tid, @p, @q)",
                    new { tid = id, p = productId, q = (double)qty });
            }

            _audit.InTx(c, userId, username, "STOCK_TRANSFER", "stock_transfer", id, $"{no}: {items.Count} item");
            return id;
        });
    }

    public PagedResult<(string ProductCode, string ProductName, decimal Qty)> Stock(long warehouseId,
        string search, int page, int pageSize)
    {
        var where = new List<string> { "ws.warehouse_id=@w" };
        var p = new DynamicParameters();
        p.Add("w", warehouseId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(pr.name LIKE @q OR pr.code LIKE @q)");
            p.Add("q", "%" + search.Trim() + "%");
        }
        var w = string.Join(" AND ", where);

        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($@"SELECT COUNT(*) FROM warehouse_stock ws
                JOIN products pr ON pr.id=ws.product_id WHERE {w}", p);
            p.Add("limit", pageSize); p.Add("off", (page - 1) * pageSize);
            var items = c.Query<(string, string, decimal)>($@"SELECT pr.code AS ProductCode, pr.name AS ProductName,
                ws.qty AS Qty FROM warehouse_stock ws JOIN products pr ON pr.id=ws.product_id
                WHERE {w} ORDER BY pr.name LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<(string, string, decimal)>
            {
                Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total
            };
        });
    }

    public long SaveWarehouse(string name, long userId, string username)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nama gudang wajib diisi");
        var now = DbEx.Iso(DateTime.Now);
        var id = _db.With(c => c.ExecuteScalar<long>(
            "INSERT INTO warehouses (name, created_at, updated_at) VALUES (@n, @t, @t); SELECT last_insert_rowid();",
            new { n = name.Trim(), t = now }));
        _audit.Log(userId, username, "WAREHOUSE_CHANGE", "warehouse", id, name);
        return id;
    }
}

public class Warehouse
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
}

/// <summary>Supplier payables (hutang belanja): purchase on credit + payments.</summary>
public class SupplierPaymentService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public SupplierPaymentService(Db db, AuditService audit) { _db = db; _audit = audit; }

    /// <summary>Settles part/all of a purchase bill (reduces outstanding).</summary>
    public decimal Pay(long purchaseId, decimal amount, PaymentMethod method, string notes,
        long userId, string username, long cashSessionId = 0)
    {
        if (amount <= 0) throw new InvalidOperationException("Jumlah harus lebih dari 0");
        var now = DbEx.Iso(DateTime.Now);
        return _db.Transaction(c =>
        {
            var pu = c.QueryFirstOrDefault<(decimal Total, decimal Paid, string Status)>(
                @"SELECT {tot} AS Total, {paid} AS Paid, payment_status AS Status FROM purchases WHERE id=@id"
                    .Replace("{tot}", DbEx.MoneyColumn("total"))
                    .Replace("{paid}", DbEx.MoneyColumn("paid_amount")),
                new { id = purchaseId });
            if (pu.Total == 0) throw new InvalidOperationException("Pembelian tidak ditemukan");
            var remaining = Money.Round(pu.Total - pu.Paid);
            amount = Money.Round(amount);
            if (amount > remaining) throw new InvalidOperationException($"Melebihi sisa hutang (sisa {Money.Format(remaining)})");

            c.Execute(@"INSERT INTO purchase_payments (purchase_id, method, amount, notes, user_id, cash_session_id, created_at)
                VALUES (@pid, @m, @a, @n, @uid, @csid, @t)",
                new
                {
                    pid = purchaseId, m = method.ToString(), a = DbEx.MoneyParam(amount), n = notes,
                    uid = userId, csid = cashSessionId > 0 ? (long?)cashSessionId : null, t = now
                });
            var newPaid = Money.Round(pu.Paid + amount);
            var status = newPaid >= pu.Total ? "PAID" : (newPaid > 0 ? "PARTIAL" : "UNPAID");
            c.Execute("UPDATE purchases SET paid_amount=@pa, payment_status=@st, updated_at=@t WHERE id=@id",
                new { pa = DbEx.MoneyParam(newPaid), st = status, t = now, id = purchaseId });

            if (method == PaymentMethod.Cash && cashSessionId > 0)
            {
                c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                    VALUES (@sid, 'CASH_OUT', 'OUT', @a, 'PURCHASE', @pid, 'Bayar hutang supplier', @uid, @t)",
                    new { sid = cashSessionId, a = DbEx.MoneyParam(amount), pid = purchaseId, uid = userId, t = now });
                c.Execute(@"UPDATE cash_sessions SET cash_out = cash_out + @a, updated_at=@t WHERE id=@sid",
                    new { a = DbEx.MoneyParam(amount), t = now, sid = cashSessionId });
            }

            _audit.InTx(c, userId, username, "SUPPLIER_PAYMENT", "purchase", purchaseId,
                $"Bayar {Money.Format(amount)} ({method}) - sisa {Money.Format(remaining - amount)}");
            return remaining - amount;
        });
    }

    public List<(long PurchaseId, string PurchaseNo, string Supplier, DateTime Date, decimal Total, decimal Paid, string Status)> Outstanding()
    {
        const string sql = @"
            SELECT pu.id AS PurchaseId, pu.purchase_no AS PurchaseNo, COALESCE(su.name,'') AS Supplier,
            pu.purchase_date AS Date,
            {tot} AS Total, {paid} AS Paid, pu.payment_status AS Status
            FROM purchases pu LEFT JOIN suppliers su ON su.id=pu.supplier_id
            WHERE pu.status='COMPLETED' AND pu.payment_status != 'PAID'
            ORDER BY pu.purchase_date";
        return _db.With(c => c.Query<(long, string, string, DateTime, decimal, decimal, string)>(
            sql.Replace("{tot}", DbEx.MoneyColumn("pu.total"))
               .Replace("{paid}", DbEx.MoneyColumn("pu.paid_amount"))).ToList());
    }

    public List<(long PaymentId, long PurchaseId, string Method, decimal Amount, string Notes, DateTime CreatedAt)> Payments(long purchaseId)
    {
        const string sql = @"
            SELECT id AS PaymentId, purchase_id AS PurchaseId, method AS Method,
            {am} AS Amount, notes AS Notes, created_at AS CreatedAt
            FROM purchase_payments WHERE purchase_id=@id ORDER BY id";
        return _db.With(c => c.Query<(long, long, string, decimal, string, DateTime)>(
            sql.Replace("{am}", DbEx.MoneyColumn("amount")),
            new { id = purchaseId }).ToList());
    }
}
