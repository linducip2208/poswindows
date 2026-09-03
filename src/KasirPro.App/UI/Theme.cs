using System.Drawing;

namespace KasirPro.App.UI;

/// <summary>
/// Visual language: Segoe UI, white/light gray surfaces, blue accent, hairline borders,
/// flat professional business look. No heavy animation, no rounded excess.
/// </summary>
public static class Theme
{
    public static Color Accent = Color.FromArgb(37, 99, 235);
    public static Color AccentDark = Color.FromArgb(29, 78, 216);
    public static Color AccentLight = Color.FromArgb(219, 234, 254);
    public static Color Bg = Color.FromArgb(244, 246, 249);
    public static Color Card = Color.White;
    public static Color Border = Color.FromArgb(226, 232, 240);
    public static Color Text = Color.FromArgb(30, 41, 59);
    public static Color Muted = Color.FromArgb(100, 116, 139);
    public static Color Danger = Color.FromArgb(220, 38, 38);
    public static Color Success = Color.FromArgb(22, 163, 74);
    public static Color Warning = Color.FromArgb(217, 119, 6);
    public static Color GridHeader = Color.FromArgb(241, 245, 249);
    public static Color GridAlt = Color.FromArgb(248, 250, 252);

    public static Font FontBase = new("Segoe UI", 9f);
    public static Font FontMedium = new("Segoe UI", 10f);
    public static Font FontMediumBold = new("Segoe UI", 10f, FontStyle.Bold);
    public static Font FontLargeBold = new("Segoe UI", 14f, FontStyle.Bold);
    public static Font FontTitle = new("Segoe UI", 18f, FontStyle.Bold);
    public static Font FontCardValue = new("Segoe UI", 16f, FontStyle.Bold);
    public static Font FontTotal = new("Segoe UI", 15f, FontStyle.Bold);
    public static Font FontPay = new("Segoe UI", 16f, FontStyle.Bold);

    public static Button PrimaryButton(string text, int w = 120, int h = 34)
    {
        var b = new Button
        {
            Text = text,
            Width = w,
            Height = h,
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = FontMediumBold,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentDark;
        return b;
    }

    public static Button SecondaryButton(string text, int w = 110, int h = 34)
    {
        var b = new Button
        {
            Text = text,
            Width = w,
            Height = h,
            FlatStyle = FlatStyle.Flat,
            BackColor = Card,
            ForeColor = Text,
            Font = FontMedium,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = GridHeader;
        return b;
    }

    public static Button DangerButton(string text, int w = 110, int h = 34)
    {
        var b = new Button
        {
            Text = text, Width = w, Height = h,
            FlatStyle = FlatStyle.Flat,
            BackColor = Danger, ForeColor = Color.White,
            Font = FontMediumBold, Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static Button WarningButton(string text, int w = 110, int h = 34)
    {
        var b = new Button
        {
            Text = text, Width = w, Height = h,
            FlatStyle = FlatStyle.Flat,
            BackColor = Warning, ForeColor = Color.White,
            Font = FontMediumBold, Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static Button SuccessButton(string text, int w = 110, int h = 34)
    {
        var b = new Button
        {
            Text = text, Width = w, Height = h,
            FlatStyle = FlatStyle.Flat,
            BackColor = Success, ForeColor = Color.White,
            Font = FontMediumBold, Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static TextBox TextBox(int w = 220, int h = 28, string placeholder = "")
    {
        var t = new TextBox
        {
            Width = w, Height = h, Font = FontMedium,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = placeholder
        };
        return t;
    }

    public static ComboBox Combo(int w = 200)
    {
        var c = new ComboBox
        {
            Width = w, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = FontMedium, FlatStyle = FlatStyle.Flat
        };
        return c;
    }

    public static Label Label(string text, int size = 9, bool bold = false, Color? color = null)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Text
        };
    }

    public static Panel CardBox(int w, int h)
    {
        var p = new Panel
        {
            Width = w, Height = h,
            BackColor = Card,
            Padding = new Padding(14)
        };
        p.Paint += (s, e) =>
        {
            using var pen = new Pen(Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        return p;
    }

    public static Panel CardFluid()
    {
        var p = new Panel { BackColor = Card, Padding = new Padding(14), Dock = DockStyle.Fill };
        p.Paint += (s, e) =>
        {
            using var pen = new Pen(Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        return p;
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BorderStyle = BorderStyle.None;
        g.BackgroundColor = Color.White;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = GridHeader;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        g.ColumnHeadersDefaultCellStyle.Font = FontMediumBold;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeader;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 8, 6, 8);
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        g.RowHeadersVisible = false;
        g.DefaultCellStyle.BackColor = Color.White;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = AccentLight;
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.DefaultCellStyle.Font = FontBase;
        g.DefaultCellStyle.Padding = new Padding(6, 6, 6, 6);
        g.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
        g.GridColor = Border;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AllowUserToResizeRows = false;
        g.ReadOnly = true;
    }

    public static void MoneyColumn(DataGridView g, string name)
    {
        if (g.Columns[name] == null) return;
        g.Columns[name].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        g.Columns[name].DefaultCellStyle.Format = "N0";
    }

    public static Panel PageHeader(string title, string? subtitle = null)
    {
        var p = new Panel { Height = 56, Dock = DockStyle.Top, BackColor = Bg };
        var lbl = Label(title, 14, true);
        lbl.Location = new Point(16, 10);
        p.Controls.Add(lbl);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = Label(subtitle, 9, false, Muted);
            sub.Location = new Point(17, 36);
            p.Controls.Add(sub);
        }
        p.Paint += (s, e) =>
        {
            using var pen = new Pen(Border, 1);
            e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
        };
        return p;
    }
}

