namespace KasirPro.Core.Domain;

public sealed class Expense
{
    public long Id { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string Category { get; set; } = "Lain-lain";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public long CashSessionId { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string Status { get; set; } = "POSTED";
    public DateTime CreatedAt { get; set; }
}
