using System.Diagnostics;
using System.Security.Cryptography;

namespace KasirPro.Infrastructure.Services;

public class UpdateInfo
{
    public string SourcePath { get; set; } = "";
    public string Version { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public List<UpdateFile> Files { get; set; } = new();
    public bool NewerThanInstalled { get; set; }
}

public class UpdateFile
{
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// Offline updater: an "update folder" (USB flashdisk / network share) contains
/// manifest.json + payload files. Every file is SHA-256 verified AND the whole
/// manifest is ECDSA-signed by the developer update key; the POS stores only the
/// public update key. Files are swapped on next launch by UpdateStager.
/// No internet involved. Release builds reject unsigned/tampered packages.
/// </summary>
public class UpdateService
{
    private readonly SettingsService _settings;
    private readonly string? _overrideUpdateKeyPem;

    public UpdateService(SettingsService settings, string? overrideUpdateKeyPem = null)
    {
        _settings = settings;
        _overrideUpdateKeyPem = overrideUpdateKeyPem;
    }

    /// <summary>
    /// ECDSA P-256 public key used to verify update manifests (PEM). Ship the real key
    /// before public releases; test key allows the dev loop but must not be reused for production.
    /// </summary>
    public const string UpdatePublicKeyPem =
@"-----BEGIN PUBLIC KEY-----
MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQAj8sV4TmTJLt+PGQD0SCGeVDPFmEJ
x5KtcOdeWYd7GCrNqCTkKIDLKfqLQGMtHZkAJ0DPaF6CqC1hLt4mJfI8vBXYxLFu
T+PkDNJyrR8g8CgTFSPCYduFkBbCXhIWl1INn+CVwmKzDBhG9aAwlNQrz1cVFwUW
CkPZ0tGnqV0bFqk3Wqg=
-----END PUBLIC KEY-----";

    public string SourceDir => _settings.UpdateSource;

    private System.Security.Cryptography.ECDsa UpdatePublicKey()
    {
        var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(_overrideUpdateKeyPem ?? UpdatePublicKeyPem);
        return key;
    }

    public UpdateInfo? Check()
    {
        if (string.IsNullOrWhiteSpace(SourceDir) || !Directory.Exists(SourceDir))
            throw new InvalidOperationException("Folder update belum diatur atau tidak ada.");

        var manifestPath = Path.Combine(SourceDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("manifest.json tidak ditemukan di folder update.");

        var manifest = System.Text.Json.JsonSerializer.Deserialize<Manifest>(
            File.ReadAllText(manifestPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("manifest.json tidak valid.");

        // ---- signature verification over canonical manifest body ----
        var sigPath = Path.Combine(SourceDir, "manifest.sig");
        if (!File.Exists(sigPath))
            throw new InvalidOperationException("manifest.sig tidak ditemukan - paket update tidak ditandatangani.");
        var signature = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());
        var canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { version = manifest.Version, notes = manifest.Notes, files = manifest.Files });
        using var pubKey = UpdatePublicKey();
        if (!pubKey.VerifyData(canonical, signature, System.Security.Cryptography.HashAlgorithmName.SHA256))
            throw new InvalidOperationException("Signature paket update TIDAK VALID. Jangan instal paket ini.");

        var info = new UpdateInfo
        {
            SourcePath = SourceDir,
            Version = manifest.Version,
            Notes = manifest.Notes ?? "",
            PublishedAt = File.GetLastWriteTime(manifestPath),
            NewerThanInstalled = CompareVersions(manifest.Version, InstalledVersion) > 0
        };

        foreach (var f in manifest.Files ?? new())
        {
            ValidateRelativePath(f.Path); // reject traversal/absolute/UNC
            var full = Path.Combine(SourceDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                throw new InvalidOperationException($"File update hilang: {f.Path}");
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(full);
            var hash = Convert.ToHexString(sha.ComputeHash(stream));
            if (string.IsNullOrWhiteSpace(f.Sha256) ||
                !string.Equals(hash, f.Sha256!.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Checksum tidak cocok: {f.Path}");
            info.Files.Add(new UpdateFile { RelativePath = f.Path, Size = f.Size, Sha256 = f.Sha256 });
        }
        return info;
    }

    /// <summary>Rejects path traversal, absolute paths, drive roots, UNC paths, and protected files.</summary>
    private static void ValidateRelativePath(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) throw new InvalidOperationException("Path file update kosong.");
        var normalized = rel.Replace('\\', '/');
        if (normalized.Contains("..") || Path.IsPathRooted(rel) || rel.Contains(':') ||
            normalized.StartsWith("//") || normalized.StartsWith("\\\\") ||
            normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 && normalized.Contains('/') == false && normalized.IndexOfAny(new[] {'<','>','|'}) >= 0)
            throw new InvalidOperationException($"Path update tidak valid: {rel}");
        foreach (var seg in normalized.Split('/'))
        {
            if (seg is "" or "." or "..")
                throw new InvalidOperationException($"Path update tidak valid: {rel}");
        }

        var relLow = normalized.ToLowerInvariant();
        if (relLow.StartsWith("data/") || relLow.StartsWith("backup/") || relLow.StartsWith("logs/") ||
            relLow.StartsWith("exports/") || relLow.StartsWith("images/") || relLow.StartsWith("updates/") ||
            relLow is "license.dat" or "appsettings.json")
            throw new InvalidOperationException($"File proteksi tidak boleh di-update: {rel}");
    }

    public string InstalledVersion
    {
        get
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly();
            var ver = entry?.GetName().Version;
            return ver == null ? "1.0.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
    }

    /// <summary>Stages verified files into Updates\pending; applied by UpdateStager on next launch.</summary>
    public string Apply(UpdateInfo info)
    {
        var pending = Path.Combine(AppPaths.Root, "Updates", "pending");
        if (Directory.Exists(pending)) Directory.Delete(pending, true);
        Directory.CreateDirectory(pending);

        foreach (var f in info.Files)
        {
            ValidateRelativePath(f.RelativePath); // re-validate at apply time
            var src = Path.Combine(info.SourcePath, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dst = Path.Combine(pending, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, true);
        }
        File.WriteAllText(Path.Combine(AppPaths.Root, "Updates", "pending.version"), info.Version);
        return pending;
    }

    /// <summary>Creates a SIGNED update package (developer-side): manifest + payload + ECDSA signature.</summary>
    public static string BuildPackage(string sourceAppDir, string targetDir, string version, string notes,
        System.Security.Cryptography.ECDsa? signingKey = null)
    {
        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);

        var files = new List<ManifestFile>();
        foreach (var file in Directory.GetFiles(sourceAppDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceAppDir, file).Replace('\\', '/');
            var relLow = rel.ToLowerInvariant();
            if (relLow.StartsWith("data/") || relLow.StartsWith("backup/") || relLow.StartsWith("logs/") ||
                relLow.StartsWith("exports/") || relLow.StartsWith("images/") || relLow.StartsWith("updates/") ||
                relLow is "license.dat" or "appsettings.json" or "manifest.json" or "manifest.sig")
                continue;
            var dest = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(file);
            files.Add(new ManifestFile
            {
                Path = rel,
                Size = new FileInfo(file).Length,
                Sha256 = Convert.ToHexString(sha.ComputeHash(stream))
            });
        }

        var manifest = new Manifest { Version = version, Notes = notes, Files = files };
        var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(targetDir, "manifest.json"), manifestJson);

        // sign canonical body with the developer update key (cached instance NOT disposed)
        var key = signingKey ?? LoadOrCreateDevUpdateKey();
        var canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { version = manifest.Version, notes = manifest.Notes, files = manifest.Files });
        var sig = key.SignData(canonical, System.Security.Cryptography.HashAlgorithmName.SHA256);
        File.WriteAllText(Path.Combine(targetDir, "manifest.sig"), Convert.ToBase64String(sig));
        return targetDir;
    }

    private static System.Security.Cryptography.ECDsa? _devUpdateKey;
    private static System.Security.Cryptography.ECDsa LoadOrCreateDevUpdateKey()
    {
        if (_devUpdateKey != null) return _devUpdateKey;
        var dir = Path.Combine(AppContext.BaseDirectory, "Keys");
        var privPath = Path.Combine(dir, "update-private.pem");
        var pubPath = Path.Combine(dir, "update-public.pem");
        Directory.CreateDirectory(dir);
        if (File.Exists(privPath))
        {
            var k = System.Security.Cryptography.ECDsa.Create();
            k.ImportFromPem(File.ReadAllText(privPath));
            _devUpdateKey = k;
            return k;
        }
        var key = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privPath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pubPath, key.ExportSubjectPublicKeyInfoPem());
        _devUpdateKey = key;
        return key;
    }

    private static int CompareVersions(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va.CompareTo(vb);
        return string.CompareOrdinal(a, b);
    }
}

public class Manifest
{
    public string Version { get; set; } = "";
    public string? Notes { get; set; }
    public List<ManifestFile> Files { get; set; } = new();
}

public class ManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string? Sha256 { get; set; }
}

/// <summary>Applies a staged update very early on next launch, before opening the DB.</summary>
public static class UpdateStager
{
    /// <summary>Returns update result message when an update was applied, else null.</summary>
    public static string? ApplyPendingUpdateIfAny(string rootDir)
    {
        try
        {
            var pending = Path.Combine(rootDir, "Updates", "pending");
            var flag = Path.Combine(rootDir, "Updates", "pending.version");
            if (!Directory.Exists(pending) || !File.Exists(flag)) return null;

            var version = File.ReadAllText(flag).Trim();
            var applied = 0;
            foreach (var src in Directory.GetFiles(pending, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(pending, src);
                var dst = Path.Combine(rootDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                // if target exe is locked, ask Windows to replace it on reboot via rename trick
                try
                {
                    File.Copy(src, dst, overwrite: true);
                    applied++;
                }
                catch (IOException)
                {
                    var bak = dst + ".old";
                    if (File.Exists(bak)) { try { File.Delete(bak); } catch { } }
                    File.Move(dst, bak);
                    File.Copy(src, dst, true);
                    applied++;
                }
            }
            Directory.Delete(pending, true);
            File.Delete(flag);
            return $"Update {version} diterapkan ({applied} file). Jalankan ulang aplikasi bila perlu.";
        }
        catch (Exception ex)
        {
            return "Update gagal diterapkan: " + ex.Message;
        }
    }
}
