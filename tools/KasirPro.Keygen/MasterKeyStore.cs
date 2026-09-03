using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using KasirPro.Licensing;

namespace KasirPro.Keygen;

/// <summary>
/// Master key store: ECDSA P-256 pair. Private key is encrypted with Windows DPAPI
/// (CurrentUser scope) before hitting disk; a passphrase layer adds a second factor.
/// </summary>
public class MasterKeyStore
{
    private readonly string _keyDir;
    private readonly string _privatePath;
    private readonly string _publicPath;

    public MasterKeyStore(string? keyDir = null)
    {
        _keyDir = keyDir ?? Path.Combine(AppContext.BaseDirectory, "Keys");
        _privatePath = Path.Combine(_keyDir, "private.key");
        _publicPath = Path.Combine(_keyDir, "public.key");
    }

    public string PrivateKeyPath => _privatePath;
    public string PublicKeyPath => _publicPath;
    public bool Exists => File.Exists(_privatePath) && File.Exists(_publicPath);

    /// <summary>Creates a new master key pair. Private key = DPAPI(passphrase, PBKDF2-derived) encrypted.</summary>
    public void CreateNew(string passphrase)
    {
        Directory.CreateDirectory(_keyDir);
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateXml = ecdsa.ExportPkcs8PrivateKey();
        var publicPem = PemEncode("PUBLIC KEY", ecdsa.ExportSubjectPublicKeyInfo());

        // derive an AES key from passphrase, then protect with DPAPI (double layer)
        var aesKey = Rfc2898DeriveBytes.Pbkdf2(passphrase, Encoding.UTF8.GetBytes("KasirPro-Keygen-Salt"),
            150000, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.GenerateIV();
        var cipher = aes.CreateEncryptor().TransformFinalBlock(privateXml, 0, privateXml.Length);

        var dpapiPayload = ProtectedData.Protect(
            Concat(aes.IV, cipher), Entropy(), DataProtectionScope.CurrentUser);

        File.WriteAllBytes(_privatePath, dpapiPayload);
        File.WriteAllText(_publicPath, publicPem);
        ecdsa.Dispose();
    }

    private static string PemEncode(string label, byte[] der)
    {
        var b64 = Convert.ToBase64String(der);
        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN " + label + "-----");
        for (var i = 0; i < b64.Length; i += 64)
            sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
        sb.AppendLine("-----END " + label + "-----");
        return sb.ToString();
    }

    /// <summary>Loads the private key (requires the correct passphrase).</summary>
    public ECDsa LoadPrivate(string passphrase)
    {
        if (!Exists) throw new InvalidOperationException("Master key belum ada. Buat master key terlebih dahulu.");
        var dpapiPayload = File.ReadAllBytes(_privatePath);
        var plain = ProtectedData.Unprotect(dpapiPayload, Entropy(), DataProtectionScope.CurrentUser);
        var iv = plain[..16];
        var cipher = plain[16..];

        var aesKey = Rfc2898DeriveBytes.Pbkdf2(passphrase, Encoding.UTF8.GetBytes("KasirPro-Keygen-Salt"),
            150000, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        byte[] pkcs8;
        try
        {
            pkcs8 = aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Passphrase salah.");
        }

        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
        return ecdsa;
    }

    public string LoadPublicPem() => File.ReadAllText(_publicPath);

    public ECDsa LoadPublic()
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(LoadPublicPem());
        return ecdsa;
    }

    /// <summary>Export public key PEM (safe to embed into the POS app).</summary>
    public void ExportPublic(string targetPath) => File.WriteAllText(targetPath, LoadPublicPem());

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Array.Copy(a, 0, r, 0, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static byte[] Entropy() =>
        SHA256.HashData(Encoding.UTF8.GetBytes("KasirPro-MasterKey-DPAPI-Entropy-V1"));
}

/// <summary>Keygen's own SQLite history, totally separate from the POS database.</summary>
public class KeygenDb
{
    private readonly string _dbPath;

    public KeygenDb(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "keygen.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var conn = Open();
        conn.ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS issued_licenses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            license_id TEXT NOT NULL,
            customer TEXT NOT NULL,
            machine_id TEXT NOT NULL,
            license_type TEXT NOT NULL,
            issued_at TEXT NOT NULL,
            expires_at TEXT,
            status TEXT NOT NULL DEFAULT 'ISSUED',
            created_at TEXT NOT NULL);");
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        conn.ExecuteNonQuery("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        return conn;
    }

    public void Record(LicensePayload payload)
    {
        using var conn = Open();
        conn.ExecuteNonQuery(@"INSERT INTO issued_licenses
            (license_id, customer, machine_id, license_type, issued_at, expires_at, status, created_at)
            VALUES (@lid, @cust, @mid, @type, @issued, @expires, 'ISSUED', @created)",
            ("@lid", payload.LicenseId), ("@cust", payload.Customer), ("@mid", payload.MachineId),
            ("@type", payload.LicenseType), ("@issued", payload.IssuedAt),
            ("@expires", (object?)payload.ExpiresAt ?? DBNull.Value),
            ("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    public void MarkRevoked(string licenseId)
    {
        using var conn = Open();
        conn.ExecuteNonQuery("UPDATE issued_licenses SET status='REVOKED' WHERE license_id=@lid",
            ("@lid", licenseId));
    }

    public List<(string LicenseId, string Customer, string MachineId, string Type, string Issued, string? Expires, string Status)> History(int limit = 200)
    {
        using var conn = Open();
        var list = new List<(string, string, string, string, string, string?, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT license_id, customer, machine_id, license_type, issued_at, expires_at, status FROM issued_licenses ORDER BY id DESC LIMIT @l";
        var p = cmd.CreateParameter(); p.ParameterName = "@l"; p.Value = limit; cmd.Parameters.Add(p);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6)));
        return list;
    }
}

public static class SqliteExtensions
{
    public static void ExecuteNonQuery(this SqliteConnection conn, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        cmd.ExecuteNonQuery();
    }
}

public class LicenseGenerator
{
    private readonly MasterKeyStore _keys;

    public LicenseGenerator(MasterKeyStore keys) { _keys = keys; }

    /// <summary>Generates a signed license token + records it in the keygen history.</summary>
    public (string Token, LicensePayload Payload) Generate(string customer, string machineId,
        string licenseType, DateTime? expiresAt, KeygenDb history, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(customer)) throw new InvalidOperationException("Customer name wajib diisi");
        if (string.IsNullOrWhiteSpace(machineId)) throw new InvalidOperationException("Machine ID wajib diisi");
        var mid = machineId.Trim().ToUpperInvariant();
        if (mid.Length != 17 && mid.Length != 64)
            throw new InvalidOperationException("Machine ID tidak valid (harus KP-XXXX-XXXX-XXXX atau full hash 64 karakter)");

        var payload = new LicensePayload
        {
            Version = 1,
            Product = LicenseToken.Product,
            Customer = customer.Trim(),
            MachineId = mid,
            LicenseType = licenseType,
            LicenseId = "LIC-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            IssuedAt = DateTime.UtcNow.ToString("o"),
            ExpiresAt = expiresAt?.ToString("o")
        };

        using var privateKey = _keys.LoadPrivate(passphrase);
        var token = LicenseToken.Sign(payload, privateKey);
        history.Record(payload);
        return (token, payload);
    }
}
