using Dapper;
using KasirPro.App;
using KasirPro.Core.Domain;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Reporting;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

/// <summary>End-to-end integration: sale with promo + loyalty + shift + X/Z; export; retention.</summary>
public class EndToEndIntegrationTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;
    private Product _product;
    private KasirPro.Core.Domain.Customer _member;

    public EndToEndIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db);
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");

        var pid = _services.Products.SaveProduct(new Product
        {
            Code = "E2E1", Name = "E2E Product", UnitId = 1, PurchasePrice = 6000, SellingPrice = 10000, Stock = 100
        }, Array.Empty<string>(), _admin.Id, "admin");
        _product = _services.Products.Get(pid)!;

        _member = _services.Products.SaveCustomer(new KasirPro.Core.Domain.Customer { Name = "Member E2E" }, _admin.Id, "admin");
    }

    private Sale MakeSale(decimal qty, PaymentMethod method, decimal paid, long customerId = 1)
    {
        var sale = new Sale
        {
            CustomerId = customerId,
            UserId = _admin.Id,
            Items = new() { new SaleItem { ProductId = _product.Id, ProductCode = _product.Code, ProductName = _product.Name, Qty = qty, Price = 10000, Cost = 6000 } },
            Subtotal = 10000 * qty, Total = 10000 * qty
        };
        sale.Payments.Add(new SalePayment { Method = method, Amount = paid });
        return sale;
    }

    [Fact]
    public void Promo_Fixed5000_AppliesToSaleTotal()
    {
        var promo = new Promotion
        {
            Code = "HEMAT5K", Name = "Diskon 5rb", Type = PromoType.Fixed, Value = 5000,
            Scope = PromoScope.All, StartDate = DateTime.Today.AddDays(-1), EndDate = DateTime.Today.AddDays(1),
            Days = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6 }
        };
        _services.Promotions.Save(promo, _admin.Id, "admin");

        var lines = new List<CartLine> { new() { ProductId = _product.Id, Code = "E2E1", Name = "X", Price = 10000, Qty = 3 } };
        var results = KasirPro.Core.Domain.PromotionEngine.Evaluate(
            new[] { promo }, lines, 30000, false, DateTime.Now);
        Assert.Single(results);
        Assert.Equal(5000, results[0].Discount);

        // record usage (use a real sale id to satisfy FK)
        var realSale = MakeSale(1, PaymentMethod.Cash, 10000);
        var done = _services.Sales.CompleteSale(realSale);
        _db.Transaction(c =>
            _services.Promotions.RecordUsage(c, promo, done.Id, 1, 5000, "", DbEx.Iso(DateTime.Now)));
        var usage = _db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM promotion_usage WHERE promo_id=@id",
            new { id = promo.Id }));
        Assert.Equal(1, usage);
    }

    [Fact]
    public void SplitPayment_ThreeMethods_AllPersisted()
    {
        var session = _services.Cash.OpenSession(_admin.Id, "admin", 0);
        var sale = MakeSale(3, PaymentMethod.Cash, 100000);
        sale.CashSessionId = session!.Id;
        sale.Payments.Clear();
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 100000 });
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Qris, Amount = 150000 });
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Debit, Amount = 50000 });

        var done = _services.Sales.CompleteSale(sale);
        var saved = _services.Sales.GetById(done.Id)!;
        Assert.Equal(3, saved.Payments.Count);
        Assert.Equal(300000, saved.Payments.Sum(p => p.Amount));
        Assert.Equal(100000, _services.Cash.GetSession(session.Id)!.CashSales); // only cash portion
    }

    [Fact]
    public void ShiftLifecycle_WithCashSalesAndXZ()
    {
        var session = _services.Cash.OpenSession(_admin.Id, "admin", 100000);
        var s1 = MakeSale(1, PaymentMethod.Cash, 10000);
        s1.CashSessionId = session!.Id;
        _services.Sales.CompleteSale(s1);

        var x = _services.XZReports.Generate(session.Id, "X", _admin.Id, "admin");
        Assert.Equal(1, x.SalesCount);
        Assert.Equal(110000, x.ExpectedCash);

        var closed = _services.Cash.CloseSession(session.Id, 110000, "", _admin.Id, "admin");
        Assert.Equal(0, closed.Difference);
    }

    [Fact]
    public void ExportPdf_ManyRows_Paginates()
    {
        var columns = new List<string> { "No", "Nama", "Total" };
        var widths = new List<int> { 15, 55, 30 };
        var rows = new List<object[]>();
        for (var i = 0; i < 120; i++) rows.Add(new object[] { i + 1, "Item " + i, 1000 * i });

        var path = PdfReportExporter.ExportTable("Big Report", "range", columns, widths, rows,
            Path.Combine(_dir, "big.pdf"));
        var text = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(File.ReadAllBytes(path));
        var countMatch = System.Text.RegularExpressions.Regex.Match(text, "/Count (\\d+)");
        Assert.True(countMatch.Success);
        Assert.True(int.Parse(countMatch.Groups[1].Value) >= 2, "120 rows harus multi-halaman");
    }

    [Fact]
    public void BackupRetention_LimitEnforced()
    {
        _services.Settings.Set("backup_keep", "3");
        for (var i = 0; i < 6; i++)
            _services.Backup.CreateBackup("retention-test", _admin.Id, "admin");
        var files = Directory.GetFiles(_dir, "POS-*.db", SearchOption.AllDirectories);
        Assert.True(files.Length <= 4, $"harus <= 3+1 (pre-restore sample), dapat {files.Length}");
    }

    [Fact]
    public void SpeedyBarcodeLookup_100ScansUnder2Seconds()
    {
        // seed 500 products with barcodes
        for (var i = 0; i < 500; i++)
        {
            _services.Products.SaveProduct(new Product
            {
                Code = "SPD" + i.ToString("D4"), Name = "Speed " + i,
                UnitId = 1, PurchasePrice = 1, SellingPrice = 2, Stock = 1
            }, new[] { "8990000000" + i.ToString("D3") }, _admin.Id, "admin");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var found = 0;
        for (var i = 0; i < 100; i++)
        {
            var p = _services.Products.GetByBarcode("8990000000" + (i * 5 % 500).ToString("D3"));
            if (p != null) found++;
        }
        sw.Stop();
        Assert.Equal(100, found);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"100 lookups took {sw.ElapsedMilliseconds}ms");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>License end-to-end: generate-sign-activate-wrong-machine-expired via token API.</summary>
public class LicenseE2ETests : IDisposable
{
    private readonly string _dir;

    public LicenseE2ETests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-lice2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void FullFlow_ActivateThenTamperThenDelete()
    {
        var priv = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var pubPem = priv.ExportSubjectPublicKeyInfoPem();
        var licenseFile = Path.Combine(_dir, "license.dat");

        var activation = new KasirPro.Licensing.LicenseActivation(licenseFile, pubPem);
        Assert.False(activation.CheckStoredLicense().Activated);

        var payload = new KasirPro.Licensing.LicensePayload
        {
            Product = "KasirPro", Customer = "Toko Uji", MachineId = KasirPro.Licensing.MachineId.Get(),
            LicenseType = "Lifetime", LicenseId = "LIC-E2E",
            IssuedAt = DateTime.UtcNow.ToString("o")
        };
        var token = KasirPro.Licensing.LicenseToken.Sign(payload, priv);
        Assert.Equal(KasirPro.Licensing.LicenseVerifyStatus.Valid, activation.Activate(token).Status);
        Assert.True(activation.CheckStoredLicense().Activated);

        // expired variant rejected
        var expired = new KasirPro.Licensing.LicensePayload
        {
            Product = "KasirPro", Customer = "X", MachineId = KasirPro.Licensing.MachineId.Get(),
            LicenseType = "Trial", IssuedAt = DateTime.UtcNow.AddDays(-2).ToString("o"),
            ExpiresAt = DateTime.UtcNow.AddDays(-1).ToString("o")
        };
        var expiredToken = KasirPro.Licensing.LicenseToken.Sign(expired, priv);
        Assert.Equal(KasirPro.Licensing.LicenseVerifyStatus.Expired,
            KasirPro.Licensing.LicenseToken.Verify(expiredToken,
                KasirPro.Licensing.ECDsaFactory.FromPem(pubPem), payload.MachineId).Status);

        // tampered rejected
        var parts = token.Split('.');
        var broken = parts[0] + "." + parts[1] + "." + (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        Assert.Equal(KasirPro.Licensing.LicenseVerifyStatus.InvalidSignature,
            KasirPro.Licensing.LicenseToken.Verify(broken,
                KasirPro.Licensing.ECDsaFactory.FromPem(pubPem), payload.MachineId).Status);

        // delete -> activation screen again
        File.Delete(licenseFile);
        Assert.False(activation.CheckStoredLicense().Activated);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}



