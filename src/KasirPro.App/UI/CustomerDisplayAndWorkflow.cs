using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

/// <summary>
/// Customer Display: fullscreen summary window intended for a second monitor
/// (or any secondary window). Updated live from the POS cart.
/// </summary>
public class CustomerDisplayForm : Form
{
    private readonly Label _welcome = new();
    private readonly Label _lastItem = new();
    private readonly Label _lblQty = new();
    private readonly Label _lblSubtotal = new();
    private readonly Label _lblTotal = new();

    public CustomerDisplayForm()
    {
        Text = "Customer Display";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(15, 23, 42);
        DoubleBuffered = true;

        _welcome.Text = "SELAMAT DATANG";
        _welcome.Font = new Font("Segoe UI", 28f, FontStyle.Bold);
        _welcome.ForeColor = Color.FromArgb(147, 197, 253);
        _welcome.Dock = DockStyle.Top;
        _welcome.Height = 90;
        _welcome.TextAlign = ContentAlignment.MiddleCenter;

        _lastItem.Font = new Font("Segoe UI", 14f);
        _lastItem.ForeColor = Color.White;
        _lastItem.Dock = DockStyle.Top;
        _lastItem.Height = 80;
        _lastItem.TextAlign = ContentAlignment.MiddleCenter;

        _lblQty.Font = new Font("Segoe UI", 14f);
        _lblQty.ForeColor = Color.FromArgb(148, 163, 184);
        _lblQty.Dock = DockStyle.Top;
        _lblQty.Height = 50;
        _lblQty.TextAlign = ContentAlignment.MiddleCenter;

        _lblSubtotal.Font = new Font("Segoe UI", 16f);
        _lblSubtotal.ForeColor = Color.White;
        _lblSubtotal.Dock = DockStyle.Top;
        _lblSubtotal.Height = 60;
        _lblSubtotal.TextAlign = ContentAlignment.MiddleCenter;

        _lblTotal.Text = "TOTAL\nRp 0";
        _lblTotal.Font = new Font("Segoe UI", 30f, FontStyle.Bold);
        _lblTotal.ForeColor = Color.FromArgb(74, 222, 128);
        _lblTotal.Dock = DockStyle.Fill;
        _lblTotal.TextAlign = ContentAlignment.MiddleCenter;

        Controls.Add(_lblTotal);
        Controls.Add(_lblSubtotal);
        Controls.Add(_lblQty);
        Controls.Add(_lastItem);
        Controls.Add(_welcome);

        // place on secondary screen when available
        try
        {
            var screens = Screen.AllScreens;
            var target = screens.Length > 1 ? screens[1] : screens[0];
            StartPosition = FormStartPosition.Manual;
            Location = target.Bounds.Location;
            WindowState = FormWindowState.Maximized;
        }
        catch { }
    }

    public void UpdateCart(IReadOnlyList<CartLine> lines, decimal grandTotal)
    {
        if (IsDisposed) return;
        var last = lines.LastOrDefault(l => l.Qty > 0);
        _lastItem.Text = last == null ? "" : $"{last.Name}\n{last.Qty:0.##} x {Money.FormatPlain(last.Price)}";
        var totalQty = lines.Sum(l => l.Qty);
        var subtotal = lines.Sum(l => l.Subtotal);
        _lblQty.Text = totalQty > 0 ? $"{totalQty:0.##} item" : "";
        _lblSubtotal.Text = subtotal > 0 ? "Subtotal  " + Money.Format(subtotal) : "";
        _lblTotal.Text = "TOTAL\n" + Money.Format(grandTotal);
    }

    public void ShowWelcome() => _lastItem.Text = "Silakan pilih produk Anda";
}

/// <summary>Purchase Order workflow: drafts -> approve -> receive (partial).</summary>
public class PurchaseWorkflowDialog : Form
{
    private readonly DataGridView _grid = new();

    public PurchaseWorkflowDialog()
    {
        Text = "Purchase Orders";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 540);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader("Purchase Orders", "Draft -> Ordered -> Received (parsial) -> Closed");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var approve = Theme.SuccessButton("APPROVE", 110, 30);
        approve.Location = new Point(16, 8);
        approve.Click += (s, e) => DoAction("approve");
        var receive = Theme.PrimaryButton("RECEIVE", 110, 30);
        receive.Location = new Point(132, 8);
        receive.Click += (s, e) => DoAction("receive");
        var cancel = Theme.DangerButton("BATALKAN", 110, 30);
        cancel.Location = new Point(248, 8);
        cancel.Click += (s, e) => DoAction("cancel");
        toolbar.Controls.AddRange(new Control[] { approve, receive, cancel });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("po", "PO No");
        _grid.Columns.Add("supplier", "Supplier");
        _grid.Columns.Add("status", "Status");
        _grid.Columns.Add("workflow", "Workflow");
        _grid.Columns.Add("total", "Total");
        _grid.Columns.Add("_id", "");
        _grid.Columns["_id"].Visible = false;
        _grid.Columns["po"].FillWeight = 20;
        _grid.Columns["supplier"].FillWeight = 26;
        _grid.Columns["status"].FillWeight = 14;
        _grid.Columns["workflow"].FillWeight = 16;
        _grid.Columns["total"].FillWeight = 24;
        Theme.MoneyColumn(_grid, "total");

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += (s, e) => LoadData();
    }

    private void LoadData()
    {
        var drafts = UiHelpers.Run(() => Program.Services.Purchases.ListDrafts()) ?? new();
        _grid.Rows.Clear();
        foreach (var (id, no, supplier, status, wf, total) in drafts)
            _grid.Rows.Add(no, supplier, status, wf, total, id);
    }

    private void DoAction(string action)
    {
        if (_grid.CurrentRow == null) return;
        var id = Convert.ToInt64(_grid.CurrentRow.Cells["_id"].Value);
        try
        {
            if (action == "approve")
            {
                if (!Program.Session!.Has("PURCHASE.APPROVE")) { UiHelpers.Warn("Butuh izin PURCHASE.APPROVE."); return; }
                Program.Services.Purchases.Approve(id, Program.Session.UserId, Program.Session.Username);
                Program.Session.DataChangedSinceBackup = true;
                UiHelpers.Info("PO disetujui (ORDERED).");
            }
            else if (action == "receive")
            {
                var dialog = new ReceiveDialog(id);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Program.Session!.DataChangedSinceBackup = true;
                    UiHelpers.Info("Penerimaan tercatat, stok bertambah.");
                }
            }
            else
            {
                var reason = InputDialog.Show("Alasan pembatalan:", "Cancel PO");
                if (string.IsNullOrWhiteSpace(reason)) return;
                Program.Services.Purchases.CancelDraft(id, reason, Program.Session!.UserId, Program.Session.Username);
                Program.Session.DataChangedSinceBackup = true;
                UiHelpers.Info("PO dibatalkan.");
            }
            LoadData();
        }
        catch (InvalidOperationException ex) { UiHelpers.Warn(ex.Message); }
        catch (Exception ex) { UiHelpers.ShowChildError(ex); }
    }

    private class ReceiveDialog : Form
    {
        private readonly long _poId;
        private readonly DataGridView _grid = new();

        public ReceiveDialog(long poId)
        {
            _poId = poId;
            Text = "Receive Goods (parsial diperbolehkan)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 440);
            BackColor = Theme.Bg;
            Font = Theme.FontBase;

            var po = Program.Services.Purchases.GetById(poId);
            var title = Theme.Label($"{po?.PurchaseNo} - {po?.SupplierName}", 11, true, Theme.Accent);
            title.Location = new Point(16, 12);

            Theme.StyleGrid(_grid);
            _grid.Location = new Point(16, 46);
            _grid.Size = new Size(528, 300);
            _grid.Columns.Add("name", "Product");
            _grid.Columns.Add("ordered", "Dipesan");
            _grid.Columns.Add("received", "Diterima");
            _grid.Columns.Add("_pid", "");
            _grid.Columns["_pid"].Visible = false;
            _grid.Columns["name"].FillWeight = 46;
            _grid.Columns["ordered"].FillWeight = 26;
            _grid.Columns["received"].FillWeight = 28;
            _grid.ReadOnly = false;
            foreach (var i in po?.Items ?? new List<PurchaseItem>())
                _grid.Rows.Add(i.ProductName, i.Qty.ToString("0.##"), "0", i.ProductId);

            var notesLabel = Theme.Label("Catatan:", 9, true);
            notesLabel.Location = new Point(16, 356);
            var notes = Theme.TextBox(360);
            notes.Location = new Point(90, 352);

            var ok = Theme.SuccessButton("TERIMA", 130, 38);
            ok.Location = new Point(414, 390);
            ok.Click += (s, e) =>
            {
                var lines = new List<(long, decimal, decimal)>();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    decimal.TryParse(row.Cells["received"].Value?.ToString(), out var received);
                    if (received <= 0) continue;
                    decimal.TryParse(row.Cells["ordered"].Value?.ToString(), out var ordered);
                    lines.Add((Convert.ToInt64(row.Cells["_pid"].Value), ordered, received));
                }
                if (lines.Count == 0) { UiHelpers.Warn("Isi qty diterima."); return; }
                Program.Services.Purchases.Receive(_poId, lines, notes.Text.Trim(),
                    Program.Session!.UserId, Program.Session.Username);
                Program.Session.DataChangedSinceBackup = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = Theme.SecondaryButton("Batal", 90, 38);
            cancel.Location = new Point(314, 390);
            cancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { title, _grid, notesLabel, notes, ok, cancel });
        }
    }
}
