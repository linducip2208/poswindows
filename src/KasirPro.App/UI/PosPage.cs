using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

/// <summary>
/// POS screen: barcode/search + product grid on the left, cart + totals + PAY on the right.
/// Barcode scanners work as USB-HID keyboards; Enter in the barcode field adds the product.
/// </summary>
public class PosPage : Panel, IPage
{
    private readonly TextBox _barcode;
    private readonly TextBox _search;
    private readonly ComboBox _category;
    private readonly FlowLayoutPanel _productFlow;
    private readonly DataGridView _cartGrid;
    private readonly ComboBox _customer;
    private readonly TextBox _discount;
    private readonly Label _lblSubtotal;
    private readonly Label _lblDiscount;
    private readonly Label _lblTotal;
    private readonly Label _lblTax;
    private readonly Button _pay;
    private readonly Label _emptyHint;
    private long? _heldId;

    private List<Product> _products = new();
    private readonly List<CartLine> _cart = new();
    private Dictionary<string, Product> _scanCache = new(StringComparer.OrdinalIgnoreCase);

    public PosPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        Padding = new Padding(10);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Bg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        // ============ LEFT: search + product grid ============
        var left = Theme.CardFluid();
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Theme.Card };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchRow = new Panel { Dock = DockStyle.Top, BackColor = Theme.Card };
        _barcode = Theme.TextBox(260);
        _barcode.Location = new Point(2, 6);
        _barcode.PlaceholderText = "Barcode / scan (Enter) - F2 untuk kembali ke sini";
        _barcode.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _barcode.KeyDown += OnBarcodeKeyDown;
        _search = Theme.TextBox(220);
        _search.Location = new Point(272, 6);
        _search.PlaceholderText = "Cari nama / kode produk...";
        _search.TextChanged += (s, e) => LoadProducts();
        var catLabel = Theme.Label("Kategori:", 9);
        catLabel.Location = new Point(500, 11);
        _category = Theme.Combo(160);
        _category.Location = new Point(566, 8);
        _category.SelectedIndexChanged += (s, e) => LoadProducts();

        searchRow.Controls.AddRange(new Control[] { _barcode, _search, catLabel, _category });

        // ============ RIGHT: cart ============
        var right = Theme.CardFluid();
        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Theme.Card };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));

        var cartHeader = new Panel { Dock = DockStyle.Top, BackColor = Theme.Card };
        var cartTitle = Theme.Label("Shopping Cart", 11, true);
        cartTitle.Location = new Point(2, 9);
        var custLabel = Theme.Label("Pelanggan:", 9);
        custLabel.Location = new Point(160, 12);
        _customer = Theme.Combo(200);
        _customer.Location = new Point(232, 8);
        cartHeader.Controls.AddRange(new Control[] { cartTitle, custLabel, _customer });

        _cartGrid = new DataGridView();
        Theme.StyleGrid(_cartGrid);
        _cartGrid.Dock = DockStyle.Fill;
        _cartGrid.Columns.Add("name", "Item");
        _cartGrid.Columns.Add("qty", "Qty");
        _cartGrid.Columns.Add("price", "Price");
        _cartGrid.Columns.Add("subtotal", "Subtotal");
        _cartGrid.Columns.Add("_pid", "");
        _cartGrid.Columns["_pid"].Visible = false;
        _cartGrid.Columns["name"].FillWeight = 46;
        _cartGrid.Columns["qty"].FillWeight = 12;
        _cartGrid.Columns["price"].FillWeight = 20;
        _cartGrid.Columns["subtotal"].FillWeight = 22;
        Theme.MoneyColumn(_cartGrid, "price");
        Theme.MoneyColumn(_cartGrid, "subtotal");
        _cartGrid.CellDoubleClick += (s, e) => RemoveLine();
        _cartGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;

        _emptyHint = Theme.Label("Belum ada item.\nScan barcode atau pilih produk di sebelah kiri.", 10, false, Theme.Muted);
        _emptyHint.Dock = DockStyle.Fill;
        _emptyHint.TextAlign = ContentAlignment.MiddleCenter;

        var totalsPanel = new Panel { Dock = DockStyle.Top, BackColor = Theme.Card };
        _lblSubtotal = TotalLine("Subtotal", 4);
        _lblDiscount = TotalLine("Discount", 30);
        _lblTotal = TotalLine("GRAND TOTAL", 58);
        _lblTotal.Font = Theme.FontTotal;
        _lblTotal.ForeColor = Theme.Accent;
        _discount = Theme.TextBox(110);
        _discount.Location = new Point(150, 60);
        _discount.TextAlign = HorizontalAlignment.Right;
        _discount.Text = "0";
        _discount.TextChanged += (s, e) => UpdateTotals();
        var dLabel = Theme.Label("Diskon Rp", 8, false, Theme.Muted);
        dLabel.Location = new Point(78, 64);
        _lblTax = TotalLine("PPN:  0", 84);
        _lblTax.ForeColor = Theme.Muted;
        var clearBtn = Theme.SecondaryButton("Clear Cart", 100, 28);
        clearBtn.Location = new Point(150, 96);
        clearBtn.Click += (s, e) => { _cart.Clear(); RefreshCart(); };
        var holdBtn = Theme.SecondaryButton("HOLD (Parkir)", 120, 28);
        holdBtn.Location = new Point(258, 96);
        holdBtn.Click += (s, e) => HoldCart();
        var recallBtn = Theme.SecondaryButton("Panggil (Recall)", 130, 28);
        recallBtn.Location = new Point(386, 96);
        recallBtn.Click += (s, e) => RecallCart();
        totalsPanel.Controls.AddRange(new Control[] { _lblSubtotal, _lblDiscount, _lblTax, _lblTotal, _discount, dLabel, clearBtn, holdBtn, recallBtn });

        _pay = new Button
        {
            Text = "PAY (F9)",
            Dock = DockStyle.Bottom,
            Height = 64,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Font = Theme.FontPay,
            Cursor = Cursors.Hand
        };
        _pay.FlatAppearance.BorderSize = 0;
        _pay.FlatAppearance.MouseOverBackColor = Theme.AccentDark;
        _pay.Click += (s, e) => BeginPayment();

        rightPanel.Controls.Add(_cartGrid, 0, 1);
        rightPanel.Controls.Add(totalsPanel, 0, 2);
        rightPanel.Controls.Add(cartHeader, 0, 0);
        right.Controls.Add(_emptyHint);
        right.Controls.Add(_pay);
        right.Controls.Add(rightPanel);
        _emptyHint.BringToFront();

        // ============ LEFT panel content ============
        _productFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Card,
            Padding = new Padding(4)
        };
        leftPanel.Controls.Add(_productFlow, 0, 2);
        leftPanel.Controls.Add(searchRow, 0, 0);
        left.Controls.Add(leftPanel);

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        Controls.Add(layout);
    }

    private static Label TotalLine(string text, int y)
    {
        var l = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(2, y + 2)
        };
        return l;
    }

    private Label _lblTotalValue = null!;

    private Label TotalLineValue(string text, int y) => TotalLine(text, y);

    public void FocusBarcode()
    {
        _barcode.Focus();
        _barcode.SelectAll();
    }

    public void RefreshData()
    {
        LoadCategories();
        LoadCustomers();
        LoadProducts();
        RebuildScanCache();
        UpdateTotals();
    }

    /// <summary>One query loads all active barcodes+codes into memory for instant scanning.</summary>
    private void RebuildScanCache()
    {
        _scanCache = UiHelpers.Run(() => Program.DbMain.With(c =>
        {
            var dict = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var rows = Dapper.SqlMapper.Query<(long Id, string Barcode, string Code, string Name, decimal Selling, decimal Cost, decimal Stock, string Unit)>(c,
                @"SELECT p.id AS Id, b.barcode AS Barcode, p.code AS Code, p.name AS Name,
                  CAST(ROUND(p.selling_price/100.0,2) AS REAL) AS Selling,
                  CAST(ROUND(p.purchase_price/100.0,2) AS REAL) AS Cost,
                  p.stock AS Stock, COALESCE(u.name,'') AS Unit
                  FROM products p
                  JOIN product_barcodes b ON b.product_id = p.id
                  LEFT JOIN units u ON u.id = p.unit_id
                  WHERE p.is_active = 1");
            foreach (var r in rows)
            {
                var prod = new Product
                {
                    Id = r.Id, Code = r.Code, Name = r.Name, SellingPrice = r.Selling,
                    PurchasePrice = r.Cost, Stock = r.Stock, UnitName = r.Unit, Barcode = r.Barcode
                };
                dict[r.Barcode] = prod;
                dict[r.Code] = prod;
            }
            return dict;
        })) ?? new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
    }

    private void LoadCategories()
    {
        var current = _category.SelectedIndex;
        _category.Items.Clear();
        _category.Items.Add("Semua");
        foreach (var c in UiHelpers.Run(() => Program.Services.Products.GetCategories()) ?? new List<Category>())
            _category.Items.Add(c.Name);
        _category.SelectedIndex = current >= 0 && current < _category.Items.Count ? current : 0;
    }

    private void LoadCustomers()
    {
        if (_customer.Items.Count > 0) return;
        var customers = UiHelpers.Run(() => Program.Services.Products.GetCustomers()) ?? new List<Customer>();
        _customer.DataSource = customers;
        _customer.DisplayMember = "Name";
    }

    private void LoadProducts()
    {
        var categoryId = 0L;
        if (_category.SelectedIndex > 0)
        {
            var cat = UiHelpers.Run(() => Program.Services.Products.GetCategories())
                ?.FirstOrDefault(c => c.Name == _category.Text);
            categoryId = cat?.Id ?? 0;
        }
        _products = UiHelpers.Run(() => Program.Services.Products.GetQuickList(categoryId, _search.Text)) ?? new();
        RenderProductButtons();
    }

    private void RenderProductButtons()
    {
        _productFlow.SuspendLayout();
        _productFlow.Controls.Clear();
        foreach (var p in _products.Take(120))
        {
            var btn = new Button
            {
                Width = 148,
                Height = 58,
                FlatStyle = FlatStyle.Flat,
                BackColor = p.Stock <= 0 ? Theme.GridHeader : Theme.Card,
                ForeColor = Theme.Text,
                TextAlign = ContentAlignment.TopLeft,
                Font = Theme.FontBase,
                Cursor = Cursors.Hand,
                Margin = new Padding(4),
                Tag = p.Id
            };
            btn.Text = (p.Name.Length > 30 ? p.Name[..30] + "…" : p.Name) +
                       $"\n{Money.FormatPlain(p.SellingPrice)}  Stok {p.Stock:0.##} {p.UnitName}";
            btn.FlatAppearance.BorderColor = Theme.Border;
            btn.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
            btn.Click += (s, e) => AddProduct(p.Id, 1);
            _productFlow.Controls.Add(btn);
        }
        if (_products.Count == 0)
        {
            var lbl = Theme.Label("Tidak ada produk yang cocok.", 10, false, Theme.Muted);
            lbl.Margin = new Padding(10);
            _productFlow.Controls.Add(lbl);
        }
        _productFlow.ResumeLayout();
    }

    private void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        var code = _barcode.Text.Trim();
        if (code.Length == 0) return;

        // fast path: in-memory scan cache (rebuilt on refresh), zero DB roundtrip per scan
        Product? product = null;
        decimal qtyFromScale = 0;

        // scale barcode first (timbangan): prefix + item + weight
        if (Program.Services.Settings.Get("scale_enabled", "0") == "1")
        {
            var prefixes = Program.Services.Settings.Get("scale_prefixes", "21,02")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).ToArray();
            var divisor = decimal.TryParse(Program.Services.Settings.Get("scale_weight_divisor", "1000"), out var dv) ? dv : 1000;
            var scale = ScaleBarcode.TryParse(code, prefixes, divisor);
            if (scale != null)
            {
                qtyFromScale = scale.Qty;
                if (_scanCache.TryGetValue("S" + scale.Barcode, out var scaled) && scaled.Id > 0)
                    product = scaled;
                else
                {
                    product = UiHelpers.Run(() => Program.Services.Products.GetByCode(scale.Barcode));
                    if (product != null) _scanCache["S" + scale.Barcode] = product;
                }
            }
        }

        if (product == null && _scanCache.TryGetValue(code.ToUpperInvariant(), out var cached) && cached.Id > 0)
        {
            product = cached;
        }
        else if (product == null)
        {
            product = UiHelpers.Run(() =>
                Program.Services.Products.GetByBarcode(code) ?? Program.Services.Products.GetByCode(code));
            if (product != null) _scanCache[code.ToUpperInvariant()] = product;
        }
        if (product == null)
        {
            // fall back to search text
            _search.Text = code;
            _barcode.Clear();
            _barcode.Focus();
            return;
        }
        AddProduct(product.Id, qtyFromScale > 0 ? qtyFromScale : 1);
        _barcode.Clear();
        _barcode.Focus();
    }

    public void AddProduct(long productId, decimal qty)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId) ??
                      UiHelpers.Run(() => Program.Services.Products.Get(productId));
        if (product == null) return;

        // wholesale price kicks in when qty meets the configured threshold
        var effectivePrice = product.SellingPrice;
        if (product.WholesalePrice > 0 && product.WholesaleMinQty > 0 && qty >= product.WholesaleMinQty)
            effectivePrice = product.WholesalePrice;

        var line = _cart.FirstOrDefault(l => l.ProductId == productId);
        if (line != null)
        {
            line.Qty += qty;
            // re-evaluate wholesale tier after quantity change
            if (product.WholesalePrice > 0 && product.WholesaleMinQty > 0)
                line.Price = line.Qty >= product.WholesaleMinQty ? product.WholesalePrice : product.SellingPrice;
        }
        else
        {
            _cart.Add(new CartLine
            {
                ProductId = product.Id,
                Code = product.Code,
                Name = product.Name,
                Price = effectivePrice,
                Cost = product.PurchasePrice,
                Stock = product.Stock,
                Unit = product.UnitName,
                Qty = qty
            });
        }
        RefreshCart();
        _barcode.Focus();
    }

    private void RemoveLine()
    {
        if (_cartGrid.CurrentRow == null) return;
        var pid = Convert.ToInt64(_cartGrid.CurrentRow.Cells["_pid"].Value);
        var line = _cart.FirstOrDefault(l => l.ProductId == pid);
        if (line == null) return;
        if (line.Qty > 1)
        {
            line.Qty -= 1;
        }
        else
        {
            if (UiHelpers.Confirm($"Hapus {line.Name} dari keranjang?"))
                _cart.Remove(line);
        }
        RefreshCart();
    }

    private void RefreshCart()
    {
        _cartGrid.Rows.Clear();
        foreach (var l in _cart)
        {
            var idx = _cartGrid.Rows.Add(l.Name, l.Qty.ToString("0.##"), l.Price, l.Subtotal, l.ProductId);
            if (l.Qty > l.Stock)
                _cartGrid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Danger;
        }
        _emptyHint.Visible = _cart.Count == 0;
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        decimal discount = 0;
        decimal.TryParse(_discount.Text.Replace(".", "").Replace(",", ""), out discount);
        var cfg = TaxConfig.FromSettings(
            Program.Services.Settings.Get("tax_enabled", "0"),
            Program.Services.Settings.Get("tax_rate_percent", "11"),
            Program.Services.Settings.Get("tax_inclusive", "1"));
        var totals = SaleCalculator.Calculate(_cart, discount, cfg);
        _lblSubtotal.Text = $"Subtotal:  {Money.FormatPlain(totals.Subtotal)}";
        _lblDiscount.Text = $"Discount:  {Money.FormatPlain(totals.Discount)}";
        _lblTax.Text = cfg.Enabled ? $"PPN ({cfg.RatePercent:0.##}%{(cfg.Inclusive ? ", include" : "")}):  {Money.FormatPlain(totals.Tax)}" : "PPN: -";
        _lblTotal.Text = $"GRAND TOTAL   Rp {Money.FormatPlain(totals.GrandTotal)}";
        _lblTotal.Location = new Point(2, 58);
        if (_cartGrid.Columns.Contains("price")) Theme.MoneyColumn(_cartGrid, "price");
    }

    private void HoldCart()
    {
        if (_cart.Count == 0) { UiHelpers.Warn("Keranjang kosong."); return; }
        var label = InputDialog.Show("Label parkir (mis. nama pembeli):", "Hold Transaksi");
        if (label == null) return;
        var customer = _customer.SelectedItem as Customer;
        decimal.TryParse(_discount.Text.Replace(".", "").Replace(",", ""), out var discount);
        var id = UiHelpers.Run(() => Program.Services.Holds.Save(
            string.IsNullOrWhiteSpace(label) ? "Hold" : label.Trim(),
            Program.Session!.UserId, customer?.Id ?? 0, customer?.Name ?? "", discount, _cart.ToList(), _heldId));
        if (id <= 0) return;
        _heldId = null;
        _cart.Clear();
        _discount.Text = "0";
        RefreshCart();
        UiHelpers.Info("Transaksi diparkir. Buka 'Panggil (Recall)' untuk melanjutkan.");
    }

    private void RecallCart()
    {
        var holds = UiHelpers.Run(() => Program.Services.Holds.List(Program.Session!.UserId)) ?? new();
        if (holds.Count == 0) { UiHelpers.Info("Tidak ada transaksi yang diparkir."); return; }

        using var dlg = new Form
        {
            Text = "Recall Transaksi",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(520, 340),
            BackColor = Theme.Bg, Font = Theme.FontBase
        };
        var grid = new DataGridView();
        Theme.StyleGrid(grid);
        grid.Location = new Point(16, 14);
        grid.Size = new Size(488, 230);
        grid.Columns.Add("label", "Label");
        grid.Columns.Add("items", "Item");
        grid.Columns.Add("total", "Total");
        grid.Columns.Add("_id", "");
        grid.Columns["_id"].Visible = false;
        Theme.MoneyColumn(grid, "total");
        foreach (var h in holds)
            grid.Rows.Add(h.Label + (h.CustomerName.Length > 0 ? $" ({h.CustomerName})" : ""), h.ItemCount, h.Total, h.Id);

        var pick = Theme.PrimaryButton("PANGGIL", 110, 34);
        pick.Location = new Point(288, 258);
        pick.Click += (s, e) =>
        {
            if (grid.CurrentRow == null) return;
            var id = Convert.ToInt64(grid.CurrentRow.Cells["_id"].Value);
            var hold = UiHelpers.Run(() => Program.Services.Holds.Get(id));
            if (hold == null) return;
            _cart.Clear();
            _cart.AddRange(hold.Items);
            _heldId = id;
            var customerIdx = -1;
            if (hold.CustomerId > 0)
            {
                foreach (var it in _customer.Items)
                    if (it is Customer cst && cst.Id == hold.CustomerId) { customerIdx = _customer.Items.IndexOf(it); break; }
            }
            if (customerIdx >= 0) _customer.SelectedIndex = customerIdx;
            RefreshCart();
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };
        var del = Theme.DangerButton("Hapus", 90, 34);
        del.Location = new Point(188, 258);
        del.Click += (s, e) =>
        {
            if (grid.CurrentRow == null) return;
            var id = Convert.ToInt64(grid.CurrentRow.Cells["_id"].Value);
            Program.Services.Holds.Delete(id);
            grid.Rows.RemoveAt(grid.CurrentRow.Index);
        };
        var cancel = Theme.SecondaryButton("Tutup", 90, 34);
        cancel.Location = new Point(68, 258);
        cancel.Click += (s, e) => dlg.Close();

        dlg.Controls.AddRange(new Control[] { grid, pick, del, cancel });
        dlg.ShowDialog(FindForm());
        FocusBarcode();
    }

    public void BeginPayment()
    {
        if (_cart.Count == 0)
        {
            UiHelpers.Warn("Keranjang kosong.");
            return;
        }

        // shift requirement: warn when no open shift
        var open = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
        if (open == null)
        {
            if (!UiHelpers.Confirm("Shift belum dibuka. Transaksi tetap dapat disimpan, " +
                    "tetapi penjualan tunai tidak tercatat di kas.\n\nBuka shift dulu? (Yes = buka shift)"))
            {
                /* continue without shift */
            }
            else
            {
                using var dlg = new OpenShiftDialog();
                dlg.ShowDialog();
                open = UiHelpers.Run(() => Program.Services.Cash.GetOpenSession(Program.Session!.UserId));
            }
        }

        decimal.TryParse(_discount.Text.Replace(".", "").Replace(",", ""), out var invoiceDiscount);
        var cfg = TaxConfig.FromSettings(
            Program.Services.Settings.Get("tax_enabled", "0"),
            Program.Services.Settings.Get("tax_rate_percent", "11"),
            Program.Services.Settings.Get("tax_inclusive", "1"));
        var totals = SaleCalculator.Calculate(_cart, invoiceDiscount, cfg);

        var allowCredit = Program.Services.Settings.AllowCredit;
        var loyaltyEnabled = Program.Services.Loyalty.Enabled;
        var selectedCustomer0 = _customer.SelectedItem as Customer;
        using var payment = new PaymentDialog(totals.GrandTotal, allowCredit,
            loyaltyEnabled && selectedCustomer0 is { Id: > 1 }
                ? Program.Services.Loyalty.RedeemableValue(selectedCustomer0) : 0);
        if (payment.ShowDialog(FindForm()) != DialogResult.OK) return;

        var selectedCustomer = _customer.SelectedItem as Customer;
        if (payment.CreditAmount > 0 && (selectedCustomer == null || selectedCustomer.Id <= 1))
        {
            UiHelpers.Warn("Piutang harus atas nama pelanggan. Pilih pelanggan dulu (bukan Umum).");
            return;
        }

        var sale = new Sale
        {
            CustomerId = selectedCustomer?.Id ?? 1,
            CustomerName = selectedCustomer?.Name ?? "Umum",
            UserId = Program.Session!.UserId,
            CashierName = Program.Session.Username,
            CashSessionId = open?.Id ?? 0,
            Items = _cart.Select(l => new SaleItem
            {
                ProductId = l.ProductId,
                ProductCode = l.Code,
                ProductName = l.Name,
                Qty = l.Qty,
                Price = l.Price,
                Cost = l.Cost,
                Discount = l.Discount,
                Subtotal = l.Subtotal
            }).ToList(),
            Payments = payment.ResultPayments
                .Where(p => p.Method != PaymentMethod.Credit || p.Amount > 0)
                .Select(p => new SalePayment
            {
                Method = p.Method,
                Amount = p.Amount,
                Reference = p.Reference
            }).ToList(),
            Subtotal = totals.Subtotal,
            Discount = totals.Discount,
            Tax = totals.Tax,
            Total = totals.GrandTotal
        };

        var saved = UiHelpers.Run(() => Program.Services.Sales.CompleteSale(sale));
        if (saved == null || saved.Id == 0) return;

        // loyalty: apply earn + redemption
        if (payment.PointsRedeemed > 0 && selectedCustomer is { Id: > 1 })
        {
            UiHelpers.Run<object?>(() =>
            {
                Program.Services.Loyalty.ApplyForSale(saved.Id, selectedCustomer.Id,
                    totals.GrandTotal, payment.PointsRedeemed,
                    Program.Session!.UserId, Program.Session.Username);
                return null;
            });
        }

        Program.Session.DataChangedSinceBackup = true;

        var change = ChangeCalculator.Change(totals.GrandTotal, payment.ResultPayments.Select(p => (p.Method, p.Amount)));
        var creditMsg = saved.Outstanding > 0
            ? $"\nPiutang: {Money.Format(saved.Outstanding)} a.n. {saved.CustomerName}\n"
            : "";
        var pointsMsg = payment.PointsRedeemed > 0
            ? $"Poin dipakai: {payment.PointsRedeemable:N0} ({Money.Format(payment.PointsRedeemed)})\n" +
              $"Poin didapat: {Program.Services.Loyalty.EarnFor(totals.GrandTotal):N0}\n"
            : "";
        var printReceipt = saved.Total > 0 && UiHelpers.Confirm(
            $"Transaksi {saved.InvoiceNo} berhasil.\n" +
            (change > 0 ? $"Kembalian: {Money.Format(change)}\n" : "") +
            creditMsg + pointsMsg +
            "\nCetak struk?");

        if (printReceipt)
        {
            UiHelpers.Run(() => Program.Services.Printer.PrintReceipt(saved));
        }

        _cart.Clear();
        _heldId = null;
        _discount.Text = "0";
        RefreshCart();
        LoadProducts();
        RebuildScanCache();
        _barcode.Focus();
        ((MainForm?)FindForm())?.UpdateStatusBar();
    }
}



