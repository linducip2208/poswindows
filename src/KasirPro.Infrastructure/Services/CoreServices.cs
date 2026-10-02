using Dapper;
using KasirPro.Core.Domain;
using KasirPro.Core.Security;

namespace KasirPro.Infrastructure.Services;

public class UserService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public UserService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public bool AnyUser() =>
        _db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM users WHERE is_active=1")) > 0;

    public List<User> GetAll() =>
        _db.With(c => c.Query<User>(@"SELECT id AS Id, username AS Username, full_name AS FullName,
                role_id AS Role, is_active AS IsActive, created_at AS CreatedAt
                FROM users ORDER BY username").ToList()).Select(MapRole).ToList();

    private static User MapRole(User u)
    {
        u.Role = u.Role switch { "1" => "Admin", "2" => "Cashier", _ => u.Role };
        return u;
    }

    public User? GetByUsername(string username) =>
        _db.With(c =>
        {
            var row = c.QueryFirstOrDefault<(long Id, string Username, string PasswordHash, string Salt, string FullName, long RoleId, long IsActive)>(
                @"SELECT id, username, password_hash, salt, full_name, role_id, is_active
                  FROM users WHERE username = @u", new { u = username.Trim() });
            if (row.Id == 0) return null;
            return new User
            {
                Id = row.Id,
                Username = row.Username,
                PasswordHash = row.PasswordHash,
                Salt = row.Salt,
                FullName = row.FullName,
                Role = row.RoleId == 1 ? "Admin" : "Cashier",
                IsActive = row.IsActive == 1
            };
        });

    /// <summary>Creates the first admin user. Password stored as PBKDF2 hash + salt.</summary>
    public User CreateAdmin(string username, string password, string fullName)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username wajib diisi");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            throw new InvalidOperationException("Password minimal 4 karakter");
        var (hash, salt) = PasswordHasher.Hash(password);
        var now = DateTime.Now;
        var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO users (username, password_hash, salt, full_name, role_id, is_active, created_at, updated_at)
                VALUES (@u, @h, @s, @f, 1, 1, @c, @u2); SELECT last_insert_rowid();",
            new { u = username.Trim(), h = hash, s = salt, f = fullName, c = DbEx.Iso(now), u2 = DbEx.Iso(now) }));
        _audit.Log(0, "SYSTEM", AuditAction.UserChange, "user", id, $"Admin '{username}' dibuat");
        return new User { Id = id, Username = username.Trim(), FullName = fullName, Role = "Admin", IsActive = true };
    }

    public User CreateCashier(string username, string password, string fullName)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username wajib diisi");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            throw new InvalidOperationException("Password minimal 4 karakter");
        var (hash, salt) = PasswordHasher.Hash(password);
        var now = DateTime.Now;
        var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO users (username, password_hash, salt, full_name, role_id, is_active, created_at, updated_at)
                VALUES (@u, @h, @s, @f, 2, 1, @c, @u2); SELECT last_insert_rowid();",
            new { u = username.Trim(), h = hash, s = salt, f = fullName, c = DbEx.Iso(now), u2 = DbEx.Iso(now) }));
        _audit.Log(0, "SYSTEM", AuditAction.UserChange, "user", id, $"Cashier '{username}' dibuat");
        return new User { Id = id, Username = username.Trim(), FullName = fullName, Role = "Cashier", IsActive = true };
    }

    public void SetPassword(long userId, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new InvalidOperationException("Password minimal 4 karakter");
        var (hash, salt) = PasswordHasher.Hash(newPassword);
        _db.With(c => c.Execute(@"UPDATE users SET password_hash=@h, salt=@s, updated_at=@n WHERE id=@id",
            new { h = hash, s = salt, n = DbEx.Iso(DateTime.Now), id = userId }));
        _audit.Log(0, "SYSTEM", AuditAction.UserChange, "user", userId, "Password diubah");
    }

    public void SetActive(long userId, bool active)
    {
        _db.With(c => c.Execute("UPDATE users SET is_active=@a, updated_at=@n WHERE id=@id",
            new { a = active ? 1 : 0, n = DbEx.Iso(DateTime.Now), id = userId }));
        _audit.Log(0, "SYSTEM", AuditAction.UserChange, "user", userId, active ? "User diaktifkan" : "User dinonaktifkan");
    }

    public User? Login(string username, string password)
    {
        var user = GetByUsername(username);
        if (user == null || !user.IsActive) return null;
        if (!PasswordHasher.Verify(password, user.PasswordHash, user.Salt)) return null;
        _audit.Log(user.Id, user.Username, AuditAction.Login, "user", user.Id, "Login berhasil");
        return user;
    }
}

public class SettingsService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public SettingsService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public string Get(string key, string def = "") =>
        _db.With(c => c.ExecuteScalar<string>("SELECT value FROM settings WHERE key=@k", new { k = key })) ?? def;

    public void Set(string key, string value, long userId = 0, string username = "")
    {
        _db.With(c => c.Execute(@"INSERT INTO settings (key, value, updated_at) VALUES (@k, @v, @n)
            ON CONFLICT(key) DO UPDATE SET value=@v, updated_at=@n",
            new { k = key, v = value, n = DbEx.Iso(DateTime.Now) }));
        _audit.Log(userId, username, AuditAction.SettingChange, "setting", 0, $"{key} = {value}");
    }

    public string StoreName => Get("store_name", "KasirPro Store");
    public string StoreAddress => Get("store_address", "");
    public string StorePhone => Get("store_phone", "");
    public string ReceiptFooter => Get("receipt_footer", "Terima kasih");
    public string ReceiptPaper => Get("receipt_paper", "80");
    public string PrinterName => Get("printer_name", "");
    public bool SetupDone => Get("setup_done", "0") == "1";
    public bool AutoBackup => Get("auto_backup", "1") == "1";
    public int BackupKeep => int.TryParse(Get("backup_keep", "30"), out var k) ? k : 30;
    public string BackupDirectory => Get("backup_dir", "");
    public string InvoicePrefix => Get("invoice_prefix", "INV");
    public bool AllowCredit => Get("allow_credit", "0") == "1";
    public string Language => Get("language", "id");
    public string UpdateSource => Get("update_source", "");
}

public class AuditService
{
    private readonly Db _db;
    public AuditService(Db db) { _db = db; }

    public void Log(long userId, string username, string action, string entity, long entityId, string description)
    {
        try
        {
            _db.With(c => c.Execute(@"INSERT INTO audit_logs (user_id, username, action, entity, entity_id, description, created_at)
                VALUES (@uid, @un, @a, @e, @eid, @d, @t)",
                new { uid = userId, un = username, a = action, e = entity, eid = entityId, d = description, t = DbEx.Iso(DateTime.Now) }));
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("audit log failed", ex);
        }
    }

    /// <summary>Audit entry inside the caller's transaction (atomic with the business operation).</summary>
    public void InTx(System.Data.IDbConnection c, long userId, string username, string action,
        string entity, long entityId, string description)
    {
        try
        {
            c.Execute(@"INSERT INTO audit_logs (user_id, username, action, entity, entity_id, description, created_at)
                VALUES (@uid, @un, @a, @e, @eid, @d, @t)",
                new { uid = userId, un = username, a = action, e = entity, eid = entityId, d = description, t = DbEx.Iso(DateTime.Now) });
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("audit log (tx) failed", ex);
        }
    }

    public List<AuditLog> Recent(int limit = 300) =>
        _db.With(c => c.Query<AuditLog>(@"SELECT id AS Id, user_id AS UserId, username AS Username, action AS Action,
            entity AS Entity, entity_id AS EntityId, description AS Description, created_at AS CreatedAt
            FROM audit_logs ORDER BY id DESC LIMIT @l", new { l = limit }).ToList());
}
