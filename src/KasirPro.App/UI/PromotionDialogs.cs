using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Promotion list + create (percent/fixed/BXGY/special price) + coupon support.</summary>
public class PromotionListDialog : Form
{
    private readonly DataGridView _grid = new();

    public PromotionListDialog()
    {
        Text = "Promotions";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 540);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader("Promotions", "Diskon persen/fixed, Buy X Get Y, harga spesial + coupon");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        var btnNew = Theme.PrimaryButton("+ PROMO BARU", 150, 34);
        btnNew.Location = new Point(16, 8);
        btnNew.Click += (s, e) => { new PromotionEditDialog(null).ShowDialog(); LoadData(); };
        var btnEdit = Theme.SecondaryButton("Edit", 80, 34);
        btnEdit.Location = new Point(172, 8);
        btnEdit.Click += (s, e) => EditSelected();
        var btnDeactivate = Theme.DangerButton("Nonaktifkan", 110, 34);
        btnDeactivate.Location = new Point(258, 8);
        btnDeactivate.Click += (s, e) => DeactivateSelected();
        toolbar.Controls.AddRange(new Control[] { btnNew, btnEdit, btnDeactivate });

        Theme.StyleGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("code", "Code");
        _grid.Columns.Add("name", "Name");
        _grid.Columns.Add("type", "Type");
        _grid.Columns.Add("range", "Periode");
        _grid.Columns.Add("priority", "Priority");
        _grid.CellDoubleClick += (s, e) => EditSelected();

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += (s, e) => LoadData();
    }

    private void LoadData()
    {
        var list = UiHelpers.Run(() => Program.Services.Promotions.List()) ?? new();
        _grid.Rows.Clear();
        foreach (var (code, name, type, range, prio) in list)
            _grid.Rows.Add(code, name, type, range, prio);
    }

    private void EditSelected()
    {
        if (_grid.CurrentRow == null) return;
        var code = _grid.CurrentRow.Cells["code"].Value?.ToString() ?? "";
        var promo = UiHelpers.Run(() => Program.Services.Promotions.GetByCoupon(code));
        if (promo == null) return;
        new PromotionEditDialog(promo).ShowDialog();
        LoadData();
    }

    private void DeactivateSelected()
    {
        if (_grid.CurrentRow == null) return;
        var code = _grid.CurrentRow.Cells["code"].Value?.ToString() ?? "";
        if (!UiHelpers.Confirm($"Nonaktifkan promo '{code}'?")) return;
        UiHelpers.Run<object?>(() =>
        {
            Program.Services.Promotions.Deactivate(code, Program.Session!.UserId, Program.Session.Username);
            return null;
        });
        LoadData();
    }
}

public class PromotionEditDialog : Form
{
    private readonly Promotion? _promo;
    private readonly TextBox _code = Theme.TextBox(140);
    private readonly TextBox _name = Theme.TextBox(280);
    private readonly ComboBox _type = Theme.Combo(160);
    private readonly TextBox _value = Theme.TextBox(110);
    private readonly ComboBox _scope = Theme.Combo(130);
    private readonly TextBox _scopeRef = Theme.TextBox(160);
    private readonly TextBox _minPurchase = Theme.TextBox(120);
    private readonly TextBox _minQty = Theme.TextBox(80);
    private readonly TextBox _buyQty = Theme.TextBox(70);
    private readonly TextBox _getQty = Theme.TextBox(70);
    private readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _end = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly ComboBox _startTime = Theme.Combo(80);
    private readonly ComboBox _endTime = Theme.Combo(80);
    private readonly CheckBox _memberOnly = new() { Text = "Member only", AutoSize = true, Font = Theme.FontBase };
    private readonly CheckBox _stackable = new() { Text = "Stackable", AutoSize = true, Font = Theme.FontBase };
    private readonly TextBox _coupon = Theme.TextBox(140);
    private readonly TextBox _priority = Theme.TextBox(60);

    public PromotionEditDialog(Promotion? promo)
    {
        _promo = promo;
        Text = promo == null ? "Promo Baru" : $"Edit Promo - {promo.Code}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 560);
        AutoScroll = true;
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var title = Theme.Label(promo == null ? "Promo Baru" : "Edit Promo", 13, true, Theme.Accent);
        title.Location = new Point(20, 12);

        var y = 50;
        void Field(string label, Control c)
        {
            var l = Theme.Label(label, 9, true);
            l.Location = new Point(20, y);
            c.Location = new Point(170, y - 3);
            Controls.Add(l);
            Controls.Add(c);
            y += 34;
        }

        Field("Kode:", _code);
        Field("Nama:", _name);
        Field("Tipe:", _type);
        _type.Items.AddRange(new object[] { "Diskon %", "Diskon Fixed", "Buy X Get Y", "Harga Spesial" });
        _type.SelectedIndex = 0;
        Field("Nilai:", _value);
        _value.PlaceholderText = "% atau Rp";
        Field("Scope:", _scope);
        _scope.Items.AddRange(new object[] { "Semua", "Produk (kode)", "Kategori (nama)", "Brand" });
        _scope.SelectedIndex = 0;
        Field("Scope ref:", _scopeRef);
        Field("Min beli (Rp):", _minPurchase);
        Field("Min qty:", _minQty);
        Field("Buy X:", _buyQty);
        Field("Get Y:", _getQty);

        var lDate = Theme.Label("Periode:", 9, true);
        lDate.Location = new Point(20, y);
        _start.Location = new Point(170, y - 3);
        _end.Location = new Point(290, y - 3);
        Controls.Add(lDate); Controls.Add(_start); Controls.Add(_end); y += 32;

        var lTime = Theme.Label("Jam (opsional):", 9, true);
        lTime.Location = new Point(20, y);
        _startTime.Location = new Point(170, y - 3);
        _endTime.Location = new Point(260, y - 3);
        _startTime.Items.Add("");
        _endTime.Items.Add("");
        _startTime.SelectedIndex = 0; _endTime.SelectedIndex = 0;
        for (var h = 0; h < 24; h++)
        {
            _startTime.Items.Add($"{h:00}:00");
            _endTime.Items.Add($"{h:00}:59");
        }
        Controls.Add(lTime); Controls.Add(_startTime); Controls.Add(_endTime); y += 32;

        _memberOnly.Location = new Point(170, y); Controls.Add(_memberOnly); y += 28;
        _stackable.Location = new Point(300, y); Controls.Add(_stackable); y += 32;

        Field("Coupon code:", _coupon);
        Field("Priority:", _priority);

        var save = Theme.PrimaryButton("SIMPAN", 120, 40);
        save.Location = new Point(420, y + 6);
        save.Click += OnSave;
        var cancel = Theme.SecondaryButton("Batal", 90, 40);
        cancel.Location = new Point(320, y + 6);
        cancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { title, save, cancel });

        if (promo != null)
        {
            _code.Text = promo.Code; _name.Text = promo.Name;
            _type.SelectedIndex = (int)promo.Type;
            _value.Text = promo.Type == PromoType.Percent ? promo.Value.ToString("0.##") : promo.Value.ToString("0");
            _scope.SelectedIndex = (int)promo.Scope;
            _scopeRef.Text = promo.ScopeRef;
            _minPurchase.Text = promo.MinPurchase.ToString("0");
            _minQty.Text = promo.MinQty.ToString("0.##");
            _buyQty.Text = promo.BuyQty.ToString("0.##");
            _getQty.Text = promo.GetQty.ToString("0.##");
            _start.Value = promo.StartDate; _end.Value = promo.EndDate;
            _startTime.Text = promo.StartTime?.ToString(@"hh\:mm") ?? "";
            _endTime.Text = promo.EndTime?.ToString(@"hh\:mm") ?? "";
            _memberOnly.Checked = promo.MemberOnly;
            _stackable.Checked = promo.Stackable;
            _coupon.Text = promo.CouponCode;
            _priority.Text = promo.Priority.ToString();
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text))
        {
            UiHelpers.Warn("Kode dan nama wajib diisi.");
            return;
        }
        decimal Parse(TextBox t) =>
            decimal.TryParse(t.Text.Replace(".", "").Replace(",", "."), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        var promo = _promo ?? new Promotion();
        promo.Code = _code.Text.Trim().ToUpperInvariant();
        promo.Name = _name.Text.Trim();
        promo.Type = (PromoType)_type.SelectedIndex;
        promo.Value = Parse(_value);
        promo.Scope = (PromoScope)_scope.SelectedIndex;
        promo.ScopeRef = _scopeRef.Text.Trim();
        promo.MinPurchase = Parse(_minPurchase);
        promo.MinQty = Parse(_minQty);
        promo.BuyQty = Parse(_buyQty);
        promo.GetQty = Parse(_getQty);
        promo.StartDate = _start.Value.Date;
        promo.EndDate = _end.Value.Date;
        promo.StartTime = string.IsNullOrWhiteSpace(_startTime.Text) ? null : TimeSpan.Parse(_startTime.Text);
        promo.EndTime = string.IsNullOrWhiteSpace(_endTime.Text) ? null : TimeSpan.Parse(_endTime.Text);
        promo.MemberOnly = _memberOnly.Checked;
        promo.Stackable = _stackable.Checked;
        promo.CouponCode = _coupon.Text.Trim().ToUpperInvariant();
        promo.Priority = int.TryParse(_priority.Text, out var pr) ? pr : 0;

        var id = UiHelpers.Run(() => Program.Services.Promotions.Save(promo, Program.Session!.UserId, Program.Session.Username));
        if (id <= 0) return;
        UiHelpers.Info("Promo tersimpan dan aktif.");
        DialogResult = DialogResult.OK;
        Close();
    }
}
