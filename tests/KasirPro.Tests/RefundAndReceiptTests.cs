using Dapper;
using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Refund modes on returns: none/cash/store credit, incl. cash flow into shift.</summary>
public class RefundModeTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;
    private Product _product;
    private long _customerId;

    public RefundModeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-refund-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "RF1", Name = "Refund Prod", UnitId = 1, PurchasePrice = 4000, SellingPrice = 10000, Stock = 20
        }, Array.Empty<string>(), _admin.Id, "admin");
        _product = _services.Products.Get(pid)!;
        var cust = _services.Products.SaveCustomer(new KasirPro.Core.Domain.Customer { Name = "Ref Cust" }, _admin.Id, "admin");
        _customerId = cust.Id;
    }

    private (long SaleId, long ItemId) MakeSale(decimal qty)
    {
        var sale = new Sale
        {
            CustomerId = _customerId, UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = _product.Id, ProductCode = _product.Code, ProductName = _product.Name, Qty = qty, Price = 10000, Cost = 4000 } },
            Subtotal = 10000 * qty, Total = 10000 * qty
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 10000 * qty });
        var done = _services.Sales.CompleteSale(sale);
        return (done.Id, _services.Sales.GetById(done.Id)!.Items[0].Id);
    }

    [Fact]
    public void RefundNone_StockOnly_NoCashNoCredit()
    {
        var (saleId, itemId) = MakeSale(2);
        _services.Sales.CreateReturn(saleId, new List<(long, decimal)> { (itemId, 1) }, "tes", _admin.Id, "admin");
        Assert.Equal(0, _services.Ledgers.StoreCreditBalance(_customerId));
    }

    [Fact]
    public void RefundCash_RequiresOpenShift_AndAffectsCash()
    {
        var session = _services.Cash.OpenSession(_admin.Id, "admin", 0);
        var (saleId, itemId) = MakeSale(2);

        // no shift -> CASH refund rejected
        Assert.Throws<InvalidOperationException>(() =>
            _services.Sales.CreateReturn(saleId, new List<(long, decimal)> { (itemId, 1) }, "tes",
                _admin.Id, "admin", "CASH", 0));

        // with shift -> cash_out increases
        _services.Sales.CreateReturn(saleId, new List<(long, decimal)> { (itemId, 1) }, "tes",
            _admin.Id, "admin", "CASH", session!.Id);
        var closed = _services.Cash.CloseSession(session.Id, 0, "", _admin.Id, "admin");
        Assert.Equal(10000, closed.CashOut);
    }

    [Fact]
    public void RefundCredit_AddsStoreCredit_WithLedger()
    {
        var (saleId, itemId) = MakeSale(2);
        _services.Sales.CreateReturn(saleId, new List<(long, decimal)> { (itemId, 2) }, "rusak",
            _admin.Id, "admin", "CREDIT", 0);

        Assert.Equal(20000, _services.Ledgers.StoreCreditBalance(_customerId));
        var history = _services.Ledgers.StoreCreditHistory(_customerId);
        Assert.Single(history);
        Assert.Equal("IN", history[0].Direction);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>Cart quantity edit semantics: price tier re-resolution (mirrors POS CellEndEdit).</summary>
public class CartQuantityEditTests
{
    [Fact]
    public void QtyChange_ReEvaluatesTier()
    {
        var tiers = new List<PriceRule>
        {
            new() { MinQty = 6, Price = 8500 },
            new() { MinQty = 12, Price = 8000 }
        };
        // start at 5 -> base; increase to 6 -> tier1; then 12 -> tier2
        Assert.Equal(10000, PriceResolver.Resolve(10000, tiers, null, 0, 5));
        Assert.Equal(8500, PriceResolver.Resolve(10000, tiers, null, 0, 6));
        Assert.Equal(8000, PriceResolver.Resolve(10000, tiers, null, 0, 12));
        // decrease again -> back to base
        Assert.Equal(10000, PriceResolver.Resolve(10000, tiers, null, 0, 3));
    }
}

/// <summary>Receipt extras: line insertion, reprint watermark, method labels.</summary>
public class ReceiptExtrasTests
{
    private Sale MakeSale()
    {
        return new Sale
        {
            InvoiceNo = "INV-20260904-0001",
            SaleDate = DateTime.Now,
            CustomerName = "Budi",
            CashierName = "kasir",
            Items = new()
            {
                new SaleItem { ProductId = 1, ProductCode = "A", ProductName = "Kopi Sachet", Qty = 2, Price = 5000, Cost = 0 },
                new SaleItem { ProductId = 2, ProductCode = "B", ProductName = "Gula", Qty = 1, Price = 12000, Cost = 0 }
            },
            Subtotal = 22000, Discount = 0, Total = 22000,
            Tax = 0
        };
    }

    [Fact]
    public void PrintReceipt_WithExtras_DoesNotThrow()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "kp-rx-" + Guid.NewGuid() + ".db");
        var db = new Db(dbPath);
        new Migrator(db).Migrate();
        var settings = new SettingsService(db, new AuditService(db));
        var printer = new PrinterService(settings);

        // build receipt lines via public PrintReceipt is hard without printer; validate labels logic instead
        // (PrintReceipt itself requires a printer; here we verify no exception on line assembly through reflection)
        var sale = MakeSale();
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Qris, Amount = 22000 });
        var method = typeof(PrinterService).GetMethod("BuildReceipt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var lines = (List<string>)method!.Invoke(printer, new object[] { sale, false })!;
        Assert.Contains(lines, l => l.Contains("QRIS"));
        Assert.Contains(lines, l => l.Contains("TOTAL"));

        var reprintLines = (List<string>)method.Invoke(printer, new object[] { sale, true })!;
        Assert.Contains(reprintLines, l => l.Contains("REPRINT"));
    }
}
