using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Current stock with paging + filter + stock value.</summary>
public class CurrentStockPage : Panel, IPage
{
    private const int PageSize = 30;
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = Theme.TextBox(220);
    private readonly CheckBox _lowOnly = new() { Text = "Low stock only", AutoSize = true, Font = Theme.FontBase };
    private readonly PagingBar _paging = new();
    private int _page = 1;
    private readonly Label _totalValue = Theme.Label("", 10, true, Theme.Muted);

    public CurrentStockPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Current Stock", "Stok berjalan per produk (dari inventory ledger)");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _search.Location = new Point(16, 10);
        _search.TextChanged += (s, e) => { _page = 1; LoadData(); };
        _lowOnly.Location = new Point(244, 13);
        _lowOnly.CheckedChanged += (s, e) => { _page = 1; LoadData(); };
        _totalValue.Location = new Point(360, 13);
        toolbar.Controls.AddRange(new Control[] { _search, _lowOnly, _totalValue });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("name", "Product");
        _grid.Columns.Add("category", "Category");
        _grid.Columns.Add("stock", "Stock");
        _grid.Columns.Add("unit", "Unit");
        _grid.Columns.Add("minstock", "Min Stock");
        _grid.Columns.Add("value", "Stock Value");
        Theme.MoneyColumn(_grid, "value");
        _grid.Columns["code"].FillWeight = 12;
        _grid.Columns["name"].FillWeight = 30;
        _grid.Columns["category"].FillWeight = 16;
        _grid.Columns["stock"].FillWeight = 10;
        _grid.Columns["unit"].FillWeight = 8;
        _grid.Columns["minstock"].FillWeight = 10;
        _grid.Columns["value"].FillWeight = 14;
        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var result = UiHelpers.Run(() => Program.Services.Inventory.CurrentStock(
            _search.Text, 0, _lowOnly.Checked, _page, PageSize));
        if (result == null) return;
        _grid.Rows.Clear();
        foreach (var r in result.Items)
        {
            var idx = _grid.Rows.Add(r.Code, r.Name, r.Category, r.Stock.ToString("0.##"), r.Unit, r.MinStock.ToString("0.##"), r.StockValue);
            if (r.Stock <= r.MinStock)
                _grid.Rows[idx].Cells["stock"].Style.ForeColor = Theme.Danger;
        }
        var total = result.Items.Sum(i => i.StockValue);
        _totalValue.Text = $"Nilai stok (halaman ini): {Money.Format(total)}";
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }
}

/// <summary>Full inventory ledger view.</summary>
public class StockMovementPage : Panel, IPage
{
    private const int PageSize = 40;
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = Theme.TextBox(220);
    private readonly ComboBox _refType = Theme.Combo(160);
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly PagingBar _paging = new();
    private int _page = 1;

    public StockMovementPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Stock Movement", "Buku besar inventaris (semua perubahan stok tercatat)");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _search.Location = new Point(16, 10);
        _refType.Location = new Point(244, 10);
        _refType.Items.AddRange(new object[] { "Semua Referensi", StockRef.Purchase, StockRef.Sale, StockRef.SaleReturn,
            StockRef.PurchaseReturn, StockRef.Adjustment, StockRef.Opname, StockRef.ManualIn, StockRef.ManualOut, StockRef.Opening });
        _refType.SelectedIndex = 0;
        _from.Location = new Point(412, 10);
        _to.Location = new Point(528, 10);
        var btn = Theme.PrimaryButton("Refresh", 100);
        btn.Location = new Point(646, 8);
        btn.Click += (s, e) => { _page = 1; LoadData(); };
        toolbar.Controls.AddRange(new Control[] { _search, _refType, _from, _to, btn });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("date", "Date");
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("product", "Product");
        _grid.Columns.Add("ref", "Reference");
        _grid.Columns.Add("dir", "Direction");
        _grid.Columns.Add("qty", "Qty");
        _grid.Columns.Add("after", "Stock After");
        _grid.Columns.Add("user", "User");
        _grid.Columns.Add("notes", "Notes");
        _grid.Columns["date"].FillWeight = 14;
        _grid.Columns["code"].FillWeight = 8;
        _grid.Columns["product"].FillWeight = 22;
        _grid.Columns["ref"].FillWeight = 14;
        _grid.Columns["dir"].FillWeight = 8;
        _grid.Columns["qty"].FillWeight = 8;
        _grid.Columns["after"].FillWeight = 10;
        _grid.Columns["user"].FillWeight = 8;
        _grid.Columns["notes"].FillWeight = 8;
        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var refType = _refType.SelectedIndex > 0 ? _refType.Text : "";
        var result = UiHelpers.Run(() => Program.Services.Inventory.Movements(
            _search.Text, 0, refType, _from.Value, _to.Value, _page, PageSize));
        if (result == null) return;
        _grid.Rows.Clear();
        foreach (var m in result.Items)
        {
            var idx = _grid.Rows.Add(m.Date.ToString("dd/MM/yyyy HH:mm"), m.Code, m.Product,
                $"{m.ReferenceType}#{m.ReferenceId}", m.Direction, m.Qty.ToString("0.##"),
                m.StockAfter.ToString("0.##"), m.User, m.Notes);
            var cell = _grid.Rows[idx].Cells["dir"];
            if (m.Direction == "IN") { cell.Style.ForeColor = Theme.SuccessText; cell.Style.Font = Theme.FontMediumBold; }
            else { cell.Style.ForeColor = Theme.Danger; cell.Style.Font = Theme.FontMediumBold; }
        }
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }
}

/// <summary>Stock In / Out / Adjustment dialog.</summary>
public class StockAdjustDialog : Form
{
    public enum Mode { In, Out, Adjust }

    private readonly Mode _mode;
    private readonly ComboBox _product = Theme.Combo(340);
    private readonly TextBox _qty = Theme.TextBox(120);
    private readonly TextBox _notes = Theme.TextBox(340);

    public StockAdjustDialog(Mode mode)
    {
        _mode = mode;
        Text = mode switch { Mode.In => "Stock In", Mode.Out => "Stock Out", _ => "Stock Adjustment" };
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 250);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label(Text, 14, true, mode == Mode.In ? Theme.Success : mode == Mode.Out ? Theme.Danger : Theme.Warning);
        title.Location = new Point(20, 14);

        var l1 = Theme.Label("Produk:", 9, true); l1.Location = new Point(20, 60);
        _product.Location = new Point(20, 82);
        _product.DropDownStyle = ComboBoxStyle.DropDownList;

        var l2 = Theme.Label(mode == Mode.Adjust ? "Stok fisik baru:" : "Jumlah:", 9, true);
        l2.Location = new Point(20, 122);
        _qty.Location = new Point(20, 144);
        _qty.TextAlign = HorizontalAlignment.Right;

        var l3 = Theme.Label("Catatan:", 9, true); l3.Location = new Point(20, 0);
        var lblCat = Theme.Label("Catatan:", 9, true); lblCat.Location = new Point(160, 122);
        _notes.Location = new Point(160, 144);
        _notes.PlaceholderText = "alasan penyesuaian...";

        var ok = Theme.PrimaryButton("SIMPAN", 140, 40);
        ok.Location = new Point(320, 190);
        ok.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(220, 190);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, l1, _product, l2, _qty, lblCat, _notes, ok, cancel });
        Load += (s, e) => LoadProducts();
    }

    private void LoadProducts()
    {
        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        _product.DataSource = products;
        _product.DisplayMember = "Name";
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (_product.SelectedItem is not Product p) return;
        if (!decimal.TryParse(_qty.Text.Replace(".", "").Replace(",", "."), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var qty))
        {
            UiHelpers.Warn("Jumlah tidak valid.");
            return;
        }
        var notes = string.IsNullOrWhiteSpace(_notes.Text) ? Text : _notes.Text.Trim();
        var ok = UiHelpers.Run<object?>(() =>
        {
            if (_mode == Mode.In)
                Program.Services.Inventory.StockIn(p.Id, qty, notes, Program.Session!.UserId, Program.Session.Username);
            else if (_mode == Mode.Out)
                Program.Services.Inventory.StockOut(p.Id, qty, notes, Program.Session!.UserId, Program.Session.Username);
            else
                Program.Services.Inventory.Adjust(p.Id, qty, notes, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info("Perubahan stok tersimpan dan tercatat di Stock Movement.");
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Stock opname: create session, count, compare, post adjustments with audit.</summary>
public class StockOpnamePage : Panel, IPage
{
    private readonly ComboBox _sessions = Theme.Combo(200);
    private readonly DataGridView _grid = new();
    private readonly Button _post;
    private StockOpname? _current;

    public StockOpnamePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Stock Opname", "Hitung stok fisik, bandingkan dengan system, lalu sesuaikan");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _sessions.Location = new Point(16, 10);
        _sessions.SelectedIndexChanged += (s, e) => LoadSession();
        var btnNew = Theme.PrimaryButton("+ Sesi Baru", 110);
        btnNew.Location = new Point(226, 8);
        btnNew.Click += (s, e) => CreateSession();
        var btnSaveCounts = Theme.SecondaryButton("Simpan Hitungan", 130);
        btnSaveCounts.Location = new Point(342, 8);
        btnSaveCounts.Click += (s, e) => SaveCounts();
        _post = Theme.SuccessButton("POSTING (Sesuaikan Stok)", 210);
        _post.Location = new Point(478, 8);
        _post.Click += (s, e) => Post();
        toolbar.Controls.AddRange(new Control[] { _sessions, btnNew, btnSaveCounts, _post });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("name", "Product");
        _grid.Columns.Add("system", "System Stock");
        _grid.Columns.Add("counted", "Physical Count");
        _grid.Columns.Add("diff", "Difference");
        _grid.Columns.Add("adjusted", "Adjusted");
        _grid.Columns["code"].FillWeight = 12;
        _grid.Columns["name"].FillWeight = 38;
        _grid.Columns["system"].FillWeight = 15;
        _grid.Columns["counted"].FillWeight = 15;
        _grid.Columns["diff"].FillWeight = 12;
        _grid.Columns["adjusted"].FillWeight = 8;
        _grid.ReadOnly = false;
        _grid.CellEndEdit += (s, e) => RecalcDiff(e.RowIndex);

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData() => LoadSessions();

    private void LoadSessions()
    {
        var list = UiHelpers.Run(() => Program.Services.Inventory.GetOpnames()) ?? new();
        _sessions.Items.Clear();
        foreach (var o in list)
            _sessions.Items.Add($"{o.OpnameNo} [{o.Status}]");
        if (_sessions.Items.Count > 0) _sessions.SelectedIndex = 0;
        else { _grid.Rows.Clear(); _current = null; }
    }

    private void CreateSession()
    {
        var op = UiHelpers.Run(() => Program.Services.Inventory.CreateOpname(
            Program.Session!.UserId, Program.Session.Username, ""));
        if (op == null) return;
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"Sesi {op.OpnameNo} dibuat.\nInput physical count lalu klik Simpan Hitungan / Posting.");
        LoadSessions();
        _sessions.SelectedIndex = 0;
    }

    private void LoadSession()
    {
        var list = UiHelpers.Run(() => Program.Services.Inventory.GetOpnames()) ?? new();
        if (_sessions.SelectedIndex < 0 || _sessions.SelectedIndex >= list.Count) return;
        var id = list[_sessions.SelectedIndex].Id;
        var op = UiHelpers.Run(() => Program.Services.Inventory.GetOpname(id));
        if (op == null) return;
        _current = op;
        _grid.Rows.Clear();
        foreach (var i in op.Items)
        {
            var idx = _grid.Rows.Add(i.ProductCode, i.ProductName, i.SystemQty.ToString("0.##"),
                i.CountedQty.ToString("0.##"), i.Difference.ToString("0.##"), i.Adjusted ? "Ya" : "");
            _grid.Rows[idx].Cells["counted"].ReadOnly = op.Status == "POSTED";
            if (i.Difference != 0)
                _grid.Rows[idx].Cells["diff"].Style.ForeColor = i.Difference > 0 ? Theme.Success : Theme.Danger;
        }
        _post.Enabled = op.Status == "DRAFT";
    }

    private void RecalcDiff(int row)
    {
        if (row < 0 || row >= _grid.Rows.Count) return;
        var r = _grid.Rows[row];
        decimal.TryParse(r.Cells["system"].Value?.ToString(), out var sys);
        decimal.TryParse(r.Cells["counted"].Value?.ToString(), out var cnt);
        r.Cells["diff"].Value = (cnt - sys).ToString("0.##");
        r.Cells["diff"].Style.ForeColor = cnt - sys > 0 ? Theme.Success : cnt - sys < 0 ? Theme.Danger : Theme.Text;
    }

    private void SaveCounts()
    {
        if (_current == null) return;
        var counted = new Dictionary<long, decimal>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells["counted"].Value == null) continue;
            if (!decimal.TryParse(row.Cells["counted"].Value.ToString(), out var cnt)) continue;
            counted[ExtractProductId(row)] = cnt;
        }
        if (counted.Count == 0) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Inventory.SaveOpnameCounts(_current.Id, counted, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        UiHelpers.Info("Hitungan tersimpan.");
        LoadSession();
    }

    private long ExtractProductId(DataGridViewRow row)
    {
        // resolve via product code from current opname items
        var code = row.Cells["code"].Value?.ToString() ?? "";
        var item = _current!.Items.FirstOrDefault(i => i.ProductCode == code);
        return item?.ProductId ?? 0;
    }

    private void Post()
    {
        if (_current == null) return;
        SaveCounts();
        if (!UiHelpers.Confirm("Posting akan menyesuaikan stok sesuai selisih opname.\nLanjutkan?")) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Inventory.PostOpname(_current.Id, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info("Opname diposting. Stok telah disesuaikan dengan audit trail.");
        LoadSession();
    }
}

public class LowStockPage : Panel, IPage
{
    private readonly DataGridView _grid = new();

    public LowStockPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Low Stock", "Produk dengan stok <= minimum");

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("name", "Product");
        _grid.Columns.Add("category", "Category");
        _grid.Columns.Add("stock", "Stock");
        _grid.Columns.Add("minstock", "Min Stock");
        _grid.Columns.Add("short", "Kekurangan");

        Controls.Add(_grid);
        Controls.Add(header);
    }

    public void RefreshData()
    {
        var list = UiHelpers.Run(() => Program.Services.Inventory.LowStock()) ?? new();
        _grid.Rows.Clear();
        foreach (var r in list)
            _grid.Rows.Add(r.Code, r.Name, r.Category, r.Stock.ToString("0.##"), r.MinStock.ToString("0.##"),
                (r.MinStock - r.Stock).ToString("0.##"));
    }
}

/// <summary>Barcode lookup + manage barcodes of a product.</summary>
public class BarcodeDialog : Form
{
    private readonly TextBox _barcode = Theme.TextBox(240);
    private readonly Label _result = Theme.Label("", 10, false);

    public BarcodeDialog()
    {
        Text = "Barcode Lookup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 200);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Cek Barcode", 14, true, Theme.Accent);
        title.Location = new Point(20, 14);
        var l = Theme.Label("Scan / ketik barcode:", 9, true);
        l.Location = new Point(20, 60);
        _barcode.Location = new Point(20, 84);
        _barcode.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Lookup();
        };

        _result.Location = new Point(20, 124);
        _result.Size = new Size(420, 50);

        Controls.AddRange(new Control[] { title, l, _barcode, _result });
    }

    private void Lookup()
    {
        var p = UiHelpers.Run(() => Program.Services.Products.GetByBarcode(_barcode.Text.Trim()));
        _result.Text = p == null
            ? "Barcode tidak ditemukan."
            : $"Ditemukan: {p.Code} - {p.Name}\nHarga jual: {Money.Format(p.SellingPrice)}   Stok: {p.Stock:0.##}";
    }
}

/// <summary>Bulk price update by %.</summary>
public class PriceUpdateDialog : Form
{
    private readonly ComboBox _category = Theme.Combo(220);
    private readonly TextBox _percent = Theme.TextBox(100);
    private readonly CheckBox _selling = new() { Text = "Harga Jual", Checked = true, AutoSize = true, Font = Theme.FontBase };
    private readonly CheckBox _purchase = new() { Text = "Harga Beli", AutoSize = true, Font = Theme.FontBase };

    public PriceUpdateDialog()
    {
        Text = "Price Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 260);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Update Harga Massal (%)", 13, true, Theme.Accent);
        title.Location = new Point(20, 14);

        var l1 = Theme.Label("Kategori:", 9, true); l1.Location = new Point(20, 62);
        _category.Location = new Point(120, 58);
        _category.Items.Add("Semua");
        foreach (var c in UiHelpers.Run(() => Program.Services.Products.GetCategories()) ?? new())
            _category.Items.Add(c.Name);
        _category.SelectedIndex = 0;

        var l2 = Theme.Label("Persen (%):", 9, true); l2.Location = new Point(20, 100);
        _percent.Location = new Point(120, 96);
        _percent.PlaceholderText = "misal 10 atau -5";

        _selling.Location = new Point(120, 134);
        _purchase.Location = new Point(210, 134);

        var ok = Theme.PrimaryButton("UPDATE", 140, 40);
        ok.Location = new Point(260, 190);
        ok.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(160, 190);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, l1, _category, l2, _percent, _selling, _purchase, ok, cancel });
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (!decimal.TryParse(_percent.Text.Replace(",", "."), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            UiHelpers.Warn("Persen tidak valid.");
            return;
        }
        if (!_selling.Checked && !_purchase.Checked)
        {
            UiHelpers.Warn("Pilih minimal satu jenis harga.");
            return;
        }
        if (!UiHelpers.Confirm($"Semua harga {(_selling.Checked ? "jual " : "")}{(_purchase.Checked ? "beli" : "")} " +
            $"kategori '{_category.Text}' akan diubah {pct}%.\nLanjutkan?")) return;

        var categoryId = 0L;
        if (_category.SelectedIndex > 0)
            categoryId = (UiHelpers.Run(() => Program.Services.Products.GetCategories())
                ?.FirstOrDefault(c => c.Name == _category.Text))?.Id ?? 0;

        var affected = UiHelpers.Run(() => Program.Services.Products.UpdatePrices(
            categoryId, _selling.Checked, _purchase.Checked, pct, Program.Session!.UserId, Program.Session.Username));
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"{affected} harga diperbarui.");
        DialogResult = DialogResult.OK;
        Close();
    }
}
