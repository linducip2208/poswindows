using System.Text.Json.Serialization;

namespace KasirPro.Licensing;

public class LicensePayload
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("product")]
    public string Product { get; set; } = "KasirPro";

    [JsonPropertyName("customer")]
    public string Customer { get; set; } = "";

    [JsonPropertyName("machineId")]
    public string MachineId { get; set; } = "";

    [JsonPropertyName("licenseType")]
    public string LicenseType { get; set; } = "Lifetime";

    [JsonPropertyName("licenseId")]
    public string LicenseId { get; set; } = "";

    /// <summary>ISO-8601 UTC.</summary>
    [JsonPropertyName("issuedAt")]
    public string IssuedAt { get; set; } = "";

    /// <summary>ISO-8601 UTC or null for lifetime.</summary>
    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }
}
