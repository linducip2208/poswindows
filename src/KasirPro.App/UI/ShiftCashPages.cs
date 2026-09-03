using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Open shift: input opening cash. Only one open shift per user.</summary>
public class OpenShiftDialog : Form
{
    private readonly TextBox _openingCash = Theme.TextBox(200);

    public OpenShiftDialog()
    {
        Text = "Open Shift";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 190);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var existing = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
        if (existing != null)
        {
            UiHelpers.Info($"Shift sudah terbuka sejak {existing.OpenedAt:HH:mm}.");
            Close();
            return;
        }

        var title = Theme.Label("Buka Shift Kasir", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);
        var l1 = Theme.Label("Modal awal (Rp):", 9, true);
        l1.Location = new Point(20, 62);
        _openingCash.Location = new Point(20, 86);
        _openingCash.TextAlign = HorizontalAlignment.Right;
        _openingCash.Text = "0";

        var ok = Theme.SuccessButton("BUKA SHIFT", 150, 40);
        ok.Location = new Point(230, 128);
        ok.Click += OnOpen;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(130, 128);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, l1, _openingCash, ok, cancel });
        _openingCash.SelectAll();
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        if (!decimal.TryParse(_openingCash.Text.Replace(".", "").Replace(",", ""), out var cash) || cash < 0)
        {
            UiHelpers.Warn("Modal awal tidak valid.");
            return;
        }
        var session = UiHelpers.Run(() =>
            Program.Services.Cash.OpenSession(Program.Session!.UserId, Program.Session.Username, cash));
        if (session == null) return;
        UiHelpers.Info($"Shift dibuka pada {session.OpenedAt:HH:mm} dengan modal {Money.Format(cash)}.");
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Close shift: system cash vs actual cash + difference, then stores summary + auto backup.</summary>
public class CloseShiftDialog : Form
{
    private readonly CashSession _session;
    private readonly TextBox _actual = Theme.TextBox(200);
    private readonly Label _lblSystem;
    private readonly Label _lblDiff;

    public CloseShiftDialog()
    {
        _session = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
        Text = "Close Shift";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 330);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Tutup Shift", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);
        var info = Theme.Label(
            $"Dibuka: {_session.OpenedAt:dd/MM/yyyy HH:mm}\nKasir: {_session.UserName}", 9, false, Theme.Muted);
        info.Location = new Point(20, 46);
        info.Size = new Size(380, 40);

        _lblSystem = Theme.Label("System Cash: Rp 0", 12, true);
        _lblSystem.Location = new Point(20, 100);

        var l2 = Theme.Label("Uang fisik di kasir (Rp):", 9, true);
        l2.Location = new Point(20, 140);
        _actual.Location = new Point(20, 164);
        _actual.TextAlign = HorizontalAlignment.Right;

        _lblDiff = Theme.Label("Selisih: Rp 0", 11, true);
        _lblDiff.Location = new Point(20, 200);
        _actual.TextChanged += (s, e) => UpdateDiff();

        var detail = Theme.Label(
            $"Penjualan tunai: {Money.Format(_session.CashSales)}\n" +
            $"Pelunasan piutang (cash): {Money.Format(_session.DebtPayments)}\n" +
            $"Cash In: {Money.Format(_session.CashIn)}\n" +
            $"Cash Out: {Money.Format(_session.CashOut)}", 9, false, Theme.Muted);
        detail.Location = new Point(20, 236);
        detail.Size = new Size(380, 68);

        var ok = Theme.DangerButton("TUTUP SHIFT", 150, 40);
        ok.Location = new Point(260, 278);
        ok.Click += OnClose;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(160, 278);
        cancel.Click += (s, e) => Close();

        var expected = Money.Round(_session.OpeningCash + _session.CashSales + _session.DebtPayments
            + _session.CashIn - _session.CashOut);
        _lblSystem.Text = $"System Cash: {Money.Format(expected)}";

        Controls.AddRange(new Control[] { title, info, _lblSystem, l2, _actual, _lblDiff, detail, ok, cancel });
        if (_session.Id == 0) Close();
    }

    private void UpdateDiff()
    {
        var expected = Money.Round(_session.OpeningCash + _session.CashSales + _session.DebtPayments
            + _session.CashIn - _session.CashOut);
        if (decimal.TryParse(_actual.Text.Replace(".", "").Replace(",", ""), out var actual))
        {
            var diff = Money.Round(actual - expected);
            _lblDiff.Text = "Selisih: " + Money.Format(diff);
            _lblDiff.ForeColor = diff == 0 ? Theme.Success : Theme.Danger;
        }
    }

    private void OnClose(object? sender, EventArgs e)
    {
        if (!decimal.TryParse(_actual.Text.Replace(".", "").Replace(",", ""), out var actual))
        {
            UiHelpers.Warn("Masukkan jumlah uang fisik di kasir.");
            return;
        }
        var result = UiHelpers.Run(() =>
            Program.Services.Cash.CloseSession(_session.Id, actual, "", Program.Session!.UserId, Program.Session.Username));
        if (result == null) return;
        Program.Session!.DataChangedSinceBackup = true;

        // auto backup on close shift
        if (Program.Services.Settings.AutoBackup)
        {
            UiHelpers.Run(() => Program.Services.Backup.CreateBackup("close-shift", Program.Session.UserId, Program.Session.Username));
        }

        UiHelpers.Info(
            "Shift ditutup.\n" +
            $"System cash: {Money.Format(result.ClosingCashExpected)}\n" +
            $"Uang fisik: {Money.Format(result.ClosingCashActual)}\n" +
            $"Selisih: {Money.Format(result.Difference)}");
        DialogResult = DialogResult.OK;
        ((MainForm?)Owner)?.UpdateStatusBar();
        Close();
    }
}

/// <summary>Cash In / Cash Out during an open shift.</summary>
public class CashMovementDialog : Form
{
    private readonly bool _isIn;
    private readonly TextBox _amount = Theme.TextBox(200);
    private readonly TextBox _notes = Theme.TextBox(330);

    public CashMovementDialog(bool isCashIn)
    {
        _isIn = isCashIn;
        Text = isCashIn ? "Cash In" : "Cash Out";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 220);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var session = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
        if (session == null)
        {
            UiHelpers.Warn("Buka shift terlebih dahulu (Data > Transaction > Open Shift).");
            Load += (s, e) => Close();
            return;
        }

        var title = Theme.Label(isCashIn ? "Uang Masuk (Cash In)" : "Uang Keluar (Cash Out)", 14, true,
            isCashIn ? Theme.Success : Theme.Danger);
        title.Location = new Point(20, 14);

        var l1 = Theme.Label("Jumlah (Rp):", 9, true);
        l1.Location = new Point(20, 60);
        _amount.Location = new Point(20, 84);
        _amount.TextAlign = HorizontalAlignment.Right;

        var l2 = Theme.Label("Keterangan:", 9, true);
        l2.Location = new Point(20, 122);
        _notes.Location = new Point(20, 144);

        var ok = isCashIn ? Theme.SuccessButton("SIMPAN", 120, 40) : Theme.DangerButton("SIMPAN", 120, 40);
        ok.Location = new Point(280, 160);
        ok.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(180, 160);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, l1, _amount, l2, _notes, ok, cancel });
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (!decimal.TryParse(_amount.Text.Replace(".", "").Replace(",", ""), out var amount) || amount <= 0)
        {
            UiHelpers.Warn("Jumlah tidak valid.");
            return;
        }
        var session = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
        if (session == null) return;
        var notes = string.IsNullOrWhiteSpace(_notes.Text) ? (_isIn ? "Cash in" : "Cash out") : _notes.Text.Trim();

        var ok = UiHelpers.Run<object?>(() =>
        {
            if (_isIn)
                Program.Services.Cash.CashIn(session.Id, amount, notes, Program.Session.UserId, Program.Session.Username);
            else
                Program.Services.Cash.CashOut(session.Id, amount, notes, Program.Session.UserId, Program.Session.Username);
            Program.Session.DataChangedSinceBackup = true;
            return null;
        });
        UiHelpers.Info("Tersimpan.");
        DialogResult = DialogResult.OK;
        ((MainForm?)Owner)?.UpdateStatusBar();
        Close();
    }
}

public class ShiftHistoryPage : Panel, IPage
{
    private readonly DataGridView _grid = new();
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };

    public ShiftHistoryPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Shift History", "Riwayat sesi kasir");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg };
        _from.Location = new Point(16, 8);
        _to.Location = new Point(132, 8);
        var btn = Theme.PrimaryButton("Refresh", 100);
        btn.Location = new Point(248, 6);
        btn.Click += (s, e) => LoadData();
        toolbar.Controls.AddRange(new Control[] { _from, _to, btn });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("id", "Session");
        _grid.Columns.Add("cashier", "Cashier");
        _grid.Columns.Add("opened", "Dibuka");
        _grid.Columns.Add("closed", "Ditutup");
        _grid.Columns.Add("opening", "Modal");
        _grid.Columns.Add("sales", "Cash Sales");
        _grid.Columns.Add("cin", "Cash In");
        _grid.Columns.Add("cout", "Cash Out");
        _grid.Columns.Add("expected", "System");
        _grid.Columns.Add("actual", "Fisik");
        _grid.Columns.Add("diff", "Selisih");
        _grid.Columns.Add("status", "Status");
        foreach (var col in new[] { "opening", "sales", "cin", "cout", "expected", "actual", "diff" })
            Theme.MoneyColumn(_grid, col);
        _grid.CellDoubleClick += (s, e) => ShowMovements();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var list = UiHelpers.Run(() => Program.Services.Cash.History(_from.Value, _to.Value)) ?? new();
        _grid.Rows.Clear();
        foreach (var s in list)
        {
            var idx = _grid.Rows.Add("#" + s.Id, s.UserName, s.OpenedAt.ToString("dd/MM/yyyy HH:mm"),
                s.ClosedAt?.ToString("dd/MM/yyyy HH:mm") ?? "-",
                s.OpeningCash, s.CashSales, s.CashIn, s.CashOut,
                s.Status == "CLOSED" ? s.ClosingCashExpected : 0,
                s.Status == "CLOSED" ? s.ClosingCashActual : 0,
                s.Status == "CLOSED" ? s.Difference : 0, s.Status);
            if (s.Status == "CLOSED" && s.Difference != 0)
                _grid.Rows[idx].Cells["diff"].Style.ForeColor = Theme.Danger;
        }
    }

    private void ShowMovements()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["id"].Value.ToString().TrimStart('#'));
        var movements = UiHelpers.Run(() => Program.Services.Cash.GetMovements(id)) ?? new();
        var text = string.Join("\n", movements.Select(m =>
            $"{m.CreatedAt:HH:mm} {m.Type,-10} {(m.Direction == KasirPro.Core.Domain.StockDirection.In ? "+" : "-")}{Money.FormatPlain(m.Amount)}  {m.Notes}"));
        UiHelpers.Info(string.IsNullOrEmpty(text) ? "Belum ada mutasi kas." : text, "Mutasi Kas Session #" + id);
    }
}

