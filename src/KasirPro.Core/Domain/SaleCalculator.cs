namespace KasirPro.Core.Domain;

public class CartLine
{
    public long ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public decimal Stock { get; set; }
    public string Unit { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Discount { get; set; }
    public decimal Subtotal => Money.Round((Price * Qty) - Discount);
}

public class CartTotals
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    /// <summary>Tax shown on the receipt (0 for inclusive or disabled).</summary>
    public decimal Tax { get; set; }
    public decimal GrandTotal { get; set; }
}

public class TaxConfig
{
    public bool Enabled { get; set; }
    public decimal RatePercent { get; set; } = 11;
    public bool Inclusive { get; set; } = true;

    public static TaxConfig FromSettings(string enabled, string rate, string inclusive)
    {
        var parsed = decimal.TryParse(rate, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 11m;
        return new TaxConfig
        {
            Enabled = enabled == "1",
            RatePercent = parsed,
            Inclusive = inclusive != "0"
        };
    }
}

public static class TaxCalculator
{
    /// <summary>
    /// Tax for one line given product-level override. Exclusive adds on top;
    /// Inclusive extracts from the price (display only).
    /// </summary>
    public static decimal LineTax(decimal lineSubtotal, string productTaxMode, TaxConfig cfg)
    {
        if (!cfg.Enabled || cfg.RatePercent <= 0) return 0;
        var mode = productTaxMode switch
        {
            TaxMode.None => TaxMode.None,
            TaxMode.Inclusive => TaxMode.Inclusive,
            TaxMode.Exclusive => TaxMode.Exclusive,
            _ => cfg.Inclusive ? TaxMode.Inclusive : TaxMode.Exclusive
        };
        if (mode == TaxMode.None) return 0;
        return mode == TaxMode.Exclusive
            ? Money.Round(lineSubtotal * cfg.RatePercent / 100)
            : Money.Round(lineSubtotal - lineSubtotal / (1 + cfg.RatePercent / 100));
    }
}

public static class SaleCalculator
{
    public static decimal LineSubtotal(decimal price, decimal qty, decimal discountPerLine) =>
        Money.Round(Money.Round(price * qty) - discountPerLine);

    public static CartTotals Calculate(IEnumerable<CartLine> lines, decimal invoiceDiscount) =>
        Calculate(lines, invoiceDiscount, null);

    /// <summary>Full totals with optional tax support. Exclusive adds tax; inclusive extracts it (display only).</summary>
    public static CartTotals Calculate(IEnumerable<CartLine> lines, decimal invoiceDiscount, TaxConfig? cfg, string defaultTaxMode = TaxMode.Default)
    {
        decimal subtotal = 0m;
        foreach (var l in lines)
        {
            subtotal += l.Qty > 0 ? SaleCalculator.LineSubtotal(l.Price, l.Qty, l.Discount) : 0m;
        }
        subtotal = Money.Round(subtotal);
        var discount = Money.Round(invoiceDiscount);
        if (discount < 0) discount = 0;
        if (discount > subtotal) discount = subtotal;

        decimal addedTax = 0;      // exclusive: charged on top of prices
        decimal extractedTax = 0;  // inclusive: already inside prices (display only)
        if (cfg != null && cfg.Enabled)
        {
            foreach (var l in lines)
            {
                if (l.Qty <= 0) continue;
                var lineSub = SaleCalculator.LineSubtotal(l.Price, l.Qty, l.Discount);
                if (discount > 0 && subtotal > 0)
                    lineSub = Money.Round(lineSub * (subtotal - discount) / subtotal);

                var mode = defaultTaxMode switch
                {
                    TaxMode.None => TaxMode.None,
                    TaxMode.Inclusive => TaxMode.Inclusive,
                    TaxMode.Exclusive => TaxMode.Exclusive,
                    _ => cfg.Inclusive ? TaxMode.Inclusive : TaxMode.Exclusive
                };
                var t = TaxCalculator.LineTax(lineSub, mode, cfg);
                if (mode == TaxMode.Exclusive) addedTax += t; else extractedTax += t;
            }
        }

        var grandTotal = Money.Round(subtotal - discount + addedTax);
        return new CartTotals
        {
            Subtotal = subtotal,
            Discount = discount,
            Tax = cfg != null && cfg.Inclusive ? extractedTax : addedTax,
            GrandTotal = grandTotal
        };
    }
}

public static class ChangeCalculator
{
    public static decimal Change(decimal grandTotal, IEnumerable<(PaymentMethod Method, decimal Amount)> payments)
    {
        var paid = Money.Round(payments.Sum(p => Money.Round(p.Amount)));
        var total = Money.Round(grandTotal);
        var change = Money.Round(paid - total);
        return change > 0 ? change : 0m;
    }

    public static decimal PaidSoFar(IEnumerable<(PaymentMethod Method, decimal Amount)> payments) =>
        Money.Round(payments.Sum(p => Money.Round(p.Amount)));

    public static decimal Remaining(decimal grandTotal, IEnumerable<(PaymentMethod Method, decimal Amount)> payments)
    {
        var rem = Money.Round(Money.Round(grandTotal) - PaidSoFar(payments));
        return rem > 0 ? rem : 0m;
    }
}
