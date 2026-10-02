using System.Text;

namespace KasirPro.Core.Domain;

/// <summary>
/// Robust CSV parser: handles quoted fields, escaped quotes (""), embedded
/// separators and newlines. No naive Split().
/// </summary>
public static class CsvParser
{
    public static List<string[]> Parse(string content, params char[] separators)
    {
        if (separators.Length == 0) separators = new[] { ';', '\t', ',' };
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var sep = separators[0];

        // auto-detect separator from the first line when ambiguous
        var firstLine = content.Split('\n').FirstOrDefault() ?? "";
        foreach (var s in separators)
            if (firstLine.Count(c => c == s) > firstLine.Count(c => c == sep)) sep = s;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == sep)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r')
            {
                // skip
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0) || rows.Count > 0) rows.Add(row.ToArray());
                row.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    /// <summary>Serializes rows back to CSV with quoting when needed.</summary>
    public static string Write(IEnumerable<(string Label, string? Value)[]> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var cells = row.Select(c => Escape(c.Value ?? ""));
            sb.AppendLine(string.Join(";", cells));
        }
        return sb.ToString();
    }

    public static string Escape(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}

/// <summary>Validated product import row (from CSV/Excel-like sources).</summary>
public class ImportRow
{
    public int LineNo { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Category { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal Stock { get; set; }
    public decimal MinStock { get; set; }
    public string Supplier { get; set; } = "";
    public string Location { get; set; } = "";
    public List<string> Errors { get; } = new();
    public bool Valid => Errors.Count == 0;
}

public static class ProductImportValidator
{
    public static List<ImportRow> Validate(IEnumerable<string[]> rawRows, out List<string> headerWarnings)
    {
        headerWarnings = new List<string>();
        var result = new List<ImportRow>();
        var lineNo = 1;
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cells in rawRows)
        {
            lineNo++;
            if (cells.All(c => string.IsNullOrWhiteSpace(c))) continue;
            var row = new ImportRow { LineNo = lineNo };

            string Get(int i) => cells.Length > i ? cells[i].Trim() : "";
            row.Code = Get(0);
            row.Name = Get(1);
            row.Barcode = Get(2);
            row.Category = Get(3);
            row.Brand = Get(4);
            row.Unit = Get(5);
            row.PurchasePrice = ParseNum(Get(6), row.Errors, "harga beli");
            row.SellingPrice = ParseNum(Get(7), row.Errors, "harga jual");
            row.Stock = ParseNum(Get(8), row.Errors, "stok");
            row.MinStock = ParseNum(Get(9), row.Errors, "min stok");
            row.Supplier = Get(10);
            row.Location = Get(11);

            if (string.IsNullOrWhiteSpace(row.Name)) row.Errors.Add("nama produk kosong");

            if (!string.IsNullOrWhiteSpace(row.Code))
            {
                if (!seenCodes.Add(row.Code)) row.Errors.Add($"kode duplikat dalam file: {row.Code}");
            }
            if (!string.IsNullOrWhiteSpace(row.Barcode))
            {
                if (row.Barcode.All(char.IsDigit) && row.Barcode.Length is 8 or 12 or 13 or 14
                    && !BarcodeMath.IsValidGtin(row.Barcode))
                    headerWarnings.Add($"baris {lineNo}: check digit EAN '{row.Barcode}' kemungkinan salah");
                if (!seenBarcodes.Add(row.Barcode)) row.Errors.Add($"barcode duplikat dalam file: {row.Barcode}");
            }
            if (row.SellingPrice > 0 && row.PurchasePrice > row.SellingPrice)
                row.Errors.Add("harga beli > harga jual");
            result.Add(row);
        }
        return result;
    }

    private static decimal ParseNum(string text, List<string> errors, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (decimal.TryParse(text.Replace(".", "").Replace(",", "."), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        errors.Add($"'{label}' bukan angka: {text}");
        return 0;
    }
}
