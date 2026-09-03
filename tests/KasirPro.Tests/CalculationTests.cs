using KasirPro.Core.Domain;
using Xunit;

namespace KasirPro.Tests;

public class CalculationTests
{
    [Fact]
    public void SaleTotal_Calculation()
    {
        var lines = new List<CartLine>
        {
            new() { ProductId = 1, Price = 15000, Qty = 2 },
            new() { ProductId = 2, Price = 8750, Qty = 3 }
        };
        var totals = SaleCalculator.Calculate(lines, 0);
        Assert.Equal(56250, totals.Subtotal);
        Assert.Equal(56250, totals.GrandTotal);
    }

    [Fact]
    public void Discount_Calculation_ClampedToSubtotal()
    {
        var lines = new List<CartLine> { new() { Price = 10000, Qty = 1 } };
        var totals = SaleCalculator.Calculate(lines, 3000);
        Assert.Equal(7000, totals.GrandTotal);

        var totals2 = SaleCalculator.Calculate(lines, 99999);
        Assert.Equal(0, totals2.GrandTotal);
        Assert.Equal(10000, totals2.Discount);
    }

    [Fact]
    public void NegativeDiscount_Ignored()
    {
        var lines = new List<CartLine> { new() { Price = 10000, Qty = 1 } };
        var totals = SaleCalculator.Calculate(lines, -500);
        Assert.Equal(10000, totals.GrandTotal);
    }

    [Fact]
    public void CashChange_Calculation()
    {
        var change = ChangeCalculator.Change(50000, new[] { (PaymentMethod.Cash, 100000m) });
        Assert.Equal(50000, change);
    }

    [Fact]
    public void SplitPayment_Remaining()
    {
        var payments = new List<(PaymentMethod, decimal)>
        {
            (PaymentMethod.Cash, 200000),
            (PaymentMethod.Qris, 150000)
        };
        Assert.Equal(150000, ChangeCalculator.Remaining(500000, payments));
        payments.Add((PaymentMethod.Debit, 150000));
        Assert.Equal(0, ChangeCalculator.Remaining(500000, payments));
    }

    [Fact]
    public void InsufficientPayment_ChangeIsZero()
    {
        var change = ChangeCalculator.Change(50000, new[] { (PaymentMethod.Cash, 30000m) });
        Assert.Equal(0, change);
    }

    [Fact]
    public void Money_Rounding_AwayFromZero()
    {
        Assert.Equal(1.25m, Money.Round(1.245m));
        Assert.Equal(150500, Money.Round(150499.996m));
    }

    [Fact]
    public void LineSubtotal_PartialLineDiscount()
    {
        Assert.Equal(28000, SaleCalculator.LineSubtotal(15000, 2, 2000));
    }
}
