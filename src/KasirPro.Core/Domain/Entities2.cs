namespace KasirPro.Core.Domain;

public class Sale
{
    public long Id { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime SaleDate { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = "Umum";
    public long UserId { get; set; }
    public string CashierName { get; set; } = "";
    public long CashSessionId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal CostTotal { get; set; }
    public string Status { get; set; } = "COMPLETED";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SaleItem> Items { get; set; } = new();
    public List<SalePayment> Payments { get; set; } = new();

    /// <summary>Unpaid remainder tracked in sale_debts (0 when fully paid).</summary>
    public decimal Outstanding { get; set; }

    /// <summary>Tax amount charged on this sale (0 when disabled/inclusive-not-shown).</summary>
    public decimal Tax { get; set; }
}

public class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public decimal Discount { get; set; }
    public decimal Subtotal { get; set; }
}

public class SalePayment
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class SaleReturn
{
    public long Id { get; set; }
    public string ReturnNo { get; set; } = "";
    public long SaleId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime ReturnDate { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public decimal Total { get; set; }
    public string Reason { get; set; } = "";
    public List<SaleReturnItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class SaleReturnItem
{
    public long Id { get; set; }
    public long SaleReturnId { get; set; }
    public long SaleItemId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal Subtotal { get; set; }
}

public class Purchase
{
    public long Id { get; set; }
    public string PurchaseNo { get; set; } = "";
    public string SupplierInvoiceNo { get; set; } = "";
    public DateTime PurchaseDate { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "COMPLETED";
    /// <summary>UNPAID / PARTIAL / PAID (hutang supplier).</summary>
    public string PaymentStatus { get; set; } = "PAID";
    public decimal PaidAmount { get; set; }
    public string Notes { get; set; } = "";
    public List<PurchaseItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PurchaseItem
{
    public long Id { get; set; }
    public long PurchaseId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Cost { get; set; }
    public decimal Subtotal { get; set; }
}

public class PurchaseReturn
{
    public long Id { get; set; }
    public string ReturnNo { get; set; } = "";
    public long PurchaseId { get; set; }
    public string PurchaseNo { get; set; } = "";
    public DateTime ReturnDate { get; set; }
    public long SupplierId { get; set; }
    public long UserId { get; set; }
    public decimal Total { get; set; }
    public string Reason { get; set; } = "";
    public List<PurchaseReturnItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class PurchaseReturnItem
{
    public long Id { get; set; }
    public long PurchaseReturnId { get; set; }
    public long PurchaseItemId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Cost { get; set; }
    public decimal Subtotal { get; set; }
}
