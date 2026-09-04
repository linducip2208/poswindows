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
        ClientSize = new Size(420, 330);
        BackColor = Theme.Bg;

        var brand = Theme.Label(Strings.T("login_title"), 26, true, Theme.Accent);
        brand.Location = new Point(40, 22);
        var brandSub = Theme.Label(Strings.T("login_subtitle"), 10, false, Theme.Muted);
        brandSub.Location = new Point(42, 62);

        var userLabel = Theme.Label(Strings.T("login_user"), 9, true);
        userLabel.Location = new Point(40, 112);
        _user = Theme.Combo(340);
        _user.Location = new Point(40, 134);
        foreach (var u in Program.Services.Users.GetAll().Where(x => x.IsActive))
            _user.Items.Add(u.Username);
        if (_user.Items.Count == 0)
        {
            UiHelpers.Error("Belum ada pengguna. Jalankan setup awal.");
            Program.Services.Users.CreateAdmin("admin", "admin123", "Administrator");
            _user.Items.Add("admin");
        }
        _user.SelectedIndex = 0;

        var passLabel = Theme.Label(Strings.T("login_password"), 9, true);
        passLabel.Location = new Point(40, 176);
        _password = Theme.TextBox(340);
        _password.Location = new Point(40, 198);
        _password.UseSystemPasswordChar = true;

        _login = Theme.PrimaryButton(Strings.T("login_button"), 340, 40);
        _login.Location = new Point(40, 248);
        _login.Click += OnLogin;

        Controls.AddRange(new Control[] { brand, brandSub, userLabel, _user, passLabel, _password, _login });
        AcceptButton = _login;
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
        DialogResult = DialogResult.OK;
        Close();
    }
}

