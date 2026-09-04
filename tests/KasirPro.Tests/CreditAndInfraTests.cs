using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Reporting;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>Credit sales (piutang): debt creation, settlement, guards, cash session integration.</summary>
public class CreditSaleTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;

    public CreditSaleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-debt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
    }

    private KasirPro.Core.Domain.Customer NewCustomer(decimal creditLimit = 500000)
    {
        var cust = _services.Products.SaveCustomer(
            new KasirPro.Core.Domain.Customer { Name = "Pelanggan " + Guid.NewGuid().ToString("N")[..6] },
            _admin.Id, "admin");
        _services.Debts.SetCreditLimit(cust.Id, creditLimit, _admin.Id, "admin");
        return cust;
    }

    private (long ProductId, Product P) NewProduct(decimal price = 10000)
    {
        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "C" + Guid.NewGuid().ToString("N")[..8],
            Name = "Credit Prod", UnitId = 1, PurchasePrice = 5000, SellingPrice = price, Stock = 100
        }, Array.Empty<string>(), _admin.Id, "admin");
        return (pid, _services.Products.Get(pid)!);
    }

    [Fact]
    public void CreditDisabled_Shortfall_Rejected()
    {
        var (pid, _) = NewProduct();
        var sale = new Sale
        {
            CustomerId = NewCustomer().Id, UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 4000 });
        Assert.Throws<InvalidOperationException>(() => _services.Sales.CompleteSale(sale));
    }

    [Fact]
    public void CreditSale_CreatesDebt_WithCorrectOutstanding()
    {
        _services.Settings.Set("allow_credit", "1");
        var (pid, _) = NewProduct();
        var sale = new Sale
        {
            CustomerId = NewCustomer().Id, CustomerName = "Budi", UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 2, Price = 10000, Cost = 0 } },
            Subtotal = 20000, Total = 20000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 5000 });

        var done = _services.Sales.CompleteSale(sale);
        Assert.Equal(15000, done.Outstanding);

        var debts = _services.Debts.Outstanding("", "", 1, 50);
        Assert.Equal(1, debts.TotalItems);
        Assert.Equal(15000, debts.Items[0].OriginalAmount);
        Assert.Equal(15000, debts.Items[0].Remaining);
    }

    [Fact]
    public void Credit_WalkinCustomer_Rejected()
    {
        _services.Settings.Set("allow_credit", "1");
        var (pid, _) = NewProduct();
        var sale = new Sale
        {
            CustomerId = 1, UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 1000 });
        Assert.Throws<InvalidOperationException>(() => _services.Sales.CompleteSale(sale));
    }

    [Fact]
    public void Settle_PartialThenFull_UpdatesStatus_AndCashSession()
    {
        _services.Settings.Set("allow_credit", "1");
        var session = _services.Cash.OpenSession(_admin.Id, "admin", 0);
        Assert.NotNull(session);

        var (pid, _) = NewProduct();
        var sale = new Sale
        {
            CustomerId = NewCustomer().Id, UserId = _admin.Id, CashSessionId = session!.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Credit, Amount = 0 });
        var done = _services.Sales.CompleteSale(sale);
        Assert.Equal(10000, done.Outstanding);

        var debts = _services.Debts.Outstanding("", "", 1, 10);
        var debtId = debts.Items[0].Id;

        // partial: cash settlement flows into open session
        _services.Debts.Settle(debtId, 4000, PaymentMethod.Cash, "cicil 1", _admin.Id, "admin", session.Id);
        var afterPartial = _services.Debts.Get(debtId)!;
        Assert.Equal("PARTIAL", afterPartial.Status);
        Assert.Equal(6000, afterPartial.Remaining);
        Assert.Equal(4000, _services.Cash.GetSession(session.Id)!.DebtPayments);

        // overpay guard
        Assert.Throws<InvalidOperationException>(() =>
            _services.Debts.Settle(debtId, 99999, PaymentMethod.Cash, "", _admin.Id, "admin", session.Id));

        // full
        var settled = _services.Debts.Settle(debtId, 6000, PaymentMethod.Transfer, "lunas", _admin.Id, "admin");
        Assert.Equal("SETTLED", settled.Status);
        Assert.Equal(0, settled.Remaining);

        // settled debts no longer in outstanding list
        var outstanding = _services.Debts.Outstanding("", "OUTSTANDING", 1, 10);
        Assert.Equal(0, outstanding.TotalItems);

        // session expected closing cash = 0 opening + 4000 debt-cash
        var closed = _services.Cash.CloseSession(session.Id, 4000, "", _admin.Id, "admin");
        Assert.Equal(4000, closed.ClosingCashExpected);
    }

    [Fact]
    public void VoidSale_Blocked_WhileDebtOutstanding()
    {
        _services.Settings.Set("allow_credit", "1");
        var (pid, _) = NewProduct();
        var sale = new Sale
        {
            CustomerId = NewCustomer().Id, UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Credit, Amount = 0 });
        var done = _services.Sales.CompleteSale(sale);

        Assert.Throws<InvalidOperationException>(() =>
            _services.Sales.VoidSale(done.Id, "test", _admin.Id, "admin"));

        // settle then void succeeds
        var debtId = _services.Debts.Outstanding("", "", 1, 5).Items[0].Id;
        _services.Debts.Settle(debtId, 10000, PaymentMethod.Cash, "lunas", _admin.Id, "admin");
        _services.Sales.VoidSale(done.Id, "after settle", _admin.Id, "admin");
        Assert.Equal("VOIDED", _services.Sales.GetById(done.Id)!.Status);
    }

    [Fact]
    public void PaidSale_HasNoOutstanding()
    {
        var (pid, _) = NewProduct();
        var sale = new Sale
        {
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = pid, ProductCode = "X", ProductName = "X", Qty = 1, Price = 10000, Cost = 0 } },
            Subtotal = 10000, Total = 10000
        };
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 10000 });
        var done = _services.Sales.CompleteSale(sale);
        Assert.Equal(0, done.Outstanding);
        Assert.Equal(0, _services.Sales.GetById(done.Id)!.Outstanding);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

public class PdfExportTests : IDisposable
{
    private readonly string _dir;

    public PdfExportTests() { _dir = Path.Combine(Path.GetTempPath(), "kp-pdf-" + Guid.NewGuid().ToString("N")); }

    [Fact]
    public void Export_ProducesValidPdfStructure()
    {
        var target = Path.Combine(_dir, "report.pdf");
        var columns = new List<string> { "Invoice", "Total" };
        var widths = new List<int> { 60, 40 };
        var rows = new List<object[]>();
        for (var i = 0; i < 60; i++) rows.Add(new object[] { $"INV-2026090{i % 10}-000{i}", 15000 + i });

        var path = PdfReportExporter.ExportTable("Laporan Penjualan", "01/09/2026 - 07/09/2026",
            columns, widths, rows, target);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 500);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));

        var text = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
        Assert.Contains("%%EOF", text);
        Assert.Contains("/Count 3", text); // 60 rows -> paginated to 3 pages
        Assert.Contains("Laporan Penjualan", text);

        // startxref points at the actual xref table
        var sxIdx = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        var offset = int.Parse(text[(sxIdx + 10)..].Trim().Split('\n')[0]);
        Assert.Equal("xref", text[offset..(offset + 4)]);
    }

    [Fact]
    public void Export_EscapesSpecialCharacters()
    {
        var target = Path.Combine(_dir, "esc.pdf");
        var path = PdfReportExporter.ExportTable("Test (parens)", "",
            new List<string> { "Name" }, new List<int> { 100 },
            new List<object[]> { new object[] { "a(b)c \\ 100%" } }, target);
        var text = File.ReadAllText(path);
        Assert.Contains("a\\(b\\)c \\\\", text);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

public class UpdateServiceTests : IDisposable
{
    private readonly string _dir;

    public UpdateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void Check_MissingManifest_Throws()
    {
        var db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(db).Migrate();
        var settings = new SettingsService(db, new AuditService(db));
        var svc = new UpdateService(settings);
        Assert.Throws<InvalidOperationException>(() => svc.Check());
    }

    [Fact]
    public void BuildAndCheck_Package_VerifiesChecksums()
    {
        // fake installed app dir
        var appDir = Path.Combine(_dir, "app");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "KasirPro.dll"), "fake payload");

        // build SIGNED package with version FAR ABOVE the installed one (assembly is 15.x)
        var packageDir = UpdateService.BuildPackage(appDir, Path.Combine(_dir, "pkg"), "99.0.0", "test update");
        Assert.True(File.Exists(Path.Combine(packageDir, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(packageDir, "manifest.sig")));
        Assert.True(File.Exists(Path.Combine(packageDir, "KasirPro.dll")));

        // settings point to package; use dev update public key created by BuildPackage
        var db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(db).Migrate();
        var audit = new AuditService(db);
        var settings = new SettingsService(db, audit);
        settings.Set("update_source", packageDir);
        var pubPem = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem"));
        var svc = new UpdateService(settings, pubPem);

        var info = svc.Check();
        Assert.Equal("99.0.0", info.Version);
        Assert.True(info.NewerThanInstalled,
            $"available={info.Version} vs installed={svc.InstalledVersion}");
        Assert.Single(info.Files);
    }

    [Fact]
    public void Apply_StagesFiles_WithoutDataOrLicense()
    {
        var appDir = Path.Combine(_dir, "app2");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "KasirPro.dll"), "new dll");
        File.WriteAllText(Path.Combine(appDir, "license.dat"), "SECRET");
        Directory.CreateDirectory(Path.Combine(appDir, "Data"));
        File.WriteAllText(Path.Combine(appDir, "Data", "pos.db"), "never copy");

        var packageDir = UpdateService.BuildPackage(appDir, Path.Combine(_dir, "pkg2"), "99.0.1", "");
        var db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(db).Migrate();
        var settings = new SettingsService(db, new AuditService(db));
        settings.Set("update_source", packageDir);
        var pubPem = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem"));
        var svc = new UpdateService(settings, pubPem);

        var info = svc.Check();
        Assert.DoesNotContain(info.Files, f => f.RelativePath == "license.dat");
        Assert.DoesNotContain(info.Files, f => f.RelativePath.StartsWith("Data/"));

        // stage into a copy of an app root (UpdateService uses AppPaths.Root - emulate via staging folder)
        var staged = svc.Apply(info);
        Assert.True(Directory.Exists(staged));
        Assert.True(File.Exists(Path.Combine(staged, "KasirPro.dll")));
        Assert.False(File.Exists(Path.Combine(staged, "license.dat")));
        Assert.False(File.Exists(Path.Combine(staged, "Data", "pos.db")));
        Assert.Equal("99.0.1", File.ReadAllText(Path.Combine(Path.GetDirectoryName(staged)!, "pending.version")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}



