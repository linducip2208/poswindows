using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KasirPro.Licensing;

public enum LicenseVerifyStatus
{
    Valid,
    Corrupted,
    InvalidSignature,
    WrongProduct,
    WrongMachine,
    Expired
}

public class LicenseVerifyResult
{
    public LicenseVerifyStatus Status { get; set; }
    public LicensePayload? Payload { get; set; }

    public string Message => Status switch
    {
        LicenseVerifyStatus.Valid => "License valid",
        LicenseVerifyStatus.Corrupted => "Corrupted License",
        LicenseVerifyStatus.InvalidSignature => "Invalid License",
        LicenseVerifyStatus.WrongProduct => "Invalid License",
        LicenseVerifyStatus.WrongMachine => "License for Different Computer",
        LicenseVerifyStatus.Expired => "Expired License",
        _ => "Invalid License"
    };

    public static LicenseVerifyResult Fail(LicenseVerifyStatus s) => new() { Status = s };
}

/// <summary>
/// Signs license payloads with an ECDSA P-256 private key (Master Keygen only)
/// and verifies with the embedded public key (POS app only).
/// </summary>
public static class LicenseToken
{
    public const string Prefix = "KPR1";
    public const string Product = "KasirPro";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>Sign the payload and produce the compact token: KPR1.payload.signature</summary>
    public static string Sign(LicensePayload payload, ECDsa privateKey)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var signature = privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256);
        return $"{Prefix}.{Base64Url.Encode(payloadBytes)}.{Base64Url.Encode(signature)}";
    }

    /// <summary>Verify token signature + attributes against a public key.</summary>
    public static LicenseVerifyResult Verify(string token, ECDsa publicKey, string? expectedMachineId = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return LicenseVerifyResult.Fail(LicenseVerifyStatus.Corrupted);

        var parts = token.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
            return LicenseVerifyResult.Fail(LicenseVerifyStatus.Corrupted);

        byte[] payloadBytes;
        byte[] signature;
        LicensePayload? payload;
        try
        {
            payloadBytes = Base64Url.Decode(parts[1]);
            signature = Base64Url.Decode(parts[2]);
        }
        catch (FormatException)
        {
            return LicenseVerifyResult.Fail(LicenseVerifyStatus.Corrupted);
        }

        if (!publicKey.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
            return LicenseVerifyResult.Fail(LicenseVerifyStatus.InvalidSignature);

        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOpts);
        }
        catch (JsonException)
        {
            return LicenseVerifyResult.Fail(LicenseVerifyStatus.Corrupted);
        }

        if (payload == null) return LicenseVerifyResult.Fail(LicenseVerifyStatus.Corrupted);
        if (!string.Equals(payload.Product, Product, StringComparison.OrdinalIgnoreCase))
            return LicenseVerifyResult.Fail(LicenseVerifyStatus.WrongProduct);

        if (!string.IsNullOrEmpty(expectedMachineId))
        {
            var given = expectedMachineId.Trim().ToUpperInvariant();
            if (!string.Equals(payload.MachineId?.Trim().ToUpperInvariant(), given))
                return LicenseVerifyResult.Fail(LicenseVerifyStatus.WrongMachine);
        }
        else if (!MachineId.Matches(payload.MachineId))
        {
            return LicenseVerifyResult.Fail(LicenseVerifyStatus.WrongMachine);
        }

        if (!string.IsNullOrEmpty(payload.ExpiresAt))
        {
            if (!DateTime.TryParse(payload.ExpiresAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var expires))
                return LicenseVerifyResult.Fail(LicenseVerifyStatus.Corrupted);
            if (DateTime.UtcNow > expires)
                return LicenseVerifyResult.Fail(LicenseVerifyStatus.Expired);
        }

        return new LicenseVerifyResult { Status = LicenseVerifyStatus.Valid, Payload = payload };
    }
}
