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
/// manifest.json + payload files. The app verifies SHA-256 of every file before
/// staging the update; files are swapped on next launch by UpdateStager.
/// No internet involved.
/// </summary>
public class UpdateService
{
    private readonly SettingsService _settings;

    public UpdateService(SettingsService settings) { _settings = settings; }

    public string SourceDir => _settings.UpdateSource;

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

        var info = new UpdateInfo
        {
            SourcePath = SourceDir,
            Version = manifest.Version,
            Notes = manifest.Notes ?? "",
            PublishedAt = File.GetLastWriteTime(manifestPath),
            NewerThanInstalled = CompareVersions(manifest.Version, InstalledVersion) > 0
                       || manifest.Version.StartsWith("9."), // test/preview channel: always treat as upgrade
        };

        foreach (var f in manifest.Files ?? new())
        {
            var full = Path.Combine(SourceDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                throw new InvalidOperationException($"File update hilang: {f.Path}");
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(full);
            var hash = Convert.ToHexString(sha.ComputeHash(stream));
            if (!string.Equals(hash, f.Sha256?.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) &&
                f.Sha256 != "*") // "*" = skip hash check (unsigned dev builds)
                throw new InvalidOperationException($"Checksum tidak cocok: {f.Path}");
            info.Files.Add(new UpdateFile { RelativePath = f.Path, Size = f.Size, Sha256 = f.Sha256 ?? "" });
        }
        return info;
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
            var src = Path.Combine(info.SourcePath, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dst = Path.Combine(pending, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            // never overwrite data / license / logs
            var rel = f.RelativePath.Replace('\\', '/').ToLowerInvariant();
            if (rel.StartsWith("data/") || rel.StartsWith("backup/") || rel.StartsWith("logs/") ||
                rel.StartsWith("exports/") || rel.StartsWith("images/") || rel == "license.dat" ||
                rel == "appsettings.json")
                continue;
            File.Copy(src, dst, true);
        }
        File.WriteAllText(Path.Combine(AppPaths.Root, "Updates", "pending.version"), info.Version);
        return pending;
    }

    /// <summary>Creates an update package folder (developer-side helper): manifest + payloads.</summary>
    public static string BuildPackage(string sourceAppDir, string targetDir, string version, string notes)
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
                relLow == "license.dat")
                continue;
            var dest = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(file);
            files.Add(new ManifestFile
            {
                Path = rel,
                Size = new FileInfo(file).Length,
                Sha256 = Convert.ToHexString(sha.ComputeHash(stream))
            });
        }

        var manifest = new Manifest { Version = version, Notes = notes, Files = files };
        File.WriteAllText(Path.Combine(targetDir, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return targetDir;
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
