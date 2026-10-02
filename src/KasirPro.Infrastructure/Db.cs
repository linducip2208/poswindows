using Microsoft.Data.Sqlite;

namespace KasirPro.Infrastructure;

/// <summary>
/// SQLite connection factory with safe offline configuration:
/// foreign_keys=ON, journal_mode=WAL, synchronous=NORMAL, busy_timeout + retry.
/// </summary>
public class Db
{
    private readonly string _dbPath;
    private static readonly object LastMaintenanceLock = new();
    private static DateTime _lastMaintenance = DateTime.MinValue;

    public Db(string? dbPath = null)
    {
        _dbPath = dbPath ?? AppPaths.DatabaseFile;
    }

    public string DbPath => _dbPath;

    public SqliteConnection Open()
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true
        };
        var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            // WAL is intentional for the offline POS: readers remain available while a sale writes.
            // This must be set on a real connection; appsettings.json alone does not configure SQLite.
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteScalar();
            cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=1000; PRAGMA temp_store=MEMORY;";
            cmd.ExecuteNonQuery();
        }
        RunLightMaintenanceIfDue(conn);
        return conn;
    }

    /// <summary>
    /// Lightweight, throttled (max once per 10 minutes) maintenance on open:
    /// WAL auto-checkpoint + PRAGMA optimize + quick integrity sanity check.
    /// Failures never block normal operation.
    /// </summary>
    private static void RunLightMaintenanceIfDue(SqliteConnection conn)
    {
        var now = DateTime.UtcNow;
        lock (LastMaintenanceLock)
        {
            if ((now - _lastMaintenance).TotalMinutes < 10) return;
            _lastMaintenance = now;
        }
        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE); PRAGMA optimize;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA quick_check;";
                var result = cmd.ExecuteScalar()?.ToString();
                if (result != "ok")
                    AppLogger.Instance.Error("quick_check gagal: " + result);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("light maintenance gagal (diabaikan)", ex);
        }
    }

    public T With<T>(Func<SqliteConnection, T> fn) => WithRetry(conn => fn(conn));

    public void With(Action<SqliteConnection> fn) => WithRetry<object?>(conn => { fn(conn); return null; });

    /// <summary>Runs work with SQLITE_BUSY retry (3 attempts, backoff) beyond busy_timeout.</summary>
    private T WithRetry<T>(Func<SqliteConnection, T> fn)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var conn = Open();
                return fn(conn);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5 /* SQLITE_BUSY */ && attempt < 3)
            {
                System.Threading.Thread.Sleep(150 * attempt);
            }
        }
    }

    /// <summary>Runs work inside a single atomic SQLite transaction. Rolls back on any exception.</summary>
    public void Transaction(Action<SqliteConnection> work)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            work(conn);
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public T Transaction<T>(Func<SqliteConnection, T> work)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var result = work(conn);
            tx.Commit();
            return result;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public bool Exists()
    {
        return File.Exists(_dbPath);
    }
}

public static class DbEx
{
    public const string DateFmt = "yyyy-MM-dd HH:mm:ss";

    public static string Iso(DateTime dt) => dt.ToString(DateFmt);

    public static long Cents(decimal v) => (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero);

    public static decimal Money(long cents) => cents / 100m;

    public static object MoneyParam(decimal v) => Cents(v);

    public static object QtyParam(decimal v) => (double)v;

    public static decimal Qty(object? v) => v == null ? 0m : Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>SQL fragment converting a cents INTEGER column to decimal money in SELECTs.</summary>
    public static string MoneyColumn(string colExpr) => $"CAST(ROUND({colExpr}/100.0, 2) AS REAL)";
}
