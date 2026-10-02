using Dapper;

namespace KasirPro.Infrastructure.Services;

/// <summary>Prevents new accounting-impacting entries in a closed YYYY-MM period.</summary>
public sealed class PeriodCloseService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public PeriodCloseService(Db db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public static string Key(DateTime date) => date.ToString("yyyy-MM");

    public bool IsClosed(DateTime date) => IsClosed(Key(date));

    public bool IsClosed(string periodKey) =>
        _db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM period_closures WHERE period_key=@p", new { p = periodKey }) > 0);

    public void EnsureOpen(DateTime date)
    {
        var key = Key(date);
        if (IsClosed(key))
            throw new InvalidOperationException($"Periode {key} sudah ditutup. Transaksi baru tidak dapat dicatat pada periode ini.");
    }

    public void Close(string periodKey, long userId, string username, string notes = "")
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(periodKey, @"^\d{4}-(0[1-9]|1[0-2])$"))
            throw new InvalidOperationException("Periode harus berformat YYYY-MM");
        _db.With(c => c.Execute(@"INSERT INTO period_closures (period_key, closed_at, closed_by, notes)
            VALUES (@p,@at,@uid,@notes)", new { p = periodKey, at = DbEx.Iso(DateTime.Now), uid = userId, notes }));
        _audit.Log(userId, username, "PERIOD_CLOSE", "period", 0, $"Periode {periodKey} ditutup");
    }

    public void Reopen(string periodKey, long userId, string username)
    {
        _db.With(c => c.Execute("DELETE FROM period_closures WHERE period_key=@p", new { p = periodKey }));
        _audit.Log(userId, username, "PERIOD_REOPEN", "period", 0, $"Periode {periodKey} dibuka kembali");
    }

    public List<(string Period, DateTime ClosedAt, string ClosedBy, string Notes)> List() =>
        _db.With(c => c.Query<(string, DateTime, string, string)>(@"SELECT pc.period_key AS Period,
            pc.closed_at AS ClosedAt, COALESCE(u.username,'') AS ClosedBy, pc.notes AS Notes
            FROM period_closures pc LEFT JOIN users u ON u.id=pc.closed_by ORDER BY pc.period_key DESC").ToList());
}
