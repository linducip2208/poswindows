namespace KasirPro.App.UI;

public sealed class PeriodCloseDialog : Form
{
    private readonly TextBox _period = Theme.TextBox(100);
    private readonly TextBox _notes = Theme.TextBox(300);
    private readonly DataGridView _grid = new();

    public PeriodCloseDialog()
    {
        Text = "Penutupan Periode";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 440);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Penutupan periode", 15, true, Theme.Accent);
        title.Location = new Point(20, 16);
        var hint = Theme.Label("Periode yang ditutup tidak menerima transaksi baru tanpa dibuka kembali.", 9, false, Theme.Muted);
        hint.Location = new Point(21, 44);
        var periodLabel = Theme.Label("Periode (YYYY-MM)", 9, true);
        periodLabel.Location = new Point(20, 82);
        _period.Location = new Point(150, 78);
        _period.Text = DateTime.Today.AddMonths(-1).ToString("yyyy-MM");
        var notesLabel = Theme.Label("Catatan", 9, true);
        notesLabel.Location = new Point(270, 82);
        _notes.Location = new Point(320, 78);
        var close = Theme.PrimaryButton("Tutup periode", 120, 36);
        close.Location = new Point(500, 76);
        close.Click += (s, e) => ClosePeriod();

        Theme.StyleGrid(_grid);
        _grid.Location = new Point(20, 130);
        _grid.Size = new Size(600, 245);
        _grid.Columns.Add("period", "Periode");
        _grid.Columns.Add("closed", "Ditutup");
        _grid.Columns.Add("by", "Oleh");
        _grid.Columns.Add("notes", "Catatan");
        _grid.Columns["period"].FillWeight = 20;
        _grid.Columns["closed"].FillWeight = 24;
        _grid.Columns["by"].FillWeight = 20;
        _grid.Columns["notes"].FillWeight = 36;

        var reopen = Theme.WarningButton("Buka kembali", 120, 36);
        reopen.Location = new Point(370, 390);
        reopen.Click += (s, e) => ReopenPeriod();
        var closeForm = Theme.SecondaryButton("Tutup", 90, 36);
        closeForm.Location = new Point(520, 390);
        closeForm.Click += (s, e) => Close();
        Controls.AddRange(new Control[] { title, hint, periodLabel, _period, notesLabel, _notes, close, _grid, reopen, closeForm });
        Shown += (s, e) => LoadRows();
    }

    private void ClosePeriod()
    {
        if (!Program.Session!.IsAdmin) { UiHelpers.Warn("Hanya Owner/Admin yang boleh menutup periode."); return; }
        try
        {
            Program.Services.Periods.Close(_period.Text.Trim(), Program.Session.UserId, Program.Session.Username, _notes.Text.Trim());
            LoadRows();
            UiHelpers.Info("Periode berhasil ditutup.");
        }
        catch (Exception ex) { UiHelpers.Error("Periode gagal ditutup: " + ex.Message); }
    }

    private void ReopenPeriod()
    {
        if (!Program.Session!.IsAdmin) { UiHelpers.Warn("Hanya Owner/Admin yang boleh membuka periode."); return; }
        if (!UiHelpers.Confirm($"Buka kembali periode {_period.Text.Trim()}?")) return;
        try
        {
            Program.Services.Periods.Reopen(_period.Text.Trim(), Program.Session.UserId, Program.Session.Username);
            LoadRows();
        }
        catch (Exception ex) { UiHelpers.Error("Periode gagal dibuka: " + ex.Message); }
    }

    private void LoadRows()
    {
        _grid.Rows.Clear();
        foreach (var row in Program.Services.Periods.List())
            _grid.Rows.Add(row.Period, row.ClosedAt.ToString("dd/MM/yyyy HH:mm"), row.ClosedBy, row.Notes);
    }
}
