using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

/// <summary>Simple operational expense register, backed by SQLite and linked to cash shift when paid in cash.</summary>
public sealed class ExpensesDialog : Form
{
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox _category = Theme.Combo(150);
    private readonly TextBox _description = Theme.TextBox(220);
    private readonly TextBox _amount = Theme.TextBox(120);
    private readonly ComboBox _method = Theme.Combo(110);
    private readonly DataGridView _grid = new();

    public ExpensesDialog()
    {
        Text = "Pengeluaran Operasional";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 560);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Pengeluaran operasional", 15, true, Theme.Accent);
        title.Location = new Point(20, 16);
        var hint = Theme.Label("Pengeluaran tunai pada shift aktif otomatis masuk ke kas keluar.", 9, false, Theme.Muted);
        hint.Location = new Point(21, 44);

        var toolbar = new Panel { Location = new Point(20, 74), Size = new Size(860, 78), BackColor = Theme.Card };
        AddField(toolbar, "Tanggal", _date, 12, 8, 100);
        AddField(toolbar, "Kategori", _category, 128, 8, 150);
        AddField(toolbar, "Keterangan", _description, 290, 8, 220);
        AddField(toolbar, "Jumlah", _amount, 522, 8, 120);
        AddField(toolbar, "Bayar", _method, 654, 8, 110);
        _category.Items.AddRange(new object[] { "Operasional", "Listrik", "Sewa", "Gaji", "Transportasi", "Perlengkapan", "Biaya bank", "Lain-lain" });
        _category.SelectedIndex = 0;
        _method.Items.AddRange(new object[] { "Cash", "Qris", "Debit", "Transfer" });
        _method.SelectedIndex = 0;
        _description.PlaceholderText = "Contoh: beli kantong plastik";
        _amount.PlaceholderText = "Rupiah";
        var add = Theme.PrimaryButton("Catat", 78, 30);
        add.Location = new Point(770, 29);
        add.Click += (s, e) => AddExpense();
        toolbar.Controls.Add(add);

        Theme.StyleGrid(_grid);
        _grid.Location = new Point(20, 168);
        _grid.Size = new Size(860, 330);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.Columns.Add("date", "Tanggal");
        _grid.Columns.Add("category", "Kategori");
        _grid.Columns.Add("description", "Keterangan");
        _grid.Columns.Add("amount", "Jumlah");
        _grid.Columns.Add("method", "Pembayaran");
        _grid.Columns.Add("user", "Petugas");
        Theme.MoneyColumn(_grid, "amount");
        _grid.Columns["date"].FillWeight = 16;
        _grid.Columns["category"].FillWeight = 18;
        _grid.Columns["description"].FillWeight = 32;
        _grid.Columns["amount"].FillWeight = 16;
        _grid.Columns["method"].FillWeight = 14;
        _grid.Columns["user"].FillWeight = 14;

        var close = Theme.SecondaryButton("Tutup", 90, 36);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Location = new Point(790, 512);
        close.Click += (s, e) => Close();
        Controls.AddRange(new Control[] { title, hint, toolbar, _grid, close });
        Shown += (s, e) => LoadRows();
    }

    private static void AddField(Control parent, string label, Control control, int x, int y, int width)
    {
        var l = Theme.Label(label, 8, true, Theme.Muted);
        l.Location = new Point(x, y);
        control.Location = new Point(x, y + 20);
        control.Width = width;
        parent.Controls.Add(l);
        parent.Controls.Add(control);
    }

    private void AddExpense()
    {
        if (!decimal.TryParse(_amount.Text.Replace(".", "").Replace(",", ""), out var amount) || amount <= 0)
        {
            UiHelpers.Warn("Jumlah pengeluaran tidak valid.");
            _amount.Focus();
            return;
        }
        try
        {
            Program.Services.Expenses.Add(_date.Value.Date, _category.Text, _description.Text,
                amount, _method.Text, Program.Session!.UserId, Program.Session.Username);
            Program.Session.DataChangedSinceBackup = true;
            _description.Clear();
            _amount.Clear();
            LoadRows();
            UiHelpers.Info("Pengeluaran berhasil dicatat.");
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Pengeluaran gagal dicatat: " + ex.Message);
        }
    }

    private void LoadRows()
    {
        var rows = Program.Services.Expenses.List(DateTime.Today.AddDays(-30), DateTime.Today);
        _grid.Rows.Clear();
        foreach (var row in rows)
            _grid.Rows.Add(row.ExpenseDate.ToString("dd/MM/yyyy"), row.Category, row.Description,
                row.Amount, row.PaymentMethod, row.Username);
    }
}
