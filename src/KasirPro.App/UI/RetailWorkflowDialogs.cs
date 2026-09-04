using Dapper;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Pick serials/IMEIs for a serial-tracked product at POS.</summary>
public class SerialPickerDialog : Form
{
    private readonly CheckedListBox _list = new();
    public List<string> SelectedSerials { get; private set; } = new();

    public SerialPickerDialog(long productId, string productName, decimal qty)
    {
        Text = "Pilih Serial / IMEI";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 420);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label($"{productName} — pilih {qty:0.##} serial:", 10, true, Theme.Accent);
        title.Location = new Point(16, 12);

        _list.Location = new Point(16, 44);
        _list.Size = new Size(428, 290);
        _list.CheckOnClick = true;
        _list.Font = new Font("Consolas", 9.5f);
        var available = UiHelpers.Run(() => Program.Services.Serials.Available(productId)) ?? new();
        foreach (var (id, serial, imei, status) in available)
            _list.Items.Add($"{serial}  {imei}", _list.Items.Count < qty);

        var ok = Theme.PrimaryButton("OK", 110, 38);
        ok.Location = new Point(334, 350);
        ok.Click += (s, e) =>
        {
            SelectedSerials = _list.CheckedItems.Cast<string>()
                .Select(x => x.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries)[0]).ToList();
            if (SelectedSerials.Count != qty)
            {
                UiHelpers.Warn($"Pilih tepat {qty:0.##} serial.");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = Theme.SecondaryButton("Batal", 90, 38);
        cancel.Location = new Point(234, 350);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, _list, ok, cancel });
    }
}

/// <summary>Exchange (tukar barang): old items back, new items out, cash delta.</summary>
public class ExchangeDialog : Form
{
    private readonly TextBox _invoice = Theme.TextBox(200);
    private Sale? _sale;
    private readonly DataGridView _oldItems = new();
    private readonly DataGridView _newItems = new();
    private readonly ComboBox _productPick = Theme.Combo(300);
    private readonly TextBox _reason = Theme.TextBox(380);
    private readonly Label _lblDelta;

    public ExchangeDialog()
    {
        Text = "Exchange / Tukar Barang";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 640);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader("Exchange", "Barang lama kembali ke stok, barang baru keluar, selisih dibayar/dikembalikan");

        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg };
        _invoice.Location = new Point(16, 10);
        _invoice.PlaceholderText = "No invoice asal...";
        var find = Theme.PrimaryButton("Cari", 90);
        find.Location = new Point(222, 8);
        find.Click += (s, e) => LoadInvoice();
        top.Controls.AddRange(new Control[] { _invoice, find });

        // old items
        var oldLabel = Theme.Label("Barang dikembalikan (centang + qty):", 9, true);
        oldLabel.Location = new Point(16, 110);
        Theme.StyleGrid(_oldItems);
        _oldItems.Location = new Point(16, 134);
        _oldItems.Size = new Size(440, 220);
        _oldItems.Columns.Add("sel", "Retur");
        _oldItems.Columns.Add("name", "Product");
        _oldItems.Columns.Add("sold", "Qty");
        _oldItems.Columns.Add("qty", "Qty Retur");
        _oldItems.Columns.Add("price", "Harga");
        _oldItems.Columns.Add("_sid", "");
        _oldItems.Columns["_sid"].Visible = false;
        _oldItems.Columns["sel"].FillWeight = 8;
        _oldItems.Columns["name"].FillWeight = 40;
        _oldItems.Columns["sold"].FillWeight = 14;
        _oldItems.Columns["qty"].FillWeight = 18;
        _oldItems.Columns["price"].FillWeight = 20;
        Theme.MoneyColumn(_oldItems, "price");
        _oldItems.ReadOnly = false;

        // new items
        var pickLabel = Theme.Label("Barang baru:", 9, true);
        pickLabel.Location = new Point(476, 110);
        _productPick.Location = new Point(476, 132);
        _productPick.DropDownStyle = ComboBoxStyle.DropDownList;
        var addBtn = Theme.PrimaryButton("+ Tambah", 100);
        addBtn.Location = new Point(790, 130);
        addBtn.Click += (s, e) => AddNewItem();
        Theme.StyleGrid(_newItems);
        _newItems.Location = new Point(476, 168);
        _newItems.Size = new Size(440, 186);
        _newItems.Columns.Add("name", "Product");
        _newItems.Columns.Add("qty", "Qty");
        _newItems.Columns.Add("price", "Harga");
        _newItems.Columns.Add("_pid", "");
        _newItems.Columns["_pid"].Visible = false;
        _newItems.Columns["name"].FillWeight = 52;
        _newItems.Columns["qty"].FillWeight = 20;
        _newItems.Columns["price"].FillWeight = 28;
        Theme.MoneyColumn(_newItems, "price");
        _newItems.ReadOnly = false;
        _newItems.AllowUserToAddRows = true;

        var rl = Theme.Label("Alasan:", 9, true);
        rl.Location = new Point(16, 364);
        _reason.Location = new Point(80, 360);

        _lblDelta = Theme.Label("", 11, true);
        _lblDelta.Location = new Point(16, 396);
        _lblDelta.Size = new Size(880, 24);

        var process = Theme.SuccessButton("PROSES EXCHANGE", 170, 40);
        process.Location = new Point(746, 540);
        process.Click += OnProcess;
        var close = Theme.SecondaryButton("Tutup", 90, 40);
        close.Location = new Point(646, 540);
        close.Click += (s, e) => Close();

        Controls.Add(_oldItems);
        Controls.Add(_newItems);
        Controls.Add(_productPick);
        Controls.Add(addBtn);
        Controls.Add(pickLabel);
        Controls.Add(oldLabel);
        Controls.Add(top);
        Controls.Add(header);
        Controls.Add(rl);
        Controls.Add(_reason);
        Controls.Add(_lblDelta);
        Controls.Add(process);
        Controls.Add(close);
        Load += (s, e) => LoadProducts();
    }

    private void LoadProducts()
    {
        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        _productPick.DataSource = products;
        _productPick.DisplayMember = "Name";
    }

    private void LoadInvoice()
    {
        var sale = UiHelpers.Run(() => Program.Services.Sales.GetByInvoice(_invoice.Text.Trim()));
        if (sale == null) { UiHelpers.Warn("Invoice tidak ditemukan."); return; }
        if (sale.Status != "COMPLETED") { UiHelpers.Warn("Invoice bukan status COMPLETED."); return; }
        _sale = sale;
        _oldItems.Rows.Clear();
        foreach (var i in sale.Items)
        {
            var already = UiHelpers.Run(() => Program.DbMain.With(c => c.ExecuteScalar<decimal>(
                "SELECT COALESCE(SUM(qty),0) FROM sale_return_items WHERE sale_item_id=@id", new { id = i.Id })));
            _oldItems.Rows.Add(false, i.ProductName, (i.Qty - already).ToString("0.##"), "0", i.Price, i.Id);
        }
    }

    private void AddNewItem()
    {
        if (_productPick.SelectedItem is not Product p) return;
        _newItems.Rows.Add(p.Name, "1", p.SellingPrice, p.Id);
        UpdateDelta();
    }

    private void UpdateDelta()
    {
        decimal oldV = 0, newV = 0;
        foreach (DataGridViewRow r in _oldItems.Rows)
        {
            if (r.IsNewRow) continue;
            if (!Convert.ToBoolean(r.Cells["sel"].Value ?? false)) continue;
            decimal.TryParse(r.Cells["qty"].Value?.ToString(), out var q);
            decimal.TryParse(r.Cells["price"].Value?.ToString(), out var pr);
            oldV += Money.Round(q * pr);
        }
        foreach (DataGridViewRow r in _newItems.Rows)
        {
            if (r.IsNewRow) continue;
            decimal.TryParse(r.Cells["qty"].Value?.ToString(), out var q);
            decimal.TryParse(r.Cells["price"].Value?.ToString(), out var pr);
            newV += Money.Round(q * pr);
        }
        var delta = Money.Round(newV - oldV);
        _lblDelta.Text = delta >= 0
            ? $"Customer bayar: {Money.Format(delta)}"
            : $"Refund ke customer: {Money.Format(-delta)}";
        _lblDelta.ForeColor = delta >= 0 ? Theme.Accent : Theme.Success;
    }

    private void OnProcess(object? sender, EventArgs e)
    {
        if (_sale == null) { UiHelpers.Warn("Cari invoice asal dulu."); return; }
        var oldItems = new List<(long, decimal)>();
        foreach (DataGridViewRow r in _oldItems.Rows)
        {
            if (r.IsNewRow) continue;
            if (!Convert.ToBoolean(r.Cells["sel"].Value ?? false)) continue;
            decimal.TryParse(r.Cells["qty"].Value?.ToString(), out var q);
            if (q <= 0) continue;
            oldItems.Add((Convert.ToInt64(r.Cells["_sid"].Value), q));
        }
        var newItems = new List<(long, decimal, decimal)>();
        foreach (DataGridViewRow r in _newItems.Rows)
        {
            if (r.IsNewRow) continue;
            decimal.TryParse(r.Cells["qty"].Value?.ToString(), out var q);
            decimal.TryParse(r.Cells["price"].Value?.ToString(), out var pr);
            var pid = Convert.ToInt64(r.Cells["_pid"].Value ?? 0);
            if (pid == 0 || q <= 0) continue;
            newItems.Add((pid, q, pr));
        }
        if (oldItems.Count == 0 || newItems.Count == 0)
        {
            UiHelpers.Warn("Pilih barang lama dan barang baru minimal 1.");
            return;
        }
        var reason = string.IsNullOrWhiteSpace(_reason.Text) ? "Tukar barang" : _reason.Text.Trim();
        var session = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));

        var id = UiHelpers.Run(() => Program.Services.Exchanges.Create(_sale.Id, oldItems, newItems,
            reason, Program.Session!.UserId, Program.Session.Username, session?.Id ?? 0));
        if (id <= 0) return;
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info("Exchange tersimpan dengan nomor EXC. Stok lama masuk, baru keluar.");
        Close();
    }
}

/// <summary>Batch manager: receive lots, near-expiry list, write-off expired.</summary>
public class BatchManagerPage : Panel, IPage
{
    private readonly DataGridView _grid = new();
    private readonly ComboBox _mode = Theme.Combo(150);
    private readonly TextBox _search = Theme.TextBox(180);

    public BatchManagerPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Batch / Lot / Expiry", "Kelola batch produk, near-expiry, write-off");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        _mode.Location = new Point(16, 10);
        _mode.Items.AddRange(new object[] { "Semua Batch", "Near Expiry (30 hari)", "Sudah Expired" });
        _mode.SelectedIndex = 0;
        _mode.SelectedIndexChanged += (s, e) => LoadData();
        _search.Location = new Point(174, 10);
        _search.PlaceholderText = "Cari produk / batch...";
        _search.TextChanged += (s, e) => LoadData();
        var receive = Theme.PrimaryButton("+ Terima Batch", 140, 30);
        receive.Location = new Point(360, 8);
        receive.Click += (s, e) => ReceiveBatch();
        var writeOff = Theme.DangerButton("Write-off Expired", 150, 30);
        writeOff.Location = new Point(508, 8);
        writeOff.Click += (s, e) => WriteOff();
        toolbar.Controls.AddRange(new Control[] { _mode, _search, receive, writeOff });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("batch", "Batch No");
        _grid.Columns.Add("product", "Product");
        _grid.Columns.Add("expiry", "Expiry");
        _grid.Columns.Add("qty", "Qty");
        _grid.Columns.Add("cost", "Cost");
        Theme.MoneyColumn(_grid, "cost");

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        List<(long Id, string Batch, string Product, DateTime? Expiry, decimal Qty, decimal Cost)> rows;
        if (_mode.SelectedIndex == 1)
            rows = UiHelpers.Run(() => Program.Services.Batches.NearExpiry(30)) ?? new();
        else if (_mode.SelectedIndex == 2)
            rows = UiHelpers.Run(() => Program.Services.Batches.Expired()) ?? new();
        else
            rows = UiHelpers.Run(() => Program.DbMain.With(c => c.Query<(long, string, string, DateTime?, decimal, decimal)>(@"
                SELECT b.id AS Id, b.batch_no AS Batch, p.name AS Product, b.expiry_date AS Expiry,
                b.qty AS Qty, CAST(ROUND(b.purchase_cost/100.0,2) AS REAL) AS Cost
                FROM inventory_batches b JOIN products p ON p.id=b.product_id
                WHERE b.qty > 0 AND (p.name LIKE @q OR b.batch_no LIKE @q)
                ORDER BY b.expiry_date LIMIT 500",
                new { q = "%" + _search.Text.Trim() + "%" }).ToList())) ?? new();

        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            var idx = _grid.Rows.Add(r.Batch, r.Product, r.Expiry?.ToString("dd/MM/yyyy") ?? "-", r.Qty.ToString("0.##"), r.Cost);
            if (r.Expiry.HasValue && r.Expiry.Value < DateTime.Now)
                _grid.Rows[idx].Cells["expiry"].Style.ForeColor = Theme.Danger;
        }
    }

    private void ReceiveBatch()
    {
        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        var form = new Form
        {
            Text = "Terima Batch",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(420, 300),
            BackColor = Theme.Bg, Font = Theme.FontBase
        };
        var l1 = Theme.Label("Produk:", 9, true); l1.Location = new Point(16, 14);
        var combo = Theme.Combo(250); combo.Location = new Point(90, 10);
        combo.DataSource = products; combo.DisplayMember = "Name";
        var l2 = Theme.Label("Batch No:", 9, true); l2.Location = new Point(16, 52);
        var batch = Theme.TextBox(140); batch.Location = new Point(90, 48);
        var l3 = Theme.Label("Expiry:", 9, true); l3.Location = new Point(16, 90);
        var expiry = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 140, Location = new Point(90, 86), ShowCheckBox = true };
        var l4 = Theme.Label("Qty:", 9, true); l4.Location = new Point(16, 128);
        var qty = Theme.TextBox(90); qty.Location = new Point(90, 124);
        var l5 = Theme.Label("Cost/unit:", 9, true); l5.Location = new Point(16, 166);
        var cost = Theme.TextBox(120); cost.Location = new Point(90, 162);

        var save = Theme.PrimaryButton("SIMPAN", 120, 38);
        save.Location = new Point(280, 240);
        save.Click += (s, e) =>
        {
            if (combo.SelectedItem is not Product p) return;
            if (!decimal.TryParse(qty.Text, out var q) || q <= 0) { UiHelpers.Warn("Qty tidak valid."); return; }
            if (!decimal.TryParse(cost.Text.Replace(".", "").Replace(",", ""), out var cst)) { UiHelpers.Warn("Cost tidak valid."); return; }
            var id = UiHelpers.Run(() => Program.Services.Batches.Receive(p.Id, 1,
                batch.Text.Trim(), expiry.Checked ? expiry.Value : null, q, cst,
                Program.Session!.UserId, Program.Session.Username));
            if (id > 0)
            {
                Program.Session!.DataChangedSinceBackup = true;
                UiHelpers.Info("Batch tersimpan.");
                form.Close();
                LoadData();
            }
        };
        var cancel = Theme.SecondaryButton("Batal", 90, 38);
        cancel.Location = new Point(180, 240);
        cancel.Click += (s, e) => form.Close();

        form.Controls.AddRange(new Control[] { l1, combo, l2, batch, l3, expiry, l4, qty, l5, cost, save, cancel });
        form.ShowDialog(FindForm());
    }

    private void WriteOff()
    {
        if (!Program.Session!.Has("STOCK.ADJUST"))
        {
            UiHelpers.Warn("Tidak memiliki izin STOCK.ADJUST.");
            return;
        }
        if (!UiHelpers.Confirm("Write-off semua batch expired? Stok akan dikurangi dengan referensi EXPIRED.")) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Batches.WriteOffExpired(Program.Session.UserId, Program.Session.Username);
            return null;
        });
        UiHelpers.Info("Write-off selesai.");
        LoadData();
    }
}

/// <summary>Serial/IMEI manager: add, list by product, statuses.</summary>
public class SerialManagerPage : Panel, IPage
{
    private readonly ComboBox _product = Theme.Combo(280);
    private readonly DataGridView _grid = new();

    public SerialManagerPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Serial / IMEI", "Daftar serial per produk dan statusnya");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var pl = Theme.Label("Produk:", 9, true); pl.Location = new Point(16, 13);
        _product.Location = new Point(80, 10);
        _product.SelectedIndexChanged += (s, e) => LoadData();
        var add = Theme.PrimaryButton("+ Add Serial", 120, 30);
        add.Location = new Point(372, 8);
        add.Click += (s, e) => AddSerial();
        toolbar.Controls.AddRange(new Control[] { pl, _product, add });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("serial", "Serial No");
        _grid.Columns.Add("imei1", "IMEI 1");
        _grid.Columns.Add("imei2", "IMEI 2");
        _grid.Columns.Add("status", "Status");

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData()
    {
        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        _product.DataSource = products;
        _product.DisplayMember = "Name";
    }

    private void LoadData()
    {
        if (_product.SelectedItem is not Product p) return;
        var rows = UiHelpers.Run(() => Program.DbMain.With(c => c.Query<(string, string, string, string)>(@"
            SELECT serial_no, imei_1, imei_2, status FROM product_serials
            WHERE product_id=@p ORDER BY serial_no", new { p = p.Id }).ToList())) ?? new();
        _grid.Rows.Clear();
        foreach (var (s, i1, i2, st) in rows)
        {
            var idx = _grid.Rows.Add(s, i1, i2, st);
            _grid.Rows[idx].Cells["status"].Style.ForeColor =
                st == "AVAILABLE" ? Theme.Success : st == "SOLD" ? Theme.Accent : Theme.Danger;
        }
    }

    private void AddSerial()
    {
        if (_product.SelectedItem is not Product p) return;
        var serial = InputDialog.Show("Serial number:", "Add Serial");
        if (string.IsNullOrWhiteSpace(serial)) return;
        var imei1 = InputDialog.Show("IMEI 1 (opsional):", "IMEI 1") ?? "";
        var imei2 = InputDialog.Show("IMEI 2 (opsional):", "IMEI 2") ?? "";
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Serials.Add(p.Id, 1, serial, imei1, imei2, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        LoadData();
    }
}

/// <summary>Reorder suggestions -> create draft PO from selected suggestions.</summary>
public class ReorderPage : Panel, IPage
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _leadTime = Theme.TextBox(50);

    public ReorderPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Purchase Suggestion", "Saran reorder + konversi ke Purchase Order");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var lt = Theme.Label("Lead time (hari):", 9, true);
        lt.Location = new Point(16, 13);
        _leadTime.Location = new Point(125, 10);
        _leadTime.Text = "7";
        var refresh = Theme.PrimaryButton("Hitung Ulang", 120, 30);
        refresh.Location = new Point(185, 8);
        refresh.Click += (s, e) => LoadData();
        var createPo = Theme.SuccessButton("BUAT DRAFT PO", 150, 30);
        createPo.Location = new Point(311, 8);
        createPo.Click += (s, e) => CreateDraftPo();
        toolbar.Controls.AddRange(new Control[] { lt, _leadTime, refresh, createPo });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("sel", "Pilih");
        _grid.Columns.Add("code", "SKU");
        _grid.Columns.Add("name", "Product");
        _grid.Columns.Add("stock", "Stok");
        _grid.Columns.Add("min", "Min");
        _grid.Columns.Add("avg", "Avg/hari");
        _grid.Columns.Add("sug", "Saran Qty");
        _grid.Columns.Add("_pid", "");
        _grid.Columns["_pid"].Visible = false;
        _grid.Columns["sel"].FillWeight = 7;
        _grid.Columns["code"].FillWeight = 13;
        _grid.Columns["name"].FillWeight = 34;
        _grid.Columns["stock"].FillWeight = 10;
        _grid.Columns["min"].FillWeight = 9;
        _grid.Columns["avg"].FillWeight = 12;
        _grid.Columns["sug"].FillWeight = 15;
        _grid.ReadOnly = false;

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        int.TryParse(_leadTime.Text, out var lead);
        var rows = UiHelpers.Run(() => Program.Services.Reorder.Suggest(lead)) ?? new();
        _grid.Rows.Clear();
        foreach (var (code, name, stock, min, rp, target, avg, lt, sug) in rows)
        {
            if (sug <= 0) continue;
            var p = UiHelpers.Run(() => Program.Services.Products.Search(code, 0, false, "name", true, 1, 1));
            var pid = p?.Items.FirstOrDefault()?.Id ?? 0;
            _grid.Rows.Add(true, code, name, stock.ToString("0.##"), min.ToString("0.##"), avg.ToString("0.##"), sug.ToString("0"), pid);
        }
    }

    private void CreateDraftPo()
    {
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow && Convert.ToBoolean(r.Cells["sel"].Value ?? false))
            .Select(r => (Convert.ToInt64(r.Cells["_pid"].Value), Convert.ToDecimal(r.Cells["sug"].Value ?? 0)))
            .Where(x => x.Item2 > 0).ToList();
        if (selected.Count == 0) { UiHelpers.Warn("Pilih minimal 1 produk."); return; }

        // group all into one PO (supplier resolved per default supplier of first product)
        var po = new Purchase();
        foreach (var (pid, qty) in selected)
        {
            var prod = UiHelpers.Run(() => Program.Services.Products.Get(pid));
            if (prod == null) continue;
            po.Items.Add(new PurchaseItem
            {
                ProductId = pid, ProductCode = prod.Code, ProductName = prod.Name,
                Qty = qty, Cost = prod.PurchasePrice
            });
            if (po.SupplierId == 0 && prod.DefaultSupplierId > 0)
            {
                po.SupplierId = prod.DefaultSupplierId;
                po.SupplierName = prod.DefaultSupplierId.ToString();
            }
        }
        var id = UiHelpers.Run(() => Program.Services.Purchases.CreateDraft(po, Program.Session!.UserId, Program.Session.Username));
        if (id <= 0) return;
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"Draft PO #{id} dibuat ({selected.Count} item).\nBuka Purchasing History untuk Approve & Receive.");
    }
}

/// <summary>Per-product price level editor (Retail/Member/Wholesale tiers + qty break).</summary>
public class PriceLevelEditorDialog : Form
{
    private readonly long _productId;
    private readonly string _productName;
    private readonly DataGridView _grid = new();

    public PriceLevelEditorDialog(long productId, string productName)
    {
        _productId = productId;
        _productName = productName;
        Text = $"Price Levels - {productName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 400);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label("Harga per level (kosong = harga dasar):", 10, true, Theme.Accent);
        title.Location = new Point(16, 12);

        Theme.StyleGrid(_grid);
        _grid.Location = new Point(16, 44);
        _grid.Size = new Size(488, 270);
        _grid.Columns.Add("level", "Level");
        _grid.Columns.Add("minqty", "Min Qty");
        _grid.Columns.Add("price", "Harga");
        _grid.Columns.Add("_lid", "");
        _grid.Columns["_lid"].Visible = false;
        _grid.Columns["level"].FillWeight = 34;
        _grid.Columns["minqty"].FillWeight = 26;
        _grid.Columns["price"].FillWeight = 40;
        Theme.MoneyColumn(_grid, "price");
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = true;

        foreach (var (id, name) in UiHelpers.Run(() => Program.Services.Prices.Levels()) ?? new())
            _grid.Rows.Add(name, "", "", id);

        var existing = UiHelpers.Run(() => Program.Services.Prices.ProductPrices(productId)) ?? new();
        foreach (var (level, minQty, price) in existing)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                if (row.Cells["level"].Value?.ToString() == level && price > 0)
                {
                    row.Cells["minqty"].Value = minQty > 0 ? minQty.ToString("0.##") : "";
                    row.Cells["price"].Value = price;
                }
            }
        }

        var save = Theme.PrimaryButton("SIMPAN", 120, 38);
        save.Location = new Point(384, 330);
        save.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 38);
        cancel.Location = new Point(284, 330);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, _grid, save, cancel });
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var saved = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var levelName = row.Cells["level"].Value?.ToString();
            decimal.TryParse(row.Cells["price"].Value?.ToString(), out var price);
            decimal.TryParse(row.Cells["minqty"].Value?.ToString(), out var minQty);
            if (string.IsNullOrWhiteSpace(levelName)) continue;
            var levels = UiHelpers.Run(() => Program.Services.Prices.Levels());
            var levelId = levels?.FirstOrDefault(l => l.Name == levelName).Id ?? 0;
            if (levelId == 0) continue;
            var pid = UiHelpers.Run(() => Program.Services.Products.Search(_productName, 0, false, "name", true, 1, 1))
                ?.Items.FirstOrDefault()?.Id ?? _productId;
            UiHelpers.Run<object?>(() =>
            {
                Program.Services.Prices.SetPrice(_productId, levelId, minQty, price,
                    Program.Session!.UserId, Program.Session.Username);
                return null;
            });
            saved++;
        }
        UiHelpers.Info($"{saved} baris harga tersimpan.");
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Import preview: validation + error report before applying product import.</summary>
public class ImportPreviewDialog : Form
{
    private readonly List<ImportRow> _rows;
    private readonly string _filePath;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _updateMode = new() { Text = "Update produk yang sudah ada (berdasarkan kode)", AutoSize = true, Font = Theme.FontBase };

    public ImportPreviewDialog(string filePath, List<ImportRow> rows)
    {
        _filePath = filePath;
        _rows = rows;
        Text = "Import Preview";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 600);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var valid = rows.Count(r => r.Valid);
        var header = Theme.PageHeader("Import Preview", $"{rows.Count} baris, {valid} valid, {rows.Count - valid} error");

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("line", "Baris");
        _grid.Columns.Add("code", "Kode");
        _grid.Columns.Add("name", "Nama");
        _grid.Columns.Add("barcode", "Barcode");
        _grid.Columns.Add("price", "Harga Jual");
        _grid.Columns.Add("stock", "Stok");
        _grid.Columns.Add("status", "Status");
        foreach (var r in rows.Take(500))
        {
            var idx = _grid.Rows.Add(r.LineNo, r.Code, r.Name, r.Barcode, r.SellingPrice, r.Stock,
                r.Valid ? "OK" : string.Join("; ", r.Errors));
            if (!r.Valid) _grid.Rows[idx].Cells["status"].Style.ForeColor = Theme.Danger;
        }

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Bg };
        _updateMode.Location = new Point(16, 8);
        var ok = Theme.SuccessButton("IMPORT " + valid + " BARIS", 200, 38);
        ok.Location = new Point(660, 20);
        ok.Click += OnImport;
        var cancel = Theme.SecondaryButton("Batal", 90, 38);
        cancel.Location = new Point(560, 20);
        cancel.Click += (s, e) => Close();
        bottom.Controls.AddRange(new Control[] { _updateMode, ok, cancel });

        Controls.Add(_grid);
        Controls.Add(header);
        Controls.Add(bottom);
    }

    private void OnImport(object? sender, EventArgs e)
    {
        var validRows = _rows.Where(r => r.Valid).ToList();
        if (validRows.Count == 0) return;
        var (ins, upd) = UiHelpers.Run(() => Program.Services.Products.ImportProducts(
            validRows.Select(r => (r.Code, r.Name, r.Barcode, r.Category, r.Unit,
                r.PurchasePrice, r.SellingPrice, r.Stock)),
            Program.Session!.UserId, Program.Session.Username));
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"Import selesai: {ins} baru, {upd} diupdate.");
        DialogResult = DialogResult.OK;
        Close();
    }
}
