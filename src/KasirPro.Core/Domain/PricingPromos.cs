using System.Globalization;

namespace KasirPro.Core.Domain;

// ============ EAN/UPC check digit ============
public static class BarcodeMath
{
    /// <summary>Computes GS1 check digit for EAN-13/EAN-8/UPC-A (12+1).</summary>
    public static int CheckDigit(string digits)
    {
        if (string.IsNullOrWhiteSpace(digits) || digits.Any(c => !char.IsDigit(c)))
            throw new ArgumentException("Barcode harus berisi angka");
        // GS1: number the digits from the RIGHT (1-based); odd positions weight 3, even weight 1.
        var sum = 0;
        var reversed = digits.Reverse().ToArray();
        for (var i = 0; i < reversed.Length; i++)
            sum += (reversed[i] - '0') * (i % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10;
    }

    public static bool IsValidGtin(string gtin)
    {
        if (string.IsNullOrWhiteSpace(gtin)) return false;
        var len = gtin.Trim().Length;
        if (len is not (8 or 12 or 13 or 14)) return false;
        if (gtin.Any(c => !char.IsDigit(c))) return false;
        var body = gtin[..^1];
        return CheckDigit(body) == gtin[^1] - '0';
    }

    /// <summary>Builds an internal EAN-13: prefix 2 + 11 digits body + check digit.</summary>
    public static string InternalEan13(long sequence)
    {
        var body = "2" + sequence.ToString("D11", CultureInfo.InvariantCulture);
        if (body.Length != 12) throw new ArgumentException("Sequence terlalu besar");
        return body + CheckDigit(body);
    }
}

// ============ multi price resolution ============
public class PriceRule
{
    public long LevelId { get; set; }
    public decimal MinQty { get; set; }
    public decimal Price { get; set; }
}

public static class PriceResolver
{
    /// <summary>
    /// Deterministic price order: promotion special price > customer price level > qty tier > base price.
    /// Qty tiers must be sorted ascending; first matching tier (qty >= min) wins the LOWEST applicable band,
    /// so pass the highest matching band's price (bands evaluated from largest min-qty down).
    /// </summary>
    public static decimal Resolve(decimal basePrice,
        IEnumerable<PriceRule> qtyTiers,            // e.g. 6+ / 12+ / 50+
        PriceRule? customerLevel,                   // customer's price level price
        decimal promoPrice,                         // 0 = no promotion price
        decimal qty)
    {
        if (promoPrice > 0) return Money.Round(promoPrice);
        if (customerLevel != null && customerLevel.Price > 0) return Money.Round(customerLevel.Price);

        var best = qtyTiers?
            .Where(t => t.MinQty > 0 && qty >= t.MinQty && t.Price > 0)
            .OrderByDescending(t => t.MinQty)
            .FirstOrDefault();
        if (best != null) return Money.Round(best.Price);

        return Money.Round(basePrice);
    }
}

// ============ promotion engine ============
public enum PromoType { Percent = 0, Fixed = 1, BxGy = 2, SpecialPrice = 3 }
public enum PromoScope { All, Product, Category, Brand }

public class Promotion
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public PromoType Type { get; set; }
    public decimal Value { get; set; }             // percent / fixed amount / special price
    public PromoScope Scope { get; set; }
    public string ScopeRef { get; set; } = "";     // product code / category name / brand
    public decimal MinPurchase { get; set; }
    public decimal MinQty { get; set; }
    public decimal BuyQty { get; set; }
    public decimal GetQty { get; set; }
    public bool MemberOnly { get; set; }
    public int Priority { get; set; }
    public bool Stackable { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public HashSet<int> Days { get; set; } = new();  // 0=Sunday .. 6=Saturday
    public bool Active { get; set; } = true;
    public string CouponCode { get; set; } = "";
}

public class PromoResult
{
    public long PromoId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Discount { get; set; }
    public decimal FreeQty { get; set; }
    public List<CartLine> Lines { get; set; } = new();
}

public static class PromotionEngine
{
    public static bool IsScheduledNow(Promotion p, DateTime now)
    {
        if (!p.Active) return false;
        var date = now.Date;
        if (date < p.StartDate.Date || date > p.EndDate.Date) return false;
        if (p.Days.Count > 0 && !p.Days.Contains((int)now.DayOfWeek)) return false;
        if (p.StartTime.HasValue && now.TimeOfDay < p.StartTime.Value) return false;
        if (p.EndTime.HasValue && now.TimeOfDay > p.EndTime.Value) return false;
        return true;
    }

    public static bool MatchesScope(Promotion p, CartLine line) => p.Scope switch
    {
        PromoScope.All => true,
        PromoScope.Product => string.Equals(p.ScopeRef, line.Code, StringComparison.OrdinalIgnoreCase),
        PromoScope.Brand => string.Equals(p.ScopeRef, line.Brand, StringComparison.OrdinalIgnoreCase),
        PromoScope.Category => string.Equals(p.ScopeRef, line.Category, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>
    /// Evaluates promotions over the cart. Non-stackable: highest priority (then biggest
    /// discount) wins. Stackable promos combine. Returns applied results with discounts.
    /// </summary>
    public static List<PromoResult> Evaluate(IEnumerable<Promotion> promotions,
        IReadOnlyList<CartLine> lines, decimal invoiceSubtotal, bool isMember, DateTime now)
    {
        var applicable = promotions
            .Where(p => IsScheduledNow(p, now))
            .Where(p => !p.MemberOnly || isMember)
            .Where(p => invoiceSubtotal >= p.MinPurchase)
            .ToList();

        var results = new List<PromoResult>();
        foreach (var p in applicable)
        {
            var target = lines.Where(l => l.Qty > 0 && MatchesScope(p, l)).ToList();
            var targetQty = target.Sum(l => l.Qty);
            if (p.MinQty > 0 && targetQty < p.MinQty) continue;

            var result = new PromoResult { PromoId = p.Id, Code = p.Code, Name = p.Name };
            switch (p.Type)
            {
                case PromoType.Percent:
                {
                    var baseAmount = target.Sum(l => l.Qty > 0 ? SaleCalculator.LineSubtotal(l.Price, l.Qty, l.Discount) : 0);
                    result.Discount = Money.Round(baseAmount * p.Value / 100);
                    break;
                }
                case PromoType.Fixed:
                {
                    result.Discount = Money.Round(p.Value);
                    if (result.Discount > invoiceSubtotal) result.Discount = invoiceSubtotal;
                    break;
                }
                case PromoType.SpecialPrice:
                {
                    var baseAmount = target.Sum(l => l.Qty > 0 ? SaleCalculator.LineSubtotal(l.Price, l.Qty, l.Discount) : 0);
                    var promoAmount = target.Sum(l => Money.Round(p.Value * l.Qty));
                    result.Discount = Math.Max(0, Money.Round(baseAmount - promoAmount));
                    break;
                }
                case PromoType.BxGy:
                {
                    if (p.BuyQty <= 0 || p.GetQty <= 0) break;
                    var sets = Math.Floor(targetQty / p.BuyQty);
                    result.FreeQty = sets * p.GetQty;
                    var cheapestFirst = target.OrderBy(l => l.Price).ToList();
                    var remainingFree = result.FreeQty;
                    foreach (var l in cheapestFirst)
                    {
                        if (remainingFree <= 0) break;
                        var freeFromLine = Math.Min(l.Qty, remainingFree);
                        result.Lines.Add(new CartLine
                        {
                            ProductId = l.ProductId, Code = l.Code, Name = l.Name,
                            Price = 0, Qty = freeFromLine, Unit = l.Unit
                        });
                        remainingFree -= freeFromLine;
                    }
                    break;
                }
                case (PromoType)5: // Bundle handled as SPECIAL_PRICE at POS entry
                    break;
            }
            if (result.Discount > 0 || result.FreeQty > 0)
                results.Add(result);
        }

        // non-stackable promos: only the single best (priority, then discount) applies
        var nonStackableIds = applicable.Where(p => !p.Stackable).Select(p => p.Id).ToHashSet();
        var nonStackable = results.Where(r => nonStackableIds.Contains(r.PromoId)).ToList();
        if (nonStackable.Count > 1)
        {
            var best = nonStackable
                .OrderByDescending(r => applicable.First(p => p.Id == r.PromoId).Priority)
                .ThenByDescending(r => r.Discount)
                .First();
            results = results.Where(r => r.PromoId == best.PromoId || !nonStackableIds.Contains(r.PromoId)).ToList();
        }
        return results;
    }
}

// ============ moving average cost ============
public static class InventoryMath
{
    /// <summary>
    /// Moving average cost in cents. new_avg =
    /// (old_qty * old_avg + received_qty * purchase_cost) / (old_qty + received_qty).
    /// Integer cents, rounding away from zero.
    /// </summary>
    public static long MovingAverageCost(long oldQty, long oldAvgCents, long receivedQty, long purchaseCostCents)
    {
        var totalQty = oldQty + receivedQty;
        if (totalQty <= 0) return purchaseCostCents;
        var value = oldQty * oldAvgCents + receivedQty * purchaseCostCents;
        return (long)Math.Round((decimal)value / totalQty, MidpointRounding.AwayFromZero);
    }
}
