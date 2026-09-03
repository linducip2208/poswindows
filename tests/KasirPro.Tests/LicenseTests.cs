using System.Security.Cryptography;
using KasirPro.Licensing;
using Xunit;

namespace KasirPro.Tests;

public class LicenseTests
{
    private static (ECDsa Priv, ECDsa Pub) MakeKeys()
    {
        var priv = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ECDsa.Create();
        pub.ImportParameters(priv.ExportParameters(false));
        return (priv, pub);
    }

    private static LicensePayload SamplePayload(string machineId = "KP-A82F-991B-72E1") => new()
    {
        Version = 1,
        Product = "KasirPro",
        Customer = "Toko Makmur",
        MachineId = machineId,
        LicenseType = "Lifetime",
        LicenseId = "LIC-TEST0001",
        IssuedAt = DateTime.UtcNow.ToString("o"),
        ExpiresAt = null
    };

    [Fact]
    public void ValidSignature_Passes()
    {
        var (priv, pub) = MakeKeys();
        var token = LicenseToken.Sign(SamplePayload(), priv);
        var result = LicenseToken.Verify(token, pub, "KP-A82F-991B-72E1");
        Assert.Equal(LicenseVerifyStatus.Valid, result.Status);
        Assert.Equal("Toko Makmur", result.Payload!.Customer);
    }

    [Fact]
    public void InvalidSignature_Rejected()
    {
        var (priv, pub) = MakeKeys();
        var token = LicenseToken.Sign(SamplePayload(), priv);
        var (priv2, _) = MakeKeys();
        // re-sign same payload with a different key -> signature mismatch
        var forged = LicenseToken.Sign(SamplePayload(), priv2);
        // token body identical structure but signed by other key; verify against original pub
        var result = LicenseToken.Verify(forged, pub, "KP-A82F-991B-72E1");
        Assert.Equal(LicenseVerifyStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void TamperedPayload_Rejected()
    {
        var (priv, pub) = MakeKeys();
        var token = LicenseToken.Sign(SamplePayload(), priv);
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(KasirPro.Licensing.Base64Url.Decode(parts[1]));
        payload = payload.Replace("Toko Makmur", "Toko Lain");
        var tampered = parts[0] + "." + KasirPro.Licensing.Base64Url.Encode(System.Text.Encoding.UTF8.GetBytes(payload)) + "." + parts[2];
        var result = LicenseToken.Verify(tampered, pub, "KP-A82F-991B-72E1");
        Assert.NotEqual(LicenseVerifyStatus.Valid, result.Status);
    }

    [Fact]
    public void WrongMachine_Rejected()
    {
        var (priv, pub) = MakeKeys();
        var token = LicenseToken.Sign(SamplePayload("KP-0000-0000-0001"), priv);
        var result = LicenseToken.Verify(token, pub, "KP-A82F-991B-72E1");
        Assert.Equal(LicenseVerifyStatus.WrongMachine, result.Status);
    }

    [Fact]
    public void CorruptedPayload_Rejected()
    {
        var (_, pub) = MakeKeys();
        Assert.Equal(LicenseVerifyStatus.Corrupted, LicenseToken.Verify("", pub).Status);
        Assert.Equal(LicenseVerifyStatus.Corrupted, LicenseToken.Verify("garbage", pub).Status);
        Assert.Equal(LicenseVerifyStatus.Corrupted, LicenseToken.Verify("KPR1.!!!.???", pub).Status);
        Assert.Equal(LicenseVerifyStatus.Corrupted, LicenseToken.Verify("XXX.a.b", pub).Status);
    }

    [Fact]
    public void ExpiredLicense_Rejected()
    {
        var (priv, pub) = MakeKeys();
        var payload = SamplePayload();
        payload.ExpiresAt = DateTime.UtcNow.AddDays(-1).ToString("o");
        var token = LicenseToken.Sign(payload, priv);
        var result = LicenseToken.Verify(token, pub, payload.MachineId);
        Assert.Equal(LicenseVerifyStatus.Expired, result.Status);
    }

    [Fact]
    public void WrongProduct_Rejected()
    {
        var (priv, pub) = MakeKeys();
        var payload = SamplePayload();
        payload.Product = "KasirUltra";
        var token = LicenseToken.Sign(payload, priv);
        var result = LicenseToken.Verify(token, pub, payload.MachineId);
        Assert.Equal(LicenseVerifyStatus.WrongProduct, result.Status);
    }

    [Fact]
    public void ActivationStore_RoundTrip()
    {
        var (priv, pub) = MakeKeys();
        var pubXml = System.Text.Json.JsonSerializer.Serialize("x"); // unused
        var dir = Path.Combine(Path.GetTempPath(), "kp-lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var licenseFile = Path.Combine(dir, "license.dat");

        var pem = new System.Text.StringBuilder();
        pem.AppendLine("-----BEGIN PUBLIC KEY-----");
        var b64 = Convert.ToBase64String(priv.ExportSubjectPublicKeyInfo());
        for (var i = 0; i < b64.Length; i += 64) pem.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
        pem.AppendLine("-----END PUBLIC KEY-----");

        var activation = new LicenseActivation(licenseFile, pem.ToString());
        Assert.False(activation.CheckStoredLicense().Activated);

        var token = LicenseToken.Sign(SamplePayload(MachineId.Get()), priv);
        var activate = activation.Activate(token);
        Assert.Equal(LicenseVerifyStatus.Valid, activate.Status);

        // stored license re-verified at every startup
        var check = activation.CheckStoredLicense();
        Assert.True(check.Activated);
        Assert.Equal("Toko Makmur", check.Payload!.Customer);

        // tamper with the signed token -> signature verification fails
        var envelope = System.Text.Json.JsonSerializer.Deserialize<KasirPro.Licensing.LicenseEnvelope>(File.ReadAllText(licenseFile));
        var tamperedToken = envelope!.Token;
        var mid = tamperedToken.Length / 2;
        var brokenChar = tamperedToken[mid] == 'A' ? 'B' : 'A';
        tamperedToken = tamperedToken.Substring(0, mid) + brokenChar + tamperedToken[(mid + 1)..];
        File.WriteAllText(licenseFile, System.Text.Json.JsonSerializer.Serialize(
            new KasirPro.Licensing.LicenseEnvelope { Token = tamperedToken }));
        Assert.False(activation.CheckStoredLicense().Activated);

        // delete -> activation screen required again
        File.Delete(licenseFile);
        Assert.False(activation.CheckStoredLicense().Activated);
    }
}
