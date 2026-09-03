using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>First-run wizard: store info -> admin PIN -> optional printer -> done.</summary>
public class FirstRunSetupForm : Form
{
    private readonly TextBox _storeName;
    private readonly TextBox _storeAddress;
    private readonly TextBox _storePhone;
    private readonly TextBox _adminUser;
    private readonly TextBox _pin1;
    private readonly TextBox _pin2;
    private readonly ComboBox _printer;
    private readonly Button _finish;
    private int _step;
    private Panel _page1;
    private Panel _page2;
    private Panel _page3;

    public FirstRunSetupForm()
    {
        Text = "KasirPro - Setup Awal";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 430);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Setup Awal KasirPro", 16, true, Theme.Accent);
        title.Location = new Point(28, 14);
        var sub = Theme.Label("Satu kali saja. Bisa diubah nanti di Data > Tools > Store Settings.", 9, false, Theme.Muted);
        sub.Location = new Point(29, 44);

        _page1 = BuildPage1(out _storeName, out _storeAddress, out _storePhone);
        _page2 = BuildPage2(out _adminUser, out _pin1, out _pin2);
        _page3 = BuildPage3(out _printer);

        _finish = Theme.PrimaryButton("LANJUT", 140, 38);
        _finish.Location = new Point(360, 370);
        _finish.Click += OnNext;

        var cancel = Theme.SecondaryButton("Keluar", 90, 38);
        cancel.Location = new Point(260, 370);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, sub, _page1, _page2, _page3, _finish, cancel });
        ShowStep(0);
    }

    private Panel BuildPage1(out TextBox name, out TextBox address, out TextBox phone)
    {
        var p = new Panel { Location = new Point(28, 78), Size = new Size(464, 280), BackColor = Theme.Bg };
        var l1 = Theme.Label("Nama Toko:", 9, true); l1.Location = new Point(0, 8);
        name = Theme.TextBox(440); name.Location = new Point(0, 30); name.Text = "Toko Saya";
        var l2 = Theme.Label("Alamat:", 9, true); l2.Location = new Point(0, 74);
        address = Theme.TextBox(440); address.Location = new Point(0, 96);
        var l3 = Theme.Label("Telepon:", 9, true); l3.Location = new Point(0, 140);
        phone = Theme.TextBox(440); phone.Location = new Point(0, 162);
        p.Controls.AddRange(new Control[] { l1, name, l2, address, l3, phone });
        return p;
    }

    private Panel BuildPage2(out TextBox adminUser, out TextBox pin1, out TextBox pin2)
    {
        var p = new Panel { Location = new Point(28, 78), Size = new Size(464, 280), BackColor = Theme.Bg };
        var l0 = Theme.Label("Buat akun administrator (PIN minimal 4 karakter):", 9, true);
        l0.Location = new Point(0, 8);
        var l1 = Theme.Label("Username:", 9, true); l1.Location = new Point(0, 44);
        adminUser = Theme.TextBox(300); adminUser.Location = new Point(0, 66); adminUser.Text = "admin";
        var l2 = Theme.Label("PIN / Password:", 9, true); l2.Location = new Point(0, 110);
        pin1 = Theme.TextBox(300); pin1.Location = new Point(0, 132); pin1.UseSystemPasswordChar = true;
        var l3 = Theme.Label("Ulangi PIN / Password:", 9, true); l3.Location = new Point(0, 176);
        pin2 = Theme.TextBox(300); pin2.Location = new Point(0, 198); pin2.UseSystemPasswordChar = true;
        p.Controls.AddRange(new Control[] { l0, l1, adminUser, l2, pin1, l3, pin2 });
        return p;
    }

    private Panel BuildPage3(out ComboBox printer)
    {
        var p = new Panel { Location = new Point(28, 78), Size = new Size(464, 280), BackColor = Theme.Bg };
        var l0 = Theme.Label("Printer struk (opsional, bisa diatur nanti):", 9, true);
        l0.Location = new Point(0, 8);
        printer = Theme.Combo(300); printer.Location = new Point(0, 30);
        printer.Items.Add("");
        try
        {
            foreach (var pr in PrinterService.GetInstalledPrinters()) printer.Items.Add(pr);
        }
        catch { }
        printer.SelectedIndex = 0;

        var l1 = Theme.Label("Kertas struk:", 9, true); l1.Location = new Point(0, 74);
        var paper = Theme.Combo(120); paper.Location = new Point(0, 96);
        paper.Items.AddRange(new object[] { "80", "58" });
        paper.SelectedIndex = 0;
        p.Tag = paper;
        p.Controls.AddRange(new Control[] { l0, printer, l1, paper });
        return p;
    }

    private void ShowStep(int step)
    {
        _step = step;
        _page1.Visible = step == 0;
        _page2.Visible = step == 1;
        _page3.Visible = step == 2;
        _finish.Text = step == 2 ? "SELESAI" : "LANJUT";
        Text = $"KasirPro - Setup Awal ({step + 1}/3)";
    }

    private void OnNext(object? sender, EventArgs e)
    {
        if (_step == 0)
        {
            if (string.IsNullOrWhiteSpace(_storeName.Text)) { UiHelpers.Warn("Nama toko wajib diisi."); return; }
            ShowStep(1);
            return;
        }
        if (_step == 1)
        {
            if (string.IsNullOrWhiteSpace(_adminUser.Text)) { UiHelpers.Warn("Username wajib diisi."); return; }
            if (_pin1.Text.Length < 4) { UiHelpers.Warn("PIN minimal 4 karakter."); return; }
            if (_pin1.Text != _pin2.Text) { UiHelpers.Warn("PIN tidak sama."); return; }
            ShowStep(2);
            return;
        }

        // final: persist everything
        try
        {
            var svc = Program.Services;
            var users = svc.Users.GetAll().Select(u => u.Username).ToHashSet();
            if (users.Contains(_adminUser.Text.Trim()))
            {
                UiHelpers.Warn("Username sudah dipakai.");
                ShowStep(1);
                return;
            }
            svc.Users.CreateAdmin(_adminUser.Text, _pin1.Text, "Administrator");
            svc.Settings.Set("store_name", _storeName.Text.Trim());
            svc.Settings.Set("store_address", _storeAddress.Text.Trim());
            svc.Settings.Set("store_phone", _storePhone.Text.Trim());
            svc.Settings.Set("printer_name", _printer.Text);
            svc.Settings.Set("receipt_paper", (string)((ComboBox)_page3.Tag!).SelectedItem!);
            svc.Settings.Set("setup_done", "1");
            UiHelpers.Info("Setup selesai! Silakan login.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            UiHelpers.Warn(ex.Message);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLogger.Instance.Error("setup failed", ex);
            UiHelpers.Error("Gagal menyimpan setup: " + ex.Message);
        }
    }
}
