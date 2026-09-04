using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Promotion engine: percent, fixed, BXGY, special price, schedule, member-only, stackable.</summary>
public class PromotionEngineTests
{
    private static int _promoId = 1;

    private static Promotion Promo(PromoType type, decimal value, PromoScope scope = PromoScope.All,
        string scopeRef = "", decimal minPurchase = 0, decimal minQty = 0,
        decimal buy = 0, decimal get = 0, bool memberOnly = false, bool stackable = false,
        DateTime? start = null, DateTime? end = null, HashSet<int>? days = null) => new()
    {
        Id = _promoId++, Code = "P" + _promoId, Name = "Promo", Type = type, Value = value, Scope = scope, ScopeRef = scopeRef,
        MinPurchase = minPurchase, MinQty = minQty, BuyQty = buy, GetQty = get, MemberOnly = memberOnly,
        Stackable = stackable,
        StartDate = start ?? new DateTime(2026, 1, 1),
        EndDate = end ?? new DateTime(2026, 12, 31),
        Days = days ?? new HashSet<int> { 0, 1, 2, 3, 4, 5, 6 },
        Active = true
    };

    private static readonly List<CartLine> Lines = new()
    {
        new() { ProductId = 1, Code = "A", Name = "A", Price = 10000, Qty = 2, Category = "Minuman", Brand = "BrandX" },
        new() { ProductId = 2, Code = "B", Name = "B", Price = 5000, Qty = 4, Category = "Snack", Brand = "BrandY" }
    };

    [Fact]
    public void Percent_Discount_OverScopedProducts()
    {
        var p = Promo(PromoType.Percent, 10, PromoScope.Category, "Minuman");
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, new DateTime(2026, 6, 15, 10, 0, 0));
        Assert.Single(results);
        Assert.Equal(2000, results[0].Discount); // 10% of 20000 (A only)
    }

    [Fact]
    public void Fixed_Discount_CappedAtSubtotal()
    {
        var p = Promo(PromoType.Fixed, 50000);
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, DateTime.Now);
        Assert.Equal(40000, results[0].Discount);
    }

    [Fact]
    public void SpecialPrice_Discount_IsDifference()
    {
        var p = Promo(PromoType.SpecialPrice, 4000, PromoScope.Product, "A"); // A jadi 4000
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, DateTime.Now);
        Assert.Equal(12000, results[0].Discount); // (10000-4000) x 2
    }

    [Fact]
    public void BxGy_FreeItems_FromCheapest()
    {
        var p = Promo(PromoType.BxGy, 0, PromoScope.Product, "B", buy: 3, get: 1);
        var lines = new List<CartLine> { new() { ProductId = 2, Code = "B", Price = 5000, Qty = 6 } };
        var results = PromotionEngine.Evaluate(new[] { p }, lines, 30000, false, DateTime.Now);
        Assert.Equal(2, results[0].FreeQty);
        Assert.Equal(2, results[0].Lines[0].Qty);
    }

    [Fact]
    public void MinPurchase_BlocksPromo()
    {
        var p = Promo(PromoType.Fixed, 5000, minPurchase: 50000);
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, DateTime.Now);
        Assert.Empty(results);
    }

    [Fact]
    public void MinQty_BlocksPromo()
    {
        var p = Promo(PromoType.Percent, 10, minQty: 10);
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, DateTime.Now);
        Assert.Empty(results);
    }

    [Fact]
    public void MemberOnly_BlocksNonMember()
    {
        var p = Promo(PromoType.Percent, 10, memberOnly: true);
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, DateTime.Now);
        Assert.Empty(results);
        var ok = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, true, DateTime.Now);
        Assert.Single(ok);
    }

    [Fact]
    public void ExpiredPromo_NotApplied()
    {
        var p = Promo(PromoType.Percent, 10, start: new DateTime(2025, 1, 1), end: new DateTime(2025, 12, 31));
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, new DateTime(2026, 6, 15));
        Assert.Empty(results);
    }

    [Fact]
    public void DayOfWeek_FiltersPromo()
    {
        var p = Promo(PromoType.Percent, 10, days: new HashSet<int> { 0 }); // Sunday only
        // 2026-06-15 is Monday
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, new DateTime(2026, 6, 15));
        Assert.Empty(results);
        var sunday = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, new DateTime(2026, 6, 21));
        Assert.Single(sunday);
    }

    [Fact]
    public void HappyHour_TimeWindow_Applies()
    {
        var p = Promo(PromoType.Percent, 10);
        p.StartTime = new TimeSpan(14, 0, 0);
        p.EndTime = new TimeSpan(16, 0, 0);
        var at15 = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, new DateTime(2026, 6, 15, 15, 0, 0));
        Assert.Single(at15);
        var at18 = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, new DateTime(2026, 6, 15, 18, 0, 0));
        Assert.Empty(at18);
    }

    [Fact]
    public void NonStackable_BiggestDiscountWins()
    {
        var big = Promo(PromoType.Fixed, 8000); big.Priority = 10;
        var small = Promo(PromoType.Fixed, 2000); small.Priority = 1;
        var results = PromotionEngine.Evaluate(new[] { big, small }, Lines, 40000, false, DateTime.Now);
        Assert.Single(results);
        Assert.Equal(8000, results[0].Discount);
    }

    [Fact]
    public void ScopeBrand_OnlyMatchingLine()
    {
        var p = Promo(PromoType.Percent, 50, PromoScope.Brand, "BrandY");
        var results = PromotionEngine.Evaluate(new[] { p }, Lines, 40000, false, DateTime.Now);
        Assert.Equal(10000, results[0].Discount); // 50% of 5000x4
    }
}

/// <summary>Multi price: deterministic resolution + qty tiers + customer level.</summary>
public class PriceResolverTests
{
    [Fact]
    public void BasePrice_WhenNothingElse()
    {
        Assert.Equal(10000, PriceResolver.Resolve(10000, null, null, 0, 1));
    }

    [Fact]
    public void CustomerLevel_BeatsBase()
    {
        Assert.Equal(9000, PriceResolver.Resolve(10000, null, new PriceRule { Price = 9000 }, 0, 1));
    }

    [Fact]
    public void QtyTier_BeatsBase_WhenReached()
    {
        var tiers = new List<PriceRule>
        {
            new() { MinQty = 6, Price = 8500 },
            new() { MinQty = 12, Price = 8000 }
        };
        Assert.Equal(8500, PriceResolver.Resolve(10000, tiers, null, 0, 6));
        Assert.Equal(8000, PriceResolver.Resolve(10000, tiers, null, 0, 12));
        Assert.Equal(8000, PriceResolver.Resolve(10000, tiers, null, 0, 50));
        Assert.Equal(10000, PriceResolver.Resolve(10000, tiers, null, 0, 5)); // below tier
    }

    [Fact]
    public void CustomerLevel_BeatsQtyTier()
    {
        var tiers = new List<PriceRule> { new() { MinQty = 6, Price = 8500 } };
        Assert.Equal(9000, PriceResolver.Resolve(10000, tiers, new PriceRule { Price = 9000 }, 0, 12));
    }

    [Fact]
    public void PromoPrice_HighestPriority()
    {
        var tiers = new List<PriceRule> { new() { MinQty = 6, Price = 8500 } };
        Assert.Equal(7000, PriceResolver.Resolve(10000, tiers, new PriceRule { Price = 9000 }, 7000, 12));
    }
}

/// <summary>EAN/UPC check digit + internal barcode generation.</summary>
public class BarcodeMathTests
{
    [Fact]
    public void Ean13_CheckDigit_Known()
    {
        // authoritative example barcode: 4006381333931
        Assert.Equal(1, BarcodeMath.CheckDigit("400638133393"));
        Assert.True(BarcodeMath.IsValidGtin("4006381333931"));
        Assert.False(BarcodeMath.IsValidGtin("4006381333932"));
    }

    [Fact]
    public void Ean8_CheckDigit()
    {
        var body = "2401333";
        var valid = body + BarcodeMath.CheckDigit(body);
        Assert.True(BarcodeMath.IsValidGtin(valid));
        Assert.Equal(8, valid.Length);
    }

    [Fact]
    public void InvalidGtin_Rejected()
    {
        Assert.False(BarcodeMath.IsValidGtin("8992753123459")); // wrong check digit
        Assert.False(BarcodeMath.IsValidGtin("12345"));         // wrong length
        Assert.False(BarcodeMath.IsValidGtin(""));              // empty
        Assert.False(BarcodeMath.IsValidGtin("89927531234a"));  // non-digit
    }

    [Fact]
    public void InternalEan13_GeneratesValid()
    {
        var b1 = BarcodeMath.InternalEan13(1);
        var b2 = BarcodeMath.InternalEan13(99999999999);
        Assert.Equal(13, b1.Length);
        Assert.StartsWith("2", b1);
        Assert.True(BarcodeMath.IsValidGtin(b1));
        Assert.True(BarcodeMath.IsValidGtin(b2));
        Assert.NotEqual(b1, b2);
    }

    [Fact]
    public void ScaleBarcode_Parse()
    {
        var r = ScaleBarcode.TryParse("2100001123456", new[] { "21" }, 1000);
        Assert.NotNull(r);
        Assert.Equal("1", r!.Barcode);
        Assert.Equal(12.345m, r.Qty);
        Assert.Null(ScaleBarcode.TryParse("8992753123458", new[] { "21" }, 1000));
    }
}

/// <summary>Moving average cost in integer cents.</summary>
public class MovingAverageTests
{
    [Fact]
    public void Basic_Average()
    {
        // 10 @ 5000 + 10 @ 6000 = avg 5500
        Assert.Equal(550000, InventoryMath.MovingAverageCost(10, 500000, 10, 600000));
    }

    [Fact]
    public void Uneven_Average_RoundsAwayFromZero()
    {
        // 1 @ 100 + 2 @ 101 = 302/3 = 100.67 -> 101
        Assert.Equal(101, InventoryMath.MovingAverageCost(1, 100, 2, 101));
    }

    [Fact]
    public void ZeroOldStock_TakesNewCost()
    {
        Assert.Equal(75000, InventoryMath.MovingAverageCost(0, 50000, 10, 75000));
    }
}

