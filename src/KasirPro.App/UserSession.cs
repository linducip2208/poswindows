using KasirPro.Core.Domain;

namespace KasirPro.App;

/// <summary>Logged-in user context + app state flags.</summary>
public class UserSession
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";

    public bool IsAdmin => Role == "Admin";

    /// <summary>Set when business data changed; drives auto-backup on exit.</summary>
    public bool DataChangedSinceBackup { get; set; }
}
