using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Receivables (piutang): list outstanding debts, settle installments, audit trail.</summary>
public class DebtService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly CashService _cash;

    public DebtService(Db db, AuditService audit, CashService cash)
    { _db = db; _audit = audit; _cash = cash; }

    public PagedResult<SaleDebt> Outstanding(string search, string status, int page, int pageSize)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (status == "ALL") { /* no status filter */ }
        else if (status == "SETTLED") where.Add("d.status='SETTLED'");
        else where.Add("d.status != 'SETTLED'");
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(s.invoice_no LIKE @q OR cu.name LIKE @q)");
            p.Add("q", "%" + search.Trim() + "%");
        }
        var w = where.Count > 0 ? string.Join(" AND ", where) : "1=1";

        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($@"SELECT COUNT(*) FROM sale_debts d
                JOIN sales s ON s.id = d.sale_id
                LEFT JOIN customers cu ON cu.id = d.customer_id
                WHERE {w}", p);
            p.Add("limit", pageSize);
            p.Add("off", (page - 1) * pageSize);
            var items = c.Query<SaleDebt>($@"SELECT d.id AS Id, d.sale_id AS SaleId, s.invoice_no AS InvoiceNo,
                s.sale_date AS SaleDate, d.customer_id AS CustomerId, COALESCE(cu.name,'') AS CustomerName,
                {DbEx.MoneyColumn("d.original_amount")} AS OriginalAmount,
                {DbEx.MoneyColumn("d.paid_amount")} AS PaidAmount,
                d.status AS Status, d.due_date AS DueDate, d.notes AS Notes
                FROM sale_debts d
                JOIN sales s ON s.id = d.sale_id
                LEFT JOIN customers cu ON cu.id = d.customer_id
                WHERE {w}
                ORDER BY d.id DESC LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<SaleDebt> { Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total };
        });
    }

    public SaleDebt? Get(long id) =>
        _db.With(c =>
        {
            var d = c.QueryFirstOrDefault<SaleDebt>(@"SELECT d.id AS Id, d.sale_id AS SaleId, s.invoice_no AS InvoiceNo,
                s.sale_date AS SaleDate, d.customer_id AS CustomerId, COALESCE(cu.name,'') AS CustomerName,
                {oa} AS OriginalAmount, {pa} AS PaidAmount, d.status AS Status, d.due_date AS DueDate, d.notes AS Notes
                FROM sale_debts d
                JOIN sales s ON s.id = d.sale_id
                LEFT JOIN customers cu ON cu.id = d.customer_id
                WHERE d.id=@id"
                    .Replace("{oa}", DbEx.MoneyColumn("d.original_amount"))
                    .Replace("{pa}", DbEx.MoneyColumn("d.paid_amount")),
                new { id });
            if (d == null) return null;
            d.Payments = c.Query<DebtPayment>(@"SELECT dp.id AS Id, dp.debt_id AS DebtId, dp.method AS Method,
                {am} AS Amount, dp.notes AS Notes, dp.user_id AS UserId,
                COALESCE(us.username,'') AS UserName, COALESCE(dp.cash_session_id,0) AS CashSessionId,
                dp.created_at AS CreatedAt
                FROM debt_payments dp LEFT JOIN users us ON us.id=dp.user_id
                WHERE dp.debt_id=@id ORDER BY dp.id"
                    .Replace("{am}", DbEx.MoneyColumn("dp.amount")),
                new { id }).ToList();
            return d;
        });

    /// <summary>
    /// Settles part or all of a debt. Cash settlements flow into the open cash
    /// session (cash_sales + debt ledger entry) inside the same transaction.
    /// </summary>
    public SaleDebt Settle(long debtId, decimal amount, PaymentMethod method, string notes,
        long userId, string username, long cashSessionId = 0)
    {
        if (amount <= 0) throw new InvalidOperationException("Jumlah pembayaran harus lebih dari 0");
        var now = DbEx.Iso(DateTime.Now);

        _db.Transaction(c =>
        {
            var d = c.QueryFirstOrDefault<(long Id, decimal Original, decimal Paid, string Status)>(
                @"SELECT id AS Id, {oa} AS Original, {pa} AS Paid, status AS Status FROM sale_debts WHERE id=@id"
                    .Replace("{oa}", DbEx.MoneyColumn("original_amount"))
                    .Replace("{pa}", DbEx.MoneyColumn("paid_amount")),
                new { id = debtId });
            if (d.Id == 0) throw new InvalidOperationException("Piutang tidak ditemukan");
            if (d.Status == "SETTLED") throw new InvalidOperationException("Piutang sudah lunas");

            var remaining = Money.Round(d.Original - d.Paid);
            amount = Money.Round(amount);
            if (amount > remaining)
                throw new InvalidOperationException($"Jumlah melebihi sisa piutang (sisa {Money.Format(remaining)})");

            c.Execute(@"INSERT INTO debt_payments (debt_id, method, amount, notes, user_id, cash_session_id, created_at)
                VALUES (@did, @m, @a, @n, @uid, @csid, @t)",
                new
                {
                    did = debtId, m = method.ToString(), a = DbEx.MoneyParam(amount),
                    n = notes, uid = userId,
                    csid = cashSessionId > 0 ? (long?)cashSessionId : null, t = now
                });

            var newPaid = Money.Round(d.Paid + amount);
            var newStatus = newPaid >= d.Original ? "SETTLED" : "PARTIAL";
            c.Execute(@"UPDATE sale_debts SET paid_amount=@pa, status=@st, updated_at=@t WHERE id=@id",
                new { pa = DbEx.MoneyParam(newPaid), st = newStatus, t = now, id = debtId });

            if (method == PaymentMethod.Cash && cashSessionId > 0)
            {
                c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                    VALUES (@sid, 'SALE', 'IN', @a, 'DEBT', @did, 'Pelunasan piutang', @uid, @t)",
                    new { sid = cashSessionId, a = DbEx.MoneyParam(amount), did = debtId, uid = userId, t = now });
                c.Execute(@"UPDATE cash_sessions SET cash_sales = cash_sales + @a, debt_payments = debt_payments + @a, updated_at=@t WHERE id=@sid",
                    new { a = DbEx.MoneyParam(amount), t = now, sid = cashSessionId });
            }
        });

        var result = Get(debtId)!;
        _audit.Log(userId, username, AuditAction.DebtSettle, "sale_debt", debtId,
            $"Pelunasan {Money.Format(amount)} ({method}) - status {result.Status}");
        return result;
    }

    /// <summary>Sets a customer's credit limit (0 = no credit allowed).</summary>
    public void SetCreditLimit(long customerId, decimal limit, long userId, string username)
    {
        _db.With(c => c.Execute("UPDATE customers SET credit_limit=@l, updated_at=@t WHERE id=@id",
            new { l = DbEx.MoneyParam(limit), t = DbEx.Iso(DateTime.Now), id = customerId }));
        _audit.Log(userId, username, "CUSTOMER_CREDIT_LIMIT", "customer", customerId,
            $"Limit kredit = {Money.Format(limit)}");
    }
}