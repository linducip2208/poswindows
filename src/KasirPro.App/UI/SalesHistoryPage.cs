using System.Text;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Paged list control shared by history pages.</summary>
public class PagingBar : Panel
{
    private readonly Button _prev;
    private readonly Button _next;
    private readonly Label _info;
    private int _page = 1;
    private int _totalPages = 1;

    public event Action? PageChanged;

    public PagingBar()
    {
        Height = 36;
        Dock = DockStyle.Bottom;
        BackColor = Theme.Bg;
        _prev = Theme.SecondaryButton("< Prev", 80, 26);
        _prev.Location = new Point(2, 4);
        _prev.Click += (s, e) => { if (_page > 1) { _page--; Raise(); } };
        _next = Theme.SecondaryButton("Next >", 80, 26);
        _next.Location = new Point(88, 4);
        _next.Click += (s, e) => { if (_page < _totalPages) { _page++; Raise(); } };
        _info = Theme.Label("", 9, false, Theme.Muted);
        _info.Location = new Point(180, 9);
        Controls.AddRange(new Control[] { _prev, _next, _info });
    }

    private void Raise() => PageChanged?.Invoke();

    public void UpdateInfo(int page, int totalPages, int totalItems)
    {
        _page = page;
        _totalPages = Math.Max(1, totalPages);
        _info.Text = $"Halaman {page}/{_totalPages} - {totalItems:N0} data";
        _prev.Enabled = _page > 1;
        _next.Enabled = _page < _totalPages;
    }
}

public class SalesHistoryPage : Panel, IPage
{
    private const int PageSize = 30;
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = Theme.TextBox(240);
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly PagingBar _paging = new();
    private int _page = 1;

    public SalesHistoryPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var header = Theme.PageHeader("Sales History", "Riwayat transaksi penjualan");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg, Padding = new Padding(16, 6, 16, 0) };
        _search.PlaceholderText = "Cari invoice / pelanggan...";
        _search.Location = new Point(16, 8);
        _from.Location = new Point(264, 8);
        _to.Location = new Point(380, 8);
        var btnSearch = Theme.PrimaryButton("Cari", 80);
        btnSearch.Location = new Point(496, 6);
        btnSearch.Click += (s, e) => { _page = 1; LoadData(); };
        var btnDetail = Theme.SecondaryButton("View Detail", 110);
        btnDetail.Location = new Point(582, 6);
        btnDetail.Click += (s, e) => ViewDetail();
        var btnVoid = Theme.DangerButton("Void", 80);
        btnVoid.Location = new Point(698, 6);
        btnVoid.Click += (s, e) => VoidSelected();
        var btnPrint = Theme.SecondaryButton("Print Struk", 100);
        btnPrint.Location = new Point(784, 6);
        btnPrint.Click += (s, e) => PrintSelected();
        toolbar.Controls.AddRange(new Control[] { _search, _from, _to, btnSearch, btnDetail, btnVoid, btnPrint });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("invoice", "Invoice");
        _grid.Columns.Add("date", "Date");
        _grid.Columns.Add("customer", "Customer");
        _grid.Columns.Add("total", "Total");
        _grid.Columns.Add("payment", "Payment");
        _grid.Columns.Add("status", "Status");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["invoice"].FillWeight = 20;
        _grid.Columns["date"].FillWeight = 18;
        _grid.Columns["customer"].FillWeight = 24;
        _grid.Columns["total"].FillWeight = 14;
        _grid.Columns["payment"].FillWeight = 16;
        _grid.Columns["status"].FillWeight = 8;
        Theme.MoneyColumn(_grid, "total");
        _grid.CellDoubleClick += (s, e) => ViewDetail();

        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var result = UiHelpers.Run(() => Program.Services.Sales.History(
            _search.Text, _from.Value, _to.Value, 0, _page, PageSize));
        if (result == null) return;
        _grid.Rows.Clear();
        foreach (var s in result.Items)
        {
            var idx = _grid.Rows.Add(s.InvoiceNo, s.SaleDate.ToString("dd/MM/yyyy HH:mm"),
                s.CustomerName, s.Total, s.CashierName, s.Status, s.Id);
            if (s.Status == "VOIDED")
                _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Danger;
        }
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }

    private (long Id, string Invoice)? Selected()
    {
        if (_grid.CurrentRow == null) return null;
        return (Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value),
            _grid.CurrentRow.Cells["invoice"].Value?.ToString() ?? "");
    }

    private void ViewDetail()
    {
        var sel = Selected();
        if (sel == null) return;
        var sale = UiHelpers.Run(() => Program.Services.Sales.GetById(sel.Value.Id));
        if (sale == null) return;
        new SaleDetailDialog(sale).ShowDialog();
    }

    private void VoidSelected()
    {
        var sel = Selected();
        if (sel == null) return;
        if (!Program.Session!.IsAdmin)
        {
            UiHelpers.Warn("Hanya Admin yang dapat membatalkan transaksi.");
            return;
        }
        var reason = InputDialog.Show("Alasan pembatalan:", $"Void {sel.Value.Invoice}");
        if (string.IsNullOrWhiteSpace(reason)) return;
        UiHelpers.Run(() =>
        {
            Program.Services.Sales.VoidSale(sel.Value.Id, reason, Program.Session.UserId, Program.Session.Username);
            UiHelpers.Info("Transaksi dibatalkan. Stok dikembalikan.");
            LoadData();
        });
    }

    private void PrintSelected()
    {
        var sel = Selected();
        if (sel == null) return;
        var sale = UiHelpers.Run(() => Program.Services.Sales.GetById(sel.Value.Id));
        if (sale == null) return;
        UiHelpers.Run(() =>
        {
            Program.Services.Printer.PrintReceipt(sale, null, 1, isReprint: true);
            Program.Services.Audit.Log(Program.Session!.UserId, Program.Session.Username,
                "RECEIPT_REPRINT", "sale", sale.Id, $"Reprint {sale.InvoiceNo}");
            return 0;
        });
    }
}

/// <summary>Invoice detail viewer (items + payments).</summary>
public class SaleDetailDialog : Form
{
    public SaleDetailDialog(Sale sale)
    {
        Text = $"Detail {sale.InvoiceNo}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 540);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var head = new StringBuilder();
        head.AppendLine("Invoice:    " + sale.InvoiceNo);
        head.AppendLine("Tanggal:    " + sale.SaleDate.ToString("dd/MM/yyyy HH:mm:ss"));
        head.AppendLine("Kasir:      " + sale.CashierName);
        head.AppendLine("Pelanggan:  " + sale.CustomerName);
        head.AppendLine("Status:     " + sale.Status);
        var headLbl = new Label
        {
            Text = head.ToString(),
            Location = new Point(20, 14),
            Size = new Size(600, 92),
            Font = new Font("Consolas", 9.5f),
            ForeColor = Theme.Text
        };

        var grid = new DataGridView();
        Theme.StyleGrid(grid);
        grid.Location = new Point(20, 112);
        grid.Size = new Size(600, 260);
        grid.Columns.Add("name", "Product");
        grid.Columns.Add("qty", "Qty");
        grid.Columns.Add("price", "Price");
        grid.Columns.Add("subtotal", "Subtotal");
        Theme.MoneyColumn(grid, "price");
        Theme.MoneyColumn(grid, "subtotal");
        foreach (var i in sale.Items)
            grid.Rows.Add(i.ProductName, i.Qty.ToString("0.##"), i.Price, i.Subtotal);

        var foot = new StringBuilder();
        foot.AppendLine($"Subtotal: {Money.Format(sale.Subtotal)}   Diskon: {Money.Format(sale.Discount)}   TOTAL: {Money.Format(sale.Total)}");
        foot.AppendLine("Pembayaran: " + string.Join(", ", sale.Payments.Select(p => $"{p.Method} {Money.Format(p.Amount)}")));
        var footLbl = new Label
        {
            Text = foot.ToString(),
            Location = new Point(20, 384),
            Size = new Size(600, 48),
            Font = new Font("Consolas", 10f, FontStyle.Bold),
            ForeColor = Theme.Text
        };

        var close = Theme.SecondaryButton("Tutup", 110, 36);
        close.Location = new Point(510, 452);
        close.Click += (s, e) => Close();
        var reprint = Theme.PrimaryButton("Cetak Ulang", 120, 36);
        reprint.Location = new Point(382, 452);
        reprint.Click += (s, e) => UiHelpers.Run(() => Program.Services.Printer.PrintReceipt(sale));

        Controls.AddRange(new Control[] { headLbl, grid, footLbl, close, reprint });
    }
}

/// <summary>Simple single-input modal.</summary>
public static class InputDialog
{
    public static string? Show(string label, string title)
    {
        string? result = null;
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 140),
            MaximizeBox = false, MinimizeBox = false,
            BackColor = Theme.Bg, Font = Theme.FontBase
        };
        var lbl = Theme.Label(label, 9, true);
        lbl.Location = new Point(16, 14);
        var txt = Theme.TextBox(340);
        txt.Location = new Point(16, 40);
        var ok = Theme.PrimaryButton("OK", 90, 30);
        ok.Location = new Point(266, 86);
        ok.Click += (s, e) => { result = txt.Text.Trim(); form.Close(); };
        form.Controls.AddRange(new Control[] { lbl, txt, ok });
        form.AcceptButton = ok;
        form.ShowDialog();
        return result;
    }
}

