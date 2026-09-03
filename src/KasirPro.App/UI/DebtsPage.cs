using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Debts (piutang): list receivables, settle installments with audit + cash integration.</summary>
public class DebtsPage : Panel, IPage
{
    private const int PageSize = 25;
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = Theme.TextBox(220);
    private readonly ComboBox _statusFilter = Theme.Combo(140);
    private readonly Label _summary = Theme.Label("", 10, true, Theme.Muted);
    private readonly PagingBar _paging = new();
    private int _page = 1;

    public DebtsPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Debts (Piutang)", "Piutang pelanggan dari transaksi belum lunas");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _search.Location = new Point(16, 10);
        _search.PlaceholderText = "Cari invoice / pelanggan...";
        _search.TextChanged += (s, e) => { _page = 1; LoadData(); };
        _statusFilter.Location = new Point(244, 10);
        _statusFilter.Items.AddRange(new object[] { "Belum Lunas", "Lunas", "Semua" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (s, e) => { _page = 1; LoadData(); };
        _summary.Location = new Point(400, 13);
        var btnSettle = Theme.SuccessButton("PELUNASAN", 130, 30);
        btnSettle.Location = new Point(900, 8);
        btnSettle.Click += (s, e) => SettleSelected();
        toolbar.Controls.AddRange(new Control[] { _search, _statusFilter, _summary, btnSettle });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("invoice", "Invoice");
        _grid.Columns.Add("date", "Tanggal Jual");
        _grid.Columns.Add("customer", "Pelanggan");
        _grid.Columns.Add("original", "Nilai Piutang");
        _grid.Columns.Add("paid", "Dibayar");
        _grid.Columns.Add("remaining", "Sisa");
        _grid.Columns.Add("due", "Jatuh Tempo");
        _grid.Columns.Add("status", "Status");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["invoice"].FillWeight = 16;
        _grid.Columns["date"].FillWeight = 13;
        _grid.Columns["customer"].FillWeight = 18;
        _grid.Columns["original"].FillWeight = 12;
        _grid.Columns["paid"].FillWeight = 12;
        _grid.Columns["remaining"].FillWeight = 12;
        _grid.Columns["due"].FillWeight = 12;
        _grid.Columns["status"].FillWeight = 8;
        foreach (var col in new[] { "original", "paid", "remaining" }) Theme.MoneyColumn(_grid, col);
        _grid.CellDoubleClick += (s, e) => SettleSelected();
        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() => LoadData();

    private string StatusKey() => _statusFilter.SelectedIndex switch
    {
        1 => "SETTLED",
        2 => "ALL",
        _ => "OUTSTANDING"
    };

    private void LoadData()
    {
        var result = UiHelpers.Run(() => Program.Services.Debts.Outstanding(
            _search.Text, StatusKey(), _page, PageSize));
        if (result == null) return;
        _grid.Rows.Clear();
        decimal totalRemaining = 0;
        foreach (var d in result.Items)
        {
            var remaining = d.Remaining;
            totalRemaining += remaining;
            var idx = _grid.Rows.Add(d.InvoiceNo, d.SaleDate.ToString("dd/MM/yyyy"), d.CustomerName,
                d.OriginalAmount, d.PaidAmount, remaining,
                d.DueDate?.ToString("dd/MM/yyyy") ?? "-", d.Status, d.Id);
            if (d.Status != "SETTLED" && d.DueDate.HasValue && d.DueDate.Value < DateTime.Now)
                _grid.Rows[idx].Cells["due"].Style.ForeColor = Theme.Danger;
            if (d.Status == "SETTLED")
                _grid.Rows[idx].Cells["status"].Style.ForeColor = Theme.Success;
        }
        _summary.Text = $"Total sisa piutang (halaman): {Money.Format(totalRemaining)}";
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }

    private void SettleSelected()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        var debt = UiHelpers.Run(() => Program.Services.Debts.Get(id));
        if (debt == null) return;
        if (debt.Status == "SETTLED") { UiHelpers.Info("Piutang ini sudah lunas."); return; }

        using var dlg = new DebtSettleDialog(debt);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Program.Session!.DataChangedSinceBackup = true;
            UiHelpers.Info($"Pelunasan tersimpan.\nStatus piutang: {dlg.Result!.Status}\nSisa: {Money.Format(dlg.Result.Remaining)}");
            LoadData();
        }
    }
}

/// <summary>Settlement dialog: amount + method + notes, cash flows into open session.</summary>
public class DebtSettleDialog : Form
{
    private readonly SaleDebt _debt;
    private readonly TextBox _amount = Theme.TextBox(180);
    private readonly ComboBox _method = Theme.Combo(160);
    private readonly TextBox _notes = Theme.TextBox(300);
    public SaleDebt? Result { get; private set; }

    public DebtSettleDialog(SaleDebt debt)
    {
        _debt = debt;
        Text = $"Pelunasan Piutang - {debt.InvoiceNo}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 290);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Pelunasan Piutang", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);
        var info = Theme.Label(
            $"Invoice: {debt.InvoiceNo}\n" +
            $"Pelanggan: {debt.CustomerName}\n" +
            $"Nilai: {Money.Format(debt.OriginalAmount)}   Dibayar: {Money.Format(debt.PaidAmount)}\n" +
            $"Sisa: {Money.Format(debt.Remaining)}", 9, false);
        info.Location = new Point(20, 48);
        info.Size = new Size(440, 66);

        var l1 = Theme.Label("Jumlah (Rp):", 9, true); l1.Location = new Point(20, 124);
        _amount.Location = new Point(130, 120);
        _amount.TextAlign = HorizontalAlignment.Right;
        _amount.Text = debt.Remaining.ToString("0");

        var l2 = Theme.Label("Metode:", 9, true); l2.Location = new Point(20, 160);
        _method.Location = new Point(130, 156);
        _method.Items.AddRange(new object[] { "Cash", "Qris", "Debit", "Transfer" });
        _method.SelectedIndex = 0;

        var l3 = Theme.Label("Catatan:", 9, true); l3.Location = new Point(20, 196);
        _notes.Location = new Point(130, 192);
        _notes.PlaceholderText = "opsional...";

        var ok = Theme.SuccessButton("SETOR PEMBAYARAN", 180, 40);
        ok.Location = new Point(280, 240);
        ok.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(180, 240);
        cancel.Click += (s, e) => Close();

        var hint = Theme.Label("Pembayaran cash masuk ke shift yang terbuka.", 8, false, Theme.Muted);
        hint.Location = new Point(20, 250);

        Controls.AddRange(new Control[] { title, info, l1, _amount, l2, _method, l3, _notes, ok, cancel, hint });
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (!decimal.TryParse(_amount.Text.Replace(".", "").Replace(",", ""), out var amount) || amount <= 0)
        {
            UiHelpers.Warn("Jumlah tidak valid.");
            return;
        }
        var session = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
        var result = UiHelpers.Run(() => Program.Services.Debts.Settle(
            _debt.Id, amount, (PaymentMethod)_method.SelectedIndex,
            string.IsNullOrWhiteSpace(_notes.Text) ? "Pelunasan" : _notes.Text.Trim(),
            Program.Session!.UserId, Program.Session.Username, session?.Id ?? 0));
        if (result == null) return;
        Result = result;
        DialogResult = DialogResult.OK;
        Close();
    }
}
