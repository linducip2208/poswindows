using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class PurchaseService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly PeriodCloseService _periods;

    public PurchaseService(Db db, AuditService audit, PeriodCloseService periods) { _db = db; _audit = audit; _periods = periods; }

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
        _periods.EnsureOpen(DateTime.Now);
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
        _periods.EnsureOpen(DateTime.Now);
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

    // ==================================================================
    // ADVANCED WORKFLOW: Purchase Order -> Approve -> Receive (partial)
    // ==================================================================

    /// <summary>Creates a PO draft (no stock effect).</summary>
    public long CreateDraft(Purchase po, long userId, string username)
    {
        var now = DbEx.Iso(DateTime.Now);
        return _db.Transaction(c =>
        {
            var no = NextPurchaseNo();
            decimal subtotal = 0;
            foreach (var it in po.Items) { it.Subtotal = Money.Round(it.Cost * it.Qty); subtotal += it.Subtotal; }
            subtotal = Money.Round(subtotal);

            var id = c.ExecuteScalar<long>(@"INSERT INTO purchases
                (purchase_no, supplier_invoice_no, purchase_date, supplier_id, user_id, subtotal, discount, total,
                 status, workflow_status, notes, created_at, updated_at)
                VALUES (@no, @sinv, @d, @sup, @uid, @sub, @disc, @tot, 'DRAFT', 'DRAFT', @notes, @t, @t2);
                SELECT last_insert_rowid();",
                new
                {
                    no, sinv = po.SupplierInvoiceNo, d = now,
                    sup = po.SupplierId > 0 ? (long?)po.SupplierId : null, uid = userId,
                    sub = DbEx.MoneyParam(subtotal), disc = DbEx.MoneyParam(po.Discount),
                    tot = DbEx.MoneyParam(Money.Round(subtotal - po.Discount)),
                    notes = po.Notes, t = now, t2 = now
                });
            foreach (var it in po.Items)
            {
                c.Execute(@"INSERT INTO purchase_items (purchase_id, product_id, product_code, product_name, qty, cost, subtotal)
                    VALUES (@pid, @prid, @pc, @pn, @q, @co, @su)",
                    new { pid = id, prid = it.ProductId, pc = it.ProductCode, pn = it.ProductName, q = (double)it.Qty, co = DbEx.MoneyParam(it.Cost), su = DbEx.MoneyParam(it.Subtotal) });
            }
            _audit.InTx(c, userId, username, "PO_CREATE", "purchase", id, $"{no} draft ({po.Items.Count} item)");
            return id;
        });
    }

    /// <summary>Draft -> Ordered (approved). No stock effect yet.</summary>
    public void Approve(long purchaseId, long userId, string username)
    {
        _db.Transaction(c =>
        {
            var st = c.QueryFirstOrDefault<(string Status, string Workflow)>(
                "SELECT status AS Status, workflow_status AS Workflow FROM purchases WHERE id=@id", new { id = purchaseId });
            if (st.Status != "DRAFT") throw new InvalidOperationException("Hanya PO draft yang bisa disetujui");
            var now = DbEx.Iso(DateTime.Now);
            c.Execute(@"UPDATE purchases SET status='ORDERED', workflow_status='ORDERED', updated_at=@t WHERE id=@id",
                new { t = now, id = purchaseId });
            _audit.InTx(c, userId, username, "PO_APPROVE", "purchase", purchaseId, $"PO #{purchaseId} disetujui");
        });
    }

    /// <summary>
    /// Receives goods (partial allowed): stock IN per received qty, updates the
    /// remaining workflow status; payment_status follows receipts.
    /// </summary>
    public long Receive(long purchaseId, List<(long ProductId, decimal OrderedQty, decimal ReceivedQty)> lines,
        string notes, long userId, string username, bool updateCost = false)
    {
        _periods.EnsureOpen(DateTime.Now);
        var now = DbEx.Iso(DateTime.Now);
        return _db.Transaction(c =>
        {
            var head = c.QueryFirstOrDefault<(string Status, string Workflow, string PoNo)>(
                @"SELECT status AS Status, workflow_status AS Workflow, purchase_no AS PoNo
                  FROM purchases WHERE id=@id", new { id = purchaseId });
            if (head.Workflow is not ("DRAFT" or "ORDERED" or "PARTIAL"))
                throw new InvalidOperationException($"PO harus berstatus DRAFT/ORDERED/PARTIAL untuk diterima (sekarang: {head.Workflow})");

            var receiptNo = InventoryService.NextNo(_db, "GRN", "purchase_receipts", "receipt_no");
            var receiptId = c.ExecuteScalar<long>(@"INSERT INTO purchase_receipts
                (receipt_no, purchase_id, receipt_date, user_id, notes, created_at)
                VALUES (@no, @pid, @d, @uid, @n, @t); SELECT last_insert_rowid();",
                new { no = receiptNo, pid = purchaseId, d = now, uid = userId, n = notes, t = now });

            var anyReceived = false;
            foreach (var (productId, ordered, received) in lines)
            {
                if (received <= 0) continue;
                anyReceived = true;
                c.Execute(@"INSERT INTO purchase_receipt_items (receipt_id, product_id, ordered_qty, received_qty)
                    VALUES (@rid, @prid, @oq, @rq)",
                    new { rid = receiptId, prid = productId, oq = (double)ordered, rq = (double)received });

                var cost = c.ExecuteScalar<decimal>(
                    "SELECT {c} FROM purchase_items WHERE purchase_id=@pid AND product_id=@prid LIMIT 1"
                        .Replace("{c}", DbEx.MoneyColumn("cost")),
                    new { pid = purchaseId, prid = productId });

                InventoryService.ApplyMovement(c, productId, StockRef.Purchase, receiptId,
                    StockDirection.In, received, $"Penerimaan {receiptNo} ({head.PoNo})", userId, now);
                if (updateCost && cost > 0)
                    c.Execute("UPDATE products SET purchase_price=@pp, updated_at=@t WHERE id=@id",
                        new { pp = DbEx.MoneyParam(cost), t = now, id = productId });
            }
            if (!anyReceived) throw new InvalidOperationException("Tidak ada barang yang diterima");

            // determine workflow status: complete when all lines fully received
            var outstanding = c.ExecuteScalar<long>(@"
                SELECT COUNT(*) FROM purchase_items pi WHERE pi.purchase_id=@id
                AND pi.qty > COALESCE((SELECT SUM(r.received_qty) FROM purchase_receipt_items r
                    JOIN purchase_receipts rr ON rr.id=r.receipt_id
                    WHERE rr.purchase_id=@id AND r.product_id=pi.product_id), 0)",
                new { id = purchaseId });
            var wf = outstanding > 0 ? "PARTIAL" : "RECEIVED";
            c.Execute(@"UPDATE purchases SET workflow_status=@wf, status='COMPLETED',
                payment_status=CASE WHEN payment_status='DRAFT_PAYMENT' THEN 'UNPAID' ELSE payment_status END,
                updated_at=@t WHERE id=@id",
                new { wf, t = now, id = purchaseId });

            _audit.InTx(c, userId, username, "PO_RECEIVE", "purchase", purchaseId,
                $"{receiptNo}: {lines.Count(l => l.ReceivedQty > 0)} item diterima ({wf})");
            return receiptId;
        });
    }

    /// <summary>Cancel a draft/ordered PO (no stock reversal needed because stock was never applied).</summary>
    public void CancelDraft(long purchaseId, string reason, long userId, string username)
    {
        _db.Transaction(c =>
        {
            var st = c.ExecuteScalar<string>("SELECT status FROM purchases WHERE id=@id", new { id = purchaseId });
            if (st is not ("DRAFT" or "ORDERED"))
                throw new InvalidOperationException("Hanya PO draft/ordered yang bisa dibatalkan");
            c.Execute(@"UPDATE purchases SET status='CANCELLED', workflow_status='CANCELLED',
                notes=COALESCE(notes,'') || ' | CANCELLED: ' || @r, updated_at=@t WHERE id=@id",
                new { r = reason, t = DbEx.Iso(DateTime.Now), id = purchaseId });
            _audit.InTx(c, userId, username, "PO_CANCEL", "purchase", purchaseId, reason);
        });
    }

    public List<(long Id, string PoNo, string Supplier, string Status, string Workflow, decimal Total)> ListDrafts()
    {
        const string sql = @"
            SELECT pu.id AS Id, pu.purchase_no AS PoNo, COALESCE(su.name,'') AS Supplier,
            pu.status AS Status, pu.workflow_status AS Workflow, {tot} AS Total
            FROM purchases pu LEFT JOIN suppliers su ON su.id=pu.supplier_id
            WHERE pu.workflow_status IN ('DRAFT','ORDERED','PARTIAL')
            ORDER BY pu.id DESC LIMIT 200";
        return _db.With(c => c.Query<(long, string, string, string, string, decimal)>(
            sql.Replace("{tot}", DbEx.MoneyColumn("pu.total"))).ToList());
    }

    /// <summary>AP aging buckets: Current / 1-30 / 31-60 / 61-90 / >90.</summary>
    public List<(string Bucket, int Count, decimal Total)> ApAging()
    {
        const string sql = @"
            SELECT
            CASE
                WHEN julianday('now','localtime') - julianday(purchase_date) <= 30 THEN '1-30'
                WHEN julianday('now','localtime') - julianday(purchase_date) <= 60 THEN '31-60'
                WHEN julianday('now','localtime') - julianday(purchase_date) <= 90 THEN '61-90'
                ELSE '>90'
            END AS Bucket,
            COUNT(*) AS Count,
            CAST(ROUND(SUM(total - paid_amount)/100.0,2) AS REAL) AS Total
            FROM purchases
            WHERE status='COMPLETED' AND payment_status != 'PAID' AND total > paid_amount
            GROUP BY Bucket ORDER BY Bucket";
        return _db.With(c => c.Query<(string, int, decimal)>(sql).ToList());
    }
}
