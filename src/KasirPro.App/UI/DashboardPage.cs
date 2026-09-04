using KasirPro.Core.Domain;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App.UI;

/// <summary>Dashboard: 4 summary cards, 7-day sales trend chart, recent transactions, top products.</summary>
public class DashboardPage : Panel, IPage
{
    private readonly Label _cardSales = ValueLabel();
    private readonly Label _cardTx = ValueLabel();
    private readonly Label _cardProfit = ValueLabel();
    private readonly Label _cardLow = ValueLabel();
    private readonly TrendChart _chart = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _gridRecent = NewGrid();
    private readonly DataGridView _gridTop = NewGrid();
    private Panel _empty = null!;

    public DashboardPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        Padding = new Padding(14);

        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 108, ColumnCount = 4, RowCount = 1, BackColor = Theme.Bg };
        for (var i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        var cardSales = SummaryCard(Strings.T("dash_sales_today"), _cardSales, Theme.Accent);
        var cardTx = SummaryCard(Strings.T("dash_transactions"), _cardTx, Theme.Success);
        var cardProfit = SummaryCard(Strings.T("dash_profit"), _cardProfit, Theme.Warning);
        var cardLow = SummaryCard(Strings.T("dash_low_stock"), _cardLow, Theme.Danger);
        cardSales.Margin = new Padding(0, 0, 6, 0);
        cardTx.Margin = new Padding(6, 0, 6, 0);
        cardProfit.Margin = new Padding(6, 0, 6, 0);
        cardLow.Margin = new Padding(6, 0, 0, 0);
        cards.Controls.Add(cardSales, 0, 0);
        cards.Controls.Add(cardTx, 1, 0);
        cards.Controls.Add(cardProfit, 2, 0);
        cards.Controls.Add(cardLow, 3, 0);
        cards.Margin = Padding.Empty;

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Bg, Padding = new Padding(0, 12, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        var trendCard = Theme.CardFluid();
        trendCard.Controls.Add(_chart);
        var trendTitle = Theme.Label(Strings.T("dash_trend_title"), 10, true);
        trendTitle.Dock = DockStyle.Top; trendTitle.Height = 26;
        trendCard.Controls.Add(trendTitle);
        _chart.BringToFront();

        var recentCard = Theme.CardFluid();
        var recentTitle = Theme.Label(Strings.T("dash_recent_title"), 10, true);
        recentTitle.Dock = DockStyle.Top; recentTitle.Height = 26;
        _gridRecent.Dock = DockStyle.Fill;
        recentCard.Controls.Add(_gridRecent);
        recentCard.Controls.Add(recentTitle);

        var topCard = Theme.CardFluid();
        var topTitle = Theme.Label(Strings.T("dash_top_title"), 10, true);
        topTitle.Dock = DockStyle.Top; topTitle.Height = 26;
        _gridTop.Dock = DockStyle.Fill;
        topCard.Controls.Add(_gridTop);
        topCard.Controls.Add(topTitle);

        body.Controls.Add(trendCard, 0, 0);
        body.Controls.Add(recentCard, 1, 0);
        body.Controls.Add(topCard, 0, 1);
        body.SetColumnSpan(topCard, 2);

        Controls.Add(body);
        Controls.Add(cards);
    }

    private static Label ValueLabel() =>
        new() { Font = Theme.FontCardValue, ForeColor = Theme.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };

    private static Panel SummaryCard(string title, Label value, Color accent)
    {
        var card = Theme.CardFluid();
        var t = Theme.Label(title, 9, false, Theme.Muted);
        t.Dock = DockStyle.Top; t.Height = 20;
        var bar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent };
        value.ForeColor = accent;
        card.Controls.Add(value);
        card.Controls.Add(t);
        card.Controls.Add(bar);
        value.BringToFront();
        return card;
    }

    private static DataGridView NewGrid()
    {
        var g = new DataGridView();
        Theme.StyleGrid(g);
        return g;
    }

    public void RefreshData()
    {
        var summary = UiHelpers.Run(() => Program.Services.Reports.Dashboard());
        if (summary == null) return;

        _cardSales.Text = summary.SalesToday == 0 && summary.TransactionsToday == 0 && summary.ProfitToday == 0
            ? Money.Format(0)
            : Money.Format(summary.SalesToday);
        _cardTx.Text = summary.TransactionsToday.ToString("N0");
        _cardProfit.Text = Money.Format(summary.ProfitToday);
        _cardLow.Text = summary.LowStockCount + " Items";

        _chart.Data = summary.Trend;
        _chart.Invalidate();

        _gridRecent.Columns.Clear();
        _gridRecent.Columns.Add("time", Strings.T("grid_time"));
        _gridRecent.Columns.Add("invoice", Strings.T("grid_invoice"));
        _gridRecent.Columns.Add("customer", Strings.T("grid_customer"));
        var cTotal = _gridRecent.Columns.Add("total", Strings.T("grid_total"));
        _gridRecent.Columns.Add("payment", Strings.T("grid_payment"));
        _gridRecent.Columns.Add("status", Strings.T("grid_status"));
        _gridRecent.Columns["time"].FillWeight = 14;
        _gridRecent.Columns["invoice"].FillWeight = 24;
        _gridRecent.Columns["customer"].FillWeight = 24;
        _gridRecent.Columns[cTotal].FillWeight = 14;
        _gridRecent.Columns["payment"].FillWeight = 14;
        _gridRecent.Columns["status"].FillWeight = 10;
        Theme.MoneyColumn(_gridRecent, "total");
        _gridRecent.Rows.Clear();
        if (summary.RecentSales.Count == 0)
        {
            _gridRecent.Rows.Add("-", "Belum ada transaksi hari ini", "", 0, "", "");
        }
        else
        {
            foreach (var s in summary.RecentSales)
            {
                _gridRecent.Rows.Add(s.SaleDate.ToString("HH:mm"), s.InvoiceNo, s.CustomerName,
                    s.Total, s.CashierName, s.Status);
            }
        }

        _gridTop.Columns.Clear();
        _gridTop.Columns.Add("name", Strings.T("grid_product"));
        _gridTop.Columns.Add("qty", Strings.T("grid_qty_sold"));
        var cRev = _gridTop.Columns.Add("revenue", Strings.T("grid_revenue"));
        Theme.MoneyColumn(_gridTop, "revenue");
        _gridTop.Columns["name"].FillWeight = 55;
        _gridTop.Columns["qty"].FillWeight = 18;
        _gridTop.Columns[cRev].FillWeight = 27;
        _gridTop.Rows.Clear();
        if (summary.TopProducts.Count == 0)
        {
            _gridTop.Rows.Add("Belum ada penjualan", 0, 0);
        }
        else
        {
            foreach (var p in summary.TopProducts)
                _gridTop.Rows.Add(p.Name, p.QtySold.ToString("0.##"), p.Revenue);
        }
    }
}

/// <summary>Simple clean bar chart (GDI+), zero dependencies.</summary>
public class TrendChart : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public List<TrendPoint> Data { get; set; } = new();

    public TrendChart() { DoubleBuffered = true; ResizeRedraw = true; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);

        if (Data.Count == 0)
        {
            using var f = new Font("Segoe UI", 9f, FontStyle.Italic);
            var sz = g.MeasureString("Belum ada data penjualan", f);
            g.DrawString("Belum ada data penjualan", f, new SolidBrush(Theme.Muted),
                (Width - sz.Width) / 2, (Height - sz.Height) / 2);
            return;
        }

        var padTop = 14; var padBottom = 28; var padX = 18;
        var chartH = Height - padTop - padBottom;
        var chartW = Width - padX * 2;
        var max = Math.Max(1000, Data.Max(d => d.Total));
        var barW = Math.Min(56, chartW / Data.Count * 0.5f);

        // horizontal gridlines
        using var gridPen = new Pen(Theme.Border, 1);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var fAxis = new Font("Segoe UI", 7.5f);
        for (var i = 0; i <= 4; i++)
        {
            var y = padTop + chartH - chartH * i / 4f;
            g.DrawLine(gridPen, padX, y, Width - padX, y);
            var val = max * i / 4;
            g.DrawString(Money.FormatPlain(val), fAxis, mutedBrush, 2, y - 6);
        }

        using var barBrush = new SolidBrush(Theme.Accent);
        using var todayBrush = new SolidBrush(Theme.AccentDark);
        using var fLabel = new Font("Segoe UI", 8f);
        for (var i = 0; i < Data.Count; i++)
        {
            var slotW = chartW / Data.Count;
            var x = padX + slotW * i + (slotW - barW) / 2;
            var h = Data[i].Total <= 0 ? 2 : chartH * (float)(Data[i].Total / max);
            var y = padTop + chartH - h;
            g.FillRectangle(Data[i].Date.Date == DateTime.Today ? todayBrush : barBrush, x, y, barW, h);

            var label = Data[i].Total > 0 ? Money.FormatPlain(Data[i].Total) : "0";
            var szL = g.MeasureString(label, fLabel);
            g.DrawString(label, fLabel, mutedBrush, x + barW / 2 - szL.Width / 2, y - 16);

            var dayLabel = Data[i].Date.ToString("ddd");
            var szD = g.MeasureString(dayLabel, fLabel);
            g.DrawString(dayLabel, fLabel, mutedBrush, x + barW / 2 - szD.Width / 2, padTop + chartH + 6);
        }
    }
}

