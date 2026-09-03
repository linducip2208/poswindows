using System.Security.Cryptography;
using System.Text;

namespace KasirPro.Licensing;

/// <summary>
/// Produces a stable, privacy-conscious Machine ID for the current Windows machine.
/// Combines HKLM MachineGuid + system UUID (when available), normalized then SHA-256 hashed.
/// Never uses IP or MAC addresses.
/// </summary>
public static class MachineId
{
    public static string Get()
    {
        var raw = GetRawIdentity();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Format(hash);
    }

    /// <summary>Stable per-machine identity string (normalized uppercase, trimmed).</summary>
    private static string GetRawIdentity()
    {
        var machineGuid = ReadRegistry(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
        var systemUuid = ReadWmiUuid();

        var combined = string.Join("|", new[] { machineGuid, systemUuid }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant()));

        if (string.IsNullOrEmpty(combined))
            combined = "KASIRPRO-FALLBACK|" + Environment.MachineName.Trim().ToUpperInvariant();

        return combined;
    }

    private static string ReadRegistry(string path, string name)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            var value = key?.GetValue(name) as string;
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string ReadWmiUuid()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (var o in searcher.Get())
            {
                using var mo = (System.Management.ManagementObject)o;
                var uuid = mo["UUID"] as string;
                if (!string.IsNullOrWhiteSpace(uuid) &&
                    !uuid.Trim().Equals("03000200-0400-0500-0006-000700080009", StringComparison.OrdinalIgnoreCase) &&
                    !uuid.Trim().Equals("00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase))
                {
                    return uuid.Trim();
                }
            }
        }
        catch
        {
            // secondary identifier unavailable - MachineGuid alone is sufficient
        }
        return "";
    }

    /// <summary>Short formatted display: KP-XXXX-XXXX-XXXX (first 12 hex chars of the hash).</summary>
    public static string Format(byte[] hash) => $"KP-{hash[0]:X2}{hash[1]:X2}-{hash[2]:X2}{hash[3]:X2}-{hash[4]:X2}{hash[5]:X2}";

    /// <summary>Canonical full machine hash used in license payloads (64 hex chars).</summary>
    public static string FullHash()
    {
        var raw = GetRawIdentity();
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>True when a short-form display id (KP-...) or full hash belongs to this machine.</summary>
    public static bool Matches(string licenseMachineId)
    {
        if (string.IsNullOrWhiteSpace(licenseMachineId)) return false;
        var display = Get();
        var full = FullHash();
        var given = licenseMachineId.Trim().ToUpperInvariant();
        return given == display.ToUpperInvariant() || given == full;
    }
}
