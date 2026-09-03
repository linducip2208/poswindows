using Dapper;
using KasirPro.Core.Domain;
using KasirPro.Core.Security;

namespace KasirPro.Infrastructure.Services;

/// <summary>
/// Development/demo data seeder. Only invoked explicitly via Tools > Database Maintenance
/// or the --seed-demo command line flag. NEVER runs automatically in production.
/// </summary>
public class DemoSeeder
{
    private readonly Db _db;
    private readonly SalesService _sales;
    private readonly PurchaseService _purchases;
    private readonly UserService _users;
    private readonly ProductService _products;

    private static readonly string[] CategoryNames =
        { "Minuman", "Makanan", "Snack", "Sembako", "Kebersihan", "Kosmetik", "Rokok", "Alat Tulis", "Elektronik", "Peralatan Dapur" };

    private static readonly string[] SupplierNames =
        { "PT Sumber Makmur", "CV Cahaya Abadi", "UD Berkah Jaya", "PT Distribusi Nusantara", "CV Mitra Niaga" };

    private static readonly string[] CustomerFirstNames =
        { "Andi", "Budi", "Citra", "Dewi", "Eko", "Fajar", "Gita", "Hendra", "Indah", "Joko",
          "Kartika", "Lestari", "Maya", "Nanda", "Oki", "Putri", "Rina", "Sari", "Tono", "Umi", "Vina", "Wawan", "Yuni", "Zaki", "Bayu" };

    public DemoSeeder(Db db, SalesService sales, PurchaseService purchases, UserService users, ProductService products)
    { _db = db; _sales = sales; _purchases = purchases; _users = users; _products = products; }

    /// <summary>Wipes all business data, then seeds demo content. Keeps settings + admin user.</summary>
    public void ResetAndSeed(long adminUserId, string adminUsername)
    {
        _db.With(c =>
        {
            c.Execute(@"DELETE FROM sale_return_items; DELETE FROM sale_returns;
                DELETE FROM purchase_return_items; DELETE FROM purchase_returns;
                DELETE FROM sale_payments; DELETE FROM sale_items; DELETE FROM sales;
                DELETE FROM purchase_items; DELETE FROM purchases;
                DELETE FROM stock_opname_items; DELETE FROM stock_opnames;
                DELETE FROM cash_movements; DELETE FROM cash_sessions;
                DELETE FROM stock_movements;
                DELETE FROM product_barcodes; DELETE FROM products;
                DELETE FROM categories; DELETE FROM units; DELETE FROM suppliers; DELETE FROM customers;
                DELETE FROM audit_logs;
                UPDATE sqlite_sequence SET seq=0;");
        });

        var admin = _users.GetByUsername(adminUsername) ??
            new User { Id = adminUserId, Username = adminUsername, Role = "Admin", IsActive = true };

        SeedCategoriesAndUnits();
        SeedCustomers();
        SeedSuppliers();
        SeedProducts(admin);
        SeedPurchases(admin);
        SeedSales(admin);
        SeedOpenShift(admin);

        AppLogger.Instance.Info("Demo data seeded");
    }
    private void SeedCategoriesAndUnits()
    {
        var now = DbEx.Iso(DateTime.Now);
        _db.With(c =>
        {
            foreach (var name in CategoryNames)
                c.Execute("INSERT INTO categories (name, created_at, updated_at) VALUES (@n, @t, @t)", new { n = name, t = now });
        });
    }

    private void SeedCustomers()
    {
        var now = DbEx.Iso(DateTime.Now);
        var rnd = new Random(7);
        _db.With(c =>
        {
            for (var i = 0; i < 25; i++)
            {
                var name = CustomerFirstNames[i] + " " + CustomerFirstNames[(i * 7) % CustomerFirstNames.Length];
                c.Execute(@"INSERT INTO customers (code, name, phone, address, created_at, updated_at)
                    VALUES (@c, @n, @p, @a, @t, @t)",
                    new
                    {
                        c = $"CUS{i + 1:D4}", n = name,
                        p = "08" + rnd.NextInt64(1000000000, 9999999999),
                        a = "Jl. Merdeka No." + (i + 1), t = now
                    });
            }
        });
    }

    private void SeedSuppliers()
    {
        var now = DbEx.Iso(DateTime.Now);
        var rnd = new Random(11);
        _db.With(c =>
        {
            for (var i = 0; i < SupplierNames.Length; i++)
            {
                c.Execute(@"INSERT INTO suppliers (code, name, phone, address, created_at, updated_at)
                    VALUES (@c, @n, @p, @a, @t, @t)",
                    new { c = $"SUP{i + 1:D4}", n = SupplierNames[i], p = "021" + rnd.NextInt64(1000000, 9999999), a = "Jl. Industri No." + (i + 1), t = now });
            }
        });
    }

    private void SeedProducts(User admin)
    {
        var rnd = new Random(42);
        var catIds = _products.GetCategories();
        var units = _products.GetUnits();
        for (var i = 0; i < 100; i++)
        {
            var cat = catIds[i % catIds.Count];
            var unit = units[i % units.Count];
            var cost = Money.Round((decimal)(rnd.Next(2, 200) * 500));
            var price = Money.Round(cost * (decimal)(1 + rnd.NextDouble() * 0.5));
            var stock = rnd.Next(0, 120);
            var minStock = rnd.Next(3, 10);
            var name = $"{cat.Name} #{i + 1:D3} " + ProductAdjectives[rnd.Next(ProductAdjectives.Length)];
            var product = new Product
            {
                Code = $"PRD{i + 1:D4}",
                Name = name,
                CategoryId = cat.Id,
                UnitId = unit.Id,
                PurchasePrice = cost,
                SellingPrice = price,
                Stock = stock,
                MinStock = minStock
            };
            var barcode = $"899{(long)rnd.NextInt64(100000000, 999999999)}{i:D3}";
            _products.SaveProduct(product, new[] { barcode }, admin.Id, admin.Username);
        }
    }

    private static readonly string[] ProductAdjectives = { "Premium", "Spesial", "Murah", "Baru", "Favorit", "Best", "Super", "Mantap", "Economis", "Jumbo" };

    private void SeedPurchases(User admin)
    {
        var suppliers = _products.GetSuppliers();
        var rnd = new Random(5);
        var allProducts = _products.GetQuickList();
        for (var p = 0; p < 8; p++)
        {
            var po = new Purchase
            {
                SupplierId = suppliers[p % suppliers.Count].Id,
                SupplierName = suppliers[p % suppliers.Count].Name,
                SupplierInvoiceNo = $"SINV-{rnd.Next(10000, 99999)}",
                Discount = 0
            };
            var count = rnd.Next(3, 8);
            for (var i = 0; i < count; i++)
            {
                var prod = allProducts[rnd.Next(allProducts.Count)];
                po.Items.Add(new PurchaseItem
                {
                    ProductId = prod.Id, ProductCode = prod.Code, ProductName = prod.Name,
                    Qty = rnd.Next(5, 40), Cost = prod.PurchasePrice
                });
            }
            _purchases.CreatePurchase(po, admin.Id, admin.Username);
        }
    }

    private void SeedSales(User admin)
    {
        var rnd = new Random(99);
        var customers = _products.GetCustomers();
        var allProducts = _products.GetQuickList();
        var methods = new[] { PaymentMethod.Cash, PaymentMethod.Cash, PaymentMethod.Qris, PaymentMethod.Debit };

        for (var day = 6; day >= 0; day--)
        {
            var txCount = 12 + rnd.Next(0, 6);
            for (var t = 0; t < txCount; t++)
            {
                var date = DateTime.Today.AddDays(-day)
                    .AddHours(8 + rnd.Next(0, 10)).AddMinutes(rnd.Next(0, 60));
                var items = new List<SaleItem>();
                var itemCount = rnd.Next(1, 5);
                for (var i = 0; i < itemCount; i++)
                {
                    var prod = allProducts[rnd.Next(allProducts.Count)];
                    var qty = rnd.Next(1, 4);
                    items.Add(new SaleItem
                    {
                        ProductId = prod.Id, ProductCode = prod.Code, ProductName = prod.Name,
                        Qty = qty, Price = prod.SellingPrice, Cost = prod.PurchasePrice
                    });
                }
                var subtotal = Money.Round(items.Sum(i => i.Price * i.Qty));
                var sale = new Sale
                {
                    CustomerId = customers[rnd.Next(customers.Count)].Id,
                    CustomerName = "Umum",
                    UserId = admin.Id,
                    CashierName = admin.Username,
                    Items = items,
                    Subtotal = subtotal,
                    Discount = rnd.Next(10) == 0 ? Money.Round(subtotal * 0.05m) : 0,
                    SaleDate = date,
                    Notes = ""
                };
                sale.Total = Money.Round(sale.Subtotal - sale.Discount);
                var method = methods[rnd.Next(methods.Length)];
                sale.Payments.Add(new SalePayment
                {
                    Method = method, Amount = sale.Total,
                    CreatedAt = date
                });

                // insert directly with the sale date (bypasses "now" timestamp used by CompleteSale)
                InsertSaleWithDate(sale, admin);
            }
        }
    }

    private void InsertSaleWithDate(Sale sale, User admin)
    {
        var now = DbEx.Iso(sale.SaleDate);
        _db.Transaction(c =>
        {
            sale.InvoiceNo = NextInvoiceNoFor(sale.SaleDate);
            sale.CostTotal = Money.Round(sale.Items.Sum(i => Money.Round(i.Cost * i.Qty)));
            sale.Id = c.ExecuteScalar<long>(@"INSERT INTO sales
                (invoice_no, sale_date, customer_id, user_id, subtotal, discount, total, cost_total, status, notes, created_at, updated_at)
                VALUES (@inv, @d, @cust, @uid, @sub, @disc, @tot, @cost, 'COMPLETED', '', @t, @t2);
                SELECT last_insert_rowid();",
                new
                {
                    inv = sale.InvoiceNo, d = now, cust = sale.CustomerId > 0 ? (long?)sale.CustomerId : null,
                    uid = admin.Id, sub = DbEx.MoneyParam(sale.Subtotal), disc = DbEx.MoneyParam(sale.Discount),
                    tot = DbEx.MoneyParam(sale.Total), cost = DbEx.MoneyParam(sale.CostTotal), t = now, t2 = now
                });
            foreach (var item in sale.Items)
            {
                item.SaleId = sale.Id;
                item.Subtotal = Money.Round(item.Price * item.Qty);
                c.Execute(@"INSERT INTO sale_items (sale_id, product_id, product_code, product_name, qty, price, cost, discount, subtotal)
                    VALUES (@sid, @pid, @pc, @pn, @q, @pr, @co, 0, @su)",
                    new { sid = sale.Id, pid = item.ProductId, pc = item.ProductCode, pn = item.ProductName, q = (double)item.Qty, pr = DbEx.MoneyParam(item.Price), co = DbEx.MoneyParam(item.Cost), su = DbEx.MoneyParam(item.Subtotal) });
                InventoryService.ApplyMovement(c, item.ProductId, StockRef.Sale, sale.Id, StockDirection.Out,
                    item.Qty, $"Penjualan {sale.InvoiceNo}", admin.Id, now);
            }
            foreach (var pay in sale.Payments)
            {
                c.Execute(@"INSERT INTO sale_payments (sale_id, method, amount, reference, created_at)
                    VALUES (@sid, @m, @a, '', @t)", new { sid = sale.Id, m = pay.Method.ToString(), a = DbEx.MoneyParam(pay.Amount), t = now });
            }
        });
    }

    private string NextInvoiceNoFor(DateTime date)
    {
        var d = date.ToString("yyyyMMdd");
        var prefix = $"INV-{d}-";
        var max = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(CAST(SUBSTR(invoice_no, LENGTH(@p)+1) AS INTEGER)),0) FROM sales WHERE invoice_no LIKE @l",
            new { p = prefix, l = prefix + "%" }));
        return $"{prefix}{max + 1:D4}";
    }

    private void SeedOpenShift(User admin)
    {
        if (_db.With(c => c.ExecuteScalar<long>("SELECT COUNT(*) FROM cash_sessions WHERE status='OPEN'")) == 0)
        {
            var now = DateTime.Now;
            var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO cash_sessions (user_id, opened_at, opening_cash, status, created_at, updated_at)
                VALUES (@uid, @d, 500000, 'OPEN', @t, @t); SELECT last_insert_rowid();",
                new { uid = admin.Id, d = DbEx.Iso(now), t = DbEx.Iso(now) }));
            _db.With(c => c.Execute(@"INSERT INTO cash_movements (cash_session_id, type, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                VALUES (@sid, 'OPENING', 'IN', 500000, 'SESSION', @sid, 'Modal awal (demo)', @uid, @t)",
                new { sid = id, uid = admin.Id, t = DbEx.Iso(now) }));
        }
    }
}

public static class SalePaymentExtensions
{
    public static DateTime MethodDate { get; set; }
}
