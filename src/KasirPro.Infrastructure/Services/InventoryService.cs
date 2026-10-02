using System.Data;
using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

public class InventoryService
{
    private readonly Db _db;
    private readonly AuditService _audit;
    private readonly PeriodCloseService? _periods;

    public InventoryService(Db db, AuditService audit, PeriodCloseService? periods = null) { _db = db; _audit = audit; _periods = periods; }

    /// <summary>
    /// Applies a stock movement + updates products.stock atomically.
    /// MUST be called inside an open transaction (the POS guarantees data integrity this way).
    /// </summary>
    public static decimal ApplyMovement(IDbConnection c, long productId, string refType, long refId,
        StockDirection direction, decimal qty, string notes, long userId, string now)
    {
        var stock = c.ExecuteScalar<decimal>("SELECT stock FROM products WHERE id=@id", new { id = productId });
        var newStock = direction == StockDirection.In ? stock + qty : stock - qty;
        c.Execute("UPDATE products SET stock=@s, updated_at=@t WHERE id=@id",
            new { s = (double)newStock, t = now, id = productId });
        c.Execute(@"INSERT INTO stock_movements (product_id, reference_type, reference_id, direction, qty, stock_after, notes, user_id, created_at)
            VALUES (@p, @rt, @rid, @dir, @q, @sa, @n, @uid, @t)",
            new
            {
                p = productId, rt = refType, rid = refId,
                dir = direction == StockDirection.In ? "IN" : "OUT",
                q = (double)qty, sa = (double)newStock, n = notes, uid = userId, t = now
            });
        return newStock;
    }

    // ---------- manual stock operations ----------

    public void StockIn(long productId, decimal qty, string notes, long userId, string username, string refType = StockRef.ManualIn)
    {
        _periods?.EnsureOpen(DateTime.Now);
        if (qty <= 0) throw new InvalidOperationException("Jumlah harus lebih dari 0");
        var id = _db.Transaction(c => NextRefId(c));
        ApplyInTx(productId, refType, id, StockDirection.In, qty, notes, userId, username);
        _audit.Log(userId, username, AuditAction.StockIn, "product", productId, $"Stock In {qty} - {notes}");
    }

    public void StockOut(long productId, decimal qty, string notes, long userId, string username, string refType = StockRef.ManualOut)
    {
        _periods?.EnsureOpen(DateTime.Now);
        if (qty <= 0) throw new InvalidOperationException("Jumlah harus lebih dari 0");
        var id = _db.Transaction(c => NextRefId(c));
        ApplyInTx(productId, refType, id, StockDirection.Out, qty, notes, userId, username);
        _audit.Log(userId, username, AuditAction.StockOut, "product", productId, $"Stock Out {qty} - {notes}");
    }

    /// <summary>Set stock to an exact value (difference becomes an ADJUSTMENT movement).</summary>
    public void Adjust(long productId, decimal newStock, string notes, long userId, string username)
    {
        _periods?.EnsureOpen(DateTime.Now);
        _db.Transaction(c =>
        {
            var current = c.ExecuteScalar<decimal>("SELECT stock FROM products WHERE id=@id", new { id = productId });
            var delta = newStock - current;
            if (delta == 0) throw new InvalidOperationException("Stok tidak berubah");
            var now = DbEx.Iso(DateTime.Now);
            ApplyMovement(c, productId, StockRef.Adjustment, 0,
                delta > 0 ? StockDirection.In : StockDirection.Out, Math.Abs(delta),
                notes, userId, now);
        });
        _audit.Log(userId, username, AuditAction.StockAdjustment, "product", productId,
            $"Adjust ke {newStock} - {notes}");
    }

    private void ApplyInTx(long productId, string refType, long refId, StockDirection dir, decimal qty, string notes, long userId, string username)
    {
        _db.Transaction(c =>
        {
            ApplyMovement(c, productId, refType, refId, dir, qty, notes, userId, DbEx.Iso(DateTime.Now));
        });
    }

    private static long NextRefId(IDbConnection c) => 0;

    // ---------- queries ----------

    public PagedResult<StockRow> CurrentStock(string search, long categoryId, bool lowOnly, int page, int pageSize)
    {
        var where = new List<string> { "p.is_active=1" };
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search)) { where.Add("p.name LIKE @q"); p.Add("q", "%" + search.Trim() + "%"); }
        if (categoryId > 0) { where.Add("p.category_id=@c"); p.Add("c", categoryId); }
        if (lowOnly) where.Add("p.stock <= p.min_stock");
        var w = string.Join(" AND ", where);

        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($"SELECT COUNT(*) FROM products p WHERE {w}", p);
            p.Add("limit", pageSize);
            p.Add("off", (page - 1) * pageSize);
            var items = c.Query<StockRow>($@"SELECT p.code AS Code, p.name AS Name, COALESCE(cat.name,'') AS Category,
                p.stock AS Stock, COALESCE(u.name,'') AS Unit, p.min_stock AS MinStock,
                {DbEx.MoneyColumn("p.purchase_price")} AS PurchasePrice,
                {DbEx.MoneyColumn("p.selling_price")} AS SellingPrice,
                CAST(ROUND((p.stock * p.purchase_price)/100.0, 2) AS REAL) AS StockValue
                FROM products p LEFT JOIN categories cat ON cat.id=p.category_id LEFT JOIN units u ON u.id=p.unit_id
                WHERE {w} ORDER BY p.name LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<StockRow> { Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total };
        });
    }

    public PagedResult<StockMovementRow> Movements(string search, long productId, string refType,
        DateTime from, DateTime to, int page, int pageSize)
    {
        var where = new List<string>
        {
            "sm.created_at >= @f", "sm.created_at <= @t2"
        };
        var p = new DynamicParameters();
        p.Add("f", DbEx.Iso(from.Date));
        p.Add("t2", DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)));
        if (productId > 0) { where.Add("sm.product_id=@p"); p.Add("p", productId); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(pr.name LIKE @q OR pr.code LIKE @q)");
            p.Add("q", "%" + search.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(refType)) { where.Add("sm.reference_type=@rt"); p.Add("rt", refType); }
        var w = string.Join(" AND ", where);

        return _db.With(c =>
        {
            var total = c.ExecuteScalar<long>($@"SELECT COUNT(*) FROM stock_movements sm
                JOIN products pr ON pr.id = sm.product_id WHERE {w}", p);
            p.Add("limit", pageSize);
            p.Add("off", (page - 1) * pageSize);
            var items = c.Query<StockMovementRow>($@"SELECT sm.created_at AS Date, pr.code AS Code, pr.name AS Product,
                sm.reference_type AS ReferenceType, sm.reference_id AS ReferenceId,
                sm.direction AS Direction, sm.qty AS Qty, sm.stock_after AS StockAfter,
                COALESCE(us.username,'') AS User, sm.notes AS Notes
                FROM stock_movements sm
                JOIN products pr ON pr.id = sm.product_id
                LEFT JOIN users us ON us.id = sm.user_id
                WHERE {w} ORDER BY sm.id DESC LIMIT @limit OFFSET @off", p).ToList();
            return new PagedResult<StockMovementRow> { Items = items, Page = page, PageSize = pageSize, TotalItems = (int)total };
        });
    }

    public List<StockRow> LowStock() =>
        _db.With(c => c.Query<StockRow>(@"SELECT p.code AS Code, p.name AS Name, COALESCE(cat.name,'') AS Category,
            p.stock AS Stock, COALESCE(u.name,'') AS Unit, p.min_stock AS MinStock,
            {pp} AS PurchasePrice, {sp} AS SellingPrice,
            CAST(ROUND((p.stock * p.purchase_price)/100.0, 2) AS REAL) AS StockValue
            FROM products p
            LEFT JOIN categories cat ON cat.id=p.category_id
            LEFT JOIN units u ON u.id=p.unit_id
            WHERE p.is_active=1 AND p.stock <= p.min_stock
            ORDER BY (p.stock - p.min_stock) ASC".Replace("{pp}", DbEx.MoneyColumn("p.purchase_price"))
                .Replace("{sp}", DbEx.MoneyColumn("p.selling_price"))).ToList());

    public int LowStockCount() =>
        _db.With(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM products WHERE is_active=1 AND stock <= min_stock"));

    // ---------- stock opname ----------

    public StockOpname CreateOpname(long userId, string username, string notes)
    {
        var now = DateTime.Now;
        var no = NextNo(_db, "OPN", "stock_opnames", "opname_no");
        var id = _db.Transaction(c =>
        {
            var opId = c.ExecuteScalar<long>(@"INSERT INTO stock_opnames (opname_no, opname_date, status, user_id, notes, created_at, updated_at)
                VALUES (@no, @d, 'DRAFT', @uid, @n, @t, @t2); SELECT last_insert_rowid();",
                new { no, d = DbEx.Iso(now), uid = userId, n = notes, t = DbEx.Iso(now), t2 = DbEx.Iso(now) });

            // snapshot all active products with system qty
            c.Execute(@"INSERT INTO stock_opname_items (stock_opname_id, product_id, system_qty, counted_qty, difference, adjusted)
                SELECT @oid, p.id, p.stock, p.stock, 0, 0 FROM products p WHERE p.is_active=1 ORDER BY p.name", new { oid = opId });
            return opId;
        });
        _audit.Log(userId, username, AuditAction.StockOpname, "stock_opname", id, $"Sesi opname {no} dibuat");
        return GetOpname(id)!;
    }

    public StockOpname? GetOpname(long id) =>
        _db.With(c =>
        {
            var op = c.QueryFirstOrDefault<StockOpname>(@"SELECT id AS Id, opname_no AS OpnameNo, opname_date AS OpnameDate,
                status AS Status, user_id AS UserId, notes AS Notes, created_at AS CreatedAt
                FROM stock_opnames WHERE id=@id", new { id });
            if (op == null) return null;
            var uname = c.ExecuteScalar<string>("SELECT username FROM users WHERE id=@u", new { u = op.UserId }) ?? "";
            op.UserName = uname;
            op.Items = c.Query<StockOpnameItem>(@"SELECT soi.id AS Id, soi.product_id AS ProductId, pr.code AS ProductCode,
                pr.name AS ProductName, soi.system_qty AS SystemQty, soi.counted_qty AS CountedQty, soi.difference AS Difference,
                soi.adjusted AS Adjusted
                FROM stock_opname_items soi JOIN products pr ON pr.id=soi.product_id
                WHERE soi.stock_opname_id=@id ORDER BY pr.name", new { id }).ToList();
            return op;
        });

    public List<StockOpname> GetOpnames(int limit = 100) =>
        _db.With(c => c.Query<StockOpname>(@"SELECT o.id AS Id, o.opname_no AS OpnameNo, o.opname_date AS OpnameDate,
            o.status AS Status, o.user_id AS UserId, COALESCE(u.username,'') AS UserName, o.notes AS Notes
            FROM stock_opnames o LEFT JOIN users u ON u.id=o.user_id
            ORDER BY o.id DESC LIMIT @l", new { l = limit }).ToList());

    /// <summary>Saves counted quantities for a draft opname.</summary>
    public void SaveOpnameCounts(long opnameId, Dictionary<long, decimal> counted, long userId, string username)
    {
        _db.Transaction(c =>
        {
            foreach (var (productId, qty) in counted)
            {
                c.Execute(@"UPDATE stock_opname_items SET counted_qty=@q, difference = (@q - system_qty)
                    WHERE stock_opname_id=@oid AND product_id=@pid",
                    new { q = (double)qty, oid = opnameId, pid = productId });
            }
        });
    }

    /// <summary>Posts the opname: applies differences as OPNAME movements with audit trail.</summary>
    public void PostOpname(long opnameId, long userId, string username)
    {
        _periods?.EnsureOpen(DateTime.Now);
        _db.Transaction(c =>
        {
            var status = c.ExecuteScalar<string>("SELECT status FROM stock_opnames WHERE id=@id", new { id = opnameId });
            if (status == "POSTED") throw new InvalidOperationException("Opname sudah diposting");
            var now = DbEx.Iso(DateTime.Now);
            var diffs = c.Query<(long ProductId, decimal Difference)>(
                "SELECT product_id AS ProductId, difference AS Difference FROM stock_opname_items WHERE stock_opname_id=@id",
                new { id = opnameId }).ToList();
            foreach (var d in diffs)
            {
                if (d.Difference == 0) continue;
                ApplyMovement(c, d.ProductId, StockRef.Opname, opnameId,
                    d.Difference > 0 ? StockDirection.In : StockDirection.Out,
                    Math.Abs(d.Difference), "Stock opname", userId, now);
                c.Execute("UPDATE stock_opname_items SET adjusted=1 WHERE stock_opname_id=@oid AND product_id=@pid",
                    new { oid = opnameId, pid = d.ProductId });
            }
            c.Execute("UPDATE stock_opnames SET status='POSTED', updated_at=@t WHERE id=@id", new { t = now, id = opnameId });
        });
        _audit.Log(userId, username, AuditAction.StockOpname, "stock_opname", opnameId, "Opname diposting + penyesuaian stok");
    }

    internal static string NextNo(Db db, string prefix, string table, string column)
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        var like = $"{prefix}-{date}-%";
        var count = db.With(c => c.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table} WHERE {column} LIKE @l", new { l = like }));
        return $"{prefix}-{date}-{count + 1:D4}";
    }
}

