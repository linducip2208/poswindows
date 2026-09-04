using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Raw ESC/POS passthrough (drawer kick, opening pulses).</summary>
public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string? DocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string? DataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendBytes(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName.Normalize(), out var hPrinter, IntPtr.Zero)) return false;
        try
        {
            var di = new DOCINFOA { DocName = "KasirPro Raw", DataType = "RAW" };
            if (!StartDocPrinter(hPrinter, 1, di)) return false;
            try
            {
                if (!StartPagePrinter(hPrinter)) return false;
                var pUnmanaged = Marshal.AllocCoTaskMem(data.Length);
                try
                {
                    Marshal.Copy(data, 0, pUnmanaged, data.Length);
                    return WritePrinter(hPrinter, pUnmanaged, data.Length, out _);
                }
                finally { Marshal.FreeCoTaskMem(pUnmanaged); }
            }
            finally { EndPagePrinter(hPrinter); }
        }
        finally { ClosePrinter(hPrinter); }
    }
}

/// <summary>
/// Thermal receipt printing via Windows PrintDocument (GDI).
/// Supports 58mm and 80mm paper. QRIS is recorded as payment method only (no gateway).
/// </summary>
public class PrinterService
{
    private readonly SettingsService _settings;

    public PrinterService(SettingsService settings) { _settings = settings; }

    public static string[] GetInstalledPrinters() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToArray();

    public bool PrintReceipt(Sale sale, string? printerName = null, int copies = 1, bool isReprint = false)
    {
        try
        {
            var lines = BuildReceipt(sale, isReprint);
            var paper = _settings.ReceiptPaper == "58" ? 58 : 80;
            var name = string.IsNullOrWhiteSpace(printerName) ? _settings.PrinterName : printerName;
            if (string.IsNullOrWhiteSpace(name))
                name = new PrinterSettings().PrinterName;

            for (var i = 0; i < Math.Max(1, copies); i++)
            {
                var doc = BuildDocument(lines, paper, name);
                doc.Print();
            }

            KickDrawerIfEnabled(name);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("print failed", ex);
            throw new InvalidOperationException("Gagal mencetak: " + ex.Message, ex);
        }
    }

    /// <summary>Sends an ESC/POS drawer-open pulse (ESC p 0 25 250) to the printer.</summary>
    public void KickDrawerIfEnabled(string? printerName)
    {
        try
        {
            if (_settings.Get("drawer_enabled", "0") != "1") return;
            var target = string.IsNullOrWhiteSpace(printerName) ? _settings.PrinterName : printerName;
            if (string.IsNullOrWhiteSpace(target)) return;
            RawPrinterHelper.SendBytes(target, new byte[] { 27, 112, 48, 25, 250 });
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("drawer kick gagal (diabaikan)", ex);
        }
    }

    public void PrintTest(string printerName, string paperSize)
    {
        var width = paperSize == "58" ? 30 : 42;
        var lines = new List<string>
        {
            "[B]" + Center("TEST PRINTER", width),
            "[B]" + Center(_settings.StoreName, width),
            Center(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), width),
            "",
            LeftRight("Kertas", paperSize + " mm", width),
            "",
            Center("Jika Anda membaca ini,", width),
            Center("printer terkonfigurasi dengan benar.", width),
            "",
            Center(".", width)
        };
        var doc = BuildDocument(lines, paperSize == "58" ? 58 : 80, printerName);
        doc.Print();
    }

    private PrintDocument BuildDocument(List<string> lines, int paperMm, string? printerName)
    {
        var doc = new PrintDocument();
        doc.DocumentName = "KasirPro Receipt";
        if (!string.IsNullOrWhiteSpace(printerName)) doc.PrinterSettings.PrinterName = printerName;

        // chars per line: 80mm ~ 42, 58mm ~ 30 (12px monospace)
        var widthInches = paperMm / 25.4f;
        doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", (int)(widthInches * 100), 400);

        var font = new Font("Consolas", 8.5f, FontStyle.Regular);
        var fontBold = new Font("Consolas", 9f, FontStyle.Bold);
        var layoutY = 0f;

        doc.PrintPage += (s, e) =>
        {
            layoutY = 4;
            foreach (var line in lines)
            {
                if (line.StartsWith("[B]"))
                {
                    e.Graphics.DrawString(line[3..], fontBold, Brushes.Black, 4, layoutY);
                    layoutY += fontBold.GetHeight(e.Graphics) + 1;
                }
                else
                {
                    e.Graphics.DrawString(line, font, Brushes.Black, 4, layoutY);
                    layoutY += font.GetHeight(e.Graphics) + 1;
                }
            }
            e.HasMorePages = false;
        };
        return doc;
    }

    private List<string> BuildReceipt(Sale sale, bool isReprint = false)
    {
        var width = _settings.ReceiptPaper == "58" ? 30 : 42;
        var lines = new List<string>
        {
            "[B]" + Center(_settings.StoreName, width)
        };
        if (isReprint) lines.Add("[B]" + Center("** REPRINT **", width));
        if (!string.IsNullOrWhiteSpace(_settings.StoreAddress))
            foreach (var l in Wrap(_settings.StoreAddress, width)) lines.Add(Center(l, width));
        if (!string.IsNullOrWhiteSpace(_settings.StorePhone))
            lines.Add(Center("Telp: " + _settings.StorePhone, width));
        lines.Add(new string('-', width));
        lines.Add(LeftRight("No", sale.InvoiceNo, width));
        lines.Add(LeftRight("Tgl", sale.SaleDate.ToString("dd/MM/yyyy HH:mm"), width));
        lines.Add(LeftRight("Kasir", sale.CashierName, width));
        if (!string.IsNullOrWhiteSpace(sale.CustomerName) && sale.CustomerName != "Umum")
            lines.Add(LeftRight("Pelanggan", sale.CustomerName, width));
        lines.Add(new string('-', width));

        foreach (var item in sale.Items)
        {
            lines.Add(item.ProductName.Length > width ? item.ProductName[..width] : item.ProductName);
            lines.Add(LeftRight($"  {item.Qty:0.##} x {Money.FormatPlain(item.Price)}", Money.FormatPlain(item.Subtotal), width));
        }
        lines.Add(new string('-', width));
        lines.Add(LeftRight("Subtotal", Money.FormatPlain(sale.Subtotal), width));
        if (sale.Discount > 0) lines.Add(LeftRight("Diskon", "-" + Money.FormatPlain(sale.Discount), width));
        if (sale.Tax > 0) lines.Add(LeftRight("PPN", Money.FormatPlain(sale.Tax), width));
        lines.Add("[B]" + LeftRight("TOTAL", Money.FormatPlain(sale.Total), width));
        foreach (var p in sale.Payments)
        {
            lines.Add(LeftRight(p.Method.ToString(), Money.FormatPlain(p.Amount), width));
        }
        var paid = sale.Payments.Sum(p => p.Amount);
        var change = Money.Round(paid - sale.Total);
        if (change > 0) lines.Add(LeftRight("Kembali", Money.FormatPlain(change), width));
        lines.Add(new string('-', width));
        if (!string.IsNullOrWhiteSpace(_settings.ReceiptFooter))
            lines.Add(Center(_settings.ReceiptFooter, width));
        lines.Add(Center("Powered by KasirPro", width));
        return lines;
    }

    private static string Center(string text, int width) =>
        text.Length >= width ? text[..width] : new string(' ', (width - text.Length) / 2) + text;

    private static string LeftRight(string left, string right, int width)
    {
        var space = width - left.Length - right.Length;
        return space <= 0 ? (left + right)[..width] : left + new string(' ', space) + right;
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var words = text.Split(' ');
        var current = "";
        foreach (var w in words)
        {
            if ((current + " " + w).Trim().Length > width)
            {
                if (current.Length > 0) yield return current;
                current = w;
            }
            else current = (current + " " + w).Trim();
        }
        if (current.Length > 0) yield return current;
    }
}
