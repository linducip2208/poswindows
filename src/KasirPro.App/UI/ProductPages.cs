using System.Text;
using Dapper;
using KasirPro.Infrastructure;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Product list: search, filter, sort, pagination, CRUD, import/export, low stock hint.</summary>
public class ProductListPage : Panel, IPage
{
    private const int PageSize = 25;
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = Theme.TextBox(220);
    private readonly ComboBox _category = Theme.Combo(160);
    private readonly CheckBox _lowOnly = new() { Text = "Low stock", AutoSize = true, Font = Theme.FontBase };
    private readonly ComboBox _sort = Theme.Combo(120);
    private readonly PagingBar _paging = new();
    private int _page = 1;

    public ProductListPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Product List", "Master data produk");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _search.PlaceholderText = "Cari nama / kode / barcode...";
        _search.Location = new Point(16, 10);
        _search.TextChanged += (s, e) => { _page = 1; LoadData(); };
        _category.Location = new Point(244, 10);
        _category.SelectedIndexChanged += (s, e) => { _page = 1; LoadData(); };
        _lowOnly.Location = new Point(414, 13);
        _lowOnly.CheckedChanged += (s, e) => { _page = 1; LoadData(); };
        _sort.Location = new Point(510, 10);
        _sort.Items.AddRange(new object[] { "Nama A-Z", "Nama Z-A", "Stok", "Harga Jual" });
        _sort.SelectedIndex = 0;
        _sort.SelectedIndexChanged += (s, e) => { _page = 1; LoadData(); };

        var btnAdd = Theme.PrimaryButton("+ Add Product", 120);
        btnAdd.Location = new Point(650, 8);
        btnAdd.Click += (s, e) => { new ProductEditDialog(null).ShowDialog(); LoadData(); };
        var btnEdit = Theme.SecondaryButton("Edit", 70);
        btnEdit.Location = new Point(776, 8);
        btnEdit.Click += (s, e) => EditSelected();
        var btnDelete = Theme.DangerButton("Delete", 80);
        btnDelete.Location = new Point(852, 8);
        btnDelete.Click += (s, e) => DeleteSelected();
        var btnImport = Theme.SecondaryButton("Import", 80);
        btnImport.Location = new Point(938, 8);
        btnImport.Click += (s, e) => ImportCsv();
        var btnExport = Theme.SecondaryButton("Export", 80);
        btnExport.Location = new Point(1024, 8);
        btnExport.Click += (s, e) => ExportCsv();
        toolbar.Controls.AddRange(new Control[] { _search, _category, _lowOnly, _sort, btnAdd, btnEdit, btnDelete, btnImport, btnExport });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("barcode", "Barcode");
        _grid.Columns.Add("name", "Product Name");
        _grid.Columns.Add("category", "Category");
        _grid.Columns.Add("purchase", "Purchase Price");
        _grid.Columns.Add("selling", "Selling Price");
        _grid.Columns.Add("stock", "Stock");
        _grid.Columns.Add("unit", "Unit");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["code"].FillWeight = 10;
        _grid.Columns["barcode"].FillWeight = 14;
        _grid.Columns["name"].FillWeight = 26;
        _grid.Columns["category"].FillWeight = 13;
        _grid.Columns["purchase"].FillWeight = 12;
        _grid.Columns["selling"].FillWeight = 12;
        _grid.Columns["stock"].FillWeight = 8;
        _grid.Columns["unit"].FillWeight = 5;
        Theme.MoneyColumn(_grid, "purchase");
        Theme.MoneyColumn(_grid, "selling");
        _grid.CellDoubleClick += (s, e) => EditSelected();
        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() { LoadCategories(); LoadData(); }

    private void LoadCategories()
    {
        var current = _category.SelectedIndex;
        _category.Items.Clear();
        _category.Items.Add("Semua Kategori");
        foreach (var c in UiHelpers.Run(() => Program.Services.Products.GetCategories()) ?? new())
            _category.Items.Add(c.Name);
        if (current >= 0 && current < _category.Items.Count) _category.SelectedIndex = current;
        else _category.SelectedIndex = 0;
    }

    private void LoadData()
    {
        var categoryId = 0L;
        if (_category.SelectedIndex > 0)
            categoryId = (UiHelpers.Run(() => Program.Services.Products.GetCategories())
                ?.FirstOrDefault(c => c.Name == _category.Text))?.Id ?? 0;
        var ascending = _sort.SelectedIndex != 1;
        var sortBy = _sort.SelectedIndex switch { 2 => "stock", 3 => "selling", _ => "name" };

        var result = UiHelpers.Run(() => Program.Services.Products.Search(
            _search.Text, categoryId, _lowOnly.Checked, sortBy, ascending, _page, PageSize));
        if (result == null) return;

        _grid.Rows.Clear();
        foreach (var p in result.Items)
        {
            var barcode = UiHelpers.Run(() => FirstBarcode(p.Id)) ?? "";
            var idx = _grid.Rows.Add(p.Code, barcode, p.Name, p.CategoryName, p.PurchasePrice, p.SellingPrice,
                p.Stock.ToString("0.##"), p.UnitName, p.Id);
            if (p.Stock <= p.MinStock)
            {
                _grid.Rows[idx].Cells["stock"].Style.ForeColor = Theme.Danger;
                _grid.Rows[idx].Cells["stock"].Style.Font = Theme.FontMediumBold;
            }
        }
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }

    private string? FirstBarcode(long productId) =>
        Program.DbMain.With(c => c.ExecuteScalar<string>(
            "SELECT barcode FROM product_barcodes WHERE product_id=@id LIMIT 1", new { id = productId }));

    private void EditSelected()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        var p = UiHelpers.Run(() => Program.Services.Products.Get(id));
        if (p == null) return;
        new ProductEditDialog(p).ShowDialog();
        LoadData();
    }

    private void DeleteSelected()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        var name = _grid.CurrentRow.Cells["name"].Value?.ToString();
        if (!UiHelpers.Confirm($"Hapus produk '{name}'?\nData historis (penjualan) tetap tersimpan.")) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Products.DeleteProduct(id, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        LoadData();
    }

    private void ImportCsv()
    {
        using var dlg = new OpenFileDialog { Filter = "CSV|*.csv", Title = "Import Produk (code;name;barcode;category;unit;purchase_price;selling_price;stock)" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName).Skip(1); // header
            var rows = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
            {
                var parts = l.Split(new[] { ';', '\t', ',' });
                decimal Parse(int i) => parts.Length > i && decimal.TryParse(parts[i], out var v) ? v : 0;
                return (parts.Length > 0 ? parts[0].Trim() : "", parts.Length > 1 ? parts[1].Trim() : "",
                        parts.Length > 2 ? parts[2].Trim() : "", parts.Length > 3 ? parts[3].Trim() : "",
                        parts.Length > 4 ? parts[4].Trim() : "", Parse(5), Parse(6), Parse(7));
            }).ToList();
            var (ins, upd) = Program.Services.Products.ImportProducts(rows, Program.Session!.UserId, Program.Session.Username);
            Program.Session.DataChangedSinceBackup = true;
            UiHelpers.Info($"Import selesai. {ins} produk baru, {upd} diperbarui.");
            LoadData();
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Import gagal: " + ex.Message);
        }
    }

    private void ExportCsv()
    {
        try
        {
            AppPaths.EnsureAll();
            var file = Path.Combine(AppPaths.ExportsDir, $"products-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var sb = new StringBuilder();
            sb.AppendLine("code;name;barcode;category;unit;purchase_price;selling_price;stock");
            var all = Program.Services.Products.Search("", 0, false, "name", true, 1, 100000);
            foreach (var p in all.Items)
            {
                var barcode = FirstBarcode(p.Id) ?? "";
                sb.AppendLine($"{p.Code};{p.Name};{barcode};{p.CategoryName};{p.UnitName};{p.PurchasePrice:0.##};{p.SellingPrice:0.##};{p.Stock:0.##}");
            }
            File.WriteAllText(file, sb.ToString());
            UiHelpers.Info("Export selesai:\n" + file);
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Export gagal: " + ex.Message);
        }
    }
}

/// <summary>Add/edit product incl. barcodes + optional image.</summary>
public class ProductEditDialog : Form
{
    private readonly Product? _product;
    private readonly TextBox _code = Theme.TextBox(200);
    private readonly TextBox _name = Theme.TextBox(340);
    private readonly ComboBox _category = Theme.Combo(200);
    private readonly ComboBox _unit = Theme.Combo(120);
    private readonly TextBox _purchasePrice = Theme.TextBox(140);
    private readonly TextBox _sellingPrice = Theme.TextBox(140);
    private readonly TextBox _stock = Theme.TextBox(100);
    private readonly TextBox _minStock = Theme.TextBox(100);
    private readonly TextBox _barcodes = Theme.TextBox(340);
    private readonly TextBox _imagePath = Theme.TextBox(280);
    private readonly TextBox _wholesalePrice = Theme.TextBox(140);
    private readonly TextBox _wholesaleMinQty = Theme.TextBox(140);
    private readonly ComboBox _taxMode = Theme.Combo(140);

    public ProductEditDialog(Product? product)
    {
        _product = product;
        Text = product == null ? "Add Product" : "Edit Product";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 600);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var y = 14;
        void Field(string label, Control c, int width = 0)
        {
            var l = Theme.Label(label, 9, true);
            l.Location = new Point(20, y);
            c.Location = new Point(160, y - 3);
            if (width > 0) c.Width = width;
            Controls.Add(l);
            Controls.Add(c);
            y += 36;
        }

        Field("Kode (kosong = auto):", _code);
        Field("Nama Produk:", _name);
        Field("Kategori:", _category);
        Field("Satuan:", _unit);
        Field("Harga Beli:", _purchasePrice);
        Field("Harga Jual:", _sellingPrice);
        if (product == null) Field("Stok Awal:", _stock);
        Field("Stok Minimum:", _minStock);
        Field("Barcode(s) koma:", _barcodes);
        Field("Harga Grosir:", _wholesalePrice);
        Field("Min Qty Grosir:", _wholesaleMinQty);
        Field("Mode PPN:", _taxMode);
        _taxMode.Items.AddRange(new object[] { "Ikut toko", "Tanpa PPN", "Include PPN", "Exclude PPN" });
        Field("Gambar:", _imagePath, 280);

        var browse = Theme.SecondaryButton("...", 40, 28);
        browse.Location = new Point(446, y - 31);
        browse.Click += (s, e) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Gambar|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == DialogResult.OK) _imagePath.Text = dlg.FileName;
        };
        Controls.Add(browse);

        var save = Theme.PrimaryButton("SIMPAN", 130, 40);
        save.Location = new Point(410, y + 12);
        save.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(310, y + 12);
        cancel.Click += (s, e) => Close();
        Controls.Add(save);
        Controls.Add(cancel);

        var cats = UiHelpers.Run(() => Program.Services.Products.GetCategories()) ?? new();
        _category.Items.Add("- none -");
        foreach (var c in cats) _category.Items.Add(c.Name);
        _category.SelectedIndex = 0;
        var units = UiHelpers.Run(() => Program.Services.Products.GetUnits()) ?? new();
        foreach (var u in units) _unit.Items.Add(u.Name);
        if (units.Count > 0) _unit.SelectedIndex = 0;

        if (product != null)
        {
            _code.Text = product.Code;
            _name.Text = product.Name;
            var catIdx = cats.FindIndex(c => c.Id == product.CategoryId);
            _category.SelectedIndex = catIdx + 1;
            var unitIdx = units.FindIndex(u => u.Name == product.UnitName);
            _unit.SelectedIndex = unitIdx >= 0 ? unitIdx : 0;
            _purchasePrice.Text = product.PurchasePrice.ToString("0.##");
            _sellingPrice.Text = product.SellingPrice.ToString("0.##");
            _wholesalePrice.Text = product.WholesalePrice > 0 ? product.WholesalePrice.ToString("0.##") : "";
            _wholesaleMinQty.Text = product.WholesaleMinQty > 0 ? product.WholesaleMinQty.ToString("0.##") : "";
            _taxMode.SelectedIndex = product.TaxMode switch
            {
                KasirPro.Core.Domain.TaxMode.None => 1,
                KasirPro.Core.Domain.TaxMode.Inclusive => 2,
                KasirPro.Core.Domain.TaxMode.Exclusive => 3,
                _ => 0
            };
            _minStock.Text = product.MinStock.ToString("0.##");
            _imagePath.Text = product.ImagePath;
            var bcs = UiHelpers.Run(() =>
                Program.DbMain.With(c => c.Query<string>("SELECT barcode FROM product_barcodes WHERE product_id=@id", new { id = product.Id }).ToList())) ?? new();
            _barcodes.Text = string.Join(", ", bcs);
            _stock.Visible = false;
            _name.Focus();
        }
        else
        {
            _taxMode.SelectedIndex = 0;
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            UiHelpers.Warn("Nama produk wajib diisi.");
            return;
        }
        decimal Parse(TextBox t) =>
            decimal.TryParse(t.Text.Replace(".", "").Replace(",", "."), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        // copy image into Images/Products (relative path stored)
        var imagePath = _imagePath.Text.Trim();
        if (imagePath.Length > 0 && File.Exists(imagePath) && !imagePath.StartsWith("Images"))
        {
            try
            {
                AppPaths.EnsureAll();
                var ext = Path.GetExtension(imagePath);
                var fileName = $"p{_product?.Id ?? 0}-{Guid.NewGuid().ToString("N")[..8]}{ext}";
                var dest = Path.Combine(AppPaths.ImagesProductsDir, fileName);
                File.Copy(imagePath, dest, true);
                imagePath = "Images/Products/" + fileName;
            }
            catch (Exception ex)
            {
                UiHelpers.Warn("Gambar tidak bisa disalin: " + ex.Message);
            }
        }

        var cat = _category.SelectedIndex > 0
            ? UiHelpers.Run(() => Program.Services.Products.GetCategories())?.FirstOrDefault(c => c.Name == _category.Text)
            : null;
        var unitName = _unit.Text;
        var unit = UiHelpers.Run(() => Program.Services.Products.GetUnits())?.FirstOrDefault(u => u.Name == unitName);

        var product = _product ?? new Product();
        product.Code = _code.Text.Trim();
        product.Name = _name.Text.Trim();
        product.CategoryId = cat?.Id ?? 0;
        product.CategoryName = cat?.Name ?? "";
        product.UnitId = unit?.Id ?? 1;
        product.UnitName = unit?.Name ?? "";
        product.PurchasePrice = Parse(_purchasePrice);
        product.SellingPrice = Parse(_sellingPrice);
        product.WholesalePrice = Parse(_wholesalePrice);
        product.WholesaleMinQty = Parse(_wholesaleMinQty);
        product.TaxMode = _taxMode.SelectedIndex switch
        {
            1 => KasirPro.Core.Domain.TaxMode.None,
            2 => KasirPro.Core.Domain.TaxMode.Inclusive,
            3 => KasirPro.Core.Domain.TaxMode.Exclusive,
            _ => KasirPro.Core.Domain.TaxMode.Default
        };
        product.MinStock = Parse(_minStock);
        product.ImagePath = imagePath;
        if (_product == null) product.Stock = Parse(_stock);

        var barcodes = _barcodes.Text.Split(',', ';', '\n')
            .Select(b => b.Trim()).Where(b => b.Length > 0).ToArray();

        var id = UiHelpers.Run(() => Program.Services.Products.SaveProduct(
            product, barcodes, Program.Session!.UserId, Program.Session.Username));
        if (id <= 0) return;
        Program.Session!.DataChangedSinceBackup = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Simple category list editor.</summary>
public class CategoriesPage : Panel, IPage
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _newName = Theme.TextBox(200);

    public CategoriesPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Categories", "Kategori produk");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _newName.Location = new Point(16, 10);
        _newName.PlaceholderText = "Nama kategori baru...";
        var add = Theme.PrimaryButton("+ Tambah", 100);
        add.Location = new Point(224, 8);
        add.Click += (s, e) => Add();
        var delete = Theme.DangerButton("Delete", 90);
        delete.Location = new Point(330, 8);
        delete.Click += (s, e) => Delete();
        toolbar.Controls.AddRange(new Control[] { _newName, add, delete });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("name", "Kategori");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var cats = UiHelpers.Run(() => Program.Services.Products.GetCategories()) ?? new();
        _grid.Rows.Clear();
        foreach (var c in cats) _grid.Rows.Add(c.Name, c.Id);
    }

    private void Add()
    {
        if (string.IsNullOrWhiteSpace(_newName.Text)) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Products.SaveCategory(new Category { Name = _newName.Text }, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        _newName.Clear();
        LoadData();
    }

    private void Delete()
    {
        if (_grid.CurrentRow == null) return;
        if (!UiHelpers.Confirm("Hapus kategori ini?")) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Products.DeleteCategory(Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value),
                Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        LoadData();
    }
}

public class UnitsPage : CategoriesPage
{
    public UnitsPage()
    {
        Controls.Clear();
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Units", "Satuan produk (PCS, BOX, KG, ...)");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var hint = Theme.Label("Satuan dikelola otomatis via import; tambah manual via Tools > Database nanti.", 9, false, Theme.Muted);
        hint.Location = new Point(16, 14);
        toolbar.Controls.Add(hint);

        var grid = new DataGridView();
        Theme.StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.Columns.Add("name", "Satuan");

        var units = UiHelpers.Run(() => Program.Services.Products.GetUnits()) ?? new();
        foreach (var u in units) grid.Rows.Add(u.Name);

        Controls.Add(grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }
}



