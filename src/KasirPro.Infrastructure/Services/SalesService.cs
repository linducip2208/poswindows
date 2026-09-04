using System.Data;
using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class SalesService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly CashService _cash;
    private readonly SettingsService _settings;
    private readonly LoyaltyService _loyalty;

    public SalesService(Db db, AuditService audit, CashService cash, SettingsService settings, LoyaltyService loyalty)
    { _db = db; _audit = audit; _cash = cash; _settings = settings; _loyalty = loyalty; }

    public string NextInvoiceNo(long? saleId = null)
    {
        // INV-YYYYMMDD-XXXX ; use max+1 for the day to guarantee uniqueness
        var date = DateTime.Now.ToString("yyyyMMdd");
        var prefix = $"INV-{date}-";
        var max = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(CAST(SUBSTR(invoice_no, LENGTH(@p)+1) AS INTEGER)),0) FROM sales WHERE invoice_no LIKE @l",
            new { p = prefix, l = prefix + "%" }));
        return $"{prefix}{max + 1:D4}";
    }

    /// <summary>
    /// Completes a sale atomically: sales + items + payments + stock movements + stock + cash session.
    /// When allow_credit is enabled and a real customer is chosen, an unpaid remainder
    /// becomes a receivable (sale_debts). Otherwise shortfall is rejected.
    /// </summary>
    public Sale CompleteSale(Sale sale)
    {
        var paid = Money.Round(sale.Payments.Sum(p => p.Amount));
        sale.Total = Money.Round(sale.Total);
        var shortfall = Money.Round(sale.Total - paid);
        if (sale.Items.Count == 0) throw new InvalidOperationException("Keranjang kosong");
        if (shortfall > 0)
        {
            if (!_settings.AllowCredit)
                throw new InvalidOperationException("Pembayaran kurang dari total transaksi");
            if (sale.CustomerId <= 1)
                throw new InvalidOperationException("Piutang harus atas nama pelanggan (bukan Umum/Walk-in)");
        }

        var now = DbEx.Iso(DateTime.Now);
        sale.SaleDate = DateTime.Now;
        sale.Outstanding = shortfall > 0 ? shortfall : 0;

        // credit limit guard: existing outstanding + new shortfall <= limit (0 = credit disabled)
        if (shortfall > 0)
        {
            var limit = _db.With(c => c.ExecuteScalar<decimal>(
                "SELECT COALESCE(credit_limit,0) FROM customers WHERE id=@id", new { id = sale.CustomerId }));
            if (limit <= 0)
                throw new InvalidOperationException("Pelanggan ini tidak memiliki limit kredit");
            var existingOutstanding = _db.With(c => c.ExecuteScalar<decimal>(
                "SELECT COALESCE(SUM(original_amount - paid_amount),0) FROM sale_debts WHERE customer_id=@id AND status != 'SETTLED'",
                new { id = sale.CustomerId }));
            if (Money.Round(existingOutstanding + shortfall) > limit)
                throw new InvalidOperationException(
                    $"Melebihi limit kredit ({Money.Format(limit)}). Piutang berjalan: {Money.Format(existingOutstanding)}");
        }

        return _db.Transaction(c =>
        {
            sale.InvoiceNo = NextInvoiceNo();
            sale.CostTotal = Money.Round(sale.Items.Sum(i => Money.Round(i.Cost * i.Qty)));

            sale.Id = c.ExecuteScalar<long>(@"INSERT INTO sales
                (invoice_no, sale_date, customer_id, user_id, cash_session_id, subtotal, discount, total, cost_total, status, notes, created_at, updated_at)
                VALUES (@inv, @d, @cust, @uid, @cs, @sub, @disc, @tot, @cost, 'COMPLETED', @notes, @t, @t2);
                SELECT last_insert_rowid();",
                new
                {
                    inv = sale.InvoiceNo, d = DbEx.Iso(sale.SaleDate),
                    cust = sale.CustomerId > 0 ? (long?)sale.CustomerId : null,
                    uid = sale.UserId,
                    cs = sale.CashSessionId > 0 ? (long?)sale.CashSessionId : null,
                    sub = DbEx.MoneyParam(sale.Subtotal), disc = DbEx.MoneyParam(sale.Discount),
                    tot = DbEx.MoneyParam(sale.Total), cost = DbEx.MoneyParam(sale.CostTotal),
                    notes = sale.Notes, t = now, t2 = now
                });

            foreach (var item in sale.Items)
            {
                item.SaleId = sale.Id;
                item.Subtotal = Money.Round((item.Price * item.Qty) - item.Discount);
                c.Execute(@"INSERT INTO sale_items (sale_id, product_id, product_code, product_name, qty, price, cost, discount, subtotal)
                    VALUES (@sid, @pid, @pc, @pn, @q, @pr, @co, @di, @su)",
                    new
                    {
                        sid = sale.Id, pid = item.ProductId, pc = item.ProductCode, pn = item.ProductName,
                        q = (double)item.Qty, pr = DbEx.MoneyParam(item.Price), co = DbEx.MoneyParam(item.Cost),
                        di = DbEx.MoneyParam(item.Discount), su = DbEx.MoneyParam(item.Subtotal)
                    });

                // serial-tracked: mark the picked serials as SOLD (fails when unavailable)
                if (item.SerialNos.Count > 0)
                    SerialService.MarkSold(c, item.ProductId, item.SerialNos, sale.Id, now);

                // inventory ledger: OUT
                InventoryService.ApplyMovement(c, item.ProductId, StockRef.Sale, sale.Id,
                    StockDirection.Out, item.Qty, $"Penjualan {sale.InvoiceNo}", sale.UserId, now);
            }

            foreach (var pay in sale.Payments)
            {
                pay.SaleId = sale.Id;
                pay.CreatedAt = DateTime.Now;
                c.Execute(@"INSERT INTO sale_payments (sale_id, method, amount, reference, created_at)
                    VALUES (@sid, @m, @a, @r, @t)",
                    new { sid = sale.Id, m = pay.Method.ToString(), a = DbEx.MoneyParam(pay.Amount), r = pay.Reference, t = DbEx.Iso(pay.CreatedAt) });

                if (pay.Method == PaymentMethod.Cash && sale.CashSessionId > 0)
                {
                    _cash.RecordSaleCash(c, sale.CashSessionId, pay.Amount, sale.Id, sale.UserId, now);
                }
            }

            // persist tax
            c.Execute("UPDATE sales SET tax=@tax WHERE id=@id",
                new { tax = DbEx.MoneyParam(sale.Tax), id = sale.Id });

            if (sale.Outstanding > 0)
            {
                var dueDays = 14;
                c.Execute(@"INSERT INTO sale_debts (sale_id, customer_id, original_amount, paid_amount, status, due_date, notes, created_at, updated_at)
                    VALUES (@sid, @cust, @amt, 0, 'OUTSTANDING', @due, @n, @t, @t2)",
                    new
                    {
                        sid = sale.Id,
                        cust = sale.CustomerId > 0 ? (long?)sale.CustomerId : null,
                        amt = DbEx.MoneyParam(sale.Outstanding),
                        due = DbEx.Iso(DateTime.Now.AddDays(dueDays)),
                        n = $"Piutang {sale.InvoiceNo}", t = now, t2 = now
                    });
            }

            _audit.InTx(c, sale.UserId, sale.CashierName, AuditAction.Sale, "sale", sale.Id,
                $"{sale.InvoiceNo} total {Money.Format(sale.Total)}" +
                (sale.Outstanding > 0 ? $" (piutang {Money.Format(sale.Outstanding)})" : ""));

            return sale;
        });
    }

    public Sale? GetByInvoice(string invoiceNo) =>
        _db.With(c => LoadSale(c, invoiceNo));

    public Sale? GetById(long id) =>
        _db.With(c => LoadSale(c, id));

    private Sale? LoadSale(IDbConnection c, object key)
    {
        var s = c.QueryFirstOrDefault<Sale>(@"SELECT s.id AS Id, s.invoice_no AS InvoiceNo, s.sale_date AS SaleDate,
            s.customer_id AS CustomerId, COALESCE(cu.name,'Umum') AS CustomerName,
            s.user_id AS UserId, COALESCE(us.full_name, us.username,'') AS CashierName,
            s.cash_session_id AS CashSessionId,
            {sub} AS Subtotal, {disc} AS Discount, {tot} AS Total, {cost} AS CostTotal, {tax} AS Tax,
            s.status AS Status, s.notes AS Notes, s.created_at AS CreatedAt
            FROM sales s
            LEFT JOIN customers cu ON cu.id = s.customer_id
            LEFT JOIN users us ON us.id = s.user_id
            WHERE s.invoice_no = @k OR s.id = @k"
                .Replace("{sub}", DbEx.MoneyColumn("s.subtotal"))
                .Replace("{disc}", DbEx.MoneyColumn("s.discount"))
                .Replace("{tot}", DbEx.MoneyColumn("s.total"))
                .Replace("{cost}", DbEx.MoneyColumn("s.cost_total"))
                .Replace("{tax}", DbEx.MoneyColumn("s.tax")),
            new { k = key });
        if (s == null) return null;
        s.Items = c.Query<SaleItem>(@"SELECT id AS Id, sale_id AS SaleId, product_id AS ProductId,
            product_code AS ProductCode, product_name AS ProductName, qty AS Qty,
            {pr} AS Price, {co} AS Cost, {di} AS Discount, {su} AS Subtotal
            FROM sale_items WHERE sale_id=@id"
                .Replace("{pr}", DbEx.MoneyColumn("price"))
                .Replace("{co}", DbEx.MoneyColumn("cost"))
                .Replace("{di}", DbEx.MoneyColumn("discount"))
                .Replace("{su}", DbEx.MoneyColumn("subtotal")),
            new { id = s.Id }).ToList();
        s.Payments = c.Query<SalePayment>(@"SELECT id AS Id, sale_id AS SaleId, method AS Method,
            {am} AS Amount, reference AS Reference
            FROM sale_payments WHERE sale_id=@id"
                .Replace("{am}", DbEx.MoneyColumn("amount")),
            new { id = s.Id }).ToList();
        s.Outstanding = c.ExecuteScalar<decimal>(@"SELECT COALESCE(
            (SELECT SUM(original_amount - paid_amount) FROM sale_debts WHERE sale_id=@id AND status != 'SETTLED'), 0)",
            new { id = s.Id });
        return s;
    }

    public PagedResult<Sale> History(string search, DateTime from, DateTime to, long userId,
        int page, int pageSize)
    {
        var where = new List<string> { "s.sale_date >= @f", "s.sale_date <= @t2" };
        var p = new DynamicParameters();
        p.Add("f", DbEx.Iso(from.Date));
        p.Add("t2", DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(s.invoice_no LIKE @q OR cu.name LIKE @q)");
            p.Add("q", "%" + search.Trim() + "%");
        }
        if (userId > 0) { where.Add("s.user_id=@u"); p.Add("u", userId); }
        var w = string.Join(" AND ", where);

        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($@"SELECT COUNT(*) FROM sales s
                LEFT JOIN customers cu ON cu.id=s.customer_id WHERE {w}", p);
            p.Add("limit", pageSize);
            p.Add("off", (page - 1) * pageSize);
            var items = c.Query<Sale>($@"SELECT s.id AS Id, s.invoice_no AS InvoiceNo, s.sale_date AS SaleDate,
                COALESCE(cu.name,'Umum') AS CustomerName,
                {DbEx.MoneyColumn("s.total")} AS Total, s.status AS Status,
                (SELECT GROUP_CONCAT(sp.method, '+') FROM sale_payments sp WHERE sp.sale_id=s.id) AS CashierName,
                s.cash_session_id AS CashSessionId
                FROM sales s LEFT JOIN customers cu ON cu.id=s.customer_id
                WHERE {w} ORDER BY s.id DESC LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<Sale> { Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total };
        });
    }

    /// <summary>Voids a completed sale: reverses stock (IN) with SALE_VOID movements.</summary>
    public void VoidSale(long saleId, string reason, long userId, string username)
    {
        _db.Transaction(c =>
        {
            var status = c.ExecuteScalar<string>("SELECT status FROM sales WHERE id=@id", new { id = saleId });
            if (status != "COMPLETED") throw new InvalidOperationException("Transaksi sudah dibatalkan");
            var outstanding = c.ExecuteScalar<decimal>(@"SELECT COALESCE(SUM(original_amount - paid_amount),0)
                FROM sale_debts WHERE sale_id=@id AND status != 'SETTLED'", new { id = saleId });
            if (outstanding > 0)
                throw new InvalidOperationException("Transaksi memiliki piutang belum lunas. Lunasi piutang dulu sebelum void.");
            var now = DbEx.Iso(DateTime.Now);
            var items = c.Query<(long ProductId, decimal Qty)>(
                "SELECT product_id AS ProductId, qty AS Qty FROM sale_items WHERE sale_id=@id",
                new { id = saleId }).ToList();
            foreach (var it in items)
            {
                InventoryService.ApplyMovement(c, it.ProductId, StockRef.Sale + "_VOID", saleId,
                    StockDirection.In, it.Qty, $"Void: {reason}", userId, now);
            }
            c.Execute("UPDATE sales SET status='VOIDED', notes=@n, updated_at=@t WHERE id=@id",
                new { n = "VOID: " + reason, t = now, id = saleId });
        });
        _audit.Log(userId, username, AuditAction.SaleVoid, "sale", saleId, $"Void: {reason}");
    }

    /// <summary>Creates a sale return (full or partial items) and restores stock.</summary>
    public SaleReturn CreateReturn(long saleId, List<(long SaleItemId, decimal Qty)> returnItems,
        string reason, long userId, string username)
    {
        if (returnItems.Count == 0) throw new InvalidOperationException("Pilih item yang diretur");
        var now = DbEx.Iso(DateTime.Now);
        var no = InventoryService.NextNo(_db, "RTN", "sale_returns", "return_no");

        var ret = _db.Transaction(c =>
        {
            var sale = c.QueryFirstOrDefault<(long Id, string InvoiceNo)>(
                "SELECT id AS Id, invoice_no AS InvoiceNo FROM sales WHERE id=@id", new { id = saleId });
            if (sale.Id == 0) throw new InvalidOperationException("Transaksi tidak ditemukan");

            // validate all requested quantities first
            var planned = new List<(long SaleItemId, long ProductId, string ProductName, decimal Qty, decimal Price, decimal Subtotal)>();
            decimal total = 0;
            foreach (var (saleItemId, qty) in returnItems)
            {
                if (qty <= 0) continue;
                var si = c.QueryFirstOrDefault<(long Id, long ProductId, string ProductName, decimal Qty, decimal Price)>(
                    @"SELECT id AS Id, product_id AS ProductId, product_name AS ProductName, qty AS Qty,
                      {pr} AS Price FROM sale_items WHERE id=@sid"
                        .Replace("{pr}", DbEx.MoneyColumn("price")),
                    new { sid = saleItemId });
                if (si.Id == 0) continue;
                var already = c.ExecuteScalar<decimal>(@"SELECT COALESCE(SUM(sri.qty),0) FROM sale_return_items sri
                    WHERE sri.sale_item_id=@sid", new { sid = saleItemId });
                var maxQty = si.Qty - already;
                if (qty > maxQty)
                    throw new InvalidOperationException($"Qty retur melebihi jumlah yang dapat diretur (sisa {maxQty})");

                var sub = Money.Round(qty * si.Price);
                total += sub;
                planned.Add((saleItemId, si.ProductId, si.ProductName, qty, si.Price, sub));
            }
            if (total <= 0) throw new InvalidOperationException("Nilai retur harus lebih dari 0");

            // header first, then items (FK integrity)
            var returnId = c.ExecuteScalar<long>(@"INSERT INTO sale_returns
                (return_no, sale_id, return_date, user_id, total, reason, created_at, updated_at)
                VALUES (@no, @sid, @d, @uid, @tot, @reason, @t, @t2); SELECT last_insert_rowid();",
                new { no, sid = saleId, d = now, uid = userId, tot = DbEx.MoneyParam(total), reason, t = now, t2 = now });

            var items = new List<SaleReturnItem>();
            foreach (var p in planned)
            {
                var itemId = c.ExecuteScalar<long>(@"INSERT INTO sale_return_items
                    (sale_return_id, sale_item_id, product_id, product_name, qty, price, subtotal)
                    VALUES (@rid, @sid2, @pid, @pn, @q, @pr2, @su); SELECT last_insert_rowid();",
                    new
                    {
                        rid = returnId, sid2 = p.SaleItemId, pid = p.ProductId, pn = p.ProductName,
                        q = (double)p.Qty, pr2 = DbEx.MoneyParam(p.Price), su = DbEx.MoneyParam(p.Subtotal)
                    });
                items.Add(new SaleReturnItem
                {
                    Id = itemId, SaleReturnId = returnId, SaleItemId = p.SaleItemId, ProductId = p.ProductId,
                    ProductName = p.ProductName, Qty = p.Qty, Price = p.Price, Subtotal = p.Subtotal
                });
            }

            foreach (var it in items)
            {
                InventoryService.ApplyMovement(c, it.ProductId, StockRef.SaleReturn, returnId,
                    StockDirection.In, it.Qty, $"Retur {no} ({sale.InvoiceNo})", userId, now);
            }
            return new SaleReturn
            {
                Id = returnId, ReturnNo = no, SaleId = saleId, InvoiceNo = sale.InvoiceNo,
                ReturnDate = DateTime.Now, UserId = userId, UserName = username, Total = total,
                Reason = reason, Items = items
            };
        });
        _audit.Log(userId, username, AuditAction.SaleReturn, "sale_return", ret.Id,
            $"{ret.ReturnNo} dari {ret.InvoiceNo} = {Money.Format(ret.Total)}");
        return ret;
    }

    public List<SaleReturn> GetReturns(DateTime from, DateTime to, int limit = 200) =>
        _db.With(c => c.Query<SaleReturn>(@"SELECT r.id AS Id, r.return_no AS ReturnNo, r.sale_id AS SaleId,
            s.invoice_no AS InvoiceNo, r.return_date AS ReturnDate, r.user_id AS UserId,
            COALESCE(u.username,'') AS UserName, {tot} AS Total, r.reason AS Reason
            FROM sale_returns r
            JOIN sales s ON s.id = r.sale_id
            LEFT JOIN users u ON u.id = r.user_id
            WHERE r.return_date >= @f AND r.return_date <= @t2
            ORDER BY r.id DESC LIMIT @l"
                .Replace("{tot}", DbEx.MoneyColumn("r.total")),
            new
            {
                f = DbEx.Iso(from.Date),
                t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)),
                l = limit
            }).ToList());
}

