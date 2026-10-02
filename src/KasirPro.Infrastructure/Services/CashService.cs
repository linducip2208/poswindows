using System.Data;
using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class CashService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly PeriodCloseService? _periods;

    public CashService(Db db, AuditService audit, PeriodCloseService? periods = null) { _db = db; _audit = audit; _periods = periods; }

    public CashSession? OpenSession(long userId, string username, decimal openingCash)
    {
        _periods?.EnsureOpen(DateTime.Now);
        var existing = GetOpenSession(userId);
        if (existing != null) return existing; // already open

        var now = DateTime.Now;
        var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO cash_sessions
            (user_id, opened_at, opening_cash, status, created_at, updated_at)
            VALUES (@uid, @d, @oc, 'OPEN', @t, @t2); SELECT last_insert_rowid();",
            new { uid = userId, d = DbEx.Iso(now), oc = DbEx.MoneyParam(openingCash), t = DbEx.Iso(now), t2 = DbEx.Iso(now) }));

        _db.With(c => c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
            VALUES (@sid, 'OPENING', 'IN', @a, 'SESSION', @sid2, 'Modal awal', @uid, @t)",
            new { sid = id, a = DbEx.MoneyParam(openingCash), sid2 = id, uid = userId, t = DbEx.Iso(now) }));

        _audit.Log(userId, username, "SHIFT_OPEN", "cash_session", id, $"Shift dibuka, modal {Money.Format(openingCash)}");
        return GetSession(id);
    }

    public CashSession? GetSession(long id) =>
        _db.With(c => c.QueryFirstOrDefault<CashSession>(@"SELECT cs.id AS Id, cs.user_id AS UserId,
            COALESCE(u.username,'') AS UserName, cs.opened_at AS OpenedAt, cs.closed_at AS ClosedAt,
            {oc} AS OpeningCash, {ce} AS ClosingCashExpected, {ca} AS ClosingCashActual,
            {cs} AS CashSales, {ci} AS CashIn, {co} AS CashOut, {dp} AS DebtPayments, {df} AS Difference,
            cs.status AS Status, cs.notes AS Notes
            FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id
            WHERE cs.id=@id"
                .Replace("{oc}", DbEx.MoneyColumn("cs.opening_cash"))
                .Replace("{ce}", DbEx.MoneyColumn("cs.closing_cash_expected"))
                .Replace("{ca}", DbEx.MoneyColumn("cs.closing_cash_actual"))
                .Replace("{cs}", DbEx.MoneyColumn("cs.cash_sales"))
                .Replace("{ci}", DbEx.MoneyColumn("cs.cash_in"))
                .Replace("{co}", DbEx.MoneyColumn("cs.cash_out"))
                .Replace("{dp}", DbEx.MoneyColumn("cs.debt_payments"))
                .Replace("{df}", DbEx.MoneyColumn("cs.difference")),
            new { id }));

    public CashSession? GetOpenSession(long userId) =>
        _db.With(c => c.QueryFirstOrDefault<long?>(
            "SELECT id FROM cash_sessions WHERE user_id=@u AND status='OPEN' ORDER BY id DESC", new { u = userId })) is long id
            ? GetSession(id)
            : null;

    public bool HasOpenSession(long userId) => GetOpenSession(userId) != null;

    /// <summary>Called inside sale transaction for cash payments.</summary>
    internal void RecordSaleCash(IDbConnection c, long sessionId, decimal amount, long saleId, long userId, string nowIso)
    {
        c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
            VALUES (@sid, 'SALE', 'IN', @a, 'SALE', @rid, 'Penjualan tunai', @uid, @t)",
            new { sid = sessionId, a = DbEx.MoneyParam(amount), rid = saleId, uid = userId, t = nowIso });
        c.Execute(@"UPDATE cash_sessions SET cash_sales = cash_sales + @a, updated_at=@t WHERE id=@sid",
            new { a = DbEx.MoneyParam(amount), t = nowIso, sid = sessionId });
    }

    public void CashIn(long sessionId, decimal amount, string notes, long userId, string username)
    {
        _periods?.EnsureOpen(DateTime.Now);
        if (amount <= 0) throw new InvalidOperationException("Jumlah harus lebih dari 0");
        _db.With(c =>
        {
            c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                VALUES (@sid, 'CASH_IN', 'IN', @a, 'MANUAL', 0, @n, @uid, @t)",
                new { sid = sessionId, a = DbEx.MoneyParam(amount), n = notes, uid = userId, t = DbEx.Iso(DateTime.Now) });
            c.Execute(@"UPDATE cash_sessions SET cash_in = cash_in + @a, updated_at=@t WHERE id=@sid",
                new { a = DbEx.MoneyParam(amount), t = DbEx.Iso(DateTime.Now), sid = sessionId });
        });
        _audit.Log(userId, username, "CASH_IN", "cash_session", sessionId, $"{Money.Format(amount)} - {notes}");
    }

    public void CashOut(long sessionId, decimal amount, string notes, long userId, string username)
    {
        _periods?.EnsureOpen(DateTime.Now);
        if (amount <= 0) throw new InvalidOperationException("Jumlah harus lebih dari 0");
        _db.With(c =>
        {
            c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                VALUES (@sid, 'CASH_OUT', 'OUT', @a, 'MANUAL', 0, @n, @uid, @t)",
                new { sid = sessionId, a = DbEx.MoneyParam(amount), n = notes, uid = userId, t = DbEx.Iso(DateTime.Now) });
            c.Execute(@"UPDATE cash_sessions SET cash_out = cash_out + @a, updated_at=@t WHERE id=@sid",
                new { a = DbEx.MoneyParam(amount), t = DbEx.Iso(DateTime.Now), sid = sessionId });
        });
        _audit.Log(userId, username, "CASH_OUT", "cash_session", sessionId, $"{Money.Format(amount)} - {notes}");
    }

    public List<CashMovement> GetMovements(long sessionId) =>
        _db.With(c => c.Query<CashMovement>(@"SELECT id AS Id, cash_session_id AS CashSessionId, type AS Type,
            direction AS Direction, {am} AS Amount, reference_type AS ReferenceType, reference_id AS ReferenceId,
            notes AS Notes, user_id AS UserId, created_at AS CreatedAt
            FROM cash_movements WHERE cash_session_id=@sid ORDER BY id"
                .Replace("{am}", DbEx.MoneyColumn("amount")),
            new { sid = sessionId }).ToList());

    /// <summary>Computes expected closing cash, closes the session and stores the summary.</summary>
    public CashSession CloseSession(long sessionId, decimal actualCash, string notes, long userId, string username)
    {
        var session = GetSession(sessionId) ?? throw new InvalidOperationException("Shift tidak ditemukan");
        if (session.Status != "OPEN") throw new InvalidOperationException("Shift sudah ditutup");

        var expected = Money.Round(session.OpeningCash + session.CashSales + session.CashIn - session.CashOut);
        var diff = Money.Round(actualCash - expected);
        var now = DbEx.Iso(DateTime.Now);

        _db.With(c =>
        {
            c.Execute(@"UPDATE cash_sessions SET closed_at=@d, closing_cash_expected=@ce, closing_cash_actual=@ca,
                difference=@df, status='CLOSED', notes=@n, updated_at=@t WHERE id=@id",
                new
                {
                    d = now, ce = DbEx.MoneyParam(expected), ca = DbEx.MoneyParam(actualCash),
                    df = DbEx.MoneyParam(diff), n = notes, t = now, id = sessionId
                });
            c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                VALUES (@sid, 'CLOSING', 'OUT', 0, 'SESSION', @sid2, @n, @uid, @t)",
                new { sid = sessionId, sid2 = sessionId, n = $"Tutup shift. Selisih {diff}", uid = userId, t = now });
        });

        _audit.Log(userId, username, "SHIFT_CLOSE", "cash_session", sessionId,
            $"Shift ditutup. Sistem {Money.Format(expected)}, fisik {Money.Format(actualCash)}, selisih {Money.Format(diff)}");
        return GetSession(sessionId)!;
    }

    public List<CashSession> History(DateTime from, DateTime to, int limit = 200) =>
        _db.With(c => c.Query<CashSession>(@"SELECT cs.id AS Id, cs.user_id AS UserId,
            COALESCE(u.username,'') AS UserName, cs.opened_at AS OpenedAt, cs.closed_at AS ClosedAt,
            {oc} AS OpeningCash, {ce} AS ClosingCashExpected, {ca} AS ClosingCashActual,
            {cs2} AS CashSales, {ci} AS CashIn, {co} AS CashOut, {df} AS Difference, cs.status AS Status
            FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id
            WHERE cs.opened_at >= @f AND cs.opened_at <= @t2
            ORDER BY cs.id DESC LIMIT @l"
                .Replace("{oc}", DbEx.MoneyColumn("cs.opening_cash"))
                .Replace("{ce}", DbEx.MoneyColumn("cs.closing_cash_expected"))
                .Replace("{ca}", DbEx.MoneyColumn("cs.closing_cash_actual"))
                .Replace("{cs2}", DbEx.MoneyColumn("cs.cash_sales"))
                .Replace("{ci}", DbEx.MoneyColumn("cs.cash_in"))
                .Replace("{co}", DbEx.MoneyColumn("cs.cash_out"))
                .Replace("{df}", DbEx.MoneyColumn("cs.difference")),
            new
            {
                f = DbEx.Iso(from.Date),
                t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)),
                l = limit
            }).ToList());

    public List<CashSession> TodaySessions(long userId) =>
        _db.With(c => c.Query<CashSession>(@"SELECT cs.id AS Id, cs.user_id AS UserId,
            COALESCE(u.username,'') AS UserName, cs.opened_at AS OpenedAt, cs.closed_at AS ClosedAt,
            {oc} AS OpeningCash, {cs2} AS CashSales, cs.status AS Status
            FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id
            WHERE cs.user_id=@u AND date(cs.opened_at)=date('now','localtime')
            ORDER BY cs.id DESC"
                .Replace("{oc}", DbEx.MoneyColumn("cs.opening_cash"))
                .Replace("{cs2}", DbEx.MoneyColumn("cs.cash_sales")),
            new { u = userId }).ToList());
}
