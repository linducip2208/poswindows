using Dapper;

namespace KasirPro.Infrastructure.Services;

public sealed record DatabaseHealth(
    string JournalMode,
    string Integrity,
    string QuickCheck,
    int SchemaVersion,
    long DatabaseBytes,
    long WalBytes,
    long ShmBytes,
    long FreeDiskBytes,
    string BackupDirectory,
    int BackupCount,
    DateTime? LatestBackup);

/// <summary>Read-only diagnostics for the local SQLite store.</summary>
public sealed class DatabaseHealthService
{
    private readonly Db _db;
    private readonly BackupService _backup;

    public DatabaseHealthService(Db db, BackupService backup)
    {
        _db = db;
        _backup = backup;
    }

    public DatabaseHealth Read()
    {
        var dbPath = _db.DbPath;
        var mode = _db.With(c => c.ExecuteScalar<string>("PRAGMA journal_mode") ?? "unknown");
        var integrity = _db.With(c => c.ExecuteScalar<string>("PRAGMA integrity_check") ?? "unknown");
        var quick = _db.With(c => c.ExecuteScalar<string>("PRAGMA quick_check") ?? "unknown");
        var schema = _db.With(c => c.ExecuteScalar<int>("SELECT COALESCE(MAX(version),0) FROM database_version"));
        var backups = _backup.ListBackups();
        var root = Path.GetPathRoot(Path.GetFullPath(dbPath));
        var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
        return new DatabaseHealth(
            mode, integrity, quick, schema,
            File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0,
            File.Exists(dbPath + "-wal") ? new FileInfo(dbPath + "-wal").Length : 0,
            File.Exists(dbPath + "-shm") ? new FileInfo(dbPath + "-shm").Length : 0,
            drive?.AvailableFreeSpace ?? 0,
            _backup.BackupDir,
            backups.Count,
            backups.FirstOrDefault().Created == default ? null : backups.First().Created);
    }

    public (string Integrity, long PageCount) Maintain()
    {
        return _db.With(c =>
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var integrity = cmd.ExecuteScalar()?.ToString() ?? "unknown";
            cmd.CommandText = "PRAGMA optimize;";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA page_count;";
            var pages = Convert.ToInt64(cmd.ExecuteScalar());
            return (integrity, pages);
        });
    }
}
