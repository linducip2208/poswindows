using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Loaded permission set for the logged-in user.</summary>
public class UserContext
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// Service-layer authorization. UI may hide menus, but services ALWAYS validate here -
/// never trust the UI.
/// </summary>
public class AuthorizationService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public AuthorizationService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public UserContext Load(long userId)
    {
        var ctx = new UserContext();
        _db.With(c =>
        {
            var u = c.QueryFirstOrDefault<(string Username, string FullName, long RoleId)>(
                "SELECT username, full_name, role_id FROM users WHERE id=@id", new { id = userId });
            if (u.Username == null) return;
            ctx.UserId = userId;
            ctx.Username = u.Username;
            ctx.FullName = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName;
            ctx.Role = u.RoleId switch { 1 => "Admin", 2 => "Cashier", 3 => "Supervisor", 4 => "Owner", _ => "Cashier" };
            ctx.Permissions = c.Query<string>(
                @"SELECT rp.permission FROM role_permissions rp
                  JOIN users us ON us.role_id = rp.role_id
                  WHERE us.id = @id", new { id = userId }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        });
        // legacy fallback: Admin role bypasses permission table if matrix missing
        if (ctx.Role == "Admin" && ctx.Permissions.Count == 0)
            ctx.Permissions = PermissionSeed.Catalog.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ctx;
    }

    /// <summary>Throws UnauthorizedAccessException when permission missing. Always audited on failure.</summary>
    public void Require(UserContext? ctx, string permission, string action)
    {
        if (ctx != null && ctx.Has(permission)) return;
        _audit.Log(ctx?.UserId ?? 0, ctx?.Username ?? "?", "ACCESS_DENIED", permission, 0, action);
        throw new UnauthorizedAccessException(
            $"Tidak memiliki izin '{permission}'. Minta bantuan Supervisor/Admin untuk: {action}");
    }

    public bool Can(UserContext? ctx, string permission) => ctx != null && ctx.Has(permission);

    /// <summary>Supervisor-or-above check (role in Admin/Owner/Supervisor).</summary>
    public bool IsSupervisorOrAbove(UserContext? ctx) =>
        ctx != null && ctx.Role is "Admin" or "Owner" or "Supervisor";

    /// <summary>
    /// Validates a supervisor authorization attempt (username + password).
    /// Returns the supervisor context when valid + holds the permission; null otherwise.
    /// Recorded in approval_log by the caller via LogApproval.
    /// </summary>
    public UserContext? AuthorizeSupervisor(string username, string password, string requiredPermission)
    {
        var user = _db.With(c =>
        {
            var row = c.QueryFirstOrDefault<(long Id, string Username, string Hash, string Salt, long RoleId)>(
                @"SELECT id, username, password_hash, salt, role_id FROM users WHERE username=@u AND is_active=1",
                new { u = username.Trim() });
            if (row.Id == 0) return null;
            if (!Core.Security.PasswordHasher.Verify(password, row.Hash, row.Salt)) return null;
            var role = row.RoleId switch { 1 => "Admin", 2 => "Cashier", 3 => "Supervisor", 4 => "Owner", _ => "Cashier" };
            if (role is not ("Admin" or "Owner" or "Supervisor")) return null;
            var perms = c.Query<string>(
                @"SELECT rp.permission FROM role_permissions rp WHERE rp.role_id=@r", new { r = row.RoleId })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new UserContext
            {
                UserId = row.Id, Username = row.Username, Role = role, Permissions = perms
            };
        });
        if (user == null || !user.Has(requiredPermission)) return null;
        return user;
    }

    public void LogApproval(string action, string refType, long refId, long requestedBy,
        UserContext approver, string reason)
    {
        _db.With(c => c.Execute(@"INSERT INTO approval_log (action, reference_type, reference_id, requested_by, approved_by, reason, created_at)
            VALUES (@a, @rt, @rid, @req, @app, @reason, @t)",
            new { a = action, rt = refType, rid = refId, req = requestedBy, app = approver.UserId, reason, t = DbEx.Iso(DateTime.Now) }));
        _audit.Log(approver.UserId, approver.Username, "SUPERVISOR_APPROVAL", refType, refId,
            $"{action} disetujui untuk user #{requestedBy}: {reason}");
    }
}
