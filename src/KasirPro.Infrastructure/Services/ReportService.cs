using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class ReportService
{
    private readonly Db _db;

    public ReportService(Db db) { _db = db; }

    public List<SaleReportRow> Sales(DateTime from, DateTime to, long userId = 0)
    {
        var p = new DynamicParameters();
        p.Add("f", DbEx.Iso(from.Date));
        p.Add("t2", DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)));
        var userFilter = userId > 0 ? "AND s.user_id=@uid" : "";
        if (userId > 0) p.Add("uid", userId);
        return _db.With(c => c.Query<SaleReportRow>(@$"SELECT s.sale_date AS Date, s.invoice_no AS InvoiceNo,
            COALESCE(cu.name,'Umum') AS Customer, COALESCE(us.full_name, us.username,'') AS Cashier,
            {DbEx.MoneyColumn("s.subtotal")} AS Subtotal, {DbEx.MoneyColumn("s.discount")} AS Discount,
            {DbEx.MoneyColumn("s.total")} AS Total,
            COALESCE((SELECT GROUP_CONCAT(sp.method, ', ') FROM sale_payments sp WHERE sp.sale_id=s.id),'') AS Payment,
            s.status AS Status
            FROM sales s
            LEFT JOIN customers cu ON cu.id=s.customer_id
            LEFT JOIN users us ON us.id=s.user_id
            WHERE s.sale_date >= @f AND s.sale_date <= @t2 {userFilter}
            ORDER BY s.sale_date, s.id").ToList());
    }

    public List<PurchaseReportRow> Purchases(DateTime from, DateTime to)
    {
        return _db.With(c => c.Query<PurchaseReportRow>(@$"SELECT pu.purchase_date AS Date,
            pu.purchase_no AS PurchaseNo, pu.supplier_invoice_no AS SupplierInvoice,
            COALESCE(su.name,'') AS Supplier, {DbEx.MoneyColumn("pu.subtotal")} AS Subtotal,
            {DbEx.MoneyColumn("pu.discount")} AS Discount, {DbEx.MoneyColumn("pu.total")} AS Total,
            pu.status AS Status
            FROM purchases pu LEFT JOIN suppliers su ON su.id=pu.supplier_id
            WHERE pu.purchase_date >= @f AND pu.purchase_date <= @t2
            ORDER BY pu.purchase_date, pu.id",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());
    }

    public List<ProfitRow> Profit(DateTime from, DateTime to) =>
        _db.With(c => c.Query<ProfitRow>(@$"SELECT date(s.sale_date) AS Date,
            CAST(ROUND(SUM(s.total)/100.0,2) AS REAL) AS Revenue,
            CAST(ROUND(SUM(s.cost_total)/100.0,2) AS REAL) AS Cost,
            CAST(ROUND((SUM(s.total)-SUM(s.cost_total))/100.0,2) AS REAL) AS Profit
            FROM sales s
            WHERE s.status='COMPLETED' AND s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY date(s.sale_date) ORDER BY date(s.sale_date)",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    public List<ProductSalesRow> ProductSales(DateTime from, DateTime to, int limit = 0)
    {
        var lim = limit > 0 ? $"LIMIT {limit}" : "";
        return _db.With(c => c.Query<ProductSalesRow>(@$"SELECT si.product_code AS Code, si.product_name AS Name,
            COALESCE(cat.name,'') AS Category,
            CAST(ROUND(SUM(CASE WHEN s.status='COMPLETED' THEN si.qty ELSE 0 END),2) AS REAL) AS QtySold,
            CAST(ROUND(SUM(CASE WHEN s.status='COMPLETED' THEN si.subtotal ELSE 0 END)/100.0,2) AS REAL) AS Revenue,
            CAST(ROUND(SUM(CASE WHEN s.status='COMPLETED' THEN (si.subtotal - si.qty*si.cost) ELSE 0 END)/100.0,2) AS REAL) AS Profit
            FROM sale_items si
            JOIN sales s ON s.id=si.sale_id
            LEFT JOIN products pr ON pr.id=si.product_id
            LEFT JOIN categories cat ON cat.id=pr.category_id
            WHERE s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY si.product_id
            ORDER BY QtySold DESC {lim}",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());
    }

    public List<StockRow> Stock(string search = "", long categoryId = 0, bool lowOnly = false)
    {
        var where = new List<string> { "p.is_active=1" };
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search)) { where.Add("p.name LIKE @q"); p.Add("q", "%" + search + "%"); }
        if (categoryId > 0) { where.Add("p.category_id=@c"); p.Add("c", categoryId); }
        if (lowOnly) where.Add("p.stock <= p.min_stock");
        return _db.With(c => c.Query<StockRow>($@"SELECT p.code AS Code, p.name AS Name, COALESCE(cat.name,'') AS Category,
            p.stock AS Stock, COALESCE(u.name,'') AS Unit, p.min_stock AS MinStock,
            {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice, {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
            CAST(ROUND((p.stock*p.purchase_price)/100.0,2) AS REAL) AS StockValue
            FROM products p LEFT JOIN categories cat ON cat.id=p.category_id LEFT JOIN units u ON u.id=p.unit_id
            WHERE {string.Join(" AND ", where)} ORDER BY p.name", p).ToList());
    }

    public List<StockMovementRow> StockMovements(DateTime from, DateTime to, long productId = 0, string refType = "") =>
        _db.With(c => c.Query<StockMovementRow>(@$"SELECT sm.created_at AS Date, pr.code AS Code, pr.name AS Product,
            sm.reference_type AS ReferenceType, sm.reference_id AS ReferenceId, sm.direction AS Direction,
            sm.qty AS Qty, sm.stock_after AS StockAfter, COALESCE(us.username,'') AS User, sm.notes AS Notes
            FROM stock_movements sm
            JOIN products pr ON pr.id=sm.product_id
            LEFT JOIN users us ON us.id=sm.user_id
            WHERE sm.created_at >= @f AND sm.created_at <= @t2
            {(productId > 0 ? "AND sm.product_id=@pid" : "")}
            {(refType != "" ? "AND sm.reference_type=@rt" : "")}
            ORDER BY sm.id DESC LIMIT 2000",
            new
            {
                f = DbEx.Iso(from.Date),
                t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)),
                pid = productId,
                rt = refType
            }).ToList());

    public List<StockRow> LowStockReport() =>
        _db.With(c => c.Query<StockRow>(@$"SELECT p.code AS Code, p.name AS Name, COALESCE(cat.name,'') AS Category,
            p.stock AS Stock, COALESCE(u.name,'') AS Unit, p.min_stock AS MinStock,
            {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice, {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
            CAST(ROUND((p.stock*p.purchase_price)/100.0,2) AS REAL) AS StockValue
            FROM products p LEFT JOIN categories cat ON cat.id=p.category_id LEFT JOIN units u ON u.id=p.unit_id
            WHERE p.is_active=1 AND p.stock <= p.min_stock
            ORDER BY (p.stock - p.min_stock) ASC").ToList());

    public List<CashReportRow> Cash(DateTime from, DateTime to) =>
        _db.With(c => c.Query<CashReportRow>(@$"SELECT date(cs.opened_at) AS Date,
            '#' || cs.id AS Session, COALESCE(us.username,'') AS Cashier,
            {DbEx.MoneyColumn("cs.opening_cash")} AS Opening,
            {DbEx.MoneyColumn("cs.cash_in")} AS CashIn,
            {DbEx.MoneyColumn("cs.cash_out")} AS CashOut,
            {DbEx.MoneyColumn("cs.cash_sales")} AS CashSales,
            {DbEx.MoneyColumn("cs.closing_cash_expected")} AS Expected,
            {DbEx.MoneyColumn("cs.closing_cash_actual")} AS Actual,
            {DbEx.MoneyColumn("cs.difference")} AS Difference,
            cs.status AS Status
            FROM cash_sessions cs LEFT JOIN users us ON us.id=cs.user_id
            WHERE cs.opened_at >= @f AND cs.opened_at <= @t2
            ORDER BY cs.id",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    public List<CashierReportRow> Cashiers(DateTime from, DateTime to) =>
        _db.With(c => c.Query<CashierReportRow>(@$"SELECT COALESCE(us.full_name, us.username,'?') AS Cashier,
            COUNT(*) AS Transactions,
            CAST(ROUND(SUM(s.total)/100.0,2) AS REAL) AS Total,
            CAST(ROUND(SUM(s.total - s.cost_total)/100.0,2) AS REAL) AS Profit
            FROM sales s LEFT JOIN users us ON us.id=s.user_id
            WHERE s.status='COMPLETED' AND s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY s.user_id ORDER BY Total DESC",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    // ===== extended reports (v2.1) =====

    /// <summary>Receivable aging buckets from outstanding sale_debts.</summary>
    public List<(string Bucket, int Count, decimal Total)> ReceivableAging()
    {
        const string sql = @"
            SELECT
            CASE
                WHEN julianday('now','localtime') - julianday(d.created_at) <= 30 THEN '1-30'
                WHEN julianday('now','localtime') - julianday(d.created_at) <= 60 THEN '31-60'
                WHEN julianday('now','localtime') - julianday(d.created_at) <= 90 THEN '61-90'
                ELSE '>90'
            END AS Bucket,
            COUNT(*) AS Count,
            CAST(ROUND(SUM(d.original_amount - d.paid_amount)/100.0,2) AS REAL) AS Total
            FROM sale_debts d
            WHERE d.status != 'SETTLED' AND d.original_amount > d.paid_amount
            GROUP BY Bucket ORDER BY Bucket";
        return _db.With(c => c.Query<(string, int, decimal)>(sql).ToList());
    }

    /// <summary>Inventory valuation by category (stock x purchase price).</summary>
    public List<(string Category, int Products, decimal StockValue)> InventoryValuation() =>
        _db.With(c => c.Query<(string, int, decimal)>(@"
            SELECT COALESCE(cat.name,'(tanpa kategori)') AS Category,
            COUNT(*) AS Products,
            CAST(ROUND(SUM(p.stock * p.purchase_price)/100.0,2) AS REAL) AS StockValue
            FROM products p LEFT JOIN categories cat ON cat.id=p.category_id
            WHERE p.is_active=1
            GROUP BY cat.id ORDER BY StockValue DESC").ToList());

    /// <summary>Monthly sales summary (last 12 months).</summary>
    public List<(string Month, int Count, decimal Total, decimal Profit)> MonthlySales() =>
        _db.With(c => c.Query<(string, int, decimal, decimal)>(@"
            SELECT strftime('%Y-%m', sale_date) AS Month, COUNT(*) AS Count,
            CAST(ROUND(SUM(total)/100.0,2) AS REAL) AS Total,
            CAST(ROUND(SUM(total-cost_total)/100.0,2) AS REAL) AS Profit
            FROM sales WHERE status='COMPLETED'
              AND sale_date >= datetime('now','localtime','-12 months')
            GROUP BY Month ORDER BY Month").ToList());

    /// <summary>Sales grouped by customer.</summary>
    public List<(string Customer, int Count, decimal Total, string LastPurchase)> SalesByCustomer(DateTime from, DateTime to) =>
        _db.With(c => c.Query<(string, int, decimal, string)>(@"
            SELECT COALESCE(cu.name,'Umum') AS Customer, COUNT(*) AS Count,
            CAST(ROUND(SUM(s.total)/100.0,2) AS REAL) AS Total,
            COALESCE(MAX(date(s.sale_date)),'-') AS LastPurchase
            FROM sales s LEFT JOIN customers cu ON cu.id=s.customer_id
            WHERE s.status='COMPLETED' AND s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY s.customer_id ORDER BY Total DESC LIMIT 200",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    /// <summary>Promotion usage summary.</summary>
    public List<(string Code, string Name, int Uses, decimal TotalDiscount)> PromoUsage(DateTime from, DateTime to) =>
        _db.With(c => c.Query<(string, string, int, decimal)>(@"
            SELECT pr.code AS Code, pr.name AS Name, COUNT(pu.id) AS Uses,
            CAST(ROUND(COALESCE(SUM(pu.discount_amount),0)/100.0,2) AS REAL) AS TotalDiscount
            FROM promotions pr LEFT JOIN promotion_usage pu ON pu.promo_id=pr.id
            WHERE pr.is_active=1
            GROUP BY pr.id ORDER BY TotalDiscount DESC").ToList());

    /// <summary>Loyalty summary: top point holders.</summary>
    public List<(string Customer, decimal Points, decimal Lifetime)> LoyaltyTop(int limit = 20) =>
        _db.With(c => c.Query<(string, decimal, decimal)>(@"
            SELECT name AS Customer, points AS Points,
            CAST(ROUND(lifetime_spending/100.0,2) AS REAL) AS Lifetime
            FROM customers WHERE points > 0 ORDER BY points DESC LIMIT @l", new { l = limit }).ToList());

    public DashboardSummary Dashboard()
    {        var today = DateTime.Today;
        var fromToday = DbEx.Iso(today);
        var toToday = DbEx.Iso(today.AddDays(1).AddSeconds(-1));

        return _db.With(c =>
        {
            var s = new DashboardSummary();
            var todayAgg = c.QueryFirstOrDefault<(decimal Total, int Count, decimal Profit)>(
                @"SELECT COALESCE(CAST(ROUND(SUM(total)/100.0,2) AS REAL),0) AS Total,
                  COUNT(*) AS Count,
                  COALESCE(CAST(ROUND(SUM(total-cost_total)/100.0,2) AS REAL),0) AS Profit
                  FROM sales WHERE status='COMPLETED' AND sale_date >= @f AND sale_date <= @t2",
                new { f = fromToday, t2 = toToday });
            s.SalesToday = todayAgg.Total;
            s.TransactionsToday = todayAgg.Count;
            s.ProfitToday = todayAgg.Profit;

            s.LowStockCount = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM products WHERE is_active=1 AND stock <= min_stock");

            for (var i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var total = c.ExecuteScalar<decimal>(@"SELECT COALESCE(CAST(ROUND(SUM(total)/100.0,2) AS REAL),0)
                    FROM sales WHERE status='COMPLETED' AND date(sale_date)=date(@d)",
                    new { d = DbEx.Iso(d) });
                s.Trend.Add(new TrendPoint { Date = d, Total = total });
            }

            s.RecentSales = c.Query<Sale>(@"SELECT s.id AS Id, s.invoice_no AS InvoiceNo, s.sale_date AS SaleDate,
                COALESCE(cu.name,'Umum') AS CustomerName,
                {tot} AS Total, s.status AS Status,
                COALESCE((SELECT GROUP_CONCAT(sp.method, '+') FROM sale_payments sp WHERE sp.sale_id=s.id),'') AS CashierName
                FROM sales s LEFT JOIN customers cu ON cu.id=s.customer_id
                ORDER BY s.id DESC LIMIT 10"
                    .Replace("{tot}", DbEx.MoneyColumn("s.total"))).ToList();

            s.TopProducts = ProductSales(today.AddDays(-6), today, 5);
            return s;
        });
    }
}

public class PurchaseReportRow
{
    public DateTime Date { get; set; }
    public string PurchaseNo { get; set; } = "";
    public string SupplierInvoice { get; set; } = "";
    public string Supplier { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
}
