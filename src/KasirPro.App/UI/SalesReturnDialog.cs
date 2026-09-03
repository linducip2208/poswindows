using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

/// <summary>Sales return: pick an invoice, choose items + qty, process (restores stock).</summary>
public class SalesReturnDialog : Form
{
    private readonly TextBox _invoiceSearch = Theme.TextBox(220);
    private readonly DataGridView _invoices = new();
    private readonly DataGridView _items = new();
    private readonly TextBox _reason = Theme.TextBox(400);
    private Sale? _selectedSale;

    public SalesReturnDialog(Form? owner)
    {
        Text = "Sales Return";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 600);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader("Sales Return", "Pilih invoice, centang item yang diretur");

        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg };
        _invoiceSearch.Location = new Point(16, 10);
        _invoiceSearch.PlaceholderText = "Cari invoice...";
        var btnFind = Theme.PrimaryButton("Cari", 90);
        btnFind.Location = new Point(242, 8);
        btnFind.Click += (s, e) => LoadInvoices();
        top.Controls.AddRange(new Control[] { _invoiceSearch, btnFind });

        Theme.StyleGrid(_invoices);
        _invoices.Dock = DockStyle.Fill;
        _invoices.Columns.Add("invoice", "Invoice");
        _invoices.Columns.Add("date", "Date");
        _invoices.Columns.Add("customer", "Customer");
        _invoices.Columns.Add("total", "Total");
        _invoices.Columns.Add("_id", "");
        _invoices.Columns["_id"].Visible = false;
        _invoices.Columns["invoice"].FillWeight = 26;
        _invoices.Columns["date"].FillWeight = 22;
        _invoices.Columns["customer"].FillWeight = 30;
        _invoices.Columns["total"].FillWeight = 22;
        Theme.MoneyColumn(_invoices, "total");
        _invoices.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _invoices.CellClick += (s, e) => { if (e.RowIndex >= 0) SelectInvoice(); };

        Theme.StyleGrid(_items);
        _items.Columns.Add("ret", "Retur");
        _items.Columns.Add("name", "Product");
        _items.Columns.Add("sold", "Qty Terjual");
        _items.Columns.Add("already", "Sudah Diretur");
        _items.Columns.Add("qty", "Qty Retur");
        _items.Columns.Add("price", "Harga");
        _items.Columns.Add("subtotal", "Subtotal");
        _items.Columns.Add("_sid", "");
        _items.Columns["_sid"].Visible = false;
        _items.Columns["ret"].FillWeight = 8;
        _items.Columns["name"].FillWeight = 34;
        _items.Columns["sold"].FillWeight = 12;
        _items.Columns["already"].FillWeight = 12;
        _items.Columns["qty"].FillWeight = 10;
        _items.Columns["price"].FillWeight = 12;
        _items.Columns["subtotal"].FillWeight = 12;
        Theme.MoneyColumn(_items, "price");
        Theme.MoneyColumn(_items, "subtotal");
        _items.ReadOnly = false;
        _items.CellValueChanged += (s, e) => UpdateSubtotals();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = Theme.Bg };
        var rl = Theme.Label("Alasan retur:", 9, true);
        rl.Location = new Point(16, 10);
        _reason.Location = new Point(110, 7);
        var processBtn = Theme.SuccessButton("PROSES RETUR", 150, 40);
        processBtn.Location = new Point(700, 55);
        processBtn.Click += OnProcess;
        var closeBtn = Theme.SecondaryButton("Tutup", 90, 40);
        closeBtn.Location = new Point(860, 55);
        closeBtn.Click += (s, e) => Close();
        var summary = Theme.Label("Total retur: Rp 0", 11, true, Theme.Danger);
        summary.Location = new Point(16, 62);
        summary.Name = "summary";
        bottom.Controls.AddRange(new Control[] { rl, _reason, processBtn, closeBtn, summary });

        var main = new SplitContainer { Dock = DockStyle.Fill };
        main.Panel1.Controls.Add(_invoices);
        main.Panel2.Controls.Add(_items);

        Controls.Add(main);
        Controls.Add(top);
        Controls.Add(header);
        Controls.Add(bottom);

        LoadInvoices();
    }

    private void LoadInvoices()
    {
        var list = UiHelpers.Run(() => Program.Services.Sales.History(
            _invoiceSearch.Text, DateTime.Today.AddDays(-30), DateTime.Today, 0, 1, 100));
        if (list == null) return;
        _invoices.Rows.Clear();
        foreach (var s in list.Items.Where(x => x.Status == "COMPLETED"))
            _invoices.Rows.Add(s.InvoiceNo, s.SaleDate.ToString("dd/MM/yyyy HH:mm"), s.CustomerName, s.Total, s.Id);
    }

    private void SelectInvoice()
    {
        var id = Convert.ToInt64(_invoices.CurrentRow.Cells["_id"].Value);
        var sale = UiHelpers.Run(() => Program.Services.Sales.GetById(id));
        if (sale == null) return;
        _selectedSale = sale;
        _items.Rows.Clear();
        foreach (var i in sale.Items)
        {
            var already = UiHelpers.Run(() => AlreadyReturned(i.Id));
            _items.Rows.Add(false, i.ProductName, i.Qty.ToString("0.##"), already.ToString("0.##"),
                "0", i.Price, "0", i.Id);
        }
    }

    private decimal AlreadyReturned(long saleItemId) =>
        Program.DbMain.With(c => c.ExecuteScalar<decimal>(
            "SELECT COALESCE(SUM(qty),0) FROM sale_return_items WHERE sale_item_id=@id", new { id = saleItemId }));

    private void UpdateSubtotals()
    {
        decimal total = 0;
        foreach (DataGridViewRow row in _items.Rows)
        {
            var isRet = Convert.ToBoolean(row.Cells["ret"].Value ?? false);
            decimal qty = 0, price = 0;
            decimal.TryParse(row.Cells["qty"].Value?.ToString(), out qty);
            decimal.TryParse(row.Cells["price"].Value?.ToString(), out price);
            var sub = isRet && qty > 0 ? Money.Round(qty * price) : 0;
            row.Cells["subtotal"].Value = sub;
            total += sub;
        }
        ((Label?)_items.Parent!.Parent!.Controls.Find("summary", true).FirstOrDefault())!.Text =
            "Total retur: " + Money.Format(total);
    }

    private void OnProcess(object? sender, EventArgs e)
    {
        if (_selectedSale == null) { UiHelpers.Warn("Pilih invoice terlebih dahulu."); return; }
        var items = new List<(long, decimal)>();
        foreach (DataGridViewRow row in _items.Rows)
        {
            var isRet = Convert.ToBoolean(row.Cells["ret"].Value ?? false);
            decimal qty = 0;
            decimal.TryParse(row.Cells["qty"].Value?.ToString(), out qty);
            if (!isRet || qty <= 0) continue;
            var sold = decimal.Parse(row.Cells["sold"].Value!.ToString()!);
            var already = decimal.Parse(row.Cells["already"].Value!.ToString()!);
            if (qty > sold - already)
            {
                UiHelpers.Warn($"Qty retur untuk '{row.Cells["name"].Value}' melebihi sisa yang bisa diretur.");
                return;
            }
            items.Add((Convert.ToInt64(row.Cells["_sid"].Value), qty));
        }
        if (items.Count == 0) { UiHelpers.Warn("Pilih minimal satu item dengan qty > 0."); return; }
        var reason = string.IsNullOrWhiteSpace(_reason.Text) ? "Retur barang" : _reason.Text.Trim();

        var result = UiHelpers.Run(() =>
            Program.Services.Sales.CreateReturn(_selectedSale.Id, items, reason,
                Program.Session!.UserId, Program.Session.Username));
        if (result == null) return;
        Program.Session!.DataChangedSinceBackup = true;
        UiHelpers.Info($"Retur {result.ReturnNo} berhasil.\nTotal: {Money.Format(result.Total)}\nStok sudah dikembalikan.");
        LoadInvoices();
        _items.Rows.Clear();
        _selectedSale = null;
    }
}


