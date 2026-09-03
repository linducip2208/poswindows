namespace KasirPro.Infrastructure;

/// <summary>
/// All runtime paths are relative to the executable folder (portable app).
/// Moving KasirPro/ to another drive automatically relocates Data/, Backup/, etc.
/// </summary>
public static class AppPaths
{
    public static string Root => AppContext.BaseDirectory;
    public static string DataDir => Path.Combine(Root, "Data");
    public static string BackupDir => Path.Combine(Root, "Backup");
    public static string LogsDir => Path.Combine(Root, "Logs");
    public static string ExportsDir => Path.Combine(Root, "Exports");
    public static string ImagesDir => Path.Combine(Root, "Images");
    public static string ImagesProductsDir => Path.Combine(ImagesDir, "Products");
    public static string ReportsDir => Path.Combine(ExportsDir, "Reports");

    public static string DatabaseFile => Path.Combine(DataDir, "pos.db");
    public static string LicenseFile => Path.Combine(Root, "license.dat");
    public static string SettingsFile => Path.Combine(Root, "appsettings.json");

    public static void EnsureAll()
    {
        string[] dirs = { DataDir, BackupDir, LogsDir, ExportsDir, ImagesDir, ImagesProductsDir, ReportsDir };
        foreach (var d in dirs) Directory.CreateDirectory(d);
    }

    /// <summary>Checks the app folder is writable before touching the database.</summary>
    public static bool TryEnsureAll(out string error)
    {
        error = "";
        try
        {
            EnsureAll();
            var probe = Path.Combine(DataDir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            error = "Folder aplikasi tidak dapat ditulis: " + Root + Environment.NewLine +
                    "Jalankan aplikasi dari folder yang dapat ditulis (jangan dari Program Files tanpa hak admin)." +
                    Environment.NewLine + "Detail: " + ex.Message;
            return false;
        }
    }

    public static string LogFile(DateTime date) =>
        Path.Combine(LogsDir, $"kasirpro-{date:yyyy-MM-dd}.log");
}
