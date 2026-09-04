namespace KasirPro.Keygen;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // CLI mode: keygen.exe --update-keys -> create the ECDSA key pair used to sign offline update packages
        if (args.Length > 0 && args[0] == "--update-keys")
        {
            try
            {
                var privPath = Path.Combine(AppContext.BaseDirectory, "Keys", "update-private.pem");
                var pubPath = Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem");
                if (File.Exists(privPath))
                {
                    Console.WriteLine("Update key pair sudah ada: " + privPath);
                    Console.WriteLine("Public key (embed ke UpdateService.UpdatePublicKeyPem):");
                    Console.WriteLine(File.ReadAllText(pubPath));
                    return 0;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(privPath)!);
                using var key = System.Security.Cryptography.ECDsa.Create(
                    System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
                File.WriteAllText(privPath, key.ExportPkcs8PrivateKeyPem());
                File.WriteAllText(pubPath, key.ExportSubjectPublicKeyInfoPem());
                Console.WriteLine("Update key pair dibuat:");
                Console.WriteLine("  Private : " + privPath + "  (JANGAN disebar ke pelanggan)");
                Console.WriteLine("  Public  : " + pubPath);
                Console.WriteLine();
                Console.WriteLine("Salin isi public key ke konstanta");
                Console.WriteLine("KasirPro.Infrastructure.Services.UpdateService.UpdatePublicKeyPem lalu build ulang KasirPro.App.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        // CLI mode: keygen.exe --fingerprint -> show master public key fingerprint
        if (args.Length > 0 && args[0] == "--fingerprint")
        {
            try
            {
                var keys = new MasterKeyStore();
                if (!keys.Exists) throw new InvalidOperationException("Master key belum ada di folder ini. Jalankan --init atau salin folder Keys dari keygen utama.");
                Console.WriteLine("Master pubkey fingerprint : " + KasirPro.Licensing.EmbeddedPublicKey.FingerprintOf(keys.LoadPublic()));
                Console.WriteLine("Public key file           : " + keys.PublicKeyPath);
                Console.WriteLine("KasirPro.exe memakai      : KasirPro.exe --diag-license");
                Console.WriteLine();
                Console.WriteLine("Kedua fingerprint HARUS sama. Kalau beda:");
                Console.WriteLine("1) Salin Keys\\ dari keygen utama, ATAU");
                Console.WriteLine("2) Export ulang public key lalu embed ke KasirPro.Licensing dan build ulang.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        // CLI mode: keygen.exe --init <passphrase>   -> create master key pair
        if (args.Length > 0 && args[0] == "--init")
        {
            try
            {
                var pass = args.Length > 1 ? args[1] : throw new InvalidOperationException("Usage: KasirPro.Keygen.exe --init <passphrase>");
                var keys = new MasterKeyStore();
                if (keys.Exists)
                {
                    Console.WriteLine("Master key already exists: " + keys.PrivateKeyPath);
                }
                else
                {
                    keys.CreateNew(pass);
                    Console.WriteLine("Master key created:");
                    Console.WriteLine("  Private : " + keys.PrivateKeyPath);
                    Console.WriteLine("  Public  : " + keys.PublicKeyPath);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        // CLI mode: keygen.exe --generate <customer> <machineId> [lifetime|annual|trial] <passphrase>
        if (args.Length > 0 && args[0] == "--generate")
        {
            try
            {
                if (args.Length < 5) throw new InvalidOperationException(
                    "Usage: KasirPro.Keygen.exe --generate <customer> <machineId> <lifetime|annual|trial> <passphrase>");
                var keys = new MasterKeyStore();
                var history = new KeygenDb();
                var type = args[3].ToLowerInvariant();
                DateTime? expires = type == "annual" ? DateTime.UtcNow.AddYears(1) : type == "trial" ? DateTime.UtcNow.AddDays(14) : null;
                var gen = new LicenseGenerator(keys);
                var (token, payload) = gen.Generate(args[1], args[2],
                    type == "annual" ? "Annual" : type == "trial" ? "Trial" : "Lifetime", expires, history, args[4]);
                Console.WriteLine("LICENSE ID : " + payload.LicenseId);
                Console.WriteLine("CUSTOMER   : " + payload.Customer);
                Console.WriteLine("MACHINE    : " + payload.MachineId);
                Console.WriteLine();
                Console.WriteLine(token);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 2;
            }
        }

        Application.Run(new MainForm());
        return 0;
    }
}
