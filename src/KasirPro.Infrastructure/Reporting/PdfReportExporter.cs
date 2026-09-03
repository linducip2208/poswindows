using System.Text;

namespace KasirPro.Infrastructure.Reporting;

/// <summary>
/// Minimal zero-dependency PDF writer (PDF 1.4, Helvetica core font, WinAnsi).
/// Supports a simple table layout for reports. Money is pre-formatted as text.
/// </summary>
public static class PdfReportExporter
{
    private const float PageW = 842f;  // A4 landscape
    private const float PageH = 595f;
    private const float Margin = 36f;

    public static string ExportTable(string title, string subtitle, IList<string> columns,
        IList<int> columnWidthsPercent, IEnumerable<object[]> rows, string targetPath)
    {
        var sb = new StringBuilder();
        var pages = new List<StringBuilder> { new() };
        var colWidths = columnWidthsPercent.Select(p => (PageW - Margin * 2) * p / 100f).ToArray();
        var rowH = 16f;
        var y = PageH - Margin - 40f;

        string Esc(string? s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        void DrawHeader(StringBuilder page)
        {
            page.Append("BT /F2 16 Tf 1 0 0 1 ").Append(Margin).Append(' ').Append(PageH - Margin - 16)
                .Append(" Tm (").Append(Esc(title)).Append(") Tj ET\n");
            if (!string.IsNullOrEmpty(subtitle))
                page.Append("BT /F1 9 Tf 1 0 0 1 ").Append(Margin).Append(' ').Append(PageH - Margin - 32)
                    .Append(" Tm (").Append(Esc(subtitle)).Append(") Tj ET\n");

            var x = Margin;
            page.Append("0.92 0.95 0.98 rg ").Append(Margin).Append(' ').Append(y - 4)
                .Append(' ').Append(PageW - Margin * 2).Append(' ').Append(rowH).Append(" re f 0 0 0 rg\n");
            for (var i = 0; i < columns.Count; i++)
            {
                page.Append("BT /F2 9 Tf 1 0 0 1 ").Append(x + 4).Append(' ').Append(y + 2)
                    .Append(" Tm (").Append(Esc(columns[i])).Append(") Tj ET\n");
                x += colWidths[i];
            }
            y -= rowH + 4;
        }

        void NewPage()
        {
            var page = new StringBuilder();
            pages.Add(page);
            y = PageH - Margin - 10f;
            page.Append("BT /F1 8 Tf 1 0 0 1 ").Append(Margin).Append(' ').Append(Margin - 14)
                .Append(" Tm (KasirPro) Tj ET\n");
            DrawHeader(page);
        }

        var currentPage = pages[0];
        DrawHeader(currentPage);

        foreach (var row in rows)
        {
            if (y < Margin + rowH + 20)
            {
                NewPage();
                currentPage = pages[^1];
            }
            var x = Margin;
            for (var i = 0; i < columns.Count; i++)
            {
                var text = i < row.Length ? row[i]?.ToString() ?? "" : "";
                currentPage.Append("BT /F1 9 Tf 1 0 0 1 ").Append(x + 4).Append(' ').Append(y)
                    .Append(" Tm (").Append(Esc(text)).Append(") Tj ET\n");
                x += colWidths[i];
            }
            currentPage.Append("0.85 0.85 0.85 RG 0.3 w ").Append(Margin).Append(' ').Append(y - 4)
                .Append(' ').Append(PageW - Margin * 2).Append(" 0 l S 0 0 0 RG\n");
            y -= rowH;
        }

        // footer page numbers
        for (var i = 0; i < pages.Count; i++)
        {
            pages[i].Append("BT /F1 8 Tf 1 0 0 1 ").Append(PageW - Margin - 40).Append(' ').Append(Margin - 14)
                .Append(" Tm (Page ").Append(i + 1).Append('/').Append(pages.Count).Append(") Tj ET\n");
        }

        // ---- assemble PDF ----
        var objects = new List<string>();
        var contentStreams = pages.Select(p => p.ToString()).ToList();

        // 1: catalog, 2: pages tree, then per page: page obj + content obj
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 2} 0 R"));
        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        for (var i = 0; i < contentStreams.Count; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW} {PageH}] " +
                        $"/Resources << /Font << /F1 {3 + pages.Count * 2} 0 R /F2 {4 + pages.Count * 2} 0 R >> >> " +
                        $"/Contents {4 + i * 2} 0 R >>");
            var stream = contentStreams[i];
            objects.Add($"<< /Length {Encoding.GetEncoding("ISO-8859-1").GetByteCount(stream)} >>\nstream\n{stream}endstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        var pdf = new StringBuilder();
        pdf.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.GetEncoding("ISO-8859-1").GetByteCount(pdf.ToString()));
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xrefPos = Encoding.GetEncoding("ISO-8859-1").GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var off in offsets)
            pdf.Append(off.ToString("D10")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1)
           .Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefPos).Append("\n%%EOF");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, pdf.ToString(), Encoding.GetEncoding("ISO-8859-1"));
        return targetPath;
    }
}
