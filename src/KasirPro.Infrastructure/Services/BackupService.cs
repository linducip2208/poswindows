using KasirPro.Core.Domain;
using Microsoft.Data.Sqlite;

namespace KasirPro.Infrastructure.Services;

public class BackupService
{
    private readonly Db _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly string? _backupDirOverride;

    public BackupService(Db db, SettingsService settings, AuditService audit, string? backupDir = null)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
        _backupDirOverride = backupDir;
    }

    public string BackupDir
    {
        get
        {
            var configured = _backupDirOverride ?? _settings.BackupDirectory;
            if (string.IsNullOrWhiteSpace(configured)) return AppPaths.BackupDir;
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppPaths.Root, configured));
        }
    }

    /// <summary>
    /// Safe online backup using the SQLite Backup API (not a raw file copy),
    /// with a WAL checkpoint first so the backup is a single consistent file.
    /// </summary>
    public string CreateBackup(string reason, long userId = 0, string username = "")
    {
        var backupDir = BackupDir;
        Directory.CreateDirectory(backupDir);
        // unique file name: multiple backups within the same second must not overwrite each other
        var target = Path.Combine(backupDir, $"POS-{DateTime.Now:yyyyMMdd-HHmmssfff}.db");
        while (File.Exists(target))
            target = Path.Combine(backupDir, $"POS-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString("N")[..4]}.db");

        using (var source = _db.Open())
        {
            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            using var dest = new SqliteConnection("Data Source=" + target + ";Pooling=False");
            dest.Open();
            source.BackupDatabase(dest);
            using var integrity = dest.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = integrity.ExecuteScalar()?.ToString();
            if (result != "ok")
                throw new InvalidOperationException("Backup gagal: integritas hasil backup tidak valid (" + result + ")");
        }
        SqliteConnection.ClearAllPools();

        _audit.Log(userId, username, AuditAction.Backup, "database", 0, $"Backup {Path.GetFileName(target)} ({reason})");
        CleanupOldBackups();
        return target;
    }

    /// <summary>Keeps only the newest N backups.</summary>
    public void CleanupOldBackups()
    {
        try
        {
            var keep = Math.Max(3, _settings.BackupKeep);
            var files = Directory.GetFiles(BackupDir, "POS-*.db")
                .OrderByDescending(f => f)
                .ToList();
            for (var i = keep; i < files.Count; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
        catch { }
    }

    /// <summary>Validates a backup file before restore.</summary>
    public void ValidateBackupFile(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("File tidak ditemukan");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[16];
        if (fs.Length < 100 || fs.Read(header, 0, 16) != 16)
            throw new InvalidOperationException("File bukan database SQLite yang valid");
        if (System.Text.Encoding.ASCII.GetString(header) != "SQLite format 3\0")
            throw new InvalidOperationException("File bukan database SQLite yang valid");

        using var check = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False");
        check.Open();
        using var cmd = check.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name IN ('database_version','products','sales','users')";
        if (Convert.ToInt32(cmd.ExecuteScalar()) < 4)
            throw new InvalidOperationException("Backup valid sebagai SQLite, tetapi schema KasirPro tidak lengkap");
    }

    /// <summary>
    /// Restores: validates target, backs up current DB first, swaps the file in, re-checks integrity.
    /// Caller must close/reload any open connections afterwards.
    /// </summary>
    public string RestoreBackup(string path, long userId = 0, string username = "")
    {
        ValidateBackupFile(path);

        // pre-restore safety backup
        var preRestore = CreateBackup("pre-restore", userId, username);

        // integrity check on source (read-only, separate connection)
        var sourceCopy = Path.Combine(Path.GetTempPath(), "kp-restore-" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(path, sourceCopy, true);
        using (var check = new SqliteConnection("Data Source=" + sourceCopy + ";Mode=ReadOnly"))
        {
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            if (result != "ok")
                throw new InvalidOperationException("File backup rusak, restore dibatalkan (" + result + ")");
        }

        var dbFile = _db.DbPath;
        foreach (var side in new[] { dbFile + "-wal", dbFile + "-shm" })
        {
            if (File.Exists(side)) { try { File.Delete(side); } catch { } }
        }

        // swap: copy over the live db after clearing pools (no open handles on our side)
        SqliteConnection.ClearAllPools();
        var tmp = dbFile + ".restore-tmp";
        File.Copy(sourceCopy, tmp, overwrite: true);
        try
        {
            File.Replace(tmp, dbFile, dbFile + ".restore-old");
        }
        catch (IOException)
        {
            // fallback: direct overwrite when File.Replace is blocked by an external handle
            File.Copy(tmp, dbFile, overwrite: true);
            try { File.Delete(tmp); } catch { }
        }
        try { if (File.Exists(dbFile + ".restore-old")) File.Delete(dbFile + ".restore-old"); } catch { }
        finally
        {
            try { if (File.Exists(sourceCopy)) File.Delete(sourceCopy); } catch { }
        }

        // verify restored file opens + has schema
        using (var verify = _db.Open())
        {
            using var cmd = verify.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check; SELECT COUNT(*) FROM sqlite_master WHERE type='table';";
            cmd.ExecuteNonQuery();
        }

        _audit.Log(userId, username, AuditAction.Restore, "database", 0, $"Restore dari {Path.GetFileName(path)}");
        return preRestore;
    }

    public List<(string FileName, long Size, DateTime Created)> ListBackups()
    {
        if (!Directory.Exists(BackupDir)) return new();
        return Directory.GetFiles(BackupDir, "POS-*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.Name)
            .Select(fi => (fi.Name, fi.Length, fi.LastWriteTime))
            .ToList();
    }

    /// <summary>Database maintenance: VACUUM + integrity check.</summary>
    public (string Integrity, long PageCount) Maintain()
    {
        return _db.With(c =>
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var integrity = cmd.ExecuteScalar()?.ToString() ?? "unknown";
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA page_count;";
                var pages = Convert.ToInt64(cmd.ExecuteScalar());
                return (integrity, pages);
            }
        });
    }
}
