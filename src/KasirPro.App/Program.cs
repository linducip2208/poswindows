using KasirPro.App.UI;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using KasirPro.Licensing;

namespace KasirPro.App;

internal static class Program
{
    public static Db Database { get; private set; } = null!;
    public static Db DbMain => Database;
    public static AppServices Services { get; private set; } = null!;
    public static LicenseActivation Licensing { get; private set; } = null!;
    public static UserSession? Session { get; set; }

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // global exception handling: user-friendly dialog, stack trace to Logs/
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => HandleCrash("Unexpected error", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            AppLogger.Instance.Error("Unhandled AppDomain exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            AppLogger.Instance.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        // CLI modes (no UI): --print-machine-id | --verify-license <token>
        if (args.Length > 0)
        {
            try
            {
                return RunCli(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        try
        {
            if (!AppPaths.TryEnsureAll(out var err))
            {
                MessageBox.Show(err, "KasirPro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            AppLogger.Instance.Info("=== KasirPro started ===");

            // apply staged offline update before opening the database
            var updateMsg = KasirPro.Infrastructure.Services.UpdateStager.ApplyPendingUpdateIfAny(AppPaths.Root);
            if (updateMsg != null)
            {
                AppLogger.Instance.Info(updateMsg);
                MessageBox.Show(updateMsg, "KasirPro Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Database = new Db(AppPaths.DatabaseFile);
            var applied = new Migrator(Database).Migrate();
            if (applied > 0) AppLogger.Instance.Info($"Database migrated ({applied} migration(s) applied)");
            Services = new AppServices(Database);

            Licensing = new LicenseActivation(AppPaths.LicenseFile);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Startup failed", ex);
            MessageBox.Show(
                "Gagal memulai aplikasi. Detail teknis tersimpan di folder Logs." +
                Environment.NewLine + Environment.NewLine + ex.Message,
                "KasirPro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // 1) license gate -> 2) first-run setup -> 3) login -> 4) main
        Strings.Init(Services.Settings.Language);

        var check = Licensing.CheckStoredLicense();
        if (!check.Activated)
        {
            var activation = new ActivationForm();
            if (activation.ShowDialog() != DialogResult.OK)
                return 0;
        }

        if (!Services.Settings.SetupDone)
        {
            var setup = new FirstRunSetupForm();
            if (setup.ShowDialog() != DialogResult.OK)
                return 0;
        }

        var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK)
            return 0;

        Application.Run(new MainForm());
        AppLogger.Instance.Info("=== KasirPro exited normally ===");
        return 0;
    }

    private static void HandleCrash(string title, Exception ex)
    {
        AppLogger.Instance.Error(title, ex);
        try
        {
            MessageBox.Show(
                "Terjadi kesalahan. Data tetap aman (transaksi dibatalkan otomatis)." +
                Environment.NewLine + "Detail teknis disimpan di folder Logs." +
                Environment.NewLine + Environment.NewLine + ex.Message,
                title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
    }

    private static int RunCli(string[] args)
    {
        AppPaths.TryEnsureAll(out _);
        switch (args[0])
        {
            case "--print-machine-id":
                Console.WriteLine(MachineId.Get());
                return 0;

            case "--diag-license":
            {
                Console.WriteLine("Machine ID        : " + MachineId.Get());
                Console.WriteLine("License file      : " + AppPaths.LicenseFile +
                    (File.Exists(AppPaths.LicenseFile) ? " (ADA)" : " (tidak ada)"));
                try
                {
                    Console.WriteLine("Embedded pubkey   : " + EmbeddedPublicKey.Fingerprint());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Embedded pubkey   : BELUM DIKONFIGURASI (" + ex.Message + ")");
                }
                var lic = new LicenseActivation(AppPaths.LicenseFile);
                var check = lic.CheckStoredLicense();
                Console.WriteLine("license.dat status: " + (check.Activated ? "VALID" : check.Message));
                Console.WriteLine();
                Console.WriteLine("Cocokkan 'Embedded pubkey' dengan fingerprint Keygen:");
                Console.WriteLine("KasirPro.Keygen.exe --fingerprint");
                return 0;
            }

            case "--verify-license":
            {
                var token = args.Length > 1 ? args[1] : Console.ReadLine() ?? "";
                var lic = new LicenseActivation(AppPaths.LicenseFile);
                var result = lic.Activate(token);
                Console.WriteLine(result.Status == LicenseVerifyStatus.Valid
                    ? "ACTIVATED"
                    : "FAILED: " + result.Message);
                return result.Status == LicenseVerifyStatus.Valid ? 0 : 1;
            }

            case "--seed-demo":
            {
                Database = new Db(AppPaths.DatabaseFile);
                new Migrator(Database).Migrate();
                Services = new AppServices(Database);
                var user = EnsureDemoAdmin();
                Services.Seeder.ResetAndSeed(user.Id, user.Username);
                Console.WriteLine("Demo data seeded.");
                return 0;
            }

            case "--reset-demo":
            {
                Database = new Db(AppPaths.DatabaseFile);
                new Migrator(Database).Migrate();
                Services = new AppServices(Database);
                var user = EnsureDemoAdmin();
                Services.Seeder.ResetAndSeed(user.Id, user.Username);
                Console.WriteLine("Demo database reset.");
                return 0;
            }

            default:
                Console.WriteLine("Unknown argument: " + args[0]);
                Console.WriteLine("Usage: KasirPro.exe [--print-machine-id | --verify-license <token> | --seed-demo | --reset-demo]");
                return 2;
        }
    }

    private static KasirPro.Core.Domain.User EnsureDemoAdmin()
    {
        var existing = Services.Users.GetByUsername("admin");
        if (existing != null) return existing;
        return Services.Users.CreateAdmin("admin", "admin123", "Administrator");
    }
}
