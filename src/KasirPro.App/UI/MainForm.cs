using System.Text;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;

namespace KasirPro.App.UI;

/// <summary>Page contract for the content host (F5 refresh support).</summary>
public interface IPage
{
    void RefreshData();
}

/// <summary>
/// Main shell: title bar, single "Data" top-level menu, content area, status strip.
/// No sidebar, no permanent navigation - clean Windows desktop business app.
/// </summary>
public class MainForm : Form
{
    private readonly MenuStrip _menu;
    private readonly Panel _content;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _lblUser;
    private readonly ToolStripStatusLabel _lblShift;
    private readonly ToolStripStatusLabel _lblTime;
    private readonly ToolStripStatusLabel _lblOffline;
    private readonly System.Windows.Forms.Timer _clock;
    private readonly Dictionary<string, Control> _pages = new();
    private PosPage? _posPage;

    public MainForm()
    {
        Text = "KasirPro - POS Retail";
        MinimumSize = new Size(1100, 680);
        Size = new Size(1366, 768);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        Font = Theme.FontBase;
        KeyPreview = true;

        _menu = BuildMenu();
        _menu.Dock = DockStyle.Top;
        _menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
        _menu.Padding = new Padding(8, 4, 0, 2);

        _status = new StatusStrip { BackColor = Theme.Card, SizingGrip = false };
        _lblUser = new ToolStripStatusLabel($"{Strings.T("status_user")}: {Program.Session!.FullName}") { Font = Theme.FontBase };
        _lblShift = new ToolStripStatusLabel($"{Strings.T("status_shift")}: -") { Font = Theme.FontBase };
        _lblTime = new ToolStripStatusLabel { Font = Theme.FontBase, Spring = true, TextAlign = ContentAlignment.MiddleRight };
        _lblOffline = new ToolStripStatusLabel(Strings.T("status_offline")) { Font = Theme.FontMediumBold, ForeColor = Theme.Muted };
        _status.Items.AddRange(new ToolStripItem[] { _lblUser, new ToolStripStatusLabel("   |   "), _lblShift, _lblTime, _lblOffline });

        _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0) };

        Controls.Add(_content);
        Controls.Add(_status);
        Controls.Add(_menu);
        MainMenuStrip = _menu;

        _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        _clock.Tick += (s, e) => { UpdateStatusBar(); CheckAutoLogout(); };
        _clock.Start();

        UpdateStatusBar();
        OpenPage<DashboardPage>("dashboard");
    }

    private DateTime _lastActivity = DateTime.Now;

    private void CheckAutoLogout()
    {
        try
        {
            var minutes = int.TryParse(Program.Services.Settings.Get("auto_logout_minutes", "0"), out var m) ? m : 0;
            if (minutes <= 0 || Program.Session == null) return;
            if ((DateTime.Now - _lastActivity).TotalMinutes >= minutes)
            {
                _lastActivity = DateTime.Now; // avoid repeat dialogs
                if (MessageBox.Show("Tidak ada aktivitas. Tetap login sebagai " + Program.Session.FullName + "?",
                        "Auto-logout", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                {
                    Close(); // triggers restart flow -> login screen
                }
            }
        }
        catch { }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _lastActivity = DateTime.Now;
        base.OnMouseMove(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        _lastActivity = DateTime.Now;
        base.OnKeyPress(e);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Border;
        public override Color MenuItemSelected => Theme.AccentLight;
        public override Color MenuItemSelectedGradientBegin => Theme.AccentLight;
        public override Color MenuItemSelectedGradientEnd => Theme.AccentLight;
        public override Color MenuItemPressedGradientBegin => Theme.AccentLight;
        public override Color MenuItemPressedGradientEnd => Theme.AccentLight;
        public override Color ToolStripBorder => Theme.Border;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }

    // ------------------------------------------------------------------
    // MENU - single top-level "Data" item, natural MenuStrip dropdown behavior
    // ------------------------------------------------------------------
    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var data = new ToolStripMenuItem(Strings.T("menu_data")) { Font = Theme.FontMediumBold };

        var transaction = new ToolStripMenuItem(Strings.T("menu_transaction"));
        transaction.DropDownItems.Add(Item(Strings.T("menu_new_sale") + "   (F2)", () => OpenPos()));
        transaction.DropDownItems.Add(Item(Strings.T("menu_sales_history"), () => OpenPage<SalesHistoryPage>("sales_history")));
        transaction.DropDownItems.Add(Item(Strings.T("menu_sales_return"), () => new SalesReturnDialog(this).ShowDialog()));
        transaction.DropDownItems.Add(Item(Strings.T("menu_debts"), () => OpenPage<DebtsPage>("debts")));
        transaction.DropDownItems.Add(new ToolStripSeparator());
        transaction.DropDownItems.Add(Item(Strings.T("menu_purchase"), () => OpenPage<PurchaseEntryPage>("purchase_entry")));
        transaction.DropDownItems.Add(Item(Strings.T("menu_purchase_history"), () => OpenPage<PurchaseHistoryPage>("purchase_history")));
        transaction.DropDownItems.Add(Item(Strings.T("menu_purchase_return"), () => PurchaseReturnEntry.Show()));
        transaction.DropDownItems.Add(new ToolStripSeparator());
        transaction.DropDownItems.Add(Item(Strings.T("menu_cash_in"), () => new CashMovementDialog(isCashIn: true).ShowDialog()));
        transaction.DropDownItems.Add(Item(Strings.T("menu_cash_out"), () => new CashMovementDialog(isCashIn: false).ShowDialog()));
        transaction.DropDownItems.Add(new ToolStripSeparator());
        transaction.DropDownItems.Add(Item(Strings.T("menu_open_shift"), () => RunDialog(new OpenShiftDialog())));
        transaction.DropDownItems.Add(Item(Strings.T("menu_close_shift"), () => RunDialog(new CloseShiftDialog())));
        transaction.DropDownItems.Add(Item(Strings.T("menu_shift_history"), () => OpenPage<ShiftHistoryPage>("shift_history")));

        var product = new ToolStripMenuItem(Strings.T("menu_product"));
        product.DropDownItems.Add(Item(Strings.T("menu_product_list"), () => OpenPage<ProductListPage>("product_list")));
        product.DropDownItems.Add(Item(Strings.T("menu_add_product"), () => new ProductEditDialog(null).ShowDialog()));
        product.DropDownItems.Add(Item(Strings.T("menu_categories"), () => OpenPage<CategoriesPage>("categories")));
        product.DropDownItems.Add(Item(Strings.T("menu_units"), () => OpenPage<UnitsPage>("units")));
        product.DropDownItems.Add(Item(Strings.T("menu_barcode"), () => new BarcodeDialog().ShowDialog()));
        product.DropDownItems.Add(Item(Strings.T("menu_price_update"), () => new PriceUpdateDialog().ShowDialog()));

        var inventory = new ToolStripMenuItem(Strings.T("menu_inventory"));
        inventory.DropDownItems.Add(Item(Strings.T("menu_current_stock"), () => OpenPage<CurrentStockPage>("current_stock")));
        inventory.DropDownItems.Add(Item(Strings.T("menu_stock_movement"), () => OpenPage<StockMovementPage>("stock_movement")));
        inventory.DropDownItems.Add(Item(Strings.T("menu_stock_in"), () => new StockAdjustDialog(StockAdjustDialog.Mode.In).ShowDialog()));
        inventory.DropDownItems.Add(Item(Strings.T("menu_stock_out"), () => new StockAdjustDialog(StockAdjustDialog.Mode.Out).ShowDialog()));
        inventory.DropDownItems.Add(Item(Strings.T("menu_stock_adjustment"), () => new StockAdjustDialog(StockAdjustDialog.Mode.Adjust).ShowDialog()));
        inventory.DropDownItems.Add(Item(Strings.T("menu_stock_opname"), () => OpenPage<StockOpnamePage>("stock_opname")));
        inventory.DropDownItems.Add(Item(Strings.T("menu_low_stock"), () => OpenPage<LowStockPage>("low_stock")));
        inventory.DropDownItems.Add(new ToolStripSeparator());
        inventory.DropDownItems.Add(Item("Gudang & Transfer Stok", () => OpenPage<WarehousePage>("warehouses")));

        var reports = new ToolStripMenuItem(Strings.T("menu_reports"));
        reports.DropDownItems.Add(Item("Sales Report", () => new ReportViewerForm(ReportKind.Sales).ShowDialog()));
        reports.DropDownItems.Add(Item("Purchase Report", () => new ReportViewerForm(ReportKind.Purchase).ShowDialog()));
        reports.DropDownItems.Add(Item("Profit Report", () => new ReportViewerForm(ReportKind.Profit).ShowDialog()));
        reports.DropDownItems.Add(Item("Product Sales Report", () => new ReportViewerForm(ReportKind.ProductSales).ShowDialog()));
        reports.DropDownItems.Add(Item("Sales by Hour", () => new ReportViewerForm(ReportKind.Hourly).ShowDialog()));
        reports.DropDownItems.Add(Item("Sales by Payment Method", () => new ReportViewerForm(ReportKind.PaymentMethod).ShowDialog()));
        reports.DropDownItems.Add(Item("Sales by Category", () => new ReportViewerForm(ReportKind.Category).ShowDialog()));
        reports.DropDownItems.Add(Item("Stock Report", () => new ReportViewerForm(ReportKind.Stock).ShowDialog()));
        reports.DropDownItems.Add(Item("Stock Movement Report", () => new ReportViewerForm(ReportKind.StockMovement).ShowDialog()));
        reports.DropDownItems.Add(Item("Low Stock Report", () => new ReportViewerForm(ReportKind.LowStock).ShowDialog()));
        reports.DropDownItems.Add(Item("Cash Report", () => new ReportViewerForm(ReportKind.Cash).ShowDialog()));
        reports.DropDownItems.Add(Item("Cashier Report", () => new ReportViewerForm(ReportKind.Cashier).ShowDialog()));
        reports.DropDownItems.Add(new ToolStripSeparator());
        reports.DropDownItems.Add(Item("X-Report (Shift Berjalan)", () => RunXZReport("X")));
        reports.DropDownItems.Add(Item("Z-Report History", () => OpenPage<XZHistoryPage>("xz_history")));

        var tools = new ToolStripMenuItem(Strings.T("menu_tools"));
        if (Program.Session!.Has("SETTINGS.MANAGE"))
            tools.DropDownItems.Add(Item(Strings.T("menu_store_settings"), () => RunDialog(new StoreSettingsDialog())));
        if (Program.Session.Has("USER.MANAGE"))
            tools.DropDownItems.Add(Item(Strings.T("menu_user_management"), () => new UserManagementDialog().ShowDialog()));
        tools.DropDownItems.Add(Item(Strings.T("menu_printer_setup"), () => new PrinterSetupDialog().ShowDialog()));
        tools.DropDownItems.Add(Item("Hardware Test Center", () => new HardwareTestCenterDialog().ShowDialog()));
        tools.DropDownItems.Add(Item("Barcode & Labels", () => new BarcodeManagerDialog().ShowDialog()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        if (Program.Session.Has("BACKUP.CREATE"))
            tools.DropDownItems.Add(Item(Strings.T("menu_backup"), () => RunBackup()));
        if (Program.Session.Has("BACKUP.RESTORE"))
            tools.DropDownItems.Add(Item(Strings.T("menu_restore"), () => new RestoreDialog(this).ShowDialog()));
        tools.DropDownItems.Add(Item(Strings.T("menu_maintenance"), () => new MaintenanceDialog(this).ShowDialog()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        if (Program.Session.Has("SETTINGS.MANAGE"))
            tools.DropDownItems.Add(Item(Strings.T("menu_check_update"), () => RunUpdateCheck()));
        if (Program.Session.Has("LICENSE.VIEW"))
            tools.DropDownItems.Add(Item(Strings.T("menu_license_info"), () => new LicenseInfoDialog().ShowDialog()));
        tools.DropDownItems.Add(Item(Strings.T("menu_about"), () => new AboutDialog().ShowDialog()));

        // promotions (Data > Promotion)
        var promotion = new ToolStripMenuItem("Promotion");
        promotion.DropDownItems.Add(Item("Promotion List", () => new PromotionListDialog().ShowDialog()));

        data.DropDownItems.AddRange(new ToolStripItem[] { transaction, product, promotion, inventory, reports, tools });
        menu.Items.Add(data);
        return menu;
    }

    private void RunUpdateCheck()
    {
        UiHelpers.Run(() =>
        {
            var info = Program.Services.Updates.Check();
            if (!info.NewerThanInstalled)
            {
                UiHelpers.Info($"Versi terinstall ({info.Version}) sudah yang terbaru.\n\nSumber: {info.SourcePath}");
                return;
            }
            var ok = UiHelpers.Confirm(
                $"Update baru ditemukan!\n\n" +
                $"Versi tersedia: {info.Version}\n" +
                $"Versi terinstall: {Program.Services.Updates.InstalledVersion}\n" +
                $"Sumber: {info.SourcePath}\n\n" +
                (info.Notes.Length > 0 ? "Catatan: " + info.Notes + "\n\n" : "") +
                $"{info.Files.Count} file akan diverifikasi & disiapkan. " +
                "Update diterapkan saat aplikasi dijalankan berikutnya.\n\nLanjutkan?");
            if (!ok) return;
            var pending = Program.Services.Updates.Apply(info);
            UiHelpers.Info("Update terverifikasi & disiapkan.\n\n" +
                "Tutup aplikasi lalu jalankan lagi untuk menerapkan update.\n" + pending);
        });
    }

    private void RunXZReport(string type)
    {
        UiHelpers.Run(() =>
        {
            var session = Program.Services.Cash.GetOpenSession(Program.Session!.UserId);
            var report = Program.Services.XZReports.Generate(session?.Id ?? 0, type,
                Program.Session.UserId, Program.Session.Username);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{(type == "Z" ? "Z-REPORT (TUTUP)" : "X-REPORT (BERJALAN)")} - {report.ReportNo}");
            sb.AppendLine($"{report.GeneratedAt:dd/MM/yyyy HH:mm}  Kasir: {Program.Session.FullName}");
            sb.AppendLine(new string('-', 40));
            sb.AppendLine($"Jumlah transaksi : {report.SalesCount}");
            sb.AppendLine($"Penjualan bruto  : {Money.Format(report.GrossSales)}");
            sb.AppendLine($"Diskon           : {Money.Format(report.Discounts)}");
            if (report.Tax > 0) sb.AppendLine($"PPN              : {Money.Format(report.Tax)}");
            sb.AppendLine($"Penjualan netto  : {Money.Format(report.NetSales)}");
            sb.AppendLine(new string('-', 40));
            foreach (var p in report.Payments)
                sb.AppendLine($"  {p.Key,-10}: {Money.Format(p.Value)}");
            sb.AppendLine(new string('-', 40));
            if (report.DebtSettlements > 0) sb.AppendLine($"Pelunasan piutang: {Money.Format(report.DebtSettlements)}");
            if (report.Refunds > 0) sb.AppendLine($"Retur            : {Money.Format(report.Refunds)}");
            if (session != null)
            {
                sb.AppendLine($"Modal awal       : {Money.Format(report.OpeningCash)}");
                sb.AppendLine($"System cash      : {Money.Format(report.ExpectedCash)}");
            }
            UiHelpers.Info(sb.ToString(), "X/Z Report");
        });
    }

    private static ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Font = Theme.FontBase };
        item.Click += (s, e) => onClick();
        return item;
    }

    private void RunDialog(Form dialog)
    {
        try { dialog.ShowDialog(this); }
        catch (Exception ex) { UiHelpers.ShowChildError(ex); }
        UpdateStatusBar();
    }

    private void RunBackup()
    {
        UiHelpers.Run(() =>
        {
            var path = Program.Services.Backup.CreateBackup("manual", Program.Session.UserId, Program.Session.Username);
            Program.Session.DataChangedSinceBackup = false;
            UiHelpers.Info("Backup berhasil:\n" + path);
        });
    }

    // ------------------------------------------------------------------
    // PAGE HOST
    // ------------------------------------------------------------------
    public void OpenPage<T>(string key) where T : Control, IPage, new()
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = new T();
            _pages[key] = page;
        }
        ShowPage(page);
    }

    public void OpenPos()
    {
        if (_posPage == null || _posPage.IsDisposed)
        {
            _posPage = new PosPage();
            _pages["pos"] = _posPage;
        }
        ShowPage(_posPage);
        _posPage.FocusBarcode();
    }

    public void ShowPage(Control page)
    {
        foreach (Control c in _content.Controls) c.Hide();
        if (!_content.Contains(page))
        {
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
        }
        page.Show();
        page.BringToFront();
        (page as IPage)?.RefreshData();
        UpdateStatusBar();
    }

    public PosPage? ActivePos => _posPage != null && _posPage.Visible && !_posPage.IsDisposed ? _posPage : null;

    // ------------------------------------------------------------------
    // KEYBOARD: F2 New Sale, F5 Refresh, F9 Payment, F11 Fullscreen
    // ------------------------------------------------------------------
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F2:
                OpenPos();
                return true;
            case Keys.F5:
                foreach (Control c in _content.Controls)
                    if (c.Visible) (c as IPage)?.RefreshData();
                UpdateStatusBar();
                return true;
            case Keys.F9:
                ActivePos?.BeginPayment();
                return true;
            case Keys.F11:
                ToggleFullscreen();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleFullscreen()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
    }

    // ------------------------------------------------------------------
    // STATUS BAR
    // ------------------------------------------------------------------
    public void UpdateStatusBar()
    {
        _lblUser.Text = $"{Strings.T("status_user")}: {(Program.Session?.FullName ?? "-")}";
        var shift = UiHelpers.Run(() =>
            Program.Session != null ? Program.Services.Cash.GetOpenSession(Program.Session.UserId) : null);
        _lblShift.Text = shift == null
            ? $"{Strings.T("status_shift")}: -"
            : $"{Strings.T("status_shift")}: {shift.OpenedAt:HH:mm} - now";
        _lblTime.Text = $"{DateTime.Now:dd/MM/yyyy HH:mm}";
    }

    /// <summary>Auto backup on close-shift and on exit when data changed.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason != CloseReason.UserClosing) return;
        try
        {
            var settings = Program.Services.Settings;
            var session = Program.Session;
            if (settings.AutoBackup && session != null && session.DataChangedSinceBackup)
            {
                Program.Services.Backup.CreateBackup("auto-exit", session.UserId, session.Username);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("auto backup on exit failed", ex);
        }
    }
}


