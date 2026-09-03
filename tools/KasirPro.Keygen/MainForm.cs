using KasirPro.Licensing;

namespace KasirPro.Keygen;

/// <summary>Master Keygen UI: create master key, generate + sign licenses, history, export public key.</summary>
public class MainForm : Form
{
    private readonly MasterKeyStore _keys;
    private readonly KeygenDb _history;
    private readonly LicenseGenerator _generator;

    private readonly TextBox _customer = new();
    private readonly TextBox _machineId = new();
    private readonly ComboBox _type = new();
    private readonly DateTimePicker _expiry = new() { Format = DateTimePickerFormat.Short, Checked = false, ShowCheckBox = true };
    private readonly TextBox _output = new();
    private readonly ListView _grid = new();
    private readonly Panel _setupPanel = new();
    private readonly Panel _workPanel = new();
    private readonly TextBox _passNew = new();
    private readonly TextBox _passNew2 = new();
    private readonly Label _status = new();

    public MainForm()
    {
        Text = "KasirPro License Manager (MASTER KEYGEN - JANGAN DISEBARKAN)";
        MinimumSize = new Size(880, 640);
        Size = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 246, 249);
        Font = new Font("Segoe UI", 9.5f);

        _keys = new MasterKeyStore();
        _history = new KeygenDb();
        _generator = new LicenseGenerator(_keys);

        BuildSetupPanel();
        BuildWorkPanel();

        Controls.Add(_workPanel);
        Controls.Add(_setupPanel);
        _workPanel.Visible = false;

        if (_keys.Exists)
        {
            _setupPanel.Visible = false;
            _workPanel.Visible = true;
            try
            {
                var fp = KasirPro.Licensing.EmbeddedPublicKey.FingerprintOf(_keys.LoadPublic());
                _status.Text = $"Status: master key OK (fingerprint {fp}) - " +
                               "cocokkan dengan: KasirPro.exe --diag-license";
            }
            catch
            {
                _status.Text = "Status: master key OK";
            }
            RefreshHistory();
        }
    }

    // ---------------------------------------------------------------
    // FIRST RUN: create master key (private, DPAPI + passphrase)
    // ---------------------------------------------------------------
    private void BuildSetupPanel()
    {
        _setupPanel.Dock = DockStyle.Fill;
        _setupPanel.BackColor = Color.FromArgb(244, 246, 249);

        var title = MakeLabel("KASIRPRO MASTER KEYGEN", 22, true, Color.FromArgb(220, 38, 38));
        title.Location = new Point(40, 30);
        var sub = MakeLabel("Inisialisasi Master Signing Key (sekali saja)", 11, false, Color.FromArgb(100, 116, 139));
        sub.Location = new Point(41, 66);

        var warn = MakeLabel(
            "PERINGATAN: MASTER PRIVATE KEY - JANGAN DISEBARKAN\n" +
            "Private key dienkripsi dengan DPAPI + passphrase. Simpan passphrase dengan aman.\n" +
            "Jika passphrase hilang, master key tidak dapat digunakan lagi.", 9.5f, true, Color.FromArgb(217, 119, 6));
        warn.Location = new Point(40, 110);
        warn.Size = new Size(760, 64);

        var l1 = MakeLabel("Passphrase master key:", 10, true);
        l1.Location = new Point(40, 190);
        _passNew.Location = new Point(40, 214);
        _passNew.Size = new Size(400, 28);
        _passNew.UseSystemPasswordChar = true;

        var l2 = MakeLabel("Ulangi passphrase:", 10, true);
        l2.Location = new Point(40, 252);
        _passNew2.Location = new Point(40, 276);
        _passNew2.Size = new Size(400, 28);
        _passNew2.UseSystemPasswordChar = true;

        var create = MakeButton("GENERATE MASTER KEY PAIR", 260, 42, Color.FromArgb(37, 99, 235));
        create.Location = new Point(40, 330);
        create.Click += OnCreateMasterKey;

        _setupPanel.Controls.AddRange(new Control[] { title, sub, warn, l1, _passNew, l2, _passNew2, create });
    }

    private void OnCreateMasterKey(object? sender, EventArgs e)
    {
        if (_passNew.Text.Length < 8)
        {
            MessageBox.Show("Passphrase minimal 8 karakter.", "Master Keygen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_passNew.Text != _passNew2.Text)
        {
            MessageBox.Show("Passphrase tidak sama.", "Master Keygen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            _keys.CreateNew(_passNew.Text);
            _setupPanel.Visible = false;
            _workPanel.Visible = true;
            RefreshHistory();
            MessageBox.Show("Master key pair dibuat.\n\nPrivate key: " + _keys.PrivateKeyPath +
                "\nPublic key: " + _keys.PublicKeyPath +
                "\n\nSelanjutnya: klik 'Export Public Key' dan masukkan hasilnya ke KasirPro.Licensing (EmbeddedPublicKey).",
                "Master Keygen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal: " + ex.Message, "Master Keygen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------------------------------------------------------
    // WORK AREA
    // ---------------------------------------------------------------
    private void BuildWorkPanel()
    {
        _workPanel.Dock = DockStyle.Fill;
        _workPanel.BackColor = Color.FromArgb(244, 246, 249);

        var title = MakeLabel("KasirPro License Manager", 16, true, Color.FromArgb(37, 99, 235));
        title.Location = new Point(24, 14);

        var masterWarn = MakeLabel("MASTER PRIVATE KEY - DO NOT DISTRIBUTE", 11, true, Color.FromArgb(220, 38, 38));
        masterWarn.Location = new Point(25, 44);

        _status.Text = "Status: master key OK";
        _status.Location = new Point(640, 48);
        _status.AutoSize = true;

        int y = 84;
        void Field(string label, Control c, int w = 300)
        {
            var l = MakeLabel(label, 9.5f, true);
            l.Location = new Point(24, y + 4);
            c.Location = new Point(190, y);
            c.Width = w;
            _workPanel.Controls.Add(l);
            _workPanel.Controls.Add(c);
            y += 38;
        }

        Field("Customer Name:", _customer);
        Field("Machine ID:", _machineId, 220);
        Field("Product:", MakeLabel("KasirPro (fixed)", 9.5f, false));
        y += 2;
        Field("License Type:", _type);
        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.Items.AddRange(new object[] { "Lifetime", "Annual", "Trial" });
        _type.SelectedIndex = 0;
        _type.SelectedIndexChanged += (s, e) =>
            _expiry.Enabled = _type.SelectedIndex != 0;

        var lExp = MakeLabel("Expiration:", 9.5f, true);
        lExp.Location = new Point(24, y + 4);
        _expiry.Location = new Point(190, y);
        _expiry.Width = 180;
        _expiry.Value = DateTime.Today.AddYears(1);
        _workPanel.Controls.Add(lExp);
        _workPanel.Controls.Add(_expiry);
        y += 46;

        var generate = MakeButton("GENERATE LICENSE", 200, 44, Color.FromArgb(22, 163, 74));
        generate.Location = new Point(24, y);
        generate.Click += OnGenerate;

        var copy = MakeButton("COPY LICENSE", 140, 44, Color.FromArgb(71, 85, 105));
        copy.Location = new Point(232, y);
        copy.Click += (s, e) => { if (_output.Text.Length > 0) { Clipboard.SetText(_output.Text); _status.Text = "Status: token tersalin"; } };

        var save = MakeButton("SAVE LICENSE", 140, 44, Color.FromArgb(71, 85, 105));
        save.Location = new Point(380, y);
        save.Click += OnSaveToken;

        var newBtn = MakeButton("NEW LICENSE", 140, 44, Color.FromArgb(71, 85, 105));
        newBtn.Location = new Point(528, y);
        newBtn.Click += (s, e) => { _customer.Clear(); _machineId.Clear(); _output.Clear(); _type.SelectedIndex = 0; };

        var exportPub = MakeButton("EXPORT PUBLIC KEY", 170, 44, Color.FromArgb(217, 119, 6));
        exportPub.Location = new Point(676, y);
        exportPub.Click += OnExportPublicKey;

        y += 60;

        var outLabel = MakeLabel("License Token:", 9.5f, true);
        outLabel.Location = new Point(24, y);
        _output.Multiline = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.Font = new Font("Consolas", 9f);
        _output.ReadOnly = true;
        _output.Location = new Point(24, y + 24);
        _output.Size = new Size(890, 110);
        y += 150;

        var histLabel = MakeLabel("Issued Licenses:", 9.5f, true);
        histLabel.Location = new Point(24, y);
        _grid.View = View.Details;
        _grid.FullRowSelect = true;
        _grid.Location = new Point(24, y + 24);
        _grid.Size = new Size(890, 170);
        _grid.Columns.Add("License ID", 120);
        _grid.Columns.Add("Customer", 190);
        _grid.Columns.Add("Machine ID", 150);
        _grid.Columns.Add("Type", 70);
        _grid.Columns.Add("Issued", 120);
        _grid.Columns.Add("Status", 80);

        _workPanel.Controls.AddRange(new Control[] { title, masterWarn, _status, outLabel, _output, histLabel, _grid,
            generate, copy, save, newBtn, exportPub });
    }

    private void OnGenerate(object? sender, EventArgs e)
    {
        try
        {
            using var passForm = new PassphraseForm("Passphrase master key:");
            if (passForm.ShowDialog(this) != DialogResult.OK) return;

            DateTime? expires = _type.SelectedIndex switch
            {
                1 => _expiry.Value,           // Annual: pakai tanggal yang dipilih
                2 => DateTime.UtcNow.AddDays(14),
                _ => null
            };

            var (token, payload) = _generator.Generate(
                _customer.Text, _machineId.Text, _type.Text, expires, _history, passForm.Passphrase);

            _output.Text = token;
            _status.Text = $"Status: generated {payload.LicenseId} for {payload.Customer}";
            RefreshHistory();

            // quick self-check with the public key
            using var pub = _keys.LoadPublic();
            var verify = LicenseToken.Verify(token, pub, payload.MachineId);
            if (verify.Status != LicenseVerifyStatus.Valid)
                MessageBox.Show("PERINGATAN: token tidak lolos verifikasi mandiri!", "Keygen",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Keygen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal generate: " + ex.Message, "Keygen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveToken(object? sender, EventArgs e)
    {
        if (_output.Text.Length == 0) { MessageBox.Show("Generate license dulu.", "Keygen", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dlg = new SaveFileDialog { Filter = "License|*.license;*.txt", FileName = "KasirPro-" + _customer.Text.Replace(" ", "") + ".license" };
        if (dlg.ShowDialog() == DialogResult.OK) File.WriteAllText(dlg.FileName, _output.Text);
    }

    private void OnExportPublicKey(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog { Filter = "PEM|*.pem", FileName = "KasirPro-public.pem" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            _keys.ExportPublic(dlg.FileName);
            MessageBox.Show("Public key diexport ke:\n" + dlg.FileName +
                "\n\nFile ini AMAN untuk dibagikan. Isi file tersebut ke konstanta " +
                "KasirPro.Licensing.EmbeddedPublicKey.Xml lalu build ulang KasirPro.App.",
                "Export Public Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal: " + ex.Message, "Keygen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshHistory()
    {
        _grid.Items.Clear();
        foreach (var (lid, cust, mid, type, issued, _, status) in _history.History())
        {
            var item = new ListViewItem(lid);
            item.SubItems.Add(cust);
            item.SubItems.Add(mid);
            item.SubItems.Add(type);
            item.SubItems.Add(issued);
            item.SubItems.Add(status);
            _grid.Items.Add(item);
        }
    }

    private static Label MakeLabel(string text, float size, bool bold, Color? color = null) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color ?? Color.FromArgb(30, 41, 59) };

    private static Button MakeButton(string text, int w, int h, Color accent) =>
        new()
        {
            Text = text, Width = w, Height = h,
            FlatStyle = FlatStyle.Flat, BackColor = accent, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
}

/// <summary>Passphrase prompt (modal).</summary>
public class PassphraseForm : Form
{
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true, Width = 300 };
    public string Passphrase => _pass.Text;

    public PassphraseForm(string label)
    {
        Text = "Master Key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(380, 130);
        BackColor = Color.White;
        var l = new Label { Text = label, AutoSize = true, Location = new Point(16, 14), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        _pass.Location = new Point(16, 40);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(230, 78), Width = 90, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White };
        Controls.AddRange(new Control[] { l, _pass, ok });
        AcceptButton = ok;
        FormClosed += (s, e) => { if (DialogResult != DialogResult.OK) return; };
    }
}
