using Dapper;
using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Batch-3 features: tax, hold, X/Z, loyalty, warehouse, supplier payable, scale barcode.</summary>
public class RetailFeaturesTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;

    public RetailFeaturesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-retail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
    }

    private Product NewProduct(decimal price = 10000, decimal cost = 5000)
    {
        var id = _services.Products.SaveProduct(new Product
        {
            Code = "R" + Guid.NewGuid().ToString("N")[..8], Name = "Retail Prod",
            UnitId = 1, PurchasePrice = cost, SellingPrice = price, Stock = 100
        }, Array.Empty<string>(), _admin.Id, "admin");
        return _services.Products.Get(id)!;
    }

    private Sale SimpleSale(Product p, decimal qty, decimal paidAmount, PaymentMethod method)
    {
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = p.Id, ProductCode = p.Code, ProductName = p.Name, Qty = qty, Price = p.SellingPrice, Cost = p.PurchasePrice } },
            Subtotal = Money.Round(p.SellingPrice * qty), Total = Money.Round(p.SellingPrice * qty)
        };
        sale.Payments.Add(new SalePayment { Method = method, Amount = paidAmount });
        return sale;
    }

    // ---------- TAX ----------

    [Fact]
    public void Tax_Exclusive_AddsOnTop()
    {
        var lines = new List<CartLine> { new() { Price = 10000, Qty = 2 } }; // 20000
        var cfg = new TaxConfig { Enabled = true, RatePercent = 11, Inclusive = false };
        var totals = SaleCalculator.Calculate(lines, 0, cfg);
        Assert.Equal(20000, totals.Subtotal);
        Assert.Equal(2200, totals.Tax);
        Assert.Equal(22200, totals.GrandTotal);
    }

    [Fact]
    public void Tax_Inclusive_ExtractedNotAdded()
    {
        var lines = new List<CartLine> { new() { Price = 11100, Qty = 1 } };
        var cfg = new TaxConfig { Enabled = true, RatePercent = 11, Inclusive = true };
        var totals = SaleCalculator.Calculate(lines, 0, cfg);
        Assert.Equal(11100, totals.GrandTotal);         // customer pays same
        Assert.Equal(1100, totals.Tax);                  // tax inside
    }

    [Fact]
    public void Tax_Discount_ReducesTaxBase()
    {
        var lines = new List<CartLine> { new() { Price = 10000, Qty = 2 } };
        var cfg = new TaxConfig { Enabled = true, RatePercent = 10, Inclusive = false };
        var totals = SaleCalculator.Calculate(lines, 10000, cfg); // 50% off
        Assert.Equal(1000, totals.Tax); // 10% of 10000
    }

    [Fact]
    public void Tax_ProductOverride_None_WinsOverDefault()
    {
        var lineSub = 10000m;
        Assert.Equal(0, TaxCalculator.LineTax(lineSub, TaxMode.None,
            new TaxConfig { Enabled = true, RatePercent = 11, Inclusive = false }));
        Assert.Equal(1100, TaxCalculator.LineTax(lineSub, TaxMode.Exclusive,
            new TaxConfig { Enabled = true, RatePercent = 11, Inclusive = true }));
    }

    [Fact]
    public void Sale_PersistsTax()
    {
        _services.Settings.Set("tax_enabled", "1");
        _services.Settings.Set("tax_rate_percent", "10");
        _services.Settings.Set("tax_inclusive", "0");
        var p = NewProduct();
        var sale = SimpleSale(p, 2, 22000, PaymentMethod.Cash); // 20000 + 10%
        sale.Tax = 2000;
        var done = _services.Sales.CompleteSale(sale);
        Assert.Equal(2000, _services.Sales.GetById(done.Id)!.Tax);
    }

    // ---------- HOLD ----------

    [Fact]
    public void Hold_SaveRecallDelete()
    {
        var lines = new List<CartLine>
        {
            new() { ProductId = 1, Code = "A", Name = "Barang A", Price = 5000, Qty = 2 },
            new() { ProductId = 2, Code = "B", Name = "Barang B", Price = 3000, Qty = 1 }
        };
        var id = _services.Holds.Save("Bu Ani", _admin.Id, 0, "", 0, lines);
        Assert.True(id > 0);

        var list = _services.Holds.List(_admin.Id);
        Assert.Single(list);
        Assert.Equal("Bu Ani", list[0].Label);
        Assert.Equal(13000, list[0].Total);

        var loaded = _services.Holds.Get(id)!;
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal(5000, loaded.Items[0].Price);

        _services.Holds.Delete(id);
        Assert.Empty(_services.Holds.List(_admin.Id));
    }

    // ---------- X/Z REPORT ----------

    [Fact]
    public void XZReport_SumsSalesPaymentsAndCash()
    {
        var session = _services.Cash.OpenSession(_admin.Id, "admin", 50000);
        var p1 = NewProduct();
        var s1 = SimpleSale(p1, 2, 20000, PaymentMethod.Cash);
        s1.CashSessionId = session!.Id;
        _services.Sales.CompleteSale(s1);
        var s2 = SimpleSale(p1, 1, 10000, PaymentMethod.Qris);
        s2.CashSessionId = session.Id;
        _services.Sales.CompleteSale(s2);

        var x = _services.XZReports.Generate(session.Id, "X", _admin.Id, "admin");
        Assert.Equal(2, x.SalesCount);
        Assert.Equal(30000, x.NetSales);
        Assert.Equal(20000, x.Payments["Cash"]);
        Assert.Equal(10000, x.Payments["Qris"]);
        Assert.Equal(70000, x.ExpectedCash); // 50000 + 20000 cash sales

        // Z finalizes
        var z = _services.XZReports.Generate(session.Id, "Z", _admin.Id, "admin");
        Assert.StartsWith("Z-", z.ReportNo);
        Assert.Equal(30000, z.NetSales);
    }

    // ---------- LOYALTY ----------

    [Fact]
    public void Loyalty_EarnAndRedeem()
    {
        _services.Settings.Set("loyalty_enabled", "1");
        _services.Settings.Set("loyalty_earn_per_1000", "1");
        _services.Settings.Set("loyalty_point_value", "100");

        Assert.Equal(15, _services.Loyalty.EarnFor(15000));
        Assert.Equal(0, _services.Loyalty.EarnFor(999));

        var cust = _services.Products.SaveCustomer(
            new KasirPro.Core.Domain.Customer { Name = "Member" }, _admin.Id, "admin");
        _services.Loyalty.ApplyForSale(1, cust.Id, 25000, 0, _admin.Id, "admin");
        Assert.Equal(25, _services.Loyalty.GetPoints(cust.Id));

        // redeem 1000 rupiah = 10 points, then earn 5 for this 5000 sale
        _services.Loyalty.ApplyForSale(2, cust.Id, 5000, 1000, _admin.Id, "admin");
        Assert.Equal(20, _services.Loyalty.GetPoints(cust.Id)); // 25 - 10 + 5
    }

    // ---------- SCALE BARCODE ----------

    [Fact]
    public void ScaleBarcode_ParsesWeight()
    {
        var result = ScaleBarcode.TryParse("2100001123456", new[] { "21" }, 1000);
        Assert.NotNull(result);
        Assert.True(result!.FromScale);
        Assert.Equal("1", result.Barcode); // item code 00001 -> trimmed
        Assert.Equal(12.345m, result.Qty);

        Assert.Null(ScaleBarcode.TryParse("8991234567890", new[] { "21" }, 1000)); // wrong prefix
        Assert.Null(ScaleBarcode.TryParse("21", new[] { "21" }, 1000)); // too short
    }

    // ---------- WAREHOUSE ----------

    [Fact]
    public void Warehouse_Transfer_MovesStock()
    {
        var p = NewProduct();
        var wh2 = _services.Warehouses.SaveWarehouse("GUDANG2", _admin.Id, "admin");

        // seed 10 in default warehouse
        _db.With(c => c.Execute(@"INSERT INTO warehouse_stock (warehouse_id, product_id, qty) VALUES (1, @p, 10)",
            new { p = p.Id }));

        _services.Warehouses.Transfer(1, wh2, new List<(long, decimal)> { (p.Id, 4) }, "", _admin.Id, "admin");

        var wh1Stock = _services.Warehouses.Stock(1, "", 1, 50).Items.First(x => x.Item1 == p.Code).Item3;
        var wh2Stock = _services.Warehouses.Stock(wh2, "", 1, 50).Items.First(x => x.Item1 == p.Code).Item3;
        Assert.Equal(6, wh1Stock);
        Assert.Equal(4, wh2Stock);

        // overselling source warehouse fails
        Assert.Throws<InvalidOperationException>(() =>
            _services.Warehouses.Transfer(1, wh2, new List<(long, decimal)> { (p.Id, 99) }, "", _admin.Id, "admin"));
    }

    // ---------- SUPPLIER PAYABLE ----------

    [Fact]
    public void SupplierPayable_PurchaseOnCredit_ThenPayOff()
    {
        var p = NewProduct();
        var po = new Purchase
        {
            SupplierId = 0, Items = new() { new PurchaseItem { ProductId = p.Id, ProductCode = p.Code, ProductName = p.Name, Qty = 10, Cost = 5000 } }
        };
        var saved = _services.Purchases.CreatePurchase(po, _admin.Id, "admin");
        Assert.Equal(50000, saved.Total);
        Assert.Equal("PAID", saved.PaymentStatus); // default PAID

        // make it a credit purchase
        _db.With(c => c.Execute("UPDATE purchases SET payment_status='UNPAID', paid_amount=0 WHERE id=@id",
            new { id = saved.Id }));

        var outstanding = _services.SupplierPayments.Outstanding();
        Assert.Contains(outstanding, o => o.PurchaseId == saved.Id);

        var remaining = _services.SupplierPayments.Pay(saved.Id, 30000, PaymentMethod.Cash, "DP", _admin.Id, "admin");
        Assert.Equal(20000, remaining);

        var after = _services.SupplierPayments.Outstanding().First(o => o.PurchaseId == saved.Id);
        Assert.Equal("PARTIAL", after.Status);

        _services.SupplierPayments.Pay(saved.Id, 20000, PaymentMethod.Transfer, "lunas", _admin.Id, "admin");
        Assert.DoesNotContain(_services.SupplierPayments.Outstanding(), o => o.PurchaseId == saved.Id);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
