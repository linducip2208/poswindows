using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Operator-facing SQLite and backup diagnostics.</summary>
public sealed class DatabaseHealthDialog : Form
{
    private readonly TextBox _output = new();

    public DatabaseHealthDialog()
    {
        Text = "Kesehatan Database";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(660, 460);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Kesehatan database & backup", 15, true, Theme.Accent);
        title.Location = new Point(20, 16);
        var hint = Theme.Label("Pemeriksaan read-only untuk memastikan data SQLite siap dipakai.", 9, false, Theme.Muted);
        hint.Location = new Point(21, 44);

        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.Font = new Font("Consolas", 9f);
        _output.BackColor = Color.White;
        _output.Location = new Point(20, 76);
        _output.Size = new Size(620, 300);

        var refresh = Theme.PrimaryButton("Periksa ulang", 120, 38);
        refresh.Location = new Point(520, 396);
        refresh.Click += (s, e) => LoadHealth();
        var maintain = Theme.SecondaryButton("Optimalkan", 110, 38);
        maintain.Location = new Point(402, 396);
        maintain.Click += (s, e) => Maintain();
        var close = Theme.SecondaryButton("Tutup", 90, 38);
        close.Location = new Point(300, 396);
        close.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, hint, _output, refresh, maintain, close });
        Shown += (s, e) => LoadHealth();
    }

    private void LoadHealth()
    {
        try
        {
            var h = Program.Services.DatabaseHealth.Read();
            var backupAge = h.LatestBackup.HasValue
                ? h.LatestBackup.Value.ToString("dd/MM/yyyy HH:mm:ss")
                : "Belum ada";
            _output.Text =
                $"Journal mode     : {h.JournalMode}\r\n" +
                $"Integrity check  : {h.Integrity}\r\n" +
                $"Quick check      : {h.QuickCheck}\r\n" +
                $"Schema version   : {h.SchemaVersion}\r\n" +
                $"Database         : {FormatBytes(h.DatabaseBytes)}\r\n" +
                $"WAL              : {FormatBytes(h.WalBytes)}\r\n" +
                $"SHM              : {FormatBytes(h.ShmBytes)}\r\n" +
                $"Free disk        : {FormatBytes(h.FreeDiskBytes)}\r\n\r\n" +
                $"Backup folder    : {h.BackupDirectory}\r\n" +
                $"Jumlah backup    : {h.BackupCount}\r\n" +
                $"Backup terakhir  : {backupAge}";
        }
        catch (Exception ex)
        {
            _output.Text = "Pemeriksaan gagal:\r\n" + ex.Message;
        }
    }

    private void Maintain()
    {
        try
        {
            var result = Program.Services.DatabaseHealth.Maintain();
            UiHelpers.Info($"Database diperiksa dan dioptimalkan.\nIntegrity: {result.Integrity}\nPage count: {result.PageCount}");
            LoadHealth();
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Optimasi database gagal: " + ex.Message);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d:0.0} MB";
        return $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
    }
}
