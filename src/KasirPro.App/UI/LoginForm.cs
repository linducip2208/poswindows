using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

/// <summary>Login screen. PIN/password stored as PBKDF2 hash - never plaintext.</summary>
public class LoginForm : Form
{
    private readonly ComboBox _user;
    private readonly TextBox _password;
    private readonly Button _login;
    public long LoggedInUserId { get; private set; }
    private int _failedAttempts;
    private DateTime _lockedUntil = DateTime.MinValue;

    public LoginForm()
    {
        Text = "KasirPro - Login";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 470);
        BackColor = Theme.Bg;

        var hero = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Theme.Accent, Padding = new Padding(32) };
        var heroTitle = Theme.Label("KasirPro", 28, true, Color.White);
        heroTitle.Location = new Point(32, 34);
        var heroSub = Theme.Label("POS retail yang cepat, aman, dan tetap berjalan tanpa internet.", 12, false, Color.FromArgb(219, 234, 254));
        heroSub.MaximumSize = new Size(230, 80);
        heroSub.Location = new Point(32, 88);
        var benefits = Theme.Label("SCAN BARCODE CEPAT\n\nSHIFT & KAS TERKONTROL\n\nLAPORAN SIAP CETAK", 10, true, Color.White);
        benefits.Location = new Point(32, 220);
        var heroFoot = Theme.Label("Offline-first · Data tersimpan lokal", 9, false, Color.FromArgb(191, 219, 254));
        heroFoot.Location = new Point(32, 410);
        hero.Controls.AddRange(new Control[] { heroTitle, heroSub, benefits, heroFoot });

        var brand = Theme.Label(Strings.T("login_title"), 26, true, Theme.Accent);
        brand.Location = new Point(370, 38);
        var brandSub = Theme.Label(Strings.T("login_subtitle"), 10, false, Theme.Muted);
        brandSub.Location = new Point(372, 78);

        var userLabel = Theme.Label(Strings.T("login_user"), 9, true);
        userLabel.Location = new Point(370, 128);
        _user = Theme.Combo(340);
        _user.Location = new Point(370, 150);
        foreach (var u in Program.Services.Users.GetAll().Where(x => x.IsActive))
            _user.Items.Add(u.Username);
        if (_user.Items.Count == 0)
        {
            UiHelpers.Error("Belum ada pengguna. Jalankan setup awal.");
            Program.Services.Users.CreateAdmin("admin", "admin123", "Administrator");
            _user.Items.Add("admin");
        }
        // pre-select the last logged-in user (username only, no secrets)
        var lastUser = UiHelpers.Run(() => Program.Services.Settings.Get("last_user", ""));
        var idx = _user.Items.IndexOf(lastUser);
        _user.SelectedIndex = idx >= 0 ? idx : 0;
        var passLabel = Theme.Label(Strings.T("login_password"), 9, true);
        passLabel.Location = new Point(370, 192);
        _password = Theme.TextBox(340);
        _password.Location = new Point(370, 214);
        _password.UseSystemPasswordChar = true;

        _login = Theme.PrimaryButton(Strings.T("login_button"), 340, 40);
        _login.Location = new Point(370, 272);
        _login.Click += OnLogin;

        var hint = Theme.Label("Gunakan akun kasir yang sudah dibuat pada setup awal.", 9, false, Theme.Muted);
        hint.Location = new Point(372, 326);
        Controls.Add(hero);
        Controls.AddRange(new Control[] { brand, brandSub, userLabel, _user, passLabel, _password, _login, hint });
        AcceptButton = _login;
        _password.Focus();
    }

    private void OnLogin(object? sender, EventArgs e)
    {
        // offline lockout: 5 failures -> 1 minute lock
        if (DateTime.Now < _lockedUntil)
        {
            UiHelpers.Error($"Terlalu banyak percobaan gagal. Coba lagi dalam {Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds)} detik.");
            return;
        }
        var username = _user.Text.Trim();
        var password = _password.Text;
        if (username.Length == 0 || password.Length == 0)
        {
            UiHelpers.Warn("Isi pengguna dan PIN/password.");
            return;
        }
        var user = UiHelpers.Run(() => Program.Services.Users.Login(username, password));
        if (user == null)
        {
            _failedAttempts++;
            if (_failedAttempts >= 5)
            {
                _lockedUntil = DateTime.Now.AddMinutes(1);
                _failedAttempts = 0;
                UiHelpers.Error("5x gagal login. Aplikasi terkunci 1 menit.");
            }
            else
            {
                UiHelpers.Error($"Login gagal. Periksa pengguna dan PIN/password. ({_failedAttempts}/5)");
            }
            _password.Clear();
            _password.Focus();
            return;
        }
        LoggedInUserId = user.Id;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Settings.Set("last_user", user.Username);
            return null;
        });
        DialogResult = DialogResult.OK;
        Close();
    }
}

