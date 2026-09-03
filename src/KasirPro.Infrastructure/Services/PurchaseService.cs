using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class PurchaseService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public PurchaseService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public string NextPurchaseNo()
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        var prefix = $"PO-{date}-";
        var max = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(CAST(SUBSTR(purchase_no, LENGTH(@p)+1) AS INTEGER)),0) FROM purchases WHERE purchase_no LIKE @l",
            new { p = prefix, l = prefix + "%" }));
        return $"{prefix}{max + 1:D4}";
    }

    /// <summary>Creates a purchase and increases inventory atomically.</summary>
    public Purchase CreatePurchase(Purchase po, long userId, string username, bool updateCost = true)
    {
        if (po.Items.Count == 0) throw new InvalidOperationException("Tambahkan minimal 1 item");
        var now = DbEx.Iso(DateTime.Now);
        po.PurchaseDate = DateTime.Now;

        return _db.Transaction(c =>
        {
            po.PurchaseNo = NextPurchaseNo();
            decimal subtotal = 0;
            foreach (var it in po.Items)
            {
                it.Subtotal = Money.Round(it.Cost * it.Qty);
                subtotal += it.Subtotal;
            }
            subtotal = Money.Round(subtotal);
            var total = Money.Round(subtotal - po.Discount);

            po.Id = c.ExecuteScalar<long>(@"INSERT INTO purchases
                (purchase_no, supplier_invoice_no, purchase_date, supplier_id, user_id, subtotal, discount, total, status, notes, created_at, updated_at)
                VALUES (@no, @sinv, @d, @sup, @uid, @sub, @disc, @tot, 'COMPLETED', @notes, @t, @t2);
                SELECT last_insert_rowid();",
                new
                {
                    no = po.PurchaseNo, sinv = po.SupplierInvoiceNo, d = DbEx.Iso(po.PurchaseDate),
                    sup = po.SupplierId > 0 ? (long?)po.SupplierId : null, uid = userId,
                    sub = DbEx.MoneyParam(subtotal), disc = DbEx.MoneyParam(po.Discount),
                    tot = DbEx.MoneyParam(total), notes = po.Notes, t = now, t2 = now
                });

            foreach (var it in po.Items)
            {
                it.PurchaseId = po.Id;
                c.Execute(@"INSERT INTO purchase_items (purchase_id, product_id, product_code, product_name, qty, cost, subtotal)
                    VALUES (@pid, @prid, @pc, @pn, @q, @co, @su)",
                    new
                    {
                        pid = po.Id, prid = it.ProductId, pc = it.ProductCode, pn = it.ProductName,
                        q = (double)it.Qty, co = DbEx.MoneyParam(it.Cost), su = DbEx.MoneyParam(it.Subtotal)
                    });

                InventoryService.ApplyMovement(c, it.ProductId, StockRef.Purchase, po.Id,
                    StockDirection.In, it.Qty, $"Pembelian {po.PurchaseNo}", userId, now);

                if (updateCost)
                    c.Execute("UPDATE products SET purchase_price=@pp, updated_at=@t WHERE id=@id",
                        new { pp = DbEx.MoneyParam(it.Cost), t = now, id = it.ProductId });
            }

            _audit.InTx(c, userId, username, AuditAction.Purchase, "purchase", po.Id,
                $"{po.PurchaseNo} dari {po.SupplierName} = {Money.Format(total)}");
            po.Subtotal = subtotal;
            po.Total = total;
            return po;
        });
    }

    public Purchase? GetById(long id) =>
        _db.With(c =>
        {
            var p = c.QueryFirstOrDefault<Purchase>(@"SELECT pu.id AS Id, pu.purchase_no AS PurchaseNo,
                pu.supplier_invoice_no AS SupplierInvoiceNo, pu.purchase_date AS PurchaseDate,
                pu.supplier_id AS SupplierId, COALESCE(su.name,'') AS SupplierName,
                pu.user_id AS UserId, COALESCE(us.username,'') AS UserName,
                {sub} AS Subtotal, {disc} AS Discount, {tot} AS Total, pu.status AS Status, pu.notes AS Notes
                FROM purchases pu
                LEFT JOIN suppliers su ON su.id=pu.supplier_id
                LEFT JOIN users us ON us.id=pu.user_id
                WHERE pu.id=@id"
                    .Replace("{sub}", DbEx.MoneyColumn("pu.subtotal"))
                    .Replace("{disc}", DbEx.MoneyColumn("pu.discount"))
                    .Replace("{tot}", DbEx.MoneyColumn("pu.total")),
                new { id });
            if (p == null) return null;
            p.Items = c.Query<PurchaseItem>(@"SELECT id AS Id, purchase_id AS PurchaseId, product_id AS ProductId,
                product_code AS ProductCode, product_name AS ProductName, qty AS Qty,
                {co} AS Cost, {su} AS Subtotal FROM purchase_items WHERE purchase_id=@id"
                    .Replace("{co}", DbEx.MoneyColumn("cost"))
                    .Replace("{su}", DbEx.MoneyColumn("subtotal")),
                new { id }).ToList();
            return p;
        });

    public PagedResult<Purchase> History(string search, DateTime from, DateTime to, int page, int pageSize)
    {
        var where = new List<string> { "pu.purchase_date >= @f", "pu.purchase_date <= @t2" };
        var p = new DynamicParameters();
        p.Add("f", DbEx.Iso(from.Date));
        p.Add("t2", DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(pu.purchase_no LIKE @q OR pu.supplier_invoice_no LIKE @q OR su.name LIKE @q)");
            p.Add("q", "%" + search.Trim() + "%");
        }
        var w = string.Join(" AND ", where);

        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($@"SELECT COUNT(*) FROM purchases pu
                LEFT JOIN suppliers su ON su.id=pu.supplier_id WHERE {w}", p);
            p.Add("limit", pageSize);
            p.Add("off", (page - 1) * pageSize);
            var items = c.Query<Purchase>($@"SELECT pu.id AS Id, pu.purchase_no AS PurchaseNo,
                pu.supplier_invoice_no AS SupplierInvoiceNo, pu.purchase_date AS PurchaseDate,
                COALESCE(su.name,'') AS SupplierName,
                {DbEx.MoneyColumn("pu.total")} AS Total, pu.status AS Status
                FROM purchases pu LEFT JOIN suppliers su ON su.id=pu.supplier_id
                WHERE {w} ORDER BY pu.id DESC LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<Purchase> { Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total };
        });
    }

    /// <summary>Creates a purchase return (stock OUT) atomically.</summary>
    public PurchaseReturn CreateReturn(long purchaseId, List<(long PurchaseItemId, decimal Qty)> returnItems,
        string reason, long userId, string username)
    {
        if (returnItems.Count == 0) throw new InvalidOperationException("Pilih item yang diretur");
        var now = DbEx.Iso(DateTime.Now);
        var no = InventoryService.NextNo(_db, "PRN", "purchase_returns", "return_no");

        var ret = _db.Transaction(c =>
        {
            var purchase = c.QueryFirstOrDefault<(long Id, string PurchaseNo, long SupplierId)>(
                "SELECT id AS Id, purchase_no AS PurchaseNo, supplier_id AS SupplierId FROM purchases WHERE id=@id",
                new { id = purchaseId });
            if (purchase.Id == 0) throw new InvalidOperationException("Pembelian tidak ditemukan");

            var planned = new List<(long PurchaseItemId, long ProductId, string ProductName, decimal Qty, decimal Cost, decimal Subtotal)>();
            decimal total = 0;
            foreach (var (purchaseItemId, qty) in returnItems)
            {
                if (qty <= 0) continue;
                var pi = c.QueryFirstOrDefault<(long Id, long ProductId, string ProductName, decimal Qty, decimal Cost)>(
                    @"SELECT id AS Id, product_id AS ProductId, product_name AS ProductName, qty AS Qty,
                      {co} AS Cost FROM purchase_items WHERE id=@pid"
                        .Replace("{co}", DbEx.MoneyColumn("cost")),
                    new { pid = purchaseItemId });
                if (pi.Id == 0) continue;
                var already = c.ExecuteScalar<decimal>(@"SELECT COALESCE(SUM(pri.qty),0) FROM purchase_return_items pri
                    WHERE pri.purchase_item_id=@pid", new { pid = purchaseItemId });
                var maxQty = pi.Qty - already;
                if (qty > maxQty)
                    throw new InvalidOperationException($"Qty retur melebihi sisa (sisa {maxQty})");

                var sub = Money.Round(qty * pi.Cost);
                total += sub;
                planned.Add((purchaseItemId, pi.ProductId, pi.ProductName, qty, pi.Cost, sub));
            }
            if (total <= 0) throw new InvalidOperationException("Nilai retur harus lebih dari 0");

            var returnId = c.ExecuteScalar<long>(@"INSERT INTO purchase_returns
                (return_no, purchase_id, return_date, supplier_id, user_id, total, reason, created_at, updated_at)
                VALUES (@no, @pid, @d, @sup, @uid, @tot, @reason, @t, @t2); SELECT last_insert_rowid();",
                new { no, pid = purchaseId, d = now, sup = purchase.SupplierId > 0 ? (long?)purchase.SupplierId : null, uid = userId, tot = DbEx.MoneyParam(total), reason, t = now, t2 = now });

            var items = new List<PurchaseReturnItem>();
            foreach (var p in planned)
            {
                c.Execute(@"INSERT INTO purchase_return_items
                    (purchase_return_id, purchase_item_id, product_id, product_name, qty, cost, subtotal)
                    VALUES (@rid, @pid2, @prid, @pn, @q, @co2, @su)",
                    new
                    {
                        rid = returnId, pid2 = p.PurchaseItemId, prid = p.ProductId, pn = p.ProductName,
                        q = (double)p.Qty, co2 = DbEx.MoneyParam(p.Cost), su = DbEx.MoneyParam(p.Subtotal)
                    });
                items.Add(new PurchaseReturnItem
                {
                    PurchaseItemId = p.PurchaseItemId, ProductId = p.ProductId, ProductName = p.ProductName,
                    Qty = p.Qty, Cost = p.Cost, Subtotal = p.Subtotal
                });
            }

            foreach (var it in items)
            {
                InventoryService.ApplyMovement(c, it.ProductId, StockRef.PurchaseReturn, returnId,
                    StockDirection.Out, it.Qty, $"Retur pembelian {no} ({purchase.PurchaseNo})", userId, now);
            }
            return new PurchaseReturn
            {
                Id = returnId, ReturnNo = no, PurchaseId = purchaseId, PurchaseNo = purchase.PurchaseNo,
                ReturnDate = DateTime.Now, SupplierId = purchase.SupplierId, UserId = userId,
                Total = total, Reason = reason, Items = items
            };
        });
        _audit.Log(userId, username, AuditAction.PurchaseReturn, "purchase_return", ret.Id,
            $"{ret.ReturnNo} dari {ret.PurchaseNo} = {Money.Format(ret.Total)}");
        return ret;
    }

    public List<PurchaseReturn> GetReturns(DateTime from, DateTime to, int limit = 200) =>
        _db.With(c => c.Query<PurchaseReturn>(@"SELECT r.id AS Id, r.return_no AS ReturnNo,
            r.purchase_id AS PurchaseId, pu.purchase_no AS PurchaseNo, r.return_date AS ReturnDate,
            r.user_id AS UserId, {tot} AS Total, r.reason AS Reason
            FROM purchase_returns r JOIN purchases pu ON pu.id=r.purchase_id
            WHERE r.return_date >= @f AND r.return_date <= @t2
            ORDER BY r.id DESC LIMIT @l"
                .Replace("{tot}", DbEx.MoneyColumn("r.total")),
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)), l = limit }).ToList());
}
