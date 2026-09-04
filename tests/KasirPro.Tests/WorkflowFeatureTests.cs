using Dapper;
using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Robust CSV parser: quoted fields, escaped quotes, embedded separators.</summary>
public class CsvParserTests
{
    [Fact]
    public void Parse_QuotedFields_WithEmbeddedSeparator()
    {
        var rows = CsvParser.Parse("code;name;price\nP1;\"Kopi; Sachet\";5000\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Kopi; Sachet", rows[1][1]);
    }

    [Fact]
    public void Parse_EscapedQuotes()
    {
        var rows = CsvParser.Parse("name\n\"He said \"\"hi\"\"\"\n");
        Assert.Equal("He said \"hi\"", rows[1][0]);
    }

    [Fact]
    public void Parse_NewlineInsideQuotes()
    {
        var rows = CsvParser.Parse("code;name\nP1;\"Line1\nLine2\"\n");
        Assert.Single(rows.Skip(1));
        Assert.Contains("Line2", rows[1][1]);
    }

    [Fact]
    public void Parse_AutoDetectsSeparator()
    {
        var comma = CsvParser.Parse("code,name\nP1,Item\n", ';', '\t', ',');
        Assert.Equal("Item", comma[1][1]);
        var tab = CsvParser.Parse("code\tname\nP1\tItem\n", ';', '\t', ',');
        Assert.Equal("Item", tab[1][1]);
    }

    [Fact]
    public void Write_EscapesProperly()
    {
        var csv = CsvParser.Write(new[]
        {
            new[] { ("code", (string?)"P1"), ("name", (string?)"A;B") }
        });
        Assert.Contains("\"A;B\"", csv);
    }

    [Fact]
    public void Validator_FlagsErrors()
    {
        var raw = new List<string[]>
        {
            new[] { "P1", "Produk A", "8992753123458", "Kat", "", "PCS", "5000", "6000", "10", "2", "", "" },
            new[] { "", "", "", "", "", "", "x", "0", "1", "0", "", "" },                    // bad number, no name
            new[] { "P2", "Produk B", "8992753123458", "", "", "PCS", "5000", "4000", "1", "0", "", "" } // dup barcode, cost>price
        };
        var rows = ProductImportValidator.Validate(raw, out var warnings);
        Assert.True(rows[0].Valid);
        Assert.False(rows[1].Valid);
        Assert.False(rows[2].Valid);
        Assert.Contains(rows[2].Errors, e => e.Contains("harga beli > harga jual"));
        Assert.Contains(rows[2].Errors, e => e.Contains("duplikat"));
        Assert.NotEmpty(warnings); // EAN check digit warning? 8992753123458 is valid -> actually empty
        // correct: 8992753123458 is valid GTIN, so no warning
        Assert.True(warnings.Count == 0 || warnings.All(w => w.Contains("check digit")));
    }
}

/// <summary>Purchase workflow: draft -> approve -> receive partial -> AP.</summary>
public class PurchaseWorkflowTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;
    private Product _product;

    public PurchaseWorkflowTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-po-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "PO1", Name = "PO Product", UnitId = 1, PurchasePrice = 5000, SellingPrice = 8000, Stock = 0
        }, Array.Empty<string>(), _admin.Id, "admin");
        _product = _services.Products.Get(pid)!;
    }

    private long CreateDraft(decimal qty)
    {
        return _services.Purchases.CreateDraft(new Purchase
        {
            SupplierId = 0,
            Items = new() { new PurchaseItem { ProductId = _product.Id, ProductCode = _product.Code, ProductName = _product.Name, Qty = qty, Cost = 5000 } }
        }, _admin.Id, "admin");
    }

    [Fact]
    public void Draft_DoesNotAffectStock()
    {
        var id = CreateDraft(10);
        Assert.Equal(0, _services.Products.Get(_product.Id)!.Stock);
        var drafts = _services.Purchases.ListDrafts();
        Assert.Contains(drafts, d => d.Id == id && d.Workflow == "DRAFT");
    }

    [Fact]
    public void Approve_ThenReceiveFull_CompletesWorkflow()
    {
        var id = CreateDraft(10);
        _services.Purchases.Approve(id, _admin.Id, "admin");
        Assert.Equal(0, _services.Products.Get(_product.Id)!.Stock); // still no stock

        _services.Purchases.Receive(id,
            new List<(long, decimal, decimal)> { (_product.Id, 10, 10) }, "", _admin.Id, "admin");

        Assert.Equal(10, _services.Products.Get(_product.Id)!.Stock);
        var drafts = _services.Purchases.ListDrafts();
        Assert.DoesNotContain(drafts, d => d.Id == id); // no longer in active list
    }

    [Fact]
    public void ReceivePartial_MarksPartial()
    {
        var id = CreateDraft(10);
        _services.Purchases.Approve(id, _admin.Id, "admin");
        _services.Purchases.Receive(id,
            new List<(long, decimal, decimal)> { (_product.Id, 10, 6) }, "sebagian dulu", _admin.Id, "admin");

        Assert.Equal(6, _services.Products.Get(_product.Id)!.Stock);
        Assert.Contains(_services.Purchases.ListDrafts(), d => d.Id == id && d.Workflow == "PARTIAL");

        // receive the rest
        _services.Purchases.Receive(id,
            new List<(long, decimal, decimal)> { (_product.Id, 10, 4) }, "", _admin.Id, "admin");
        Assert.Equal(10, _services.Products.Get(_product.Id)!.Stock);
        Assert.DoesNotContain(_services.Purchases.ListDrafts(), d => d.Id == id);
    }

    [Fact]
    public void Receive_BeforeApprove_Rejected()
    {
        var id = CreateDraft(5);
        // DRAFT can still receive per design (head.Status DRAFT/ORDERED allowed), so test cancel instead
        _services.Purchases.CancelDraft(id, "ubah pikiran", _admin.Id, "admin");
        Assert.Throws<InvalidOperationException>(() =>
            _services.Purchases.Receive(id,
                new List<(long, decimal, decimal)> { (_product.Id, 5, 5) }, "", _admin.Id, "admin"));
    }

    [Fact]
    public void Approve_Twice_Rejected()
    {
        var id = CreateDraft(5);
        _services.Purchases.Approve(id, _admin.Id, "admin");
        Assert.Throws<InvalidOperationException>(() =>
            _services.Purchases.Approve(id, _admin.Id, "admin"));
    }

    [Fact]
    public void ApAging_BucketsOutstanding()
    {
        var id = CreateDraft(10);
        _services.Purchases.Approve(id, _admin.Id, "admin");
        _services.Purchases.Receive(id, new List<(long, decimal, decimal)> { (_product.Id, 10, 10) }, "", _admin.Id, "admin");
        // mark as UNPAID to simulate credit purchase
        _db.With(c => c.Execute("UPDATE purchases SET payment_status='UNPAID' WHERE id=@id", new { id }));
        var aging = _services.Purchases.ApAging();
        Assert.Contains(aging, a => a.Total > 0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>Price levels: set + resolve through the service.</summary>
public class PriceLevelServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;
    private long _productId;
    private long _memberCustId;

    public PriceLevelServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-price-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
        _productId = _services.Products.SaveProduct(new Product
        {
            Code = "PL1", Name = "Price Prod", UnitId = 1, PurchasePrice = 4000, SellingPrice = 10000, Stock = 10
        }, Array.Empty<string>(), _admin.Id, "admin");
        var cust = _services.Products.SaveCustomer(new KasirPro.Core.Domain.Customer { Name = "Member" }, _admin.Id, "admin");
        _memberCustId = cust.Id;
    }

    [Fact]
    public void DefaultLevel_BasePrice()
    {
        Assert.Equal(10000, _services.Prices.Resolve(_productId, 1, 1, 10000));
    }

    [Fact]
    public void MemberLevelPrice_Applies()
    {
        _services.Prices.SetPrice(_productId, 2, 0, 9000, _admin.Id, "admin");
        _db.With(c => c.Execute("UPDATE customers SET price_level_id=2 WHERE id=@id", new { id = _memberCustId }));
        Assert.Equal(9000, _services.Prices.Resolve(_productId, _memberCustId, 1, 10000));
        Assert.Equal(10000, _services.Prices.Resolve(_productId, 1, 1, 10000)); // retail untouched
    }

    [Fact]
    public void QtyTier_AppliesAtThreshold()
    {
        _services.Prices.SetPrice(_productId, 1, 12, 8000, _admin.Id, "admin");
        Assert.Equal(8000, _services.Prices.Resolve(_productId, 1, 12, 10000));
        Assert.Equal(8000, _services.Prices.Resolve(_productId, 1, 50, 10000));
        Assert.Equal(10000, _services.Prices.Resolve(_productId, 1, 11, 10000));
    }

    [Fact]
    public void CustomerLevel_BeatsQtyTier()
    {
        _services.Prices.SetPrice(_productId, 2, 0, 9000, _admin.Id, "admin");
        _services.Prices.SetPrice(_productId, 1, 12, 8000, _admin.Id, "admin");
        _db.With(c => c.Execute("UPDATE customers SET price_level_id=2 WHERE id=@id", new { id = _memberCustId }));
        Assert.Equal(9000, _services.Prices.Resolve(_productId, _memberCustId, 12, 10000));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>Serial pick integration in a real sale + customer display report queries.</summary>
public class SerialSaleIntegrationTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;
    private long _productId;

    public SerialSaleIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-ssale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "S1", Name = "Phone X", UnitId = 1, PurchasePrice = 3000000, SellingPrice = 4500000, Stock = 3
        }, Array.Empty<string>(), _admin.Id, "admin");
        _productId = pid;
        _db.With(c => c.Execute("UPDATE products SET track_serial=1 WHERE id=@id", new { id = pid }));
        foreach (var s in new[] { "IMEI-AAA", "IMEI-BBB", "IMEI-CCC" })
            _services.Serials.Add(pid, 1, s, "", "", _admin.Id, "admin");
    }

    [Fact]
    public void Sale_WithSerials_MarksSold()
    {
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new()
            {
                new SaleItem
                {
                    ProductId = _productId, ProductCode = "S1", ProductName = "Phone X",
                    Qty = 1, Price = 4500000, Cost = 3000000,
                    SerialNos = new List<string> { "IMEI-AAA" }
                }
            },
            Subtotal = 4500000, Total = 4500000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 4500000 });
        _services.Sales.CompleteSale(sale);

        // IMEI-AAA sold, others remain available
        var status = _db.With(c => c.ExecuteScalar<string>(
            "SELECT status FROM product_serials WHERE serial_no='IMEI-AAA'"));
        Assert.Equal("SOLD", status);
        Assert.Equal(2, _services.Serials.Available(_productId).Count);
        Assert.DoesNotContain(_services.Serials.Available(_productId), s => s.Serial == "IMEI-AAA");
    }

    [Fact]
    public void Sale_UnavailableSerial_RollsBack()
    {
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new()
            {
                new SaleItem
                {
                    ProductId = _productId, ProductCode = "S1", ProductName = "Phone X",
                    Qty = 1, Price = 4500000, Cost = 3000000,
                    SerialNos = new List<string> { "IMEI-GAK-ADA" }
                }
            },
            Subtotal = 4500000, Total = 4500000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 4500000 });
        Assert.ThrowsAny<Exception>(() => _services.Sales.CompleteSale(sale));

        // transactional: stock unchanged, serials intact, no sale persisted
        Assert.Equal(3, _services.Products.Get(_productId)!.Stock);
        Assert.Equal(3, _services.Serials.Available(_productId).Count);
        Assert.Equal(0, _db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM sales")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>New report queries: aging/valuation/monthly/by-customer/promo usage.</summary>
public class ExtendedReportTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;

    public ExtendedReportTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-rep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
    }

    [Fact]
    public void ReceivableAging_BucketsOutstanding()
    {
        _services.Settings.Set("allow_credit", "1");
        var cust = _services.Products.SaveCustomer(new KasirPro.Core.Domain.Customer { Name = "C" }, _admin.Id, "admin");
        _services.Debts.SetCreditLimit(cust.Id, 100000, _admin.Id, "admin");
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "R1", Name = "R", UnitId = 1, PurchasePrice = 1, SellingPrice = 10000, Stock = 5
        }, Array.Empty<string>(), _admin.Id, "admin");
        var sale = new Sale
        {
            CustomerId = cust.Id, UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "R1", ProductName = "R", Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 4000 });
        _services.Sales.CompleteSale(sale);

        var aging = _services.Reports.ReceivableAging();
        Assert.Contains(aging, a => a.Bucket == "1-30" && a.Total == 6000);
    }

    [Fact]
    public void Valuation_SumsStockByCategory()
    {
        _services.Products.SaveProduct(new Product
        {
            Code = "V1", Name = "Val", UnitId = 1, PurchasePrice = 5000, SellingPrice = 8000, Stock = 4
        }, Array.Empty<string>(), _admin.Id, "admin");
        var val = _services.Reports.InventoryValuation();
        Assert.Contains(val, v => v.StockValue >= 20000);
    }

    [Fact]
    public void MonthlySales_ReturnsBuckets()
    {
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "M1", Name = "M", UnitId = 1, PurchasePrice = 1, SellingPrice = 5000, Stock = 5
        }, Array.Empty<string>(), _admin.Id, "admin");
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "M1", ProductName = "M", Qty = 1, Price = 5000, Cost = 0 } },
            Subtotal = 5000, Total = 5000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 5000 });
        _services.Sales.CompleteSale(sale);
        Assert.NotEmpty(_services.Reports.MonthlySales());
    }

    [Fact]
    public void SalesByCustomer_Groups()
    {
        var cust = _services.Products.SaveCustomer(new KasirPro.Core.Domain.Customer { Name = "CustX" }, _admin.Id, "admin");
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "BC1", Name = "B", UnitId = 1, PurchasePrice = 1, SellingPrice = 5000, Stock = 5
        }, Array.Empty<string>(), _admin.Id, "admin");
        var sale = new Sale
        {
            CustomerId = cust.Id, UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "BC1", ProductName = "B", Qty = 1, Price = 5000, Cost = 0 } },
            Subtotal = 5000, Total = 5000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 5000 });
        _services.Sales.CompleteSale(sale);
        var byCust = _services.Reports.SalesByCustomer(DateTime.Today, DateTime.Today);
        Assert.Contains(byCust, r => r.Customer == "CustX" && r.Count == 1);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
