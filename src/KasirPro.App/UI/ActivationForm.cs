using KasirPro.Licensing;

namespace KasirPro.App.UI;

/// <summary>Offline license activation screen shown when license.dat is missing/invalid.</summary>
public class ActivationForm : Form
{
    private readonly LicenseActivation _licensing = Program.Licensing;
    private readonly TextBox _tokenBox;
    private readonly Label _machineId;
    private readonly Button _activate;

    public ActivationForm()
    {
        Text = "KasirPro - Aktivasi Lisensi";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 470);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var brand = Theme.Label("KASIRPRO", 26, true, Theme.Accent);
        brand.Location = new Point(30, 18);
        var brandSub = Theme.Label("Offline License Activation", 11, false, Theme.Muted);
        brandSub.Location = new Point(32, 60);

        var midLabel = Theme.Label("Machine ID komputer ini:", 9, true);
        midLabel.Location = new Point(30, 100);
        _machineId = Theme.Label(MachineId.Get(), 13, true, Theme.Text);
        _machineId.Location = new Point(30, 124);
        var copyBtn = Theme.SecondaryButton("COPY MACHINE ID", 170);
        copyBtn.Location = new Point(30, 150);
        copyBtn.Click += (s, e) =>
        {
            Clipboard.SetText(MachineId.Get());
            copyBtn.Text = "TERSALIN!";
            Task.Run(async () =>
            {
                await Task.Delay(1500);
                BeginInvoke(() => copyBtn.Text = "COPY MACHINE ID");
            });
        };

        var keyLabel = Theme.Label("License Key (paste dari KasirPro License Manager):", 9, true);
        keyLabel.Location = new Point(30, 200);
        _tokenBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(30, 224),
            Size = new Size(500, 120),
            PlaceholderText = "KPR1.<payload>.<signature>"
        };

        _activate = Theme.PrimaryButton("ACTIVATE", 200, 40);
        _activate.Location = new Point(30, 360);
        _activate.Click += OnActivate;

        var note = Theme.Label("License terikat pada Machine ID. Hubungi developer untuk mendapatkan key.", 8, false, Theme.Muted);
        note.Location = new Point(30, 420);
        note.Size = new Size(500, 30);

        Controls.AddRange(new Control[] { brand, brandSub, midLabel, _machineId, copyBtn, keyLabel, _tokenBox, _activate, note });
        AcceptButton = _activate;
    }

    private void OnActivate(object? sender, EventArgs e)
    {
        var token = _tokenBox.Text.Trim();
        if (token.Length == 0)
        {
            UiHelpers.Warn("Masukkan license key terlebih dahulu.");
            return;
        }
        if (token.Contains('\n') || token.Contains('\r'))
            token = token.Replace("\r", "").Replace("\n", "").Trim();

        try
        {
            var result = _licensing.Activate(token);
            if (result.Status == LicenseVerifyStatus.Valid)
            {
                UiHelpers.Info("Aktivasi berhasil! Aplikasi siap digunakan.\n\nPelanggan: " + result.Payload!.Customer);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                UiHelpers.Error("Aktivasi gagal.\n\n" + result.Message +
                    "\n\nPeriksa kembali license key atau hubungi developer.");
            }
        }
        catch (InvalidOperationException ex)
        {
            UiHelpers.Error(ex.Message);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLogger.Instance.Error("Activation error", ex);
            UiHelpers.Error("Corrupted License.\n\nPastikan key disalin utuh.");
        }
    }
}
