using KasirPro.Core.Domain;
using Dapper;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Big success dialog after payment: TOTAL / PAID / CHANGE + Print / New Sale.</summary>
public class PaymentSuccessDialog : Form
{
    public bool PrintReceipt { get; private set; }

    public PaymentSuccessDialog(string invoiceNo, decimal total, decimal paid, decimal change,
        string extraLines = "")
    {
        Text = "PAYMENT SUCCESS";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 400);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var check = Theme.Label("✓", 40, true, Theme.Success);
        check.Location = new Point(210, 16);
        var title = Theme.Label("PAYMENT SUCCESS", 16, true, Theme.Success);
        title.Location = new Point(150, 76);
        var inv = Theme.Label(invoiceNo, 10, false, Theme.Muted);
        inv.Location = new Point(185, 104);

        var rows = new (string Label, decimal Value, Color Color)[]
        {
            ("TOTAL", total, Theme.Text),
            ("PAID", paid, Theme.Text),
            ("CHANGE", change, Theme.Accent)
        };
        var y = 140;
        foreach (var (label, value, color) in rows)
        {
            var l = Theme.Label(label, 12, true, Theme.Muted);
            l.Location = new Point(60, y + 4);
            var v = Theme.Label(Money.Format(value), 14, true, color);
            v.Location = new Point(230, y);
            Controls.Add(l);
            Controls.Add(v);
            y += 44;
        }
        if (!string.IsNullOrWhiteSpace(extraLines))
        {
            var ex = Theme.Label(extraLines, 8, false, Theme.Muted);
            ex.Location = new Point(60, y);
            ex.Size = new Size(380, 60);
            Controls.Add(ex);
            y += 60;
        }

        var print = Theme.PrimaryButton("PRINT RECEIPT", 180, 44);
        print.Location = new Point(50, ClientSize.Height - 70);
        print.Click += (s, e) => { PrintReceipt = true; DialogResult = DialogResult.OK; Close(); };
        var next = Theme.SuccessButton("NEW SALE", 180, 44);
        next.Location = new Point(250, ClientSize.Height - 70);
        next.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

        Controls.AddRange(new Control[] { check, title, inv, print, next });
    }
}

/// <summary>Hardware Test Center: barcode scanner, printers, drawer, scale, customer display.</summary>
public class HardwareTestCenterDialog : Form
{
    private readonly TabControl _tabs = new();
    private readonly ComboBox _printers = Theme.Combo(260);
    private readonly ComboBox _comPorts = Theme.Combo(90);
    private readonly TextBox _scanField = Theme.TextBox(260);
    private readonly Label _scanResult = new();

    public HardwareTestCenterDialog()
    {
        Text = "Hardware Test Center";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 560);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(BuildScannerTab());
        _tabs.TabPages.Add(BuildPrinterTab("Receipt Printer", "receipt_printer"));
        _tabs.TabPages.Add(BuildPrinterTab("Label Printer", "label_printer"));
        _tabs.TabPages.Add(BuildDrawerTab());
        _tabs.TabPages.Add(BuildScaleTab());

        Controls.Add(_tabs);
        var header = Theme.PageHeader("Hardware Test Center", "Tes perangkat POS: scanner, printer, drawer, timbangan");
        Controls.Add(header);
        header.BringToFront();
    }

    private TabPage BuildScannerTab()
    {
        var page = new TabPage("Barcode Scanner");
        var modeLabel = Theme.Label("Mode:", 9, true);
        modeLabel.Location = new Point(20, 20);
        var mode = Theme.Combo(120);
        mode.Location = new Point(80, 16);
        mode.Items.AddRange(new object[] { "HID", "COM" });
        mode.SelectedIndex = Program.Services.Settings.Get("scanner_mode", "HID") == "COM" ? 1 : 0;
        mode.SelectedIndexChanged += (s, e) => Program.Services.Settings.Set("scanner_mode", mode.Text);

        var portLabel = Theme.Label("Port:", 9, true);
        portLabel.Location = new Point(220, 20);
        _comPorts.Location = new Point(265, 16);
        _comPorts.Items.Add("");
        try
        {
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames()) _comPorts.Items.Add(p);
        }
        catch { }
        _comPorts.Text = Program.Services.Settings.Get("scanner_com_port", "");

        var suffixLabel = Theme.Label("Suffix:", 9, true);
        suffixLabel.Location = new Point(380, 20);
        var suffix = Theme.Combo(90);
        suffix.Location = new Point(440, 16);
        suffix.Items.AddRange(new object[] { "Enter", "Tab", "None" });
        suffix.Text = Program.Services.Settings.Get("scanner_suffix", "Enter");

        var status = Theme.Label("Status: Ready (HID) - fokus ke field lalu scan", 9, false, Theme.Muted);
        status.Location = new Point(20, 52);
        status.Size = new Size(600, 22);

        var testLabel = Theme.Label("Scan barcode sekarang...", 9, true);
        testLabel.Location = new Point(20, 86);
        _scanField.Location = new Point(20, 110);
        _scanField.Font = new Font("Consolas", 11f);
        _scanField.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            TestScan(_scanField.Text.Trim());
            _scanField.Clear();
        };

        _scanResult.Location = new Point(20, 150);
        _scanResult.Size = new Size(640, 130);
        _scanResult.Font = new Font("Consolas", 9.5f);

        var beepNote = Theme.Label("Feedback: beep sukses / error via SystemSounds.", 8, false, Theme.Muted);
        beepNote.Location = new Point(20, 290);

        page.Controls.AddRange(new Control[] { modeLabel, mode, portLabel, _comPorts, suffixLabel, suffix, status, testLabel, _scanField, _scanResult, beepNote });
        return page;
    }

    private void TestScan(string code)
    {
        try
        {
            if (code.Length == 0) return;
            System.Media.SystemSounds.Asterisk.Play();
            var product = Program.Services.Products.GetByBarcode(code) ?? Program.Services.Products.GetByCode(code);
            var type = code.Length switch { 13 => "EAN-13", 12 => "UPC-A", 8 => "EAN-8", _ => "CODE" };
            _scanResult.Text = product == null
                ? $"Raw Value    : {code}\nDetected Type: {type} (len {code.Length})\nMatched      : BARCODE BELUM TERDAFTAR"
                : $"Raw Value    : {code}\nDetected Type: {type}\nMatched      : YA\nProduct      : {product.Name}\nSKU          : {product.Code}\nPrice        : {Money.Format(product.SellingPrice)}\nStock        : {product.Stock:0.##} {product.UnitName}";
        }
        catch (Exception ex)
        {
            System.Media.SystemSounds.Hand.Play();
            _scanResult.Text = "ERROR: " + ex.Message;
        }
    }

    private TabPage BuildPrinterTab(string title, string settingKey)
    {
        var page = new TabPage(title);
        var l1 = Theme.Label("Printer:", 9, true);
        l1.Location = new Point(20, 20);
        var combo = Theme.Combo(300);
        combo.Location = new Point(90, 16);
        var btnRefresh = Theme.SecondaryButton("Refresh", 90);
        btnRefresh.Location = new Point(400, 14);
        Action load = () =>
        {
            combo.Items.Clear();
            try
            {
                foreach (var p in PrinterService.GetInstalledPrinters()) combo.Items.Add(p);
                combo.Text = Program.Services.Settings.Get(settingKey, "");
            }
            catch { }
        };
        load();
        btnRefresh.Click += (s, e) => load();

        var btnSave = Theme.PrimaryButton("SET DEFAULT", 130, 34);
        btnSave.Location = new Point(20, 60);
        btnSave.Click += (s, e) =>
        {
            Program.Services.Settings.Set(settingKey, combo.Text);
            UiHelpers.Info($"{title} default: {combo.Text}");
        };
        var btnTest = Theme.SecondaryButton("Print Test", 110, 34);
        btnTest.Location = new Point(160, 60);
        btnTest.Click += (s, e) =>
        {
            try
            {
                if (settingKey == "label_printer")
                {
                    // small 40x30 label with EAN13
                    var lines = new List<string> { "[B]TEST LABEL", Program.Services.Settings.StoreName, "1234567890128", Money.Format(10000) };
                    PrintRaw(lines, combo.Text);
                    UiHelpers.Info("Label test terkirim.");
                }
                else
                {
                    var lines = new List<string> { "[B]TEST " + title.ToUpper(), Program.Services.Settings.StoreName, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), "", "Jika ini terbaca, printer OK.", "", "." };
                    PrintRaw(lines, combo.Text);
                    UiHelpers.Info("Test page terkirim.");
                }
            }
            catch (Exception ex) { UiHelpers.Error("Test gagal: " + ex.Message); }
        };

        var note = Theme.Label("Printer Bluetooth/LAN harus sudah terpasang di Windows\n" +
            "dan muncul di daftar printer (Settings > Printers & scanners).", 8, false, Theme.Muted);
        note.Location = new Point(20, 110);
        note.Size = new Size(600, 40);

        page.Controls.AddRange(new Control[] { l1, combo, btnRefresh, btnSave, btnTest, note });
        return page;
    }

    private static void PrintRaw(List<string> lines, string printer)
    {
        var doc = new System.Drawing.Printing.PrintDocument { DocumentName = "KasirPro Test" };
        if (!string.IsNullOrWhiteSpace(printer)) doc.PrinterSettings.PrinterName = printer;
        var font = new Font("Consolas", 9f);
        var bold = new Font("Consolas", 10f, FontStyle.Bold);
        var y = 10f;
        doc.PrintPage += (s, e) =>
        {
            foreach (var line in lines)
            {
                e.Graphics!.DrawString(line.StartsWith("[B]") ? line[3..] : line,
                    line.StartsWith("[B]") ? bold : font, Brushes.Black, 6, y);
                y += 18;
            }
        };
        doc.Print();
    }

    private TabPage BuildDrawerTab()
    {
        var page = new TabPage("Cash Drawer");
        var enabled = new CheckBox
        {
            Text = "Enable Cash Drawer",
            AutoSize = true,
            Location = new Point(20, 20),
            Font = Theme.FontBase,
            Checked = Program.Services.Settings.Get("drawer_enabled", "0") == "1"
        };
        enabled.CheckedChanged += (s, e) => Program.Services.Settings.Set("drawer_enabled", enabled.Checked ? "1" : "0");

        var l1 = Theme.Label("Printer connector:", 9, true);
        l1.Location = new Point(20, 56);
        var printers = Theme.Combo(300);
        printers.Location = new Point(160, 52);
        try
        {
            foreach (var p in PrinterService.GetInstalledPrinters()) printers.Items.Add(p);
            printers.Text = Program.Services.Settings.Get("drawer_printer", Program.Services.Settings.PrinterName);
        }
        catch { }

        var open = Theme.SuccessButton("OPEN DRAWER", 160, 44);
        open.Location = new Point(20, 100);
        open.Click += (s, e) =>
        {
            if (!Program.Session.Has("DRAWER.OPEN"))
            {
                UiHelpers.Warn("Anda tidak memiliki izin DRAWER.OPEN. Minta Supervisor.");
                return;
            }
            try
            {
                Program.Services.Printer.KickDrawerIfEnabled(printers.Text);
                Program.Services.Audit.Log(Program.Session!.UserId, Program.Session.Username, "DRAWER_OPEN", "cash_drawer", 0, "Manual open dari Test Center");
                UiHelpers.Info("Sinyal drawer terkirim.");
            }
            catch (Exception ex) { UiHelpers.Error("Gagal: " + ex.Message); }
        };

        var note = Theme.Label("Drawer RJ11 terhubung ke printer thermal. Pulse ESC p 0.", 8, false, Theme.Muted);
        note.Location = new Point(20, 160);

        page.Controls.AddRange(new Control[] { enabled, l1, printers, open, note });
        return page;
    }

    private TabPage BuildScaleTab()
    {
        var page = new TabPage("Scale (Timbangan)");
        var enabled = new CheckBox
        {
            Text = "Barcode timbangan aktif",
            AutoSize = true,
            Location = new Point(20, 20),
            Font = Theme.FontBase,
            Checked = Program.Services.Settings.Get("scale_enabled", "0") == "1"
        };
        enabled.CheckedChanged += (s, e) => Program.Services.Settings.Set("scale_enabled", enabled.Checked ? "1" : "0");

        var l1 = Theme.Label("Prefix (koma):", 9, true);
        l1.Location = new Point(20, 56);
        var prefixes = Theme.TextBox(120);
        prefixes.Location = new Point(140, 52);
        prefixes.Text = Program.Services.Settings.Get("scale_prefixes", "21,02");

        var l2 = Theme.Label("Weight divisor:", 9, true);
        l2.Location = new Point(280, 56);
        var divisor = Theme.TextBox(80);
        divisor.Location = new Point(385, 52);
        divisor.Text = Program.Services.Settings.Get("scale_weight_divisor", "1000");

        var l3 = Theme.Label("Test barcode:", 9, true);
        l3.Location = new Point(20, 96);
        var test = Theme.TextBox(200);
        test.Location = new Point(120, 92);
        test.Text = "2100001123456";
        var btn = Theme.PrimaryButton("PARSE", 90);
        btn.Location = new Point(330, 90);
        var result = Theme.Label("", 9);
        result.Location = new Point(20, 130);
        result.Size = new Size(600, 60);
        result.Font = new Font("Consolas", 9.5f);

        btn.Click += (s, e) =>
        {
            var list = prefixes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            var dv = decimal.TryParse(divisor.Text, out var d) ? d : 1000;
            var parsed = ScaleBarcode.TryParse(test.Text.Trim(), list, dv);
            result.Text = parsed == null
                ? "Bukan barcode timbangan yang valid dengan prefix ini."
                : $"Item code : {parsed.Barcode}\nWeight    : {parsed.Qty:0.###}\nType      : SCALE";
        };

        page.Controls.AddRange(new Control[] { enabled, l1, prefixes, l2, divisor, l3, test, btn, result });
        return page;
    }
}

/// <summary>Barcode & Labels: generator internal, duplicate checker, label designer + print.</summary>
public class BarcodeManagerDialog : Form
{
    private readonly DataGridView _grid = new();
    private readonly ComboBox _size = Theme.Combo(130);
    private readonly CheckBox _showStore = new() { Text = "Nama toko", AutoSize = true, Font = Theme.FontBase, Checked = true };
    private readonly CheckBox _showPrice = new() { Text = "Harga", AutoSize = true, Font = Theme.FontBase, Checked = true };
    private readonly CheckBox _showSku = new() { Text = "SKU", AutoSize = true, Font = Theme.FontBase, Checked = true };
    private readonly TextBox _copies = Theme.TextBox(50);

    public BarcodeManagerDialog()
    {
        Text = "Barcode & Labels";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 620);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader("Barcode & Labels", "Generate barcode internal, cek duplikat, cetak label");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 200, BackColor = Theme.Bg };
        var btnGen = Theme.PrimaryButton("GENERATE INTERNAL BARCODE", 240, 34);
        btnGen.Location = new Point(16, 8);
        btnGen.Click += (s, e) => GenerateInternal();
        var btnDup = Theme.SecondaryButton("CEK DUPLIKAT", 130, 34);
        btnDup.Location = new Point(264, 8);
        btnDup.Click += (s, e) => CheckDuplicates();
        var btnValidate = Theme.SecondaryButton("VALIDASI EAN", 120, 34);
        btnValidate.Location = new Point(402, 8);
        btnValidate.Click += (s, e) => ValidateEans();

        var lSize = Theme.Label("Label:", 9, true);
        lSize.Location = new Point(16, 50);
        _size.Location = new Point(64, 46);
        _size.Items.AddRange(new object[] { "25x15", "30x20", "40x30", "50x25", "50x30", "50x40" });
        _size.SelectedIndex = 2;
        _showStore.Location = new Point(200, 50);
        _showPrice.Location = new Point(290, 50);
        _showSku.Location = new Point(370, 50);
        var lCopies = Theme.Label("Copies:", 9, true);
        lCopies.Location = new Point(430, 50);
        _copies.Location = new Point(485, 46);
        _copies.Text = "1";
        var btnPrint = Theme.SuccessButton("PRINT LABELS", 130, 34);
        btnPrint.Location = new Point(545, 44);
        btnPrint.Click += (s, e) => PrintLabels();
        var btnPreview = Theme.SecondaryButton("Preview", 90, 34);
        btnPreview.Location = new Point(685, 44);
        btnPreview.Click += (s, e) => Preview();

        var assignLabel = Theme.Label("Scan/assign barcode ke produk terpilih:", 9, true);
        assignLabel.Location = new Point(16, 138);
        var assignBox = Theme.TextBox(220);
        assignBox.Location = new Point(16, 160);
        assignBox.PlaceholderText = "Scan barcode di sini...";
        assignBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AssignBarcode(assignBox.Text.Trim());
            assignBox.Clear();
        };
        var assignBtn = Theme.PrimaryButton("ASSIGN", 100, 28);
        assignBtn.Location = new Point(244, 158);
        assignBtn.Click += (s, e) => { AssignBarcode(assignBox.Text.Trim()); assignBox.Clear(); };

        toolbar.Controls.AddRange(new Control[] { btnGen, btnDup, btnValidate, lSize, _size, _showStore, _showPrice, _showSku, lCopies, _copies, btnPrint, btnPreview,
            assignLabel, assignBox, assignBtn });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("sel", "Pilih");
        _grid.Columns.Add("code", "SKU");
        _grid.Columns.Add("name", "Product");
        _grid.Columns.Add("barcode", "Barcode");
        _grid.Columns.Add("price", "Harga");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["sel"].FillWeight = 8;
        _grid.Columns["code"].FillWeight = 14;
        _grid.Columns["name"].FillWeight = 34;
        _grid.Columns["barcode"].FillWeight = 22;
        _grid.Columns["price"].FillWeight = 12;
        Theme.MoneyColumn(_grid, "price");
        _grid.ReadOnly = false;
        _grid.CellValueChanged += (s, e) => { };

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += (s, e) => LoadProducts();
    }

    private void LoadProducts()
    {
        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        _grid.Rows.Clear();
        foreach (var p in products)
        {
            var idx = _grid.Rows.Add(false, p.Code, p.Name, p.Barcode, p.SellingPrice, p.Id);
        }
    }

    private IEnumerable<DataGridViewRow> Selected() =>
        _grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && Convert.ToBoolean(r.Cells["sel"].Value ?? false));

    /// <summary>Scan-to-assign: attaches a scanned barcode to the checked product with validation.</summary>
    private void AssignBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) { UiHelpers.Warn("Scan barcode dulu."); return; }
        var rows = Selected().ToList();
        if (rows.Count != 1) { UiHelpers.Warn("Centang TEPAT SATU produk untuk assign barcode."); return; }

        var numeric = barcode.All(char.IsDigit);
        if (numeric && barcode.Length is 8 or 13 && !BarcodeMath.IsValidGtin(barcode))
        {
            UiHelpers.Error($"Check digit EAN '{barcode}' salah. Barcode tidak valid.");
            System.Media.SystemSounds.Hand.Play();
            return;
        }
        var pid = Convert.ToInt64(rows[0].Cells["_id"].Value);
        var code = rows[0].Cells["code"].Value?.ToString() ?? "";
        var result = UiHelpers.Run(() =>
        {
            var owner = Program.DbMain.With(c => c.ExecuteScalar<long>(
                "SELECT product_id FROM product_barcodes WHERE barcode=@b", new { b = barcode }));
            if (owner != 0 && owner != pid)
                throw new InvalidOperationException($"Barcode sudah dipakai produk lain (ID {owner})");
            Program.DbMain.With(c => c.Execute(@"INSERT INTO product_barcodes (product_id, barcode, barcode_type, created_at)
                VALUES (@p, @b, @t, @now)", new { p = pid, b = barcode, t = numeric ? "EAN13" : "CODE128", now = DbEx.Iso(DateTime.Now) }));
            Program.Services.Audit.Log(Program.Session!.UserId, Program.Session.Username,
                "BARCODE_ASSIGN", "product", pid, $"{code} <- {barcode}");
            return true;
        });
        if (result)
        {
            System.Media.SystemSounds.Asterisk.Play();
            rows[0].Cells["barcode"].Value = barcode;
            UiHelpers.Info($"Barcode '{barcode}' ditambahkan ke produk {code}.");
        }
    }

    private void GenerateInternal()
    {
        var updated = 0;
        UiHelpers.Run<object?>(() =>
        {
            updated = _dbTxGenerate();
            return null;
        });
        UiHelpers.Info($"{updated} produk mendapat barcode internal.");
        LoadProducts();
    }

    private int _dbTxGenerate()
    {
        var updated = 0;
        Program.DbMain.Transaction(c =>
        {
            var seq = c.ExecuteScalar<long>("SELECT COALESCE(MAX(CAST(SUBSTR(barcode,2,11) AS INTEGER)),0) FROM product_barcodes WHERE barcode LIKE '2%'");
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                if (!Convert.ToBoolean(row.Cells["sel"].Value ?? false)) continue;
                var barcode = row.Cells["barcode"].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(barcode)) continue;
                seq++;
                var ean = BarcodeMath.InternalEan13(seq);
                var pid = Convert.ToInt64(row.Cells["_id"].Value);
                c.Execute("INSERT INTO product_barcodes (product_id, barcode, barcode_type, is_primary, created_at) VALUES (@p, @b, 'EAN13', 1, @t)",
                    new { p = pid, b = ean, t = DbEx.Iso(DateTime.Now) });
                row.Cells["barcode"].Value = ean;
                updated++;
            }
        });
        return updated;
    }

    private void CheckDuplicates()
    {
        var dups = UiHelpers.Run(() => Program.DbMain.With(c => c.Query<(string Barcode, int Count, string Names)>(@"
            SELECT barcode AS Barcode, COUNT(*) AS Count, GROUP_CONCAT(product_name, ' | ') AS Names
            FROM (SELECT pb.barcode AS barcode, p.name AS product_name
                  FROM product_barcodes pb JOIN products p ON p.id=pb.product_id)
            GROUP BY barcode HAVING COUNT(*) > 1").ToList()));
        UiHelpers.Info(dups == null || dups.Count == 0
            ? "Tidak ada barcode duplikat."
            : "DUPLIKAT DITEMUKAN:\n" + string.Join("\n", dups.Select(d => $"{d.Barcode} x{d.Count}: {d.Names}")),
            "Duplicate Barcode Checker");
    }

    private void ValidateEans()
    {
        var invalid = UiHelpers.Run(() => Program.DbMain.With(c => c.Query<string>(
            "SELECT barcode FROM product_barcodes WHERE barcode_type='EAN13' AND LENGTH(barcode)=13").ToList()
            .Where(b => b.All(char.IsDigit) && !BarcodeMath.IsValidGtin(b)).ToList()));
        UiHelpers.Info(invalid == null || invalid.Count == 0
            ? "Semua EAN-13 valid."
            : "EAN-13 INVALID (check digit salah):\n" + string.Join("\n", invalid.Take(20)));
    }

    private void Preview()
    {
        var items = Selected().ToList();
        if (items.Count == 0) { UiHelpers.Warn("Pilih produk dulu (centang kolom Pilih)."); return; }
        var wmm = int.Parse(_size.Text.Split('x')[0]);
        var hmm = int.Parse(_size.Text.Split('x')[1]);
        new LabelPreviewForm(items, wmm, hmm, _showStore.Checked, _showPrice.Checked, _showSku.Checked).ShowDialog();
    }

    private void PrintLabels()
    {
        var items = Selected().ToList();
        if (items.Count == 0) { UiHelpers.Warn("Pilih produk dulu (centang kolom Pilih)."); return; }
        var wmm = int.Parse(_size.Text.Split('x')[0]);
        var hmm = int.Parse(_size.Text.Split('x')[1]);
        var copies = int.TryParse(_copies.Text, out var c) ? Math.Max(1, c) : 1;
        var printer = Program.Services.Settings.Get("label_printer", "");
        var doc = new System.Drawing.Printing.PrintDocument { DocumentName = "KasirPro Labels" };
        if (!string.IsNullOrWhiteSpace(printer)) doc.PrinterSettings.PrinterName = printer;
        doc.DefaultPageSettings.PaperSize = new System.Drawing.Printing.PaperSize("Label", Mm(wmm), Mm(hmm));
        doc.DefaultPageSettings.Margins = new System.Drawing.Printing.Margins(2, 2, 2, 2);

        var queue = new List<(string Sku, string Name, string Barcode, decimal Price)>();
        foreach (var row in items)
            for (var i = 0; i < copies; i++)
                queue.Add((row.Cells["code"].Value?.ToString() ?? "", row.Cells["name"].Value?.ToString() ?? "",
                    row.Cells["barcode"].Value?.ToString() ?? "", Convert.ToDecimal(row.Cells["price"].Value ?? 0m)));

        var idx = 0;
        doc.PrintPage += (s, e) =>
        {
            e.Graphics!.PageUnit = GraphicsUnit.Millimeter;
            var store = Program.Services.Settings.StoreName;
            // barcode rendering
            var barcodeFont = new Font("Libre Barcode 128", 10f);
            var fallback = new Font("Consolas", 5.5f);
            var nameFont = new Font("Segoe UI", 2.6f, FontStyle.Bold);
            var small = new Font("Segoe UI", 2.2f);

            var x = 1f; var y = 1f;
            if (idx < queue.Count)
            {
                var item = queue[idx];
                if (_showStore.Checked) e.Graphics.DrawString(store, small, Brushes.Black, x, y);
                if (_showPrice.Checked)
                    e.Graphics.DrawString(Money.FormatPlain(item.Price), new Font("Segoe UI", 3.2f, FontStyle.Bold), Brushes.Black, x, y + 3.2f);
                e.Graphics.DrawString(item.Name.Length > 28 ? item.Name[..28] : item.Name, nameFont, Brushes.Black, x, y + 7f);
                if (_showSku.Checked) e.Graphics.DrawString(item.Sku, small, Brushes.Black, x, y + 10.5f);
                try
                {
                    var barcodeImg = BarcodeRenderer.RenderCode128(item.Barcode, 200, 40);
                    e.Graphics.DrawImage(barcodeImg, x, y + 13f, wmm - 2f, 6.5f);
                }
                catch
                {
                    e.Graphics.DrawString("*" + item.Barcode + "*", fallback, Brushes.Black, x, y + 14f);
                }
            }
            idx++;
            e.HasMorePages = idx < queue.Count;
        };
        doc.Print();
        UiHelpers.Info($"{queue.Count} label dikirim ke printer" +
            (string.IsNullOrWhiteSpace(printer) ? " default." : $": {printer}"));
    }

    private static int Mm(float mm) => (int)(mm / 25.4f * 100);
}

/// <summary>Label preview window (scaled).</summary>
public class LabelPreviewForm : Form
{
    private readonly List<DataGridViewRow> _items;
    private readonly int _wmm, _hmm;
    private readonly bool _store, _price, _sku;

    public LabelPreviewForm(List<DataGridViewRow> items, int wmm, int hmm, bool store, bool price, bool sku)
    {
        _items = items.Take(12).ToList();
        _wmm = wmm; _hmm = hmm; _store = store; _price = price; _sku = sku;
        Text = "Preview Label";
        ClientSize = new Size(640, 420);
        BackColor = Theme.Bg;
        Paint += OnPaint;
    }

    private void OnPaint(object? s, PaintEventArgs e)
    {
        var g = e.Graphics!;
        g.Clear(Theme.Card);
        var scale = 3f;
        var w = _wmm * scale;
        var h = _hmm * scale;
        var x = 12f; var y = 12f;
        var store = Program.Services.Settings.StoreName;
        foreach (var row in _items)
        {
            if (y + h > ClientSize.Height - 10) { x += w + 8; y = 12f; }
            using var pen = new Pen(Theme.Border);
            g.DrawRectangle(pen, x, y, w, h);
            var name = row.Cells["name"].Value?.ToString() ?? "";
            var sku = row.Cells["code"].Value?.ToString() ?? "";
            var barcode = row.Cells["barcode"].Value?.ToString() ?? "";
            var price = Convert.ToDecimal(row.Cells["price"].Value ?? 0m);
            var small = new Font("Segoe UI", 6f);
            var bold = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            var cy = y + 3;
            if (_store) { g.DrawString(store, small, Brushes.Black, x + 3, cy); cy += 11; }
            if (_price) { g.DrawString(Money.FormatPlain(price), bold, Brushes.Black, x + 3, cy); cy += 14; }
            g.DrawString(name.Length > 24 ? name[..24] : name, bold, Brushes.Black, x + 3, cy); cy += 14;
            if (_sku) { g.DrawString(sku, small, Brushes.Black, x + 3, cy); cy += 11; }
            try
            {
                var img = BarcodeRenderer.RenderCode128(barcode, (int)w * 3, 36);
                g.DrawImage(img, x + 3, cy, w - 6, 30);
            }
            catch { g.DrawString("*" + barcode + "*", small, Brushes.Black, x + 3, cy); }
            y += h + 8;
        }
    }
}

/// <summary>Minimal Code128-B renderer (pure GDI+, zero dependency).</summary>
public static class BarcodeRenderer
{
    public static Bitmap RenderCode128(string data, int width, int height)
    {
        var patterns = Code128B();
        var codes = new List<int> { 104 }; // START B
        foreach (var ch in data)
        {
            var v = ch - 32;
            if (v < 0 || v > 94) v = 0;
            codes.Add(v);
        }
        var checksum = codes[0];
        for (var i = 1; i < codes.Count; i++) checksum += codes[i] * i;
        codes.Add(checksum % 103);
        codes.Add(106); // STOP

        var pattern = string.Concat(codes.Select(c => patterns[c]));
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        var barW = width / (float)pattern.Length;
        float x = 0;
        foreach (var ch in pattern)
        {
            if (ch == '1') g.FillRectangle(Brushes.Black, x, 0, Math.Max(1f, barW), height);
            x += barW;
        }
        return bmp;
    }

    private static string[] Code128B()
    {
        // 107 patterns of 11 modules (space-separated 1/0)
        string raw = "11011001100 11001101100 11001100110 10010011000 10010001100 10001001100 10011001000 10011000100" +
"10001100100 11001001000 11001000100 11000100100 10110011100 10011011100 10011001110 10111001100" +
"10011101100 10011100110 11001110010 11001011100 11001001110 11011100100 11001110100 11101101110" +
"11101001100 11100101100 11100100110 11101100100 11100110100 11100110010 11011011000 11011000110" +
"11000110110 10100011000 10001011000 10001000110 10110001000 10001101000 10001100010 11010001000" +
"11000101000 11000100010 10110111000 10110001110 10001101110 10111011000 10111000110 10001110110" +
"11101110110 11010001110 11000101110 11011101000 11011100010 11011101110 11101011000 11101000110" +
"11100010110 11101101000 11101100010 11100011010 11101111010 11001000010 11110001010 10100110000" +
"10100001100 10010110000 10010000110 10000101100 10000100110 10110010000 10110000100 10011010000" +
"10011000010 10000110100 10000110010 11000010010 11001010000 11110111010 11000010100 10001111010" +
"10100111100 10010111100 10010011110 10111100100 10011110100 10011110010 11110100100 11110010100" +
"11110010010 11011011110 11011110110 11110110110 10101111000 10100011110 10001011110 10111101000" +
"10111100010 11110101000 11110100010 10111011110 10111101110 11101011110 11110101110 11010000100" +
"11010010000 11010011100 11000111010";
        return raw.Split(' ');
    }
}




