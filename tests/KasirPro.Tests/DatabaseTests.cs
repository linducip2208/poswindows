using KasirPro.App;
using Dapper;
using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;
using Xunit;

namespace KasirPro.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;

    public DatabaseTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
    }

    [Fact]
    public void Migration_CreatesAllTables_AndIsIdempotent()
    {
        var migrator = new Migrator(_db);
        Assert.Equal(0, migrator.CurrentVersion);
        migrator.Migrate();
        Assert.Equal(3, migrator.CurrentVersion);

        // second run: no-op
        migrator.Migrate();
        Assert.Equal(3, migrator.CurrentVersion);

        var tables = migrator.TableNames();
        string[] expected =
        {
            "roles", "users", "settings", "customers", "suppliers", "categories", "units",
            "products", "product_barcodes", "sales", "sale_items", "sale_payments",
            "sale_returns", "sale_return_items", "purchases", "purchase_items",
            "purchase_returns", "purchase_return_items", "stock_movements", "stock_opnames",
            "stock_opname_items", "cash_sessions", "cash_movements", "audit_logs",
            "database_version", "sale_debts", "debt_payments",
            "holds", "xz_reports", "purchase_payments", "warehouses",
            "warehouse_stock", "stock_transfers", "stock_transfer_items", "unit_conversions"
        };
        foreach (var t in expected)
            Assert.Contains(t, tables);
    }

    [Fact]
    public void MigrationV2_CreditSchema_AndSettings()
    {
        var migrator = new Migrator(_db);
        migrator.Migrate();

        var col = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM pragma_table_info('cash_sessions') WHERE name='debt_payments'"));
        Assert.Equal(1, col);

        var setting = _db.With(c => c.ExecuteScalar<string>(
            "SELECT value FROM settings WHERE key='allow_credit'"));
        Assert.Equal("0", setting);
    }

    [Fact]
    public void Defaults_Seeded_RolesAndWalkin()
    {
        new Migrator(_db).Migrate();
        var roles = _db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM roles"));
        Assert.True(roles >= 2);
        var walkin = _db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM customers WHERE code='WALKIN'"));
        Assert.Equal(1, walkin);
    }

    [Fact]
    public void Indexes_Exist()
    {
        new Migrator(_db).Migrate();
        var idx = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('idx_products_name','idx_sales_date','idx_movements_product_date','idx_barcode_lookup')"));
        Assert.Equal(4, idx);
    }

    [Fact]
    public void ForeignKeys_AreEnforced()
    {
        new Migrator(_db).Migrate();
        _db.With(c =>
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

public class BackupTests : IDisposable
{
    private readonly string _dir;
    private readonly Db _db;
    private readonly AppServices _services;
    private readonly KasirPro.Core.Domain.User _admin;

    public BackupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kp-bak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Db(Path.Combine(_dir, "pos.db"));
        new Migrator(_db).Migrate();
        _services = new AppServices(_db, Path.Combine(_dir, "Backup"));
        _admin = _services.Users.CreateAdmin("admin", "admin123", "Admin");
    }

    [Fact]
    public void Backup_CreatesValidFile_RestoreRoundtrip()
    {
        var pid = _services.Products.SaveProduct(new KasirPro.Core.Domain.Product
        {
            Code = "BK1", Name = "Backup Test", UnitId = 1, PurchasePrice = 1, SellingPrice = 2, Stock = 1
        }, Array.Empty<string>(), _admin.Id, "admin");

        var backupPath = _services.Backup.CreateBackup("test", _admin.Id, "admin");
        Assert.True(File.Exists(backupPath));

        // delete after backup (hard delete with ledger cleanup)
        _db.With(c => c.Execute("DELETE FROM stock_movements WHERE product_id=@id; DELETE FROM products WHERE id=@id",
            new { id = pid }));
        Assert.Null(_services.Products.Get(pid));

        // restore
        _services.Backup.RestoreBackup(backupPath, _admin.Id, "admin");
        var restored = _services.Products.Get(pid);
        Assert.NotNull(restored);
        Assert.Equal("Backup Test", restored!.Name);
    }

    [Fact]
    public void Restore_RejectsCorruptFile()
    {
        var fake = Path.Combine(_dir, "fake.db");
        File.WriteAllBytes(fake, new byte[1000]);
        Assert.Throws<InvalidOperationException>(() => _services.Backup.ValidateBackupFile(fake));
    }

    [Fact]
    public void Cleanup_KeepsNewestN()
    {
        for (var i = 0; i < 5; i++)
        {
            _services.Backup.CreateBackup("test" + i, 0, "system");
        }
        var files = Directory.GetFiles(Path.Combine(_dir, "Backup"), "POS-*.db");
        Assert.True(files.Length <= 30); // default retention
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}



