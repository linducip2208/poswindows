using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>History of X/Z reports with reprint.</summary>
public class XZHistoryPage : Panel, IPage
{
    private readonly DataGridView _grid = new();

    public XZHistoryPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("X/Z Report History", "Riwayat laporan kasir X (berjalan) dan Z (tutup)");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var btnRefresh = Theme.PrimaryButton("Refresh", 100);
        btnRefresh.Location = new Point(16, 8);
        btnRefresh.Click += (s, e) => LoadData();
        var btnDetail = Theme.SecondaryButton("Lihat Detail", 110);
        btnDetail.Location = new Point(122, 8);
        btnDetail.Click += (s, e) => ShowDetail();
        toolbar.Controls.AddRange(new Control[] { btnRefresh, btnDetail });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("no", "No Report");
        _grid.Columns.Add("type", "Tipe");
        _grid.Columns.Add("when", "Waktu");
        _grid.Columns.Add("count", "Transaksi");
        _grid.Columns.Add("net", "Penjualan Netto");
        _grid.Columns.Add("diff", "Selisih Kas");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["no"].FillWeight = 20;
        _grid.Columns["type"].FillWeight = 8;
        _grid.Columns["when"].FillWeight = 20;
        _grid.Columns["count"].FillWeight = 12;
        _grid.Columns["net"].FillWeight = 20;
        _grid.Columns["diff"].FillWeight = 20;
        Theme.MoneyColumn(_grid, "net");
        Theme.MoneyColumn(_grid, "diff");
        _grid.CellDoubleClick += (s, e) => ShowDetail();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public void RefreshData() => LoadData();

    private void LoadData()
    {
        var list = UiHelpers.Run(() => Program.Services.XZReports.History()) ?? new();
        _grid.Rows.Clear();
        foreach (var r in list)
        {
            var idx = _grid.Rows.Add(r.ReportNo, r.Type, r.GeneratedAt.ToString("dd/MM/yyyy HH:mm"),
                r.SalesCount, r.NetSales, r.Difference, r.Id);
            if (r.Type == "Z" && r.Difference != 0)
                _grid.Rows[idx].Cells["diff"].Style.ForeColor = Theme.Danger;
        }
    }

    private void ShowDetail()
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        var r = UiHelpers.Run(() => Program.Services.XZReports.Get(id));
        if (r == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{r.ReportNo} ({r.Type}) - {r.GeneratedAt:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Transaksi : {r.SalesCount}");
        sb.AppendLine($"Bruto     : {Money.Format(r.GrossSales)}");
        sb.AppendLine($"Diskon    : {Money.Format(r.Discounts)}");
        if (r.Tax > 0) sb.AppendLine($"PPN       : {Money.Format(r.Tax)}");
        sb.AppendLine($"Netto     : {Money.Format(r.NetSales)}");
        sb.AppendLine("Pembayaran:");
        foreach (var p in r.Payments) sb.AppendLine($"  {p.Key,-10}: {Money.Format(p.Value)}");
        sb.AppendLine($"Piutang   : {Money.Format(r.DebtSettlements)}");
        sb.AppendLine($"Retur     : {Money.Format(r.Refunds)}");
        sb.AppendLine($"System    : {Money.Format(r.ExpectedCash)}");
        if (r.ActualCash > 0) sb.AppendLine($"Fisik     : {Money.Format(r.ActualCash)}  (selisih {Money.Format(r.Difference)})");
        UiHelpers.Info(sb.ToString(), "Detail X/Z Report");
    }
}

/// <summary>Multi-warehouse stock + transfers (gudang).</summary>
public class WarehousePage : Panel, IPage
{
    private readonly ComboBox _warehouse = Theme.Combo(160);
    private readonly TextBox _search = Theme.TextBox(200);
    private readonly DataGridView _grid = new();
    private readonly PagingBar _paging = new();
    private int _page = 1;

    public WarehousePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        var header = Theme.PageHeader("Gudang & Transfer Stok", "Stok per gudang + transfer antar gudang");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var wl = Theme.Label("Gudang:", 9, true); wl.Location = new Point(16, 13);
        _warehouse.Location = new Point(80, 10);
        _warehouse.SelectedIndexChanged += (s, e) => { _page = 1; LoadData(); };
        _search.Location = new Point(250, 10);
        _search.PlaceholderText = "Cari produk...";
        _search.TextChanged += (s, e) => { _page = 1; LoadData(); };
        var btnAddWh = Theme.SecondaryButton("+ Gudang", 90);
        btnAddWh.Location = new Point(458, 8);
        btnAddWh.Click += (s, e) => AddWarehouse();
        var btnTransfer = Theme.PrimaryButton("Transfer Stok", 120);
        btnTransfer.Location = new Point(554, 8);
        btnTransfer.Click += (s, e) => DoTransfer();
        toolbar.Controls.AddRange(new Control[] { wl, _warehouse, _search, btnAddWh, btnTransfer });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("name", "Product");
        _grid.Columns.Add("qty", "Qty di Gudang");
        _grid.Columns["code"].FillWeight = 20;
        _grid.Columns["name"].FillWeight = 55;
        _grid.Columns["qty"].FillWeight = 25;
        _paging.PageChanged += () => LoadData();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(_paging);
    }

    public void RefreshData() { LoadWarehouses(); LoadData(); }

    private void LoadWarehouses()
    {
        var list = UiHelpers.Run(() => Program.Services.Warehouses.GetAll()) ?? new();
        _warehouse.DataSource = list;
        _warehouse.DisplayMember = "Name";
    }

    private void LoadData()
    {
        if (_warehouse.SelectedItem is not Warehouse w) return;
        var result = UiHelpers.Run(() => Program.Services.Warehouses.Stock(
            w.Id, _search.Text, _page, 30));
        if (result == null) return;
        _grid.Rows.Clear();
        foreach (var (code, name, qty) in result.Items)
            _grid.Rows.Add(code, name, qty.ToString("0.##"));
        _paging.UpdateInfo(result.Page, result.TotalPages, result.TotalItems);
    }

    private void AddWarehouse()
    {
        var name = InputDialog.Show("Nama gudang baru:", "Tambah Gudang");
        if (string.IsNullOrWhiteSpace(name)) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Warehouses.SaveWarehouse(name, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        LoadWarehouses();
    }

    private void DoTransfer()
    {
        var warehouses = UiHelpers.Run(() => Program.Services.Warehouses.GetAll()) ?? new();
        if (warehouses.Count < 2) { UiHelpers.Warn("Butuh minimal 2 gudang untuk transfer."); return; }

        using var dlg = new Form
        {
            Text = "Transfer Stok Antar Gudang",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(560, 420),
            BackColor = Theme.Bg, Font = Theme.FontBase
        };
        var lFrom = Theme.Label("Dari:", 9, true); lFrom.Location = new Point(18, 14);
        var from = Theme.Combo(200); from.Location = new Point(70, 10);
        from.DataSource = warehouses.ToList();
        from.DisplayMember = "Name";
        var lTo = Theme.Label("Ke:", 9, true); lTo.Location = new Point(300, 14);
        var to = Theme.Combo(200); to.Location = new Point(335, 10);
        to.DataSource = warehouses.ToList();
        to.DisplayMember = "Name";

        var grid = new DataGridView();
        Theme.StyleGrid(grid);
        grid.Location = new Point(18, 46);
        grid.Size = new Size(524, 260);
        grid.Columns.Add("add", "+");
        grid.Columns.Add("name", "Produk");
        grid.Columns.Add("qty", "Qty");
        grid.Columns.Add("_pid", "");
        grid.Columns["_pid"].Visible = false;
        grid.Columns["add"].FillWeight = 8;
        grid.Columns["name"].FillWeight = 60;
        grid.Columns["qty"].FillWeight = 32;
        grid.ReadOnly = false;
        grid.AllowUserToAddRows = true;

        var products = UiHelpers.Run(() => Program.Services.Products.GetQuickList()) ?? new();
        var combo = new DataGridViewComboBoxColumn();
        combo.Name = "pick";
        combo.HeaderText = "Pilih Produk";
        combo.FillWeight = 50;
        grid.Columns.Add(combo);
        var pickCol = grid.Columns["pick"];
        foreach (var p in products)
            ((DataGridViewComboBoxColumn)pickCol!).Items.Add(p.Name);
        grid.Columns["name"].Visible = false;

        var save = Theme.SuccessButton("TRANSFER", 140, 38);
        save.Location = new Point(400, 318);
        save.Click += (s, e) =>
        {
            if (from.SelectedItem is not Warehouse fw || to.SelectedItem is not Warehouse tw) return;
            if (fw.Id == tw.Id) { UiHelpers.Warn("Gudang asal = tujuan."); return; }
            var items = new List<(long, decimal)>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var pickName = row.Cells["pick"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(pickName)) continue;
                decimal.TryParse(row.Cells["qty"].Value?.ToString(), out var qty);
                if (qty <= 0) continue;
                var prod = products.FirstOrDefault(p => p.Name == pickName);
                if (prod == null) continue;
                items.Add((prod.Id, qty));
            }
            if (items.Count == 0) { UiHelpers.Warn("Isi minimal 1 produk + qty."); return; }
            var result = UiHelpers.Run<object?>(() =>
            {
                Program.Services.Warehouses.Transfer(fw.Id, tw.Id, items, "",
                    Program.Session!.UserId, Program.Session.Username);
                return null;
            });
            Program.Session!.DataChangedSinceBackup = true;
            UiHelpers.Info("Transfer tersimpan. Stok gudang diperbarui.");
            dlg.Close();
            LoadData();
        };
        var cancel = Theme.SecondaryButton("Batal", 90, 38);
        cancel.Location = new Point(300, 318);
        cancel.Click += (s, e) => dlg.Close();

        dlg.Controls.AddRange(new Control[] { lFrom, from, lTo, to, grid, save, cancel });
        dlg.ShowDialog(FindForm());
        LoadData();
    }
}
