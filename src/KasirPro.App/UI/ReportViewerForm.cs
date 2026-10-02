using System.Data;
using System.Drawing.Printing;
using System.Text;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

public enum ReportKind
{
    Sales, Purchase, Profit, ProductSales, Stock, StockMovement, LowStock, Cash, Cashier,
    Hourly, PaymentMethod, Category, CustomerSales, Valuation, NearExpiry, DeadStock,
    Monthly, ReceivableAging, PayableAging, PromoUsage, Loyalty, Expenses
}

/// <summary>
/// Generic report viewer: date range + refresh + print + export CSV.
/// All data comes live from SQLite (no internet, no mock).
/// </summary>
public class ReportViewerForm : Form
{
    private readonly ReportKind _kind;
    private readonly DataGridView _grid = new();
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly Label _summary = Theme.Label("", 10, true, Theme.Muted);

    public ReportViewerForm(ReportKind kind)
    {
        _kind = kind;
        Text = Title;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 640);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader(Title, "Laporan - data langsung dari database lokal");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _from.Location = new Point(16, 10);
        _to.Location = new Point(132, 10);
        var btnRefresh = Theme.PrimaryButton("Refresh", 100);
        btnRefresh.Location = new Point(248, 8);
        btnRefresh.Click += (s, e) => LoadData();
        var btnPrint = Theme.SecondaryButton("Print", 90);
        btnPrint.Location = new Point(354, 8);
        btnPrint.Click += (s, e) => Print();
        var btnCsv = Theme.SecondaryButton("Export CSV", 110);
        btnCsv.Location = new Point(450, 8);
        btnCsv.Click += (s, e) => ExportCsv();
        var btnPdf = Theme.SecondaryButton("Export PDF", 110);
        btnPdf.Location = new Point(566, 8);
        btnPdf.Click += (s, e) => ExportPdf();
        _summary.Location = new Point(686, 14);
        toolbar.Controls.AddRange(new Control[] { _from, _to, btnRefresh, btnPrint, btnCsv, btnPdf, _summary });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);

        if (kind is ReportKind.Stock or ReportKind.LowStock)
        {
            toolbar.Height = 0; // no date range for stock snapshots
            _from.Visible = false;
            _to.Visible = false;
        }
        if (_grid.Columns.Contains("date")) { /* keep */ }
        LoadData();
    }

    private string Title => _kind switch
    {
        ReportKind.Sales => "Sales Report",
        ReportKind.Purchase => "Purchase Report",
        ReportKind.Profit => "Profit Report",
        ReportKind.ProductSales => "Product Sales Report",
        ReportKind.Stock => "Stock Report",
        ReportKind.StockMovement => "Stock Movement Report",
        ReportKind.LowStock => "Low Stock Report",
        ReportKind.Cash => "Cash Report",
        ReportKind.Cashier => "Cashier Report",
        ReportKind.Hourly => "Sales by Hour",
        ReportKind.PaymentMethod => "Sales by Payment Method",
        ReportKind.Category => "Sales by Category",
        ReportKind.CustomerSales => "Sales by Customer",
        ReportKind.Valuation => "Inventory Valuation",
        ReportKind.NearExpiry => "Near Expiry Report",
        ReportKind.DeadStock => "Dead Stock Report",
        ReportKind.Monthly => "Monthly Sales",
        ReportKind.ReceivableAging => "Receivable Aging",
        ReportKind.PayableAging => "Payable Aging",
        ReportKind.PromoUsage => "Promotion Usage",
        ReportKind.Expenses => "Pengeluaran Operasional",
        _ => "Loyalty Report"
    };

    private DataTable BuildTable()
    {
        var svc = Program.Services.Reports;
        var from = _from.Value;
        var to = _to.Value;
        return _kind switch
        {
            ReportKind.Sales => ToTable(new[] { ("Date", 0), ("Invoice", 0), ("Customer", 0), ("Cashier", 0), ("Subtotal", 1), ("Discount", 1), ("Total", 1), ("Payment", 0), ("Status", 0) },
                svc.Sales(from, to).Select(r => new object[]
                {
                    r.Date.ToString("dd/MM/yyyy HH:mm"), r.InvoiceNo, r.Customer, r.Cashier,
                    r.Subtotal, r.Discount, r.Total, r.Payment, r.Status
                })),
            ReportKind.Purchase => ToTable(new[] { ("Date", 0), ("Purchase No", 0), ("Supplier Invoice", 0), ("Supplier", 0), ("Subtotal", 1), ("Discount", 1), ("Total", 1), ("Status", 0) },
                svc.Purchases(from, to).Select(r => new object[]
                {
                    r.Date.ToString("dd/MM/yyyy HH:mm"), r.PurchaseNo, r.SupplierInvoice, r.Supplier,
                    r.Subtotal, r.Discount, r.Total, r.Status
                })),
            ReportKind.Profit => ToTable(new[] { ("Date", 0), ("Revenue", 1), ("Cost", 1), ("Expenses", 1), ("Profit", 1) },
                svc.Profit(from, to).Select(r => new object[]
                {
                    r.Date.ToString("dd/MM/yyyy"), r.Revenue, r.Cost, r.Expenses, r.Profit
                })),
            ReportKind.ProductSales => ToTable(new[] { ("Code", 0), ("Product", 0), ("Category", 0), ("Qty Sold", 2), ("Revenue", 1), ("Profit", 1) },
                svc.ProductSales(from, to).Select(r => new object[]
                {
                    r.Code, r.Name, r.Category, r.QtySold, r.Revenue, r.Profit
                })),
            ReportKind.Stock => ToTable(new[] { ("Code", 0), ("Product", 0), ("Category", 0), ("Stock", 2), ("Unit", 0), ("Min", 2), ("Purchase Price", 1), ("Stock Value", 1) },
                svc.Stock().Select(r => new object[]
                {
                    r.Code, r.Name, r.Category, r.Stock, r.Unit, r.MinStock, r.PurchasePrice, r.StockValue
                })),
            ReportKind.StockMovement => ToTable(new[] { ("Date", 0), ("Code", 0), ("Product", 0), ("Reference", 0), ("Direction", 0), ("Qty", 2), ("After", 2), ("User", 0), ("Notes", 0) },
                svc.StockMovements(from, to).Select(r => new object[]
                {
                    r.Date.ToString("dd/MM/yyyy HH:mm"), r.Code, r.Product,
                    r.ReferenceType + "#" + r.ReferenceId, r.Direction, r.Qty, r.StockAfter, r.User, r.Notes
                })),
            ReportKind.LowStock => ToTable(new[] { ("Code", 0), ("Product", 0), ("Category", 0), ("Stock", 2), ("Min Stock", 2), ("Kekurangan", 2), ("Stock Value", 1) },
                svc.LowStockReport().Select(r => new object[]
                {
                    r.Code, r.Name, r.Category, r.Stock, r.MinStock, r.MinStock - r.Stock, r.StockValue
                })),
            ReportKind.Cash => ToTable(new[] { ("Date", 0), ("Session", 0), ("Cashier", 0), ("Opening", 1), ("Cash In", 1), ("Cash Out", 1), ("Cash Sales", 1), ("Expected", 1), ("Actual", 1), ("Diff", 1), ("Status", 0) },
                svc.Cash(from, to).Select(r => new object[]
                {
                    r.Date.ToString("dd/MM/yyyy"), r.Session, r.Cashier, r.Opening, r.CashIn, r.CashOut,
                    r.CashSales, r.Expected, r.Actual, r.Difference, r.Status
                })),
            ReportKind.Hourly => ToTable(new[] { ("Jam", 0), ("Transaksi", 2), ("Total", 1) },
                Program.Services.Analytics.ByHour(from, to).Select(r => new object[]
                {
                    $"{r.Hour:00}:00 - {r.Hour:00}:59", r.Count, r.Total
                })),
            ReportKind.PaymentMethod => ToTable(new[] { ("Metode", 0), ("Transaksi", 2), ("Total", 1) },
                Program.Services.Analytics.ByPayment(from, to).Select(r => new object[]
                {
                    r.Method, r.Count, r.Total
                })),
            ReportKind.Category => ToTable(new[] { ("Kategori", 0), ("Qty", 2), ("Total", 1) },
                Program.Services.Analytics.ByCategory(from, to).Select(r => new object[]
                {
                    r.Category, r.Qty, r.Total
                })),
            ReportKind.CustomerSales => ToTable(new[] { ("Customer", 0), ("Transaksi", 2), ("Total", 1), ("Last Purchase", 0) },
                Program.Services.Reports.SalesByCustomer(from, to).Select(r => new object[]
                {
                    r.Customer, r.Count, r.Total, r.LastPurchase
                })),
            ReportKind.Monthly => ToTable(new[] { ("Bulan", 0), ("Transaksi", 2), ("Total", 1), ("Profit", 1) },
                Program.Services.Reports.MonthlySales().Select(r => new object[]
                {
                    r.Month, r.Count, r.Total, r.Profit
                })),
            ReportKind.Valuation => ToTable(new[] { ("Kategori", 0), ("Produk", 2), ("Nilai Stok", 1) },
                Program.Services.Reports.InventoryValuation().Select(r => new object[]
                {
                    r.Category, r.Products, r.StockValue
                })),
            ReportKind.NearExpiry => ToTable(new[] { ("Batch", 0), ("Product", 0), ("Expiry", 0), ("Qty", 2), ("Cost", 1) },
                Program.Services.Batches.NearExpiry(
                    int.TryParse(Program.Services.Settings.Get("near_expiry_days", "30"), out var d) ? d : 30)
                    .Select(r => new object[]
                {
                    r.Batch, r.Product, r.Expiry?.ToString("dd/MM/yyyy") ?? "-", r.Qty, r.Cost
                })),
            ReportKind.DeadStock => ToTable(new[] { ("SKU", 0), ("Product", 0), ("Stok", 2), ("Last Sale", 0) },
                Program.Services.MovementAnalytics.SlowOrDead(
                    int.TryParse(Program.Services.Settings.Get("dead_stock_days", "90"), out var dd) ? dd : 90,
                    deadOnly: true).Select(r => new object[]
                {
                    r.Code, r.Name, r.Stock, r.LastSale
                })),
            ReportKind.ReceivableAging => ToTable(new[] { ("Bucket", 0), ("Jumlah", 2), ("Total", 1) },
                Program.Services.Reports.ReceivableAging().Select(r => new object[]
                {
                    r.Bucket, r.Count, r.Total
                })),
            ReportKind.PayableAging => ToTable(new[] { ("Bucket", 0), ("Jumlah", 2), ("Total", 1) },
                Program.Services.Purchases.ApAging().Select(r => new object[]
                {
                    r.Bucket, r.Count, r.Total
                })),
            ReportKind.PromoUsage => ToTable(new[] { ("Kode", 0), ("Nama", 0), ("Dipakai", 2), ("Total Diskon", 1) },
                Program.Services.Reports.PromoUsage(from, to).Select(r => new object[]
                {
                    r.Code, r.Name, r.Uses, r.TotalDiscount
                })),
            ReportKind.Expenses => ToTable(new[] { ("Tanggal", 0), ("Kategori", 0), ("Keterangan", 0), ("Jumlah", 1), ("Pembayaran", 0), ("Petugas", 0) },
                svc.Expenses(from, to).Select(r => new object[]
                {
                    r.Date.ToString("dd/MM/yyyy HH:mm"), r.Category, r.Description, r.Amount, r.PaymentMethod, r.User
                })),
            ReportKind.Loyalty => ToTable(new[] { ("Pelanggan", 0), ("Poin", 2), ("Lifetime", 1) },
                Program.Services.Reports.LoyaltyTop().Select(r => new object[]
                {
                    r.Customer, r.Points, r.Lifetime
                })),
            _ => ToTable(new[] { ("Cashier", 0), ("Transactions", 2), ("Total", 1), ("Profit", 1) },
                svc.Cashiers(from, to).Select(r => new object[]
                {
                    r.Cashier, r.Transactions, r.Total, r.Profit
                }))
        };
    }

    private static DataTable ToTable((string Name, int Kind)[] columns, IEnumerable<object[]> rows)
    {
        var dt = new DataTable();
        foreach (var (name, _) in columns) dt.Columns.Add(name);
        foreach (var r in rows) dt.Rows.Add(r);
        dt.ExtendedProperties["Columns"] = columns;
        return dt;
    }

    private void LoadData()
    {
        var dt = UiHelpers.Run(() => BuildTable());
        if (dt == null) return;
        _grid.DataSource = dt;
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            var columns = (List<(string Name, int Kind)>?)dt.ExtendedProperties["Columns"];
            var kind = columns?.FirstOrDefault(c => c.Name == col.Name).Kind ?? 0;
            if (kind == 1)
            {
                col.DefaultCellStyle.Format = "N0";
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            else if (kind == 2)
            {
                col.DefaultCellStyle.Format = "0.##";
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        }
        var totalCols = (List<(string Name, int Kind)>?)dt.ExtendedProperties["Columns"];
        var sum = 0m;
        if (totalCols != null && totalCols.Any(c => c.Kind == 1))
        {
            var moneyCol = totalCols!.First(c => c.Kind == 1).Name;
            if (_grid.Columns.Contains(moneyCol))
            {
                foreach (DataGridViewRow row in _grid.Rows)
                    if (decimal.TryParse(row.Cells[moneyCol].Value?.ToString(), out var v)) sum += v;
            }
        }
        _summary.Text = $"{dt.Rows.Count:N0} baris";
    }

    private void ExportCsv()
    {
        try
        {
            AppPaths.EnsureAll();
            var file = Path.Combine(AppPaths.ReportsDir,
                $"{Title.Replace(" ", "")}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var sb = new StringBuilder();
            var headers = new List<string>();
            foreach (DataGridViewColumn c in _grid.Columns) headers.Add(c.HeaderText);
            sb.AppendLine(string.Join(";", headers));
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var cells = row.Cells.Cast<DataGridViewCell>()
                    .Select(c => (c.Value?.ToString() ?? "").Replace(";", ","));
                sb.AppendLine(string.Join(";", cells));
            }
            File.WriteAllText(file, sb.ToString());
            UiHelpers.Info("Export selesai:\n" + file);
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Export gagal: " + ex.Message);
        }
    }

    private void ExportPdf()
    {
        try
        {
            AppPaths.EnsureAll();
            var file = Path.Combine(AppPaths.ReportsDir,
                $"{Title.Replace(" ", "")}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            var columns = new List<string>();
            var widths = new List<int>();
            foreach (DataGridViewColumn c in _grid.Columns)
            {
                columns.Add(c.HeaderText);
                widths.Add(Math.Max(6, (int)Math.Round(c.FillWeight)));
            }
            var rows = new List<object[]>();
            foreach (DataGridViewRow row in _grid.Rows)
                rows.Add(row.Cells.Cast<DataGridViewCell>().Select(c => c.Value ?? "").ToArray());

            var range = _from.Visible ? $"{_from.Value:dd/MM/yyyy} - {_to.Value:dd/MM/yyyy}" : "";
            var path = KasirPro.Infrastructure.Reporting.PdfReportExporter.ExportTable(
                Title, range + $"  |  dicetak {DateTime.Now:dd/MM/yyyy HH:mm} oleh {Program.Session?.Username}",
                columns, widths, rows, file);
            UiHelpers.Info("PDF tersimpan:\n" + path);
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Export PDF gagal: " + ex.Message);
        }
    }

    private void Print()
    {
        try
        {
            var doc = new PrintDocument { DocumentName = Title };
            var rowIndex = 0;
            var font = new Font("Consolas", 8f);
            var fontBold = new Font("Consolas", 9f, FontStyle.Bold);
            var headers = _grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToList();

            doc.PrintPage += (s, e) =>
            {
                var y = 20;
                e.Graphics.DrawString(Title + "  -  " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    fontBold, Brushes.Black, 20, y);
                y += 26;
                e.Graphics.DrawString(string.Join("  |  ", headers), fontBold, Brushes.Black, 20, y);
                y += 22;

                while (rowIndex < _grid.Rows.Count && y < e.MarginBounds.Bottom - 30)
                {
                    var cells = _grid.Rows[rowIndex].Cells.Cast<DataGridViewCell>()
                        .Select(c => (c.Value?.ToString() ?? "").PadRight(12));
                    e.Graphics.DrawString(string.Join(" | ", cells), font, Brushes.Black, 20, y);
                    y += 18;
                    rowIndex++;
                }
                e.HasMorePages = rowIndex < _grid.Rows.Count;
            };
            doc.Print();
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Print gagal: " + ex.Message);
        }
    }
}
