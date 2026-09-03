namespace KasirPro.Core.Domain;

/// <summary>Receivable (piutang) born from a partially-paid sale.</summary>
public class SaleDebt
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime SaleDate { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal OriginalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "OUTSTANDING";
    public DateTime? DueDate { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public decimal Remaining => Money.Round(OriginalAmount - PaidAmount);

    public List<DebtPayment> Payments { get; set; } = new();
}

/// <summary>Installment / settlement of a debt.</summary>
public class DebtPayment
{
    public long Id { get; set; }
    public long DebtId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public long CashSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
