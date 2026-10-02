using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public sealed class ExpenseService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly PeriodCloseService _periods;

    public ExpenseService(Db db, AuditService audit, PeriodCloseService periods)
    {
        _db = db; _audit = audit; _periods = periods;
    }

    public List<Expense> List(DateTime from, DateTime to) =>
        _db.With(c => c.Query<Expense>(@"SELECT e.id AS Id, e.expense_date AS ExpenseDate,
            e.category AS Category, e.description AS Description,
            CAST(ROUND(e.amount / 100.0, 2) AS REAL) AS Amount,
            e.payment_method AS PaymentMethod, COALESCE(e.cash_session_id,0) AS CashSessionId,
            e.user_id AS UserId, COALESCE(u.username,'') AS Username,
            e.status AS Status, e.created_at AS CreatedAt
            FROM expenses e LEFT JOIN users u ON u.id=e.user_id
            WHERE e.expense_date >= @f AND e.expense_date <= @t
            ORDER BY e.expense_date DESC, e.id DESC",
            new { f = DbEx.Iso(from.Date), t = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    public long Add(DateTime date, string category, string description, decimal amount,
        string paymentMethod, long userId, string username)
    {
        if (amount <= 0) throw new InvalidOperationException("Jumlah pengeluaran harus lebih dari 0");
        if (string.IsNullOrWhiteSpace(category)) throw new InvalidOperationException("Kategori wajib diisi");
        _periods.EnsureOpen(date);
        var now = DbEx.Iso(DateTime.Now);
        var id = _db.Transaction(c =>
        {
            var cashSessionId = paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                ? c.ExecuteScalar<long?>("SELECT id FROM cash_sessions WHERE user_id=@uid AND status='OPEN' ORDER BY id DESC LIMIT 1", new { uid = userId })
                : null;
            var expenseId = c.ExecuteScalar<long>(@"INSERT INTO expenses
                (expense_date, category, description, amount, payment_method, cash_session_id, user_id, status, created_at)
                VALUES (@d,@cat,@desc,@amount,@method,@sid,@uid,'POSTED',@now);
                SELECT last_insert_rowid();",
                new { d = DbEx.Iso(date), cat = category.Trim(), desc = description.Trim(), amount = DbEx.MoneyParam(amount), method = paymentMethod, sid = cashSessionId, uid = userId, now });
            if (cashSessionId.HasValue)
            {
                c.Execute(@"INSERT INTO cash_movements
                    (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                    VALUES (@sid,'EXPENSE','OUT',@amount,'EXPENSE',@rid,@notes,@uid,@now)",
                    new { sid = cashSessionId.Value, amount = DbEx.MoneyParam(amount), rid = expenseId,
                        notes = category.Trim() + ": " + description.Trim(), uid = userId, now });
                c.Execute("UPDATE cash_sessions SET cash_out = cash_out + @amount, updated_at=@now WHERE id=@sid",
                    new { amount = DbEx.MoneyParam(amount), now, sid = cashSessionId.Value });
            }
            return expenseId;
        });
        _audit.Log(userId, username, "EXPENSE_POST", "expense", id, $"{category}: {Money.Format(amount)}");
        return id;
    }
}
