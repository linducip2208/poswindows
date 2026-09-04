using Dapper;
using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Permission matrix theories across all roles.</summary>
public class PermissionMatrixTests
{
    public static IEnumerable<object[]> Roles()
    {
        yield return new object[] { "Owner" };
        yield return new object[] { "Admin" };
        yield return new object[] { "Supervisor" };
        yield return new object[] { "Cashier" };
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void EveryRole_HasPosUse(string role)
    {
        var perms = PermissionSeed.Matrix.First(m => m.Role == role).Perms;
        Assert.Contains("POS.USE", perms);
        Assert.Contains("SALE.CREATE", perms);
    }

    [Fact]
    public void Owner_And_Admin_HaveFullCatalog()
    {
        foreach (var role in new[] { "Owner", "Admin" })
        {
            var perms = PermissionSeed.Matrix.First(m => m.Role == role).Perms;
            Assert.Equal(PermissionSeed.Catalog.Length, perms.Length);
        }
    }

    [Theory]
    [InlineData("USER.MANAGE")]
    [InlineData("ROLE.MANAGE")]
    [InlineData("BACKUP.RESTORE")]
    [InlineData("SETTINGS.MANAGE")]
    [InlineData("PRODUCT.DELETE")]
    [InlineData("SALE.VOID")]
    public void Cashier_Never_HasDangerousPermissions(string perm)
    {
        var perms = PermissionSeed.Matrix.First(m => m.Role == "Cashier").Perms;
        Assert.DoesNotContain(perm, perms);
    }

    [Theory]
    [InlineData("SALE.VOID")]
    [InlineData("STOCK.ADJUST")]
    [InlineData("DRAWER.OPEN")]
    [InlineData("PRICE.OVERRIDE")]
    [InlineData("BACKUP.CREATE")]
    public void Supervisor_HasOperationalControls(string perm)
    {
        var perms = PermissionSeed.Matrix.First(m => m.Role == "Supervisor").Perms;
        Assert.Contains(perm, perms);
    }

    [Fact]
    public void Catalog_HasNoDuplicates()
    {
        var codes = PermissionSeed.Catalog.Select(c => c.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void SeedSql_EscapesSingleQuotes()
    {
        var sql = string.Join("\n", PermissionSeed.PermissionInserts);
        Assert.DoesNotContain("'''", sql); // no broken escapes
    }
}

/// <summary>Money rounding & cart calculation edge cases (theories).</summary>
public class MoneyEdgeTests
{
    [Theory]
    [InlineData(0.005, 0.01)]
    [InlineData(1.245, 1.25)]
    [InlineData(2.675, 2.68)]
    [InlineData(-1.245, -1.25)]
    public void Round_AwayFromZero(decimal input, decimal expected)
    {
        Assert.Equal(expected, Money.Round(input));
    }

    [Theory]
    [InlineData(10000, 3, 30000)]
    [InlineData(3500, 0, 0)]
    [InlineData(3555.55, 2, 7111.1)]
    public void LineSubtotal_QtyTimesPrice(decimal price, decimal qty, decimal expected)
    {
        Assert.Equal(expected, SaleCalculator.LineSubtotal(price, qty, 0));
    }

    [Fact]
    public void Cart_WithMixedLines()
    {
        var lines = new List<CartLine>
        {
            new() { Price = 15000, Qty = 2 },
            new() { Price = 7500, Qty = 3 },
            new() { Price = 999, Qty = 1 }
        };
        var totals = SaleCalculator.Calculate(lines, 1000);
        Assert.Equal(53499, totals.Subtotal);
        Assert.Equal(52499, totals.GrandTotal);
    }

    [Fact]
    public void FreeItems_ZeroPrice_NoContribution()
    {
        var lines = new List<CartLine>
        {
            new() { Price = 10000, Qty = 2 },
            new() { Price = 0, Qty = 1 } // free promo item
        };
        var totals = SaleCalculator.Calculate(lines, 0);
        Assert.Equal(20000, totals.GrandTotal);
    }
}

/// <summary>Split payment & change theories.</summary>
public class SplitPaymentTheories
{
    [Theory]
    [InlineData(100000, 40000, 30000, 30000)] // cash+qris+debit
    [InlineData(50000, 25000, 25000, 0)]
    public void Remaining_AfterSplits(decimal total, decimal p1, decimal p2, decimal expectedRemaining)
    {
        var payments = new List<(PaymentMethod, decimal)> { (PaymentMethod.Cash, p1), (PaymentMethod.Qris, p2) };
        Assert.Equal(expectedRemaining, ChangeCalculator.Remaining(total, payments));
    }

    [Theory]
    [InlineData(300000, 500000, 200000)]
    [InlineData(300000, 300000, 0)]
    public void Change_NeverNegative(decimal total, decimal paid, decimal expected)
    {
        Assert.Equal(expected, ChangeCalculator.Change(total, new[] { (PaymentMethod.Cash, paid) }));
    }

    [Fact]
    public void PointsPlusCash_CoversTotal()
    {
        var payments = new List<(PaymentMethod, decimal)> { (PaymentMethod.Points, 5000), (PaymentMethod.Cash, 15000) };
        Assert.Equal(0, ChangeCalculator.Remaining(20000, payments));
        Assert.Equal(0, ChangeCalculator.Change(20000, payments));
    }
}

/// <summary>Product/stock data integrity after operations.</summary>
public class InventoryIntegrityTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;

    public InventoryIntegrityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-inv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
    }

    private Product NewProduct(decimal stock = 50)
    {
        var id = _services.Products.SaveProduct(new Product
        {
            Code = "IG" + Guid.NewGuid().ToString("N")[..6], Name = "Integrity Prod",
            UnitId = 1, PurchasePrice = 4000, SellingPrice = 10000, Stock = stock
        }, Array.Empty<string>(), _admin.Id, "admin");
        return _services.Products.Get(id)!;
    }

    [Fact]
    public void Stock_AlwaysEqualsLedgerSum()
    {
        var p = NewProduct(stock: 30);
        _services.Inventory.StockIn(p.Id, 10, "restock", _admin.Id, "admin");
        _services.Inventory.StockOut(p.Id, 5, "rusak", _admin.Id, "admin");

        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = p.Id, ProductCode = p.Code, ProductName = p.Name, Qty = 7, Price = 10000, Cost = 4000 } },
            Subtotal = 70000, Total = 70000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 70000 });
        var done = _services.Sales.CompleteSale(sale);
        var item = _services.Sales.GetById(done.Id)!.Items[0];
        _services.Sales.CreateReturn(done.Id, new List<(long, decimal)> { (item.Id, 2) }, "retur", _admin.Id, "admin");

        // ledger sum must equal products.stock
        var ledgerSum = _db.With(c => c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(CASE WHEN direction='IN' THEN qty ELSE -qty END),0)
            FROM stock_movements WHERE product_id=@id", new { id = p.Id }));
        var actual = _db.With(c => c.ExecuteScalar<decimal>("SELECT stock FROM products WHERE id=@id", new { id = p.Id }));
        Assert.Equal(ledgerSum, actual);
        Assert.Equal(30, actual); // 30+10-5-7+2
    }

    [Fact]
    public void OpeningStock_CreatesInitialMovement()
    {
        var p = NewProduct(stock: 25);
        var movements = _services.Inventory.Movements("", p.Id, StockRef.Opening, DateTime.Today, DateTime.Today, 1, 10);
        Assert.True(movements.TotalItems >= 1);
        Assert.Equal(25, movements.Items.Last().Qty);
    }

    [Fact]
    public void Adjust_ToExactValue_RecordsDelta()
    {
        var p = NewProduct(stock: 10);
        _services.Inventory.Adjust(p.Id, 4, "stok fisik 4", _admin.Id, "admin");
        Assert.Equal(4, _services.Products.Get(p.Id)!.Stock);
        var mv = _services.Inventory.Movements("", p.Id, StockRef.Adjustment, DateTime.Today, DateTime.Today, 1, 5);
        Assert.Equal(6, mv.Items.First().Qty); // OUT 6
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

