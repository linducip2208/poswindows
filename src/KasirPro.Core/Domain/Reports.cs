namespace KasirPro.Core.Domain;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);
}

public class DateRangeFilter
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public static DateRangeFilter Today()
    {
        var d = DateTime.Today;
        return new DateRangeFilter { From = d, To = d };
    }
}

public class SaleReportRow
{
    public DateTime Date { get; set; }
    public string InvoiceNo { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Cashier { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public string Payment { get; set; } = "";
    public string Status { get; set; } = "";
}

public class ProductSalesRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal QtySold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Profit { get; set; }
}

public class ProfitRow
{
    public DateTime Date { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit { get; set; }
}

public class StockRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Stock { get; set; }
    public string Unit { get; set; } = "";
    public decimal MinStock { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal StockValue { get; set; }
}

public class StockMovementRow
{
    public DateTime Date { get; set; }
    public string Code { get; set; } = "";
    public string Product { get; set; } = "";
    public string ReferenceType { get; set; } = "";
    public long ReferenceId { get; set; }
    public string Direction { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal StockAfter { get; set; }
    public string User { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class CashReportRow
{
    public DateTime Date { get; set; }
    public string Session { get; set; } = "";
    public string Cashier { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal CashSales { get; set; }
    public decimal Expected { get; set; }
    public decimal Actual { get; set; }
    public decimal Difference { get; set; }
    public string Status { get; set; } = "";
}

public class CashierReportRow
{
    public string Cashier { get; set; } = "";
    public int Transactions { get; set; }
    public decimal Total { get; set; }
    public decimal Profit { get; set; }
}

public class ExpenseReportRow
{
    public DateTime Date { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string User { get; set; } = "";
}

public class DashboardSummary
{
    public decimal SalesToday { get; set; }
    public int TransactionsToday { get; set; }
    public decimal ProfitToday { get; set; }
    public decimal ExpensesToday { get; set; }
    public int LowStockCount { get; set; }
    public List<TrendPoint> Trend { get; set; } = new();
    public List<Sale> RecentSales { get; set; } = new();
    public List<ProductSalesRow> TopProducts { get; set; } = new();

    // extended widgets (v2.2)
    public decimal GrossSalesToday { get; set; }
    public decimal AvgBasket { get; set; }
    public decimal ItemsSoldToday { get; set; }
    public decimal CashSalesToday { get; set; }
    public decimal QrisSalesToday { get; set; }
    public decimal DebitSalesToday { get; set; }
    public decimal TransferSalesToday { get; set; }
    public decimal CreditSalesToday { get; set; }
    public decimal ReceivableOutstanding { get; set; }
    public decimal PayableOutstanding { get; set; }
    public decimal StockValue { get; set; }
    public bool OpenShift { get; set; }
    public string OpenShiftSince { get; set; } = "";
}

public class TrendPoint
{
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
}
