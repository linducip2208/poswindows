using System.Text.Json;

namespace KasirPro.Core.Domain;

/// <summary>A parked (held) shopping cart.</summary>
public class Hold
{
    public long Id { get; set; }
    public string Label { get; set; } = "";
    public long UserId { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public int ItemCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public List<CartLine> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public static class HoldSerializer
{
    public static string ToJson(List<CartLine> lines) =>
        JsonSerializer.Serialize(lines);

    public static List<CartLine> FromJson(string json) =>
        string.IsNullOrWhiteSpace(json) ? new List<CartLine>()
            : JsonSerializer.Deserialize<List<CartLine>>(json) ?? new List<CartLine>();
}

/// <summary>Result of parsing a scanned barcode, including scale (timbangan) barcodes.</summary>
public class ScanResult
{
    public string Barcode { get; set; } = "";
    public decimal Qty { get; set; } = 1;
    public bool FromScale { get; set; }
}

public static class ScaleBarcode
{
    /// <summary>
    /// Parses EAN-13 style scale barcodes: PREFIX + item code (5) + weight (5) + check digit.
    /// Example: 2100001123456 -> prefix 21, item 00001, weight 12345 (kg*1000).
    /// </summary>
    public static ScanResult? TryParse(string barcode, string[] prefixes, decimal weightDivisor)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length != 13) return null;
        var prefix = barcode[..2];
        if (!prefixes.Contains(prefix)) return null;
        if (!decimal.TryParse(barcode.Substring(7, 5), out var weight)) return null;
        if (weightDivisor <= 0) weightDivisor = 1000;
        var qty = weight / weightDivisor;
        if (qty <= 0) return null;
        return new ScanResult
        {
            Barcode = barcode.Substring(2, 5).TrimStart('0'),
            Qty = qty,
            FromScale = true
        };
    }
}
