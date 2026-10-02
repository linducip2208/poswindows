using KasirPro.Core.Domain;
using System.Text;
using KasirPro.Infrastructure;
using KasirPro.Licensing;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Store identity + backup + tax/loyalty/hardware settings.</summary>
public class StoreSettingsDialog : Form
{
    private readonly TextBox _name = Theme.TextBox(360);
    private readonly TextBox _address = Theme.TextBox(360);
    private readonly TextBox _phone = Theme.TextBox(200);
    private readonly TextBox _invoicePrefix = Theme.TextBox(80);
    private readonly CheckBox _autoBackup = new() { Text = "Backup otomatis saat tutup shift & keluar aplikasi", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _backupKeep = Theme.TextBox(60);
    private readonly TextBox _backupDirectory = Theme.TextBox(300);
    private readonly ComboBox _language = Theme.Combo(160);
    private readonly CheckBox _allowCredit = new() { Text = "Aktifkan piutang (credit) untuk pelanggan terdaftar", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _updateSource = Theme.TextBox(300);
    private readonly CheckBox _taxEnabled = new() { Text = "Aktifkan PPN", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _taxRate = Theme.TextBox(60);
    private readonly ComboBox _taxInclusive = Theme.Combo(170);
    private readonly CheckBox _loyaltyEnabled = new() { Text = "Aktifkan poin loyalitas", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _loyaltyEarn = Theme.TextBox(50);
    private readonly TextBox _loyaltyValue = Theme.TextBox(80);
    private readonly CheckBox _scaleEnabled = new() { Text = "Barcode timbangan aktif", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _scalePrefixes = Theme.TextBox(90);
    private readonly CheckBox _drawerEnabled = new() { Text = "Cash drawer kick saat cetak struk", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _autoLogout = Theme.TextBox(60);

    public StoreSettingsDialog()
    {
        Text = "Store Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 680);
        AutoScroll = true;
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var svc = Program.Services.Settings;
        var title = Theme.Label("Store Settings", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var y = 52;
        void Field(string label, Control c)
        {
            var l = Theme.Label(label, 9, true);
            l.Location = new Point(20, y);
            c.Location = new Point(180, y - 3);
            Controls.Add(l);
            Controls.Add(c);
            y += 32;
        }
        void Check(CheckBox c)
        {
            c.Location = new Point(180, y - 1);
            Controls.Add(c);
            y += 28;
        }

        Field("Nama Toko:", _name); _name.Text = svc.StoreName;
        Field("Alamat:", _address); _address.Text = svc.StoreAddress;
        Field("Telepon:", _phone); _phone.Text = svc.StorePhone;
        Field("Prefix Invoice:", _invoicePrefix); _invoicePrefix.Text = svc.InvoicePrefix;

        var lLang = Theme.Label("Bahasa (Language):", 9, true); lLang.Location = new Point(20, y);
        _language.Location = new Point(180, y - 3);
        _language.Items.AddRange(new object[] { "Bahasa Indonesia", "English" });
        _language.SelectedIndex = svc.Language == "en" ? 1 : 0;
        Controls.Add(lLang); Controls.Add(_language); y += 30;

        Check(_autoBackup); _autoBackup.Checked = svc.AutoBackup;
        var lKeep = Theme.Label("Simpan backup (file):", 9, true); lKeep.Location = new Point(20, y);
        _backupKeep.Location = new Point(180, y - 3); _backupKeep.Text = svc.BackupKeep.ToString();
        Controls.Add(lKeep); Controls.Add(_backupKeep); y += 30;
        var lBackupDir = Theme.Label("Folder backup eksternal:", 9, true); lBackupDir.Location = new Point(20, y);
        _backupDirectory.Location = new Point(180, y - 3); _backupDirectory.Text = svc.BackupDirectory;
        var browseBackup = Theme.SecondaryButton("Pilih", 70, 28); browseBackup.Location = new Point(488, y - 4);
        browseBackup.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Pilih lokasi backup SQLite di luar folder aplikasi" };
            if (Directory.Exists(_backupDirectory.Text)) dlg.SelectedPath = _backupDirectory.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) _backupDirectory.Text = dlg.SelectedPath;
        };
        Controls.Add(lBackupDir); Controls.Add(_backupDirectory); Controls.Add(browseBackup); y += 34;

        Check(_allowCredit); _allowCredit.Checked = svc.AllowCredit;
        Field("Folder update offline:", _updateSource); _updateSource.Text = svc.UpdateSource;

        var sep1 = Theme.Label("PAJAK (PPN)", 9, true, Theme.Accent); sep1.Location = new Point(20, y); y += 24;
        Check(_taxEnabled); _taxEnabled.Checked = svc.Get("tax_enabled", "0") == "1";
        var lRate = Theme.Label("Tarif (%) / mode:", 9, true); lRate.Location = new Point(20, y);
        _taxRate.Location = new Point(180, y - 3); _taxRate.Text = svc.Get("tax_rate_percent", "11");
        _taxInclusive.Location = new Point(255, y - 3);
        _taxInclusive.Items.AddRange(new object[] { "Include pajak", "Exclude pajak" });
        _taxInclusive.SelectedIndex = svc.Get("tax_inclusive", "1") != "0" ? 0 : 1;
        Controls.Add(lRate); Controls.Add(_taxRate); Controls.Add(_taxInclusive); y += 32;

        var sep2 = Theme.Label("LOYALITAS (POIN)", 9, true, Theme.Accent); sep2.Location = new Point(20, y); y += 24;
        Check(_loyaltyEnabled); _loyaltyEnabled.Checked = svc.Get("loyalty_enabled", "0") == "1";
        var lEarn = Theme.Label("Poin / Rp1000:", 9, true); lEarn.Location = new Point(20, y);
        _loyaltyEarn.Location = new Point(180, y - 3); _loyaltyEarn.Text = svc.Get("loyalty_earn_per_1000", "1");
        var lVal = Theme.Label("1 poin =", 9, true); lVal.Location = new Point(238, y);
        _loyaltyValue.Location = new Point(292, y - 3); _loyaltyValue.Text = svc.Get("loyalty_point_value", "100");
        var lVal2 = Theme.Label("Rp saat ditukar", 8, false, Theme.Muted); lVal2.Location = new Point(382, y);
        Controls.Add(lEarn); Controls.Add(_loyaltyEarn); Controls.Add(lVal); Controls.Add(_loyaltyValue); Controls.Add(lVal2); y += 32;

        var sep3 = Theme.Label("HARDWARE / KEAMANAN", 9, true, Theme.Accent); sep3.Location = new Point(20, y); y += 24;
        Check(_scaleEnabled); _scaleEnabled.Checked = svc.Get("scale_enabled", "0") == "1";
        var lScale = Theme.Label("Prefix barcode (koma):", 9, true); lScale.Location = new Point(20, y);
        _scalePrefixes.Location = new Point(180, y - 3); _scalePrefixes.Text = svc.Get("scale_prefixes", "21,02");
        Controls.Add(lScale); Controls.Add(_scalePrefixes); y += 30;
        Check(_drawerEnabled); _drawerEnabled.Checked = svc.Get("drawer_enabled", "0") == "1";
        var lLogout = Theme.Label("Auto-logout (menit):", 9, true); lLogout.Location = new Point(20, y);
        _autoLogout.Location = new Point(180, y - 3); _autoLogout.Text = svc.Get("auto_logout_minutes", "0");
        var hint = Theme.Label("0 = off", 8, false, Theme.Muted); hint.Location = new Point(255, y);
        Controls.Add(lLogout); Controls.Add(_autoLogout); Controls.Add(hint); y += 42;

        var save = Theme.PrimaryButton("SIMPAN", 120, 40);
        save.Location = new Point(460, y);
        save.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(360, y);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, save, cancel });
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var svc = Program.Services.Settings;
        var langChanged = (_language.SelectedIndex == 1 ? "en" : "id") != svc.Language;
        svc.Set("store_name", _name.Text.Trim(), Program.Session.UserId, Program.Session.Username);
        svc.Set("store_address", _address.Text.Trim());
        svc.Set("store_phone", _phone.Text.Trim());
        svc.Set("invoice_prefix", string.IsNullOrWhiteSpace(_invoicePrefix.Text) ? "INV" : _invoicePrefix.Text.Trim());
        svc.Set("auto_backup", _autoBackup.Checked ? "1" : "0");
        if (int.TryParse(_backupKeep.Text, out var keep)) svc.Set("backup_keep", keep.ToString());
        svc.Set("backup_dir", _backupDirectory.Text.Trim());
        svc.Set("allow_credit", _allowCredit.Checked ? "1" : "0");
        svc.Set("update_source", _updateSource.Text.Trim());
        svc.Set("tax_enabled", _taxEnabled.Checked ? "1" : "0");
        svc.Set("tax_rate_percent", string.IsNullOrWhiteSpace(_taxRate.Text) ? "11" : _taxRate.Text.Trim());
        svc.Set("tax_inclusive", _taxInclusive.SelectedIndex == 0 ? "1" : "0");
        svc.Set("loyalty_enabled", _loyaltyEnabled.Checked ? "1" : "0");
        svc.Set("loyalty_earn_per_1000", string.IsNullOrWhiteSpace(_loyaltyEarn.Text) ? "1" : _loyaltyEarn.Text.Trim());
        svc.Set("loyalty_point_value", string.IsNullOrWhiteSpace(_loyaltyValue.Text) ? "100" : _loyaltyValue.Text.Trim());
        svc.Set("scale_enabled", _scaleEnabled.Checked ? "1" : "0");
        svc.Set("scale_prefixes", _scalePrefixes.Text.Trim());
        svc.Set("drawer_enabled", _drawerEnabled.Checked ? "1" : "0");
        svc.Set("auto_logout_minutes", _autoLogout.Text.Trim());

        var newLang = _language.SelectedIndex == 1 ? "en" : "id";
        if (newLang != Strings.Lang) { Strings.Lang = newLang; langChanged = true; }

        if (langChanged)
            UiHelpers.Info("Bahasa diterapkan pada menu utama. Halaman lain akan ikut bahasa baru setelah aplikasi dibuka ulang.");
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>User management: create cashier/admin, reset PIN, activate/deactivate.</summary>
public class UserManagementDialog : Form
{
    private readonly DataGridView _grid = new();

    public UserManagementDialog()
    {
        Text = "User Management";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 420);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("User Management", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var toolbar = new Panel { Location = new Point(20, 50), Size = new Size(580, 40), BackColor = Theme.Bg };
        var addCashier = Theme.PrimaryButton("+ Cashier", 110);
        addCashier.Location = new Point(0, 4);
        addCashier.Click += (s, e) => AddUser(false);
        var addAdmin = Theme.SecondaryButton("+ Admin", 100);
        addAdmin.Location = new Point(116, 4);
        addAdmin.Click += (s, e) => AddUser(true);
        var resetPin = Theme.SecondaryButton("Reset PIN", 100);
        resetPin.Location = new Point(222, 4);
        resetPin.Click += (s, e) => ResetPin();
        var toggle = Theme.SecondaryButton("Aktif/Nonaktif", 120);
        toggle.Location = new Point(328, 4);
        toggle.Click += (s, e) => ToggleActive();
        toolbar.Controls.AddRange(new Control[] { addCashier, addAdmin, resetPin, toggle });

        Theme.StyleGrid(_grid);
        _grid.Location = new Point(20, 100);
        _grid.Size = new Size(580, 270);
        _grid.Columns.Add("username", "Username");
        _grid.Columns.Add("fullname", "Nama");
        _grid.Columns.Add("role", "Role");
        _grid.Columns.Add("active", "Status");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;

        var close = Theme.SecondaryButton("Tutup", 90, 34);
        close.Location = new Point(510, 378);
        close.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, toolbar, _grid, close });
        LoadData();
    }

    private void LoadData()
    {
        var users = UiHelpers.Run(() => Program.Services.Users.GetAll()) ?? new();
        _grid.Rows.Clear();
        foreach (var u in users)
            _grid.Rows.Add(u.Username, u.FullName, u.Role, u.IsActive ? "Aktif" : "Nonaktif", u.Id);
    }

    private long SelectedId() =>
        _grid.CurrentRow == null ? 0 : Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);

    private void AddUser(bool admin)
    {
        var username = InputDialog.Show("Username baru:", admin ? "Tambah Admin" : "Tambah Cashier");
        if (string.IsNullOrWhiteSpace(username)) return;
        var pin = InputDialog.Show("PIN/password (min 4):", "PIN");
        if (string.IsNullOrWhiteSpace(pin)) return;
        UiHelpers.Run<object?>(() =>
        {
            if (admin) Program.Services.Users.CreateAdmin(username, pin, username);
            else Program.Services.Users.CreateCashier(username, pin, username);
            return null;
        });
        LoadData();
    }

    private void ResetPin()
    {
        if (_grid.CurrentRow == null) return;
        var username = _grid.CurrentRow.Cells["username"].Value?.ToString();
        var pin = InputDialog.Show($"PIN/password baru untuk '{username}':", "Reset PIN");
        if (string.IsNullOrWhiteSpace(pin)) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Users.SetPassword(SelectedId(), pin);
            return null;
        });
        UiHelpers.Info("PIN diperbarui.");
    }

    private void ToggleActive()
    {
        if (_grid.CurrentRow == null) return;
        var id = SelectedId();
        if (id == Program.Session.UserId)
        {
            UiHelpers.Warn("Tidak bisa menonaktifkan akun sendiri.");
            return;
        }
        var active = _grid.CurrentRow.Cells["active"].Value?.ToString() == "Aktif";
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Users.SetActive(id, !active);
            return null;
        });
        LoadData();
    }
}

public class PrinterSetupDialog : Form
{
    private readonly ComboBox _printers = Theme.Combo(340);
    private readonly ComboBox _paper = Theme.Combo(100);

    public PrinterSetupDialog()
    {
        Text = "Printer Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 240);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Printer Setup", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var l1 = Theme.Label("Printer struk:", 9, true); l1.Location = new Point(20, 62);
        _printers.Location = new Point(20, 84);
        _printers.DropDownStyle = ComboBoxStyle.DropDownList;
        _printers.Items.Add("");
        try
        {
            foreach (var p in PrinterService.GetInstalledPrinters()) _printers.Items.Add(p);
        }
        catch { }
        var saved = Program.Services.Settings.PrinterName;
        _printers.SelectedIndex = _printers.Items.Contains(saved) ? _printers.Items.IndexOf(saved) : 0;

        var l2 = Theme.Label("Kertas:", 9, true); l2.Location = new Point(20, 124);
        _paper.Location = new Point(20, 146);
        _paper.Items.AddRange(new object[] { "80", "58" });
        _paper.SelectedItem = Program.Services.Settings.ReceiptPaper;

        var test = Theme.SecondaryButton("Print Test", 110, 40);
        test.Location = new Point(20, 186);
        test.Click += OnTest;
        var save = Theme.PrimaryButton("SIMPAN", 110, 40);
        save.Location = new Point(330, 186);
        save.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(230, 186);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, l1, _printers, l2, _paper, test, save, cancel });
    }

    private void OnTest(object? sender, EventArgs e)
    {
        try
        {
            var svc = new PrinterService(Program.Services.Settings);
            svc.PrintTest(_printers.Text, _paper.Text);
            UiHelpers.Info("Test page terkirim ke printer.");
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Test print gagal: " + ex.Message);
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        Program.Services.Settings.Set("printer_name", _printers.Text);
        Program.Services.Settings.Set("receipt_paper", _paper.Text);
        UiHelpers.Info("Pengaturan printer tersimpan.");
        DialogResult = DialogResult.OK;
        Close();
    }
}

public class ReceiptSetupDialog : Form
{
    private readonly TextBox _footer = Theme.TextBox(360);
    private readonly TextBox _copies = Theme.TextBox(50);

    public ReceiptSetupDialog()
    {
        Text = "Receipt Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 200);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Receipt Setup", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var l1 = Theme.Label("Footer struk:", 9, true); l1.Location = new Point(20, 62);
        _footer.Location = new Point(20, 84);
        _footer.Text = Program.Services.Settings.ReceiptFooter;

        var l2 = Theme.Label("Jumlah salinan:", 9, true); l2.Location = new Point(20, 124);
        _copies.Location = new Point(130, 121);
        _copies.Text = Program.Services.Settings.Get("receipt_copies", "1");

        var save = Theme.PrimaryButton("SIMPAN", 110, 40);
        save.Location = new Point(330, 150);
        save.Click += (s, e) =>
        {
            Program.Services.Settings.Set("receipt_footer", _footer.Text);
            Program.Services.Settings.Set("receipt_copies", _copies.Text);
            UiHelpers.Info("Tersimpan.");
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(230, 150);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, l1, _footer, l2, _copies, save, cancel });
    }
}

public class RestoreDialog : Form
{
    private readonly MainForm _owner;
    private readonly ListView _list = new();

    public RestoreDialog(MainForm owner)
    {
        _owner = owner;
        Text = "Restore Database";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 460);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Restore Database", 14, true, Theme.Danger);
        title.Location = new Point(20, 14);
        var warn = Theme.Label("Database saat ini akan dibackup dulu sebelum restore. Aplikasi akan memuat ulang data.", 9, false, Theme.Muted);
        warn.Location = new Point(21, 46);
        warn.Size = new Size(580, 30);

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.Location = new Point(20, 84);
        _list.Size = new Size(580, 300);
        _list.Columns.Add("File", 320);
        _list.Columns.Add("Ukuran", 100);
        _list.Columns.Add("Tanggal", 140);

        var btnBrowse = Theme.SecondaryButton("Pilih file lain...", 140, 34);
        btnBrowse.Location = new Point(20, 400);
        btnBrowse.Click += OnBrowse;
        var btnRestore = Theme.DangerButton("RESTORE", 120, 34);
        btnRestore.Location = new Point(480, 400);
        btnRestore.Click += OnRestore;
        var btnClose = Theme.SecondaryButton("Tutup", 90, 34);
        btnClose.Location = new Point(350, 400);
        btnClose.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, warn, _list, btnBrowse, btnRestore, btnClose });
        LoadBackups();
    }

    private void LoadBackups()
    {
        _list.Items.Clear();
        var backups = UiHelpers.Run(() => Program.Services.Backup.ListBackups()) ?? new();
        foreach (var (name, size, created) in backups)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add($"{size / 1024:N0} KB");
            item.SubItems.Add(created.ToString("dd/MM/yyyy HH:mm"));
            _list.Items.Add(item);
        }
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "SQLite Database|*.db", Title = "Pilih file backup" };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var item = new ListViewItem(dlg.FileName);
            item.SubItems.Add("file");
            item.SubItems.Add("");
            _list.Items.Insert(0, item);
            _list.Items[0].Selected = true;
        }
    }

    private void OnRestore(object? sender, EventArgs e)
    {
        if (_list.SelectedItems.Count == 0) { UiHelpers.Warn("Pilih file backup."); return; }
        var file = _list.SelectedItems[0].Text;
        if (!File.Exists(file)) { UiHelpers.Warn("File tidak ditemukan."); return; }
        if (!UiHelpers.Confirm("Restore akan MENGGANTI database saat ini.\nDatabase lama otomatis dibackup dulu.\n\nLanjutkan?")) return;

        try
        {
            Program.Services.Backup.RestoreBackup(file, Program.Session.UserId, Program.Session.Username);
            Program.Session.DataChangedSinceBackup = true;
            UiHelpers.Info("Restore berhasil. Aplikasi akan memuat ulang data.");
            _owner.Close(); // restart flow on next launch
            Close();
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Restore gagal: " + ex.Message);
        }
    }
}

public class MaintenanceDialog : Form
{
    private readonly MainForm _owner;

    public MaintenanceDialog(MainForm owner)
    {
        _owner = owner;
        Text = "Database Maintenance";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 330);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Database Maintenance", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var info = Theme.Label(
            $"Database: {AppPaths.DatabaseFile}\n" +
            $"Ukuran: {new FileInfo(AppPaths.DatabaseFile).Length / 1024:N0} KB\n" +
            $"Backup: {AppPaths.BackupDir}", 9, false, Theme.Muted);
        info.Location = new Point(20, 50);
        info.Size = new Size(440, 60);

        var vacuum = Theme.PrimaryButton("VACUUM + Integrity Check", 240, 40);
        vacuum.Location = new Point(20, 130);
        vacuum.Click += OnVacuum;

        var audit = Theme.SecondaryButton("Lihat Audit Log", 160, 40);
        audit.Location = new Point(20, 184);
        audit.Click += (s, e) => ShowAudit();

        var seed = Theme.DangerButton("Load / Reset Demo Data", 220, 40);
        seed.Location = new Point(20, 238);
        seed.Click += OnSeed;
        var seedWarn = Theme.Label("Demo: 100 produk, 25 pelanggan, 100 transaksi. Data bisnis saat ini DIHAPUS.", 8, false, Theme.Danger);
        seedWarn.Location = new Point(20, 282);

        var close = Theme.SecondaryButton("Tutup", 90, 40);
        close.Location = new Point(370, 238);
        close.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, info, vacuum, audit, seed, seedWarn, close });
    }

    private void OnVacuum(object? sender, EventArgs e)
    {
        var result = UiHelpers.Run(() => Program.Services.Backup.Maintain());
        UiHelpers.Info($"Integrity check: {result.Integrity}\nVACUUM selesai. Database optimal.");
    }

    private void ShowAudit()
    {
        var logs = UiHelpers.Run(() => Program.Services.Audit.Recent(50)) ?? new();
        var text = string.Join("\n", logs.Select(l =>
            $"{l.CreatedAt:dd/MM HH:mm} [{l.Username}] {l.Action} {l.Entity}{(l.EntityId > 0 ? "#" + l.EntityId : "")} - {l.Description}"));
        UiHelpers.Info(string.IsNullOrEmpty(text) ? "Belum ada log." : text, "Audit Log (50 terakhir)");
    }

    private void OnSeed(object? sender, EventArgs e)
    {
        if (!UiHelpers.Confirm("SEMUA data bisnis (produk, stok, transaksi) akan DIHAPUS dan diganti data demo.\n\nLanjutkan?")) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Seeder.ResetAndSeed(Program.Session!.UserId, Program.Session.Username);
            Program.Session.DataChangedSinceBackup = true;
            return null;
        });
        UiHelpers.Info("Data demo siap. Dashboard & laporan kini berisi data uji.");
    }
}

public class LicenseInfoDialog : Form
{
    public LicenseInfoDialog()
    {
        Text = "License Information";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 380);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("License Information", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var check = Program.Licensing.CheckStoredLicense();
        var sb = new StringBuilder();
        sb.AppendLine("Product:        KasirPro Windows");
        sb.AppendLine("License Status: " + (check.Activated ? "Activated" : "NOT ACTIVATED"));
        if (check.Payload != null)
        {
            sb.AppendLine("Licensed To:    " + check.Payload.Customer);
            sb.AppendLine("License Type:   " + check.Payload.LicenseType);
            sb.AppendLine("License ID:     " + check.Payload.LicenseId);
            var issued = DateTime.TryParse(check.Payload.IssuedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var i) ? i.ToString("dd MMMM yyyy") : check.Payload.IssuedAt;
            sb.AppendLine("Issued:         " + issued);
            if (!string.IsNullOrEmpty(check.Payload.ExpiresAt))
            {
                var exp = DateTime.Parse(check.Payload.ExpiresAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None);
                sb.AppendLine("Expires:        " + exp.ToString("dd MMMM yyyy"));
            }
            else sb.AppendLine("Expires:        never (Lifetime)");
        }
        else if (!check.Activated)
        {
            sb.AppendLine("Detail: " + check.Message);
        }
        sb.AppendLine();
        sb.AppendLine("Machine ID:     " + MachineId.Get());

        var body = new Label
        {
            Text = sb.ToString(),
            Font = new Font("Consolas", 10f),
            Location = new Point(20, 54),
            Size = new Size(440, 250),
            ForeColor = Theme.Text
        };

        var close = Theme.SecondaryButton("Tutup", 90, 36);
        close.Location = new Point(370, 326);
        close.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, body, close });
    }
}

public class AboutDialog : Form
{
    public AboutDialog()
    {
        Text = "About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 280);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var brand = Theme.Label("KASIRPRO", 24, true, Theme.Accent);
        brand.Location = new Point(20, 18);
        var sub = Theme.Label("POS Retail - Windows Offline", 11, false, Theme.Muted);
        sub.Location = new Point(21, 56);
        var body = Theme.Label(
            "Version 1.0.0\n\n" +
            "Aplikasi kasir offline untuk toko retail:\n" +
            "- 100% offline (SQLite lokal, tanpa internet)\n" +
            "- Barcode scanner (USB HID)\n" +
            "- Thermal printer 58mm / 80mm\n" +
            "- Backup & restore\n" +
            "- Lisensi offline dengan tanda tangan digital\n\n" +
            "Database: " + AppPaths.DatabaseFile, 9, false, Theme.Text);
        body.Location = new Point(21, 90);
        body.Size = new Size(400, 140);

        var close = Theme.PrimaryButton("OK", 90, 36);
        close.Location = new Point(330, 230);
        close.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { brand, sub, body, close });
    }
}

