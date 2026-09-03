namespace KasirPro.Core.Domain;

public class StockMovement
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ReferenceType { get; set; } = "";
    public long ReferenceId { get; set; }
    public StockDirection Direction { get; set; }
    public decimal Qty { get; set; }
    public decimal StockAfter { get; set; }
    public string Notes { get; set; } = "";
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class StockOpname
{
    public long Id { get; set; }
    public string OpnameNo { get; set; } = "";
    public DateTime OpnameDate { get; set; }
    public string Status { get; set; } = "DRAFT";
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<StockOpnameItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class StockOpnameItem
{
    public long Id { get; set; }
    public long StockOpnameId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal SystemQty { get; set; }
    public decimal CountedQty { get; set; }
    public decimal Difference { get; set; }
    public bool Adjusted { get; set; }
}

public class CashSession
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ClosingCashExpected { get; set; }
    public decimal ClosingCashActual { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal DebtPayments { get; set; }
    public decimal Difference { get; set; }
    public string Status { get; set; } = "OPEN";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CashMovement
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public string Type { get; set; } = "";
    public StockDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string ReferenceType { get; set; } = "";
    public long ReferenceId { get; set; }
    public string Notes { get; set; } = "";
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public long EntityId { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
