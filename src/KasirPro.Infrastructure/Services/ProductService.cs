using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class ProductService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly InventoryService _inventory;

    public ProductService(Db db, AuditService audit, InventoryService inventory)
    { _db = db; _audit = audit; _inventory = inventory; }

    // ---------- categories ----------
    public List<Category> GetCategories() =>
        _db.With(c => c.Query<Category>("SELECT id AS Id, name AS Name FROM categories ORDER BY name").ToList());

    public Category SaveCategory(Category cat, long userId = 0, string username = "")
    {
        var now = DbEx.Iso(DateTime.Now);
        if (cat.Id == 0)
            cat.Id = _db.With(c => c.ExecuteScalar<long>(
                "INSERT INTO categories (name, created_at, updated_at) VALUES (@n, @t, @t2); SELECT last_insert_rowid();",
                new { n = cat.Name.Trim(), t = now, t2 = now }));
        else
            _db.With(c => c.Execute("UPDATE categories SET name=@n, updated_at=@t WHERE id=@id",
                new { n = cat.Name.Trim(), t = now, id = cat.Id }));
        _audit.Log(userId, username, AuditAction.ProductChange, "category", cat.Id, $"Kategori: {cat.Name}");
        return cat;
    }

    public void DeleteCategory(long id, long userId = 0, string username = "")
    {
        _db.With(c => c.Execute("DELETE FROM categories WHERE id=@id", new { id }));
        _audit.Log(userId, username, AuditAction.ProductChange, "category", id, "Kategori dihapus");
    }

    // ---------- units ----------
    public List<Unit> GetUnits() =>
        _db.With(c => c.Query<Unit>("SELECT id AS Id, name AS Name FROM units ORDER BY name").ToList());

    public Unit SaveUnit(Unit unit, long userId = 0, string username = "")
    {
        var now = DbEx.Iso(DateTime.Now);
        if (unit.Id == 0)
            unit.Id = _db.With(c => c.ExecuteScalar<long>(
                "INSERT INTO units (name, created_at, updated_at) VALUES (@n, @t, @t2); SELECT last_insert_rowid();",
                new { n = unit.Name.Trim(), t = now, t2 = now }));
        else
            _db.With(c => c.Execute("UPDATE units SET name=@n, updated_at=@t WHERE id=@id",
                new { n = unit.Name.Trim(), t = now, id = unit.Id }));
        _audit.Log(userId, username, AuditAction.ProductChange, "unit", unit.Id, $"Satuan: {unit.Name}");
        return unit;
    }

    // ---------- products ----------
    public PagedResult<Product> Search(string searchText, long categoryId, bool lowStockOnly,
        string sortBy, bool ascending, int page, int pageSize)
    {
        var where = new List<string> { "p.is_active=1" };
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            where.Add(@"(p.name LIKE @q OR p.code LIKE @q OR EXISTS
                        (SELECT 1 FROM product_barcodes b WHERE b.product_id=p.id AND b.barcode LIKE @q))");
            p.Add("q", "%" + searchText.Trim() + "%");
        }
        if (categoryId > 0) { where.Add("p.category_id=@c"); p.Add("c", categoryId); }
        if (lowStockOnly) where.Add("p.stock <= p.min_stock");

        var order = sortBy switch
        {
            "name" => "p.name",
            "stock" => "p.stock",
            "selling" => "p.selling_price",
            "purchase" => "p.purchase_price",
            _ => "p.name"
        };
        var dir = ascending ? "ASC" : "DESC";

        var whereSql = string.Join(" AND ", where);
        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($"SELECT COUNT(*) FROM products p WHERE {whereSql}", p);
            p.Add("limit", pageSize);
            p.Add("off", (page - 1) * pageSize);
            var items = c.Query<Product>($@"SELECT p.id AS Id, p.code AS Code, p.name AS Name,
                COALESCE(cat.name,'') AS CategoryName, COALESCE(u.name,'') AS UnitName,
                p.category_id AS CategoryId, p.unit_id AS UnitId,
                {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice,
                {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
                p.stock AS Stock, p.min_stock AS MinStock, p.image_path AS ImagePath
                FROM products p
                LEFT JOIN categories cat ON cat.id = p.category_id
                LEFT JOIN units u ON u.id = p.unit_id
                WHERE {whereSql}
                ORDER BY {order} {dir}
                LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<Product> { Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total };
        });
    }

    public Product? Get(long id) =>
        _db.With(c => c.QueryFirstOrDefault<Product>(@$"SELECT p.id AS Id, p.code AS Code, p.name AS Name,
            COALESCE(cat.name,'') AS CategoryName, COALESCE(u.name,'') AS UnitName,
            p.category_id AS CategoryId, p.unit_id AS UnitId,
            {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice,
            {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
            p.stock AS Stock, p.min_stock AS MinStock, p.image_path AS ImagePath, p.notes AS Notes
            FROM products p
            LEFT JOIN categories cat ON cat.id = p.category_id
            LEFT JOIN units u ON u.id = p.unit_id
            WHERE p.id=@id", new { id }));

    public Product? GetByCode(string code) =>
        _db.With(c => c.QueryFirstOrDefault<Product>(@$"SELECT p.id AS Id, p.code AS Code, p.name AS Name,
            COALESCE(cat.name,'') AS CategoryName, COALESCE(u.name,'') AS UnitName,
            p.category_id AS CategoryId, p.unit_id AS UnitId,
            {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice,
            {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
            p.stock AS Stock, p.min_stock AS MinStock
            FROM products p LEFT JOIN categories cat ON cat.id=p.category_id LEFT JOIN units u ON u.id=p.unit_id
            WHERE p.code=@code AND p.is_active=1", new { code = code.Trim() }));

    /// <summary>Exact barcode lookup (used by scanner, case-sensitive fast path via index).</summary>
    public Product? GetByBarcode(string barcode) =>
        _db.With(c => c.QueryFirstOrDefault<Product>(@$"SELECT p.id AS Id, p.code AS Code, p.name AS Name,
            COALESCE(cat.name,'') AS CategoryName, COALESCE(u.name,'') AS UnitName,
            p.category_id AS CategoryId, p.unit_id AS UnitId,
            {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice,
            {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
            p.stock AS Stock, p.min_stock AS MinStock
            FROM products p
            JOIN product_barcodes b ON b.product_id = p.id
            WHERE b.barcode = @bc AND p.is_active=1", new { bc = barcode.Trim() }));

    public List<Product> GetQuickList(long categoryId = 0, string search = "")
    {
        var where = new List<string> { "p.is_active=1" };
        var p = new DynamicParameters();
        if (categoryId > 0) { where.Add("p.category_id=@c"); p.Add("c", categoryId); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add(@"(p.name LIKE @q OR p.code LIKE @q OR EXISTS
                        (SELECT 1 FROM product_barcodes b WHERE b.product_id=p.id AND b.barcode LIKE @q))");
            p.Add("q", "%" + search.Trim() + "%");
        }
        return _db.With(c => c.Query<Product>($@"SELECT p.id AS Id, p.code AS Code, p.name AS Name,
            COALESCE(u.name,'') AS UnitName,
            {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
            {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice,
            p.stock AS Stock, p.min_stock AS MinStock
            FROM products p LEFT JOIN units u ON u.id=p.unit_id
            WHERE {string.Join(" AND ", where)}
            ORDER BY p.name LIMIT 200", p).ToList());
    }

    /// <summary>Saves a product with barcodes, atomically. Returns product id.</summary>
    public long SaveProduct(Product prod, string[] barcodes, long userId, string username)
    {
        var now = DbEx.Iso(DateTime.Now);
        var code = string.IsNullOrWhiteSpace(prod.Code)
            ? NextProductCode() : prod.Code.Trim();
        var cleanBarcodes = barcodes.Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim()).Distinct().ToArray();

        return _db.Transaction(c =>
        {
            // barcode uniqueness across products
            foreach (var b in cleanBarcodes)
            {
                var owner = c.ExecuteScalar<long>(
                    "SELECT product_id FROM product_barcodes WHERE barcode=@b", new { b });
                if (owner != 0 && owner != prod.Id)
                    throw new InvalidOperationException($"Barcode '{b}' sudah dipakai produk lain");
            }

            long id;
            if (prod.Id == 0)
            {
                id = c.ExecuteScalar<long>(@"INSERT INTO products
                    (code, name, category_id, unit_id, purchase_price, selling_price, stock, min_stock, image_path, notes, is_active, created_at, updated_at)
                    VALUES (@code, @name, @cat, @unit, @pp, @sp, @st, @ms, @img, @notes, 1, @t, @t2);
                    SELECT last_insert_rowid();",
                    new
                    {
                        code, name = prod.Name.Trim(),
                        cat = prod.CategoryId > 0 ? (long?)prod.CategoryId : null,
                        unit = prod.UnitId > 0 ? (long?)prod.UnitId : null,
                        pp = DbEx.MoneyParam(prod.PurchasePrice),
                        sp = DbEx.MoneyParam(prod.SellingPrice),
                        st = (double)prod.Stock,
                        ms = (double)prod.MinStock,
                        img = prod.ImagePath, notes = prod.Notes, t = now, t2 = now
                    });

                // opening stock ledger entry if stock > 0
                if (prod.Stock != 0)
                {
                    c.Execute(@"INSERT INTO stock_movements (product_id, reference_type, reference_id, direction, qty, stock_after, notes, user_id, created_at)
                        VALUES (@pid, @rt, 0, 'IN', @q, @sa, 'Stok awal', @uid, @t)",
                        new { pid = id, rt = StockRef.Opening, q = (double)prod.Stock, sa = (double)prod.Stock, uid = userId, t = now });
                }
            }
            else
            {
                id = prod.Id;
                c.Execute(@"UPDATE products SET code=@code, name=@name, category_id=@cat, unit_id=@unit,
                    purchase_price=@pp, selling_price=@sp, min_stock=@ms, image_path=@img, notes=@notes, updated_at=@t
                    WHERE id=@id",
                    new
                    {
                        code, name = prod.Name.Trim(),
                        cat = prod.CategoryId > 0 ? (long?)prod.CategoryId : null,
                        unit = prod.UnitId > 0 ? (long?)prod.UnitId : null,
                        pp = DbEx.MoneyParam(prod.PurchasePrice),
                        sp = DbEx.MoneyParam(prod.SellingPrice),
                        ms = (double)prod.MinStock,
                        img = prod.ImagePath, notes = prod.Notes, t = now, id
                    });
            }

            // rebuild barcodes
            c.Execute("DELETE FROM product_barcodes WHERE product_id=@id", new { id });
            foreach (var b in cleanBarcodes)
                c.Execute("INSERT INTO product_barcodes (product_id, barcode, created_at) VALUES (@id, @b, @t)",
                    new { id, b, t = now });

            return id;
        });
    }

    /// <summary>Soft-delete with confirmation handled in UI.</summary>
    public void DeleteProduct(long id, long userId, string username)
    {
        _db.With(c => c.Execute("UPDATE products SET is_active=0, updated_at=@t WHERE id=@id",
            new { t = DbEx.Iso(DateTime.Now), id }));
        _audit.Log(userId, username, AuditAction.ProductChange, "product", id, "Produk dihapus");
    }

    public string NextProductCode()
    {
        var prefix = "PRD" + DateTime.Now.ToString("yyyyMMdd");
        var last = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM products WHERE code LIKE @p", new { p = prefix + "%" }));
        return $"{prefix}{last + 1:D4}";
    }

    public string NextCustomerCode()
    {
        var last = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM customers WHERE code LIKE 'CUS%'"));
        return $"CUS{last + 1:D4}";
    }

    public string NextSupplierCode()
    {
        var last = _db.With(c => c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM suppliers WHERE code LIKE 'SUP%'"));
        return $"SUP{last + 1:D4}";
    }

    /// <summary>Bulk price update by percentage for a category. Returns affected count.</summary>
    public int UpdatePrices(long categoryId, bool updateSelling, bool updatePurchase, decimal percent,
        long userId, string username)
    {
        var where = categoryId > 0 ? "WHERE category_id=@c" : "";
        var affected = _db.Transaction(c =>
        {
            var n = 0;
            if (updateSelling)
                n += c.Execute($@"UPDATE products SET selling_price = CAST(ROUND(selling_price * (1 + (@pct/100.0))) AS INTEGER),
                    updated_at=@t {where}", new { pct = (double)percent, t = DbEx.Iso(DateTime.Now), c = categoryId });
            if (updatePurchase)
                n += c.Execute($@"UPDATE products SET purchase_price = CAST(ROUND(purchase_price * (1 + (@pct/100.0))) AS INTEGER),
                    updated_at=@t {where}", new { pct = (double)percent, t = DbEx.Iso(DateTime.Now), c = categoryId });
            return n;
        });
        _audit.Log(userId, username, AuditAction.ProductChange, "product", categoryId,
            $"Price update {percent}% ({(updateSelling ? "jual " : "")}{(updatePurchase ? "beli" : "")}) - {affected} produk");
        return affected;
    }

    // ---------- customers ----------
    public List<Customer> GetCustomers() =>
        _db.With(c => c.Query<Customer>("SELECT id AS Id, code AS Code, name AS Name, phone AS Phone FROM customers ORDER BY name").ToList());

    public Customer SaveCustomer(Customer cust, long userId, string username)
    {
        var now = DbEx.Iso(DateTime.Now);
        if (cust.Id == 0)
        {
            var code = string.IsNullOrWhiteSpace(cust.Code) ? NextCustomerCode() : cust.Code.Trim();
            cust.Id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO customers (code, name, phone, address, notes, created_at, updated_at)
                VALUES (@c, @n, @p, @a, @no, @t, @t2); SELECT last_insert_rowid();",
                new { c = code, n = cust.Name.Trim(), p = cust.Phone, a = cust.Address, no = cust.Notes, t = now, t2 = now }));
            cust.Code = code;
        }
        else
        {
            _db.With(c => c.Execute(@"UPDATE customers SET name=@n, phone=@p, address=@a, notes=@no, updated_at=@t WHERE id=@id",
                new { n = cust.Name.Trim(), p = cust.Phone, a = cust.Address, no = cust.Notes, t = now, id = cust.Id }));
        }
        _audit.Log(userId, username, AuditAction.CustomerChange, "customer", cust.Id, cust.Name);
        return cust;
    }

    public void DeleteCustomer(long id, long userId, string username)
    {
        if (id == 1) throw new InvalidOperationException("Pelanggan Umum tidak dapat dihapus");
        _db.With(c => c.Execute("DELETE FROM customers WHERE id=@id", new { id }));
        _audit.Log(userId, username, AuditAction.CustomerChange, "customer", id, "Customer dihapus");
    }

    // ---------- suppliers ----------
    public List<Supplier> GetSuppliers() =>
        _db.With(c => c.Query<Supplier>("SELECT id AS Id, code AS Code, name AS Name, phone AS Phone FROM suppliers ORDER BY name").ToList());

    public Supplier SaveSupplier(Supplier sup, long userId, string username)
    {
        var now = DbEx.Iso(DateTime.Now);
        if (sup.Id == 0)
        {
            var code = string.IsNullOrWhiteSpace(sup.Code) ? NextSupplierCode() : sup.Code.Trim();
            sup.Id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO suppliers (code, name, phone, address, notes, created_at, updated_at)
                VALUES (@c, @n, @p, @a, @no, @t, @t2); SELECT last_insert_rowid();",
                new { c = code, n = sup.Name.Trim(), p = sup.Phone, a = sup.Address, no = sup.Notes, t = now, t2 = now }));
            sup.Code = code;
        }
        else
        {
            _db.With(c => c.Execute(@"UPDATE suppliers SET name=@n, phone=@p, address=@a, notes=@no, updated_at=@t WHERE id=@id",
                new { n = sup.Name.Trim(), p = sup.Phone, a = sup.Address, no = sup.Notes, t = now, id = sup.Id }));
        }
        _audit.Log(userId, username, AuditAction.SupplierChange, "supplier", sup.Id, sup.Name);
        return sup;
    }

    public void DeleteSupplier(long id, long userId, string username)
    {
        _db.With(c => c.Execute("DELETE FROM suppliers WHERE id=@id", new { id }));
        _audit.Log(userId, username, AuditAction.SupplierChange, "supplier", id, "Supplier dihapus");
    }

    // ---------- import/export ----------
    public (int inserted, int updated) ImportProducts(IEnumerable<(string Code, string Name, string Barcode,
        string Category, string Unit, decimal PurchasePrice, decimal SellingPrice, decimal Stock)> rows,
        long userId, string username)
    {
        int ins = 0, upd = 0;
        var now = DbEx.Iso(DateTime.Now);
        _db.Transaction(c =>
        {
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Name)) continue;
                var catId = EnsureCategory(c, r.Category, now);
                var unitId = EnsureUnit(c, r.Unit, now);
                var existing = c.QueryFirstOrDefault<long?>(
                    "SELECT id FROM products WHERE code=@code", new { code = r.Code });
                if (existing.HasValue && existing.Value > 0)
                {
                    c.Execute(@"UPDATE products SET name=@n, category_id=@cat, unit_id=@u,
                        purchase_price=@pp, selling_price=@sp, updated_at=@t WHERE id=@id",
                        new
                        {
                            n = r.Name.Trim(), cat = catId, u = unitId,
                            pp = DbEx.MoneyParam(r.PurchasePrice), sp = DbEx.MoneyParam(r.SellingPrice),
                            t = now, id = existing.Value
                        });
                    upd++;
                    var pid = existing.Value;
                    if (!string.IsNullOrWhiteSpace(r.Barcode) && c.ExecuteScalar<long>(
                            "SELECT COUNT(*) FROM product_barcodes WHERE barcode=@b", new { b = r.Barcode.Trim() }) == 0)
                        c.Execute("INSERT INTO product_barcodes (product_id, barcode, created_at) VALUES (@p, @b, @t)",
                            new { p = pid, b = r.Barcode.Trim(), t = now });
                }
                else
                {
                    var code = string.IsNullOrWhiteSpace(r.Code) ? NextProductCode() : r.Code.Trim();
                    var pid = c.ExecuteScalar<long>(@"INSERT INTO products
                        (code, name, category_id, unit_id, purchase_price, selling_price, stock, min_stock, is_active, created_at, updated_at)
                        VALUES (@code, @n, @cat, @u, @pp, @sp, @st, 0, 1, @t, @t2); SELECT last_insert_rowid();",
                        new
                        {
                            code, n = r.Name.Trim(), cat = catId, u = unitId,
                            pp = DbEx.MoneyParam(r.PurchasePrice), sp = DbEx.MoneyParam(r.SellingPrice),
                            st = (double)r.Stock, t = now, t2 = now
                        });
                    if (!string.IsNullOrWhiteSpace(r.Barcode))
                        c.Execute("INSERT INTO product_barcodes (product_id, barcode, created_at) VALUES (@p, @b, @t)",
                            new { p = pid, b = r.Barcode.Trim(), t = now });
                    if (r.Stock != 0)
                        c.Execute(@"INSERT INTO stock_movements (product_id, reference_type, reference_id, direction, qty, stock_after, notes, user_id, created_at)
                            VALUES (@p, 'OPENING_STOCK', 0, 'IN', @q, @q, 'Import', @uid, @t)",
                            new { p = pid, q = (double)r.Stock, uid = userId, t = now });
                    ins++;
                }
            }
        });
        _audit.Log(userId, username, AuditAction.ProductChange, "product", 0, $"Import {ins} baru, {upd} update");
        return (ins, upd);
    }

    private static long EnsureCategory(System.Data.IDbConnection c, string name, string now)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var id = c.ExecuteScalar<long?>("SELECT id FROM categories WHERE name=@n", new { n = name.Trim() });
        if (id.HasValue) return id.Value;
        return c.ExecuteScalar<long>("INSERT INTO categories (name, created_at, updated_at) VALUES (@n, @t, @t); SELECT last_insert_rowid();",
            new { n = name.Trim(), t = now });
    }

    private static long EnsureUnit(System.Data.IDbConnection c, string name, string now)
    {
        if (string.IsNullOrWhiteSpace(name)) return 1;
        var id = c.ExecuteScalar<long?>("SELECT id FROM units WHERE name=@n", new { n = name.Trim().ToUpper() });
        if (id.HasValue) return id.Value;
        return c.ExecuteScalar<long>("INSERT INTO units (name, created_at, updated_at) VALUES (@n, @t, @t); SELECT last_insert_rowid();",
            new { n = name.Trim().ToUpper(), t = now });
    }
}
