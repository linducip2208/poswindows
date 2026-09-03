using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

/// <summary>New purchase entry: supplier + items grid -> stock increases atomically.</summary>
public class PurchaseEntryPage : Panel, IPage
{
    private readonly ComboBox _supplier = Theme.Combo(220);
    private readonly TextBox _supplierInvoice = Theme.TextBox(160);
    private readonly DataGridView _items = new();
    private readonly TextBox _productSearch = Theme.TextBox(240);
    private readonly Label _lblTotal;
    private readonly ComboBox _productPick = Theme.Combo(320);

    public PurchaseEntryPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Purchase", "Catat pembelian dari supplier (stok otomatis bertambah)");

        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg };
        var sl = Theme.Label("Supplier:", 9, true); sl.Location = new Point(16, 13);
        _supplier.Location = new Point(90, 10);
        var il = Theme.Label("No Invoice Supplier:", 9, true); il.Location = new Point(330, 13);
        _supplierInvoice.Location = new Point(480, 10);
        top.Controls.AddRange(new Control[] { sl, _supplier, il, _supplierInvoice });

        var pick = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg };
        var pl = Theme.Label("Tambah Produk:", 9, true); pl.Location = new Point(16, 13);
        _productPick.DropDownStyle = ComboBoxStyle.DropDown;
        _productPick.Location = new Point(120, 10);
        _productPick.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _productPick.AutoCompleteSource = AutoCompleteSource.ListItems;
        var addBtn = Theme.PrimaryButton("+ Tambah", 100);
        addBtn.Location = new Point(450, 8);
        addBtn.Click += (s, e) => AddProduct();
        var qtyLbl = Theme.Label("Qty:", 9, true); qtyLbl.Location = new Point(560, 13);
        var qtyBox = Theme.TextBox(60); qtyBox.Location = new Point(590, 10); qtyBox.Text = "1";
        qtyBox.Name = "qty";
        pick.Controls.AddRange(new Control[] { pl, _productPick, addBtn, qtyLbl, qtyBox });

        Theme.StyleGrid(_items);
        _items.Dock = DockStyle.Fill;
        _items.Columns.Add("code", "Code");
        _items.Columns.Add("name", "Product");
        _items.Columns.Add("qty", "Qty");
        _items.Columns.Add("cost", "Harga Beli");
        _items.Columns.Add("subtotal", "Subtotal");
        _items.Columns.Add("_pid", "");
        _items.Columns["_pid"].Visible = false;
        _items.Columns["code"].FillWeight = 14;
        _items.Columns["name"].FillWeight = 40;
        _items.Columns["qty"].FillWeight = 12;
        _items.Columns["cost"].FillWeight = 17;
        _items.Columns["subtotal"].FillWeight = 17;
        Theme.MoneyColumn(_items, "cost");
        Theme.MoneyColumn(_items, "subtotal");
        _items.ReadOnly = false;
        _items.AllowUserToAddRows = true;
        _items.CellValueChanged += (s, e) => UpdateTotal();
        _items.UserDeletedRow += (s, e) => UpdateTotal();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Bg };
        _lblTotal = Theme.Label("Total: Rp 0", 14, true, Theme.Accent);
        _lblTotal.Location = new Point(16, 18);
        var save = Theme.SuccessButton("SIMPAN PEMBELIAN", 180, 40);
        save.Location = new Point(640, 12);
        save.Click += OnSave;
        bottom.Controls.AddRange(new Control[] { _lblTotal, save });

        Controls.Add(_items);
        Controls.Add(pick);
        Controls.Add(top);
        Controls.Add(header);
        Controls.Add(bottom);
    }

    public void RefreshData()
    {
        var suppliers = UiHelpers.Run(() => Program.Services.Products.GetSuppliers()) ?? new();
        _supplier.DataSource = suppliers;
        _supplier.DisplayMember = "Name";

        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        _productPick.DataSource = products;
        _productPick.DisplayMember = "Name";
    }

    private void AddProduct()
    {
        if (_productPick.SelectedItem is not Product p) return;
        decimal qty = 1;
        var qtyBox = Controls.Find("qty", true).FirstOrDefault() as TextBox;
        decimal.TryParse(qtyBox?.Text, out qty);
        if (qty <= 0) qty = 1;
        _items.Rows.Add(p.Code, p.Name, qty.ToString("0.##"), p.PurchasePrice, Money.Round(qty * p.PurchasePrice), p.Id);
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        decimal total = 0;
        foreach (DataGridViewRow row in _items.Rows)
        {
            if (row.IsNewRow) continue;
            decimal.TryParse(row.Cells["qty"].Value?.ToString(), out var qty);
            decimal.TryParse(row.Cells["cost"].Value?.ToString(), out var cost);
            row.Cells["subtotal"].Value = Money.Round(qty * cost);
            total += Money.Round(qty * cost);
        }
        _lblTotal.Text = "Total: " + Money.Format(total);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var items = new List<PurchaseItem>();
        foreach (DataGridViewRow row in _items.Rows)
        {
            if (row.IsNewRow) continue;
            decimal.TryParse(row.Cells["qty"].Value?.ToString(), out var qty);
            decimal.TryParse(row.Cells["cost"].Value?.ToString(), out var cost);
            var pid = Convert.ToInt64(row.Cells["_pid"].Value ?? 0);
            if (pid == 0 || qty <= 0) continue;
            items.Add(new PurchaseItem
            {
                ProductId = pid, ProductCode = row.Cells["code"].Value?.ToString() ?? "",
                ProductName = row.Cells["name"].Value?.ToString() ?? "",
                Qty = qty, Cost = cost
            });
        }
        if (items.Count == 0) { UiHelpers.Warn("Tambahkan minimal 1 produk."); return; }

        var po = new Purchase
        {
            SupplierId = (_supplier.SelectedItem as Supplier)?.Id ?? 0,
            SupplierName = (_supplier.SelectedItem as Supplier)?.Name ?? "",
            SupplierInvoiceNo = _supplierInvoice.Text.Trim(),
            Items = items
        };
        var saved = UiHelpers.Run(() =>
            Program.Services.Purchases.CreatePurchase(po, Program.Session!.UserId, Program.Session.Username));
        if (saved == null || saved.Id == 0) return;
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"Pembelian {saved.PurchaseNo} tersimpan.\nTotal: {Money.Format(saved.Total)}\nStok telah bertambah.");
        _items.Rows.Clear();
        UpdateTotal();
    }
}

public class PurchaseHistoryPage : Panel, IPage
{
    private const int PageSize = 30;
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = Theme.TextBox(240);
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly PagingBar _paging = new();
    private int _page = 1;

    public PurchaseHistoryPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Purchase History", "Riwayat pembelian");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg };
        _search.PlaceholderText = "Cari PO / invoice supplier / supplier...";
        _search.Location = new Point(16, 8);
        _from.Location = new Point(264, 8);
        _to.Location = new Point(380, 8);
        var btnSearch = Theme.PrimaryButton("Cari", 80);
        btnSearch.Location = new Point(496, 6);
        btnSearch.Click += (s, e) => { _page = 1; LoadData(); };
        var btnDetail = Theme.SecondaryButton("View Detail", 110);
        btnDetail.Location = new Point(582, 6);
        btnDetail.Click += (s, e) => ViewDetail();
        var btnReturn = Theme.DangerButton("Return", 90);
        btnReturn.Location = new Point(698, 6);
        btnReturn.Click += (s, e) => ReturnSelected();
        toolbar.Controls.AddRange(new Control[] { _search, _from, _to, btnSearch, btnDetail, btnReturn });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("po", "No Purchase");
        _grid.Columns.Add("date", "Date");
        _grid.Columns.Add("supplier", "Supplier");
        _grid.Columns.Add("total", "Total");
        _grid.Columns.Add("status", "Status");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["po"].FillWeight = 24;
        _grid.Columns["date"].FillWeight = 20;
        _grid.Columns["supplier"].FillWeight = 28;
        _grid.Columns["total"].FillWeight = 16;
        _grid.Columns["status"].FillWeight = 12;
        Theme.MoneyColumn(_grid, "total");
        _grid.CellDoubleClick += (s, e) => ViewDetail();
        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var result = UiHelpers.Run(() => Program.Services.Purchases.History(
            _search.Text, _from.Value, _to.Value, _page, PageSize));
        if (result == null) return;
        _grid.Rows.Clear();
        foreach (var p in result.Items)
            _grid.Rows.Add(p.PurchaseNo, p.PurchaseDate.ToString("dd/MM/yyyy HH:mm"), p.SupplierName, p.Total, p.Status, p.Id);
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }

    private void ViewDetail()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        var po = UiHelpers.Run(() => Program.Services.Purchases.GetById(id));
        if (po == null) return;
        var msg = $"PO: {po.PurchaseNo}\nTanggal: {po.PurchaseDate:dd/MM/yyyy HH:mm}\nSupplier: {po.SupplierName}\n" +
                  $"Invoice Supplier: {po.SupplierInvoiceNo}\n\n" +
                  string.Join("\n", po.Items.Select(i => $"{i.ProductName} x{i.Qty:0.##} @ {Money.Format(i.Cost)}")) +
                  $"\n\nTotal: {Money.Format(po.Total)}";
        UiHelpers.Info(msg, "Detail Pembelian");
    }

    private void ReturnSelected()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        var po = UiHelpers.Run(() => Program.Services.Purchases.GetById(id));
        if (po == null) return;
        new PurchaseReturnDialog(po).ShowDialog();
        LoadData();
    }
}

/// <summary>Purchase return entry-point helper (redirects to Purchase History > Return).</summary>
public static class PurchaseReturnEntry
{
    public static void Show()
    {
        UiHelpers.Warn("Pilih pembelian dari menu Data > Purchase History, lalu klik tombol Return.");
    }
}

public class PurchaseReturnDialog : Form
{
    private readonly Purchase _po;
    private readonly DataGridView _items = new();
    private readonly TextBox _reason = Theme.TextBox(360);

    public PurchaseReturnDialog(Purchase po)
    {
        _po = po;
        Text = $"Purchase Return - {po.PurchaseNo}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 460);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var head = Theme.Label($"{po.PurchaseNo} - {po.SupplierName} - {po.PurchaseDate:dd/MM/yyyy}", 11, true);
        head.Location = new Point(18, 14);

        Theme.StyleGrid(_items);
        _items.Location = new Point(18, 48);
        _items.Size = new Size(640, 250);
        _items.Columns.Add("ret", "Retur");
        _items.Columns.Add("name", "Product");
        _items.Columns.Add("qty", "Qty Terima");
        _items.Columns.Add("qtyret", "Qty Retur");
        _items.Columns.Add("cost", "Harga");
        _items.Columns.Add("_pid", "");
        _items.Columns["_pid"].Visible = false;
        _items.Columns["ret"].FillWeight = 8;
        _items.Columns["name"].FillWeight = 40;
        _items.Columns["qty"].FillWeight = 15;
        _items.Columns["qtyret"].FillWeight = 15;
        _items.Columns["cost"].FillWeight = 22;
        Theme.MoneyColumn(_items, "cost");
        _items.ReadOnly = false;
        foreach (var i in po.Items)
            _items.Rows.Add(false, i.ProductName, i.Qty.ToString("0.##"), "0", i.Cost, i.ProductId);

        var rl = Theme.Label("Alasan:", 9, true);
        rl.Location = new Point(18, 312);
        _reason.Location = new Point(80, 308);

        var save = Theme.SuccessButton("PROSES RETUR", 150, 40);
        save.Location = new Point(500, 380);
        save.Click += OnSave;
        var close = Theme.SecondaryButton("Tutup", 90, 40);
        close.Location = new Point(400, 380);
        close.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { head, _items, rl, _reason, save, close });
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var items = new List<(long, decimal)>();
        foreach (DataGridViewRow row in _items.Rows)
        {
            var isRet = Convert.ToBoolean(row.Cells["ret"].Value ?? false);
            decimal.TryParse(row.Cells["qtyret"].Value?.ToString(), out var qty);
            if (!isRet || qty <= 0) continue;
            items.Add((Convert.ToInt64(row.Cells["_pid"].Value), qty));
        }
        if (items.Count == 0) { UiHelpers.Warn("Pilih minimal satu item dengan qty > 0."); return; }
        var reason = string.IsNullOrWhiteSpace(_reason.Text) ? "Retur ke supplier" : _reason.Text.Trim();

        var result = UiHelpers.Run(() =>
            Program.Services.Purchases.CreateReturn(_po.Id, items, reason,
                Program.Session!.UserId, Program.Session.Username));
        if (result == null) return;
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"Retur {result.ReturnNo} berhasil.\nTotal: {Money.Format(result.Total)}\nStok dikurangi.");
        DialogResult = DialogResult.OK;
        Close();
    }
}
