using KasirPro.Core.Domain;
using KasirPro.App;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Integration tests against a temporary SQLite database.</summary>
public class SalesServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;

    public SalesServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
    }

    private Product NewProduct(decimal price = 10000, decimal cost = 7000, decimal stock = 50, string? code = null) =>
        new()
        {
            Code = code ?? ("P" + Guid.NewGuid().ToString("N")[..8]),
            Name = "Produk " + code,
            UnitId = 1,
            PurchasePrice = cost,
            SellingPrice = price,
            Stock = stock,
            MinStock = 5
        };

    [Fact]
    public void Sale_CreatesStockMovement_AndDecreasesStock()
    {
        var product = _services.Products.SaveProduct(NewProduct(stock: 50), Array.Empty<string>(), _admin.Id, "admin");
        var saved = _services.Products.Get(product);
        Assert.Equal(50, saved!.Stock);

        var sale = new Sale
        {
            CustomerId = 1,
            UserId = _admin.Id,
            CashierName = "admin",
            Items = new()
            {
                new SaleItem { ProductId = product, ProductCode = saved.Code, ProductName = saved.Name, Qty = 2, Price = saved.SellingPrice, Cost = saved.PurchasePrice }
            },
            Subtotal = 20000, Discount = 0, Total = 20000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 20000 });
        var result = _services.Sales.CompleteSale(sale);

        Assert.Equal(48, _services.Products.Get(product)!.Stock);
        var movements = _services.Inventory.Movements("", product, StockRef.Sale, DateTime.Today, DateTime.Today, 1, 50);
        Assert.True(movements.TotalItems >= 1);
        Assert.Equal("OUT", movements.Items.First().Direction);
    }

    [Fact]
    public void Sale_InsufficientPayment_Rejected()
    {
        var pid = _services.Products.SaveProduct(NewProduct(), Array.Empty<string>(), _admin.Id, "admin");
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 5000 });
        Assert.Throws<InvalidOperationException>(() => _services.Sales.CompleteSale(sale));
    }

    [Fact]
    public void Purchase_IncreasesStock()
    {
        var pid = _services.Products.SaveProduct(NewProduct(stock: 10), Array.Empty<string>(), _admin.Id, "admin");
        var po = new Purchase
        {
            SupplierId = 0,
            Items = new() { new PurchaseItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 25, Cost = 7000 } }
        };
        var saved = _services.Purchases.CreatePurchase(po, _admin.Id, "admin");
        Assert.Equal(175000, saved.Total);
        Assert.Equal(35, _services.Products.Get(pid)!.Stock);

        var movements = _services.Inventory.Movements("", pid, StockRef.Purchase, DateTime.Today, DateTime.Today, 1, 10);
        Assert.Equal("IN", movements.Items.First().Direction);
    }

    [Fact]
    public void SaleReturn_RestoresStock()
    {
        var pid = _services.Products.SaveProduct(NewProduct(stock: 10), Array.Empty<string>(), _admin.Id, "admin");
        var prod = _services.Products.Get(pid)!;

        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = prod.Code, ProductName = prod.Name, Qty = 4, Price = prod.SellingPrice, Cost = prod.PurchasePrice } },
            Subtotal = 40000, Total = 40000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 40000 });
        var done = _services.Sales.CompleteSale(sale);
        Assert.Equal(6, _services.Products.Get(pid)!.Stock);

        var saleId = done.Id;
        var items = _services.Sales.GetById(saleId)!.Items;
        var ret = _services.Sales.CreateReturn(saleId,
            new List<(long, decimal)> { (items[0].Id, 1) }, "unit test", _admin.Id, "admin");

        Assert.Equal(7, _services.Products.Get(pid)!.Stock);
        Assert.Equal(10000, ret.Total);
        var movements = _services.Inventory.Movements("", pid, StockRef.SaleReturn, DateTime.Today, DateTime.Today, 1, 10);
        Assert.Equal("IN", movements.Items.First().Direction);
    }

    [Fact]
    public void SaleVoid_RestoresStock_AndMarksVoided()
    {
        var pid = _services.Products.SaveProduct(NewProduct(stock: 10), Array.Empty<string>(), _admin.Id, "admin");
        var prod = _services.Products.Get(pid)!;
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = prod.Code, ProductName = prod.Name, Qty = 3, Price = prod.SellingPrice, Cost = prod.PurchasePrice } },
            Subtotal = 30000, Total = 30000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 30000 });
        var done = _services.Sales.CompleteSale(sale);
        Assert.Equal(7, _services.Products.Get(pid)!.Stock);

        _services.Sales.VoidSale(done.Id, "test void", _admin.Id, "admin");
        Assert.Equal(10, _services.Products.Get(pid)!.Stock);
        Assert.Equal("VOIDED", _services.Sales.GetById(done.Id)!.Status);
    }

    [Fact]
    public void InvoiceNumbers_Unique()
    {
        var pid = _services.Products.SaveProduct(NewProduct(), Array.Empty<string>(), _admin.Id, "admin");
        var prod = _services.Products.Get(pid)!;
        var invoices = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var sale = new Sale
            {
                UserId = _admin.Id,
                Items = new() { new SaleItem { ProductId = pid, ProductCode = prod.Code, ProductName = prod.Name, Qty = 1, Price = 10000, Cost = 0 } },
                Subtotal = 10000, Total = 10000
            };
            sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 10000 });
            var done = _services.Sales.CompleteSale(sale);
            Assert.True(invoices.Add(done.InvoiceNo), "Invoice duplikat: " + done.InvoiceNo);
        }
        Assert.Equal(50, invoices.Count);
    }

    [Fact]
    public void ShiftLifecycle_OpenCash_CloseComputes()
    {
        var open = _services.Cash.OpenSession(_admin.Id, "admin", 100000);
        Assert.NotNull(open);
        var second = _services.Cash.OpenSession(_admin.Id, "admin", 100000);
        Assert.Equal(open!.Id, second!.Id);

        // sale with cash -> cash_sales updates
        var pid = _services.Products.SaveProduct(NewProduct(), Array.Empty<string>(), _admin.Id, "admin");
        var prod = _services.Products.Get(pid)!;
        var sale = new Sale
        {
            UserId = _admin.Id,
            CashSessionId = open!.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = prod.Code, ProductName = prod.Name, Qty = 1, Price = 20000, Cost = 0 } },
            Subtotal = 20000, Total = 20000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 20000 });
        _services.Sales.CompleteSale(sale);

        _services.Cash.CashIn(open.Id, 50000, "topup", _admin.Id, "admin");
        _services.Cash.CashOut(open.Id, 10000, "buy bags", _admin.Id, "admin");

        var closed = _services.Cash.CloseSession(open.Id, 260000, "", _admin.Id, "admin");
        // expected = 100000 + 20000 + 50000 - 10000 = 160000
        Assert.Equal(160000, closed.ClosingCashExpected);
        Assert.Equal(100000, closed.Difference);
        Assert.Equal("CLOSED", closed.Status);
    }

    [Fact]
    public void StockAdjustment_SetExactQty_CreatesLedger()
    {
        var pid = _services.Products.SaveProduct(NewProduct(stock: 10), Array.Empty<string>(), _admin.Id, "admin");
        _services.Inventory.Adjust(pid, 25, "opname test", _admin.Id, "admin");
        Assert.Equal(25, _services.Products.Get(pid)!.Stock);
        var movements = _services.Inventory.Movements("", pid, StockRef.Adjustment, DateTime.Today, DateTime.Today, 1, 10);
        Assert.Equal(1, movements.TotalItems);
        Assert.Equal(15, movements.Items.First().Qty);
    }

    [Fact]
    public void AuditLog_RecordsSale()
    {
        var pid = _services.Products.SaveProduct(NewProduct(), Array.Empty<string>(), _admin.Id, "admin");
        var prod = _services.Products.Get(pid)!;
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = prod.Code, ProductName = prod.Name, Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 10000 });
        _services.Sales.CompleteSale(sale);
        var logs = _services.Audit.Recent(10);
        Assert.Contains(logs, l => l.Action == AuditAction.Sale);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

