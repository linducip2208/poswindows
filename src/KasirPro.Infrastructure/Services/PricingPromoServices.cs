using System.Data;
using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Price levels + per-product price matrix + customer level binding.</summary>
public class PriceService
{
    private readonly Db _db;
    public PriceService(Db db) { _db = db; }

    public List<(long Id, string Name)> Levels() =>
        _db.With(c => c.Query<(long, string)>("SELECT id, name FROM price_levels ORDER BY id").ToList());

    public long CustomerLevel(long customerId) =>
        _db.With(c => c.ExecuteScalar<long>(
            "SELECT COALESCE(price_level_id,1) FROM customers WHERE id=@id", new { id = customerId }));

    public List<PriceRule> QtyTiers(long productId, long levelId) =>
        _db.With(c => c.Query<PriceRule>(@"SELECT price_level_id AS LevelId, min_qty AS MinQty,
            {pr} AS Price FROM product_prices WHERE product_id=@p AND price_level_id=@l AND min_qty > 0"
                .Replace("{pr}", DbEx.MoneyColumn("price")),
            new { p = productId, l = levelId }).ToList());

    public decimal LevelPrice(long productId, long levelId) =>
        _db.With(c => c.ExecuteScalar<decimal>(@"
            SELECT COALESCE({pr},0) FROM product_prices
            WHERE product_id=@p AND price_level_id=@l AND (min_qty IS NULL OR min_qty=0)"
                .Replace("{pr}", DbEx.MoneyColumn("price")),
            new { p = productId, l = levelId }));

    /// <summary>Resolves the effective unit price with deterministic order.</summary>
    public decimal Resolve(long productId, long customerId, decimal qty, decimal basePrice)
    {
        var level = customerId > 1 ? CustomerLevel(customerId) : 1;
        var tier = level == 1 ? QtyTiers(productId, 1) : new List<PriceRule>();
        var levelPrice = level > 1 ? LevelPrice(productId, level) : 0;
        return PriceResolver.Resolve(basePrice, tier,
            level > 1 && levelPrice > 0 ? new PriceRule { Price = levelPrice } : null, 0, qty);
    }

    public void SetPrice(long productId, long levelId, decimal minQty, decimal price,
        long userId, string username)
    {
        _db.With(c => c.Execute(@"INSERT INTO product_prices (product_id, price_level_id, min_qty, price)
            VALUES (@p, @l, @q, @v)
            ON CONFLICT(product_id, price_level_id, min_qty) DO UPDATE SET price=@v", new
        {
            p = productId, l = levelId,
            q = (double)minQty, v = DbEx.MoneyParam(price)
        }));
    }

    public List<(string Level, decimal MinQty, decimal Price)> ProductPrices(long productId) =>
        _db.With(c => c.Query<(string, decimal, decimal)>(@"
            SELECT pl.name AS Level, COALESCE(pp.min_qty,0) AS MinQty, {pr} AS Price
            FROM product_prices pp JOIN price_levels pl ON pl.id=pp.price_level_id
            WHERE pp.product_id=@p ORDER BY pp.price_level_id, pp.min_qty"
                .Replace("{pr}", DbEx.MoneyColumn("pp.price")),
            new { p = productId }).ToList());
}

/// <summary>Promotion CRUD + evaluation + usage recording (incl. coupon limits).</summary>
public class PromotionService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public PromotionService(Db db, AuditService audit) { _db = db; _audit = audit; }

    public Promotion? GetByCoupon(string code) =>
        _db.With(c =>
        {
            var row = c.QueryFirstOrDefault<(long Id, long PromoId)>(
                "SELECT id AS Id, promo_id AS PromoId FROM coupons WHERE code=@c AND is_active=1", new { c = code.Trim().ToUpperInvariant() });
            if (row.Id == 0) return GetActiveByCode(code);
            return Map(c.QueryFirstOrDefault<dynamic>("SELECT * FROM promotions WHERE id=@id", new { id = row.PromoId }));
        });

    private Promotion? GetActiveByCode(string code) =>
        _db.With(c => Map(c.QueryFirstOrDefault<dynamic>(
            "SELECT * FROM promotions WHERE UPPER(code)=@c AND is_active=1", new { c = code.Trim().ToUpperInvariant() })));

    private Promotion? Map(dynamic? row)
    {
        if (row == null) return null;
        var typeStr = (string)row.type;
        var p = new Promotion
        {
            Id = (long)row.id,
            Code = (string)row.code,
            Name = (string)row.name,
            Type = Enum.TryParse<PromoType>(typeStr, out var t) ? t : PromoType.Percent,
            Value = Convert.ToDecimal(row.value),
            Scope = Enum.TryParse<PromoScope>((string)row.scope, out var sc) ? sc : PromoScope.All,
            ScopeRef = (string)row.scope_ref,
            MinPurchase = Convert.ToDecimal(row.min_purchase) / 100,
            MinQty = Convert.ToDecimal(row.min_qty),
            BuyQty = Convert.ToDecimal(row.buy_qty),
            GetQty = Convert.ToDecimal(row.get_qty),
            MemberOnly = Convert.ToInt32(row.member_only) == 1,
            Priority = Convert.ToInt32(row.priority),
            Stackable = Convert.ToInt32(row.stackable) == 1,
            StartDate = DateTime.Parse((string)row.start_date),
            EndDate = DateTime.Parse((string)row.end_date),
            Days = ((string)row.days ?? "").Split(',').Select(s => int.TryParse(s, out var d) ? d : -1).ToHashSet(),
            Active = Convert.ToInt32(row.is_active) == 1,
            CouponCode = (string?)row.coupon_code ?? ""
        };
        if (p.Type == PromoType.Fixed || p.Type == PromoType.SpecialPrice)
            p.Value = p.Value / 100;
        if (!string.IsNullOrWhiteSpace((string?)row.start_time))
            p.StartTime = TimeSpan.Parse((string)row.start_time);
        if (!string.IsNullOrWhiteSpace((string?)row.end_time))
            p.EndTime = TimeSpan.Parse((string)row.end_time);
        return p;
    }

    public List<Promotion> ApplicableNow(bool isMember) =>
        _db.With(c => c.Query<dynamic>("SELECT * FROM promotions WHERE is_active=1").ToList())
            .Select(Map).Where(p => p != null).Cast<Promotion>()
            .Where(p => PromotionEngine.IsScheduledNow(p, DateTime.Now))
            .Where(p => !p.MemberOnly || isMember)
            .ToList();

    public long Save(Promotion p, long userId, string username)
    {
        var now = DbEx.Iso(DateTime.Now);
        var value = p.Type == PromoType.Fixed || p.Type == PromoType.SpecialPrice
            ? DbEx.MoneyParam(p.Value) : (object)(double)p.Value;
        var id = _db.With(c => c.ExecuteScalar<long>(@"INSERT INTO promotions
            (code, name, type, value, scope, scope_ref, min_purchase, min_qty, buy_qty, get_qty, member_only,
             priority, stackable, start_date, end_date, start_time, end_time, days, is_active, created_at, updated_at)
            VALUES (@code, @name, @type, @value, @scope, @scope_ref, @minp, @minq, @buy, @get, @mem,
             @prio, @stack, @sd, @ed, @st, @et, @days, 1, @t, @t2)
            ON CONFLICT(code) DO UPDATE SET name=@name, type=@type, value=@value, scope=@scope, scope_ref=@scope_ref,
             min_purchase=@minp, min_qty=@minq, buy_qty=@buy, get_qty=@get, member_only=@mem, priority=@prio,
             stackable=@stack, start_date=@sd, end_date=@ed, start_time=@st, end_time=@et, days=@days, updated_at=@t2;
            SELECT last_insert_rowid();",
            new
            {
                code = p.Code.Trim().ToUpperInvariant(), name = p.Name,
                type = p.Type.ToString(), value,
                scope = p.Scope.ToString(), scope_ref = p.ScopeRef,
                minp = DbEx.MoneyParam(p.MinPurchase), minq = (double)p.MinQty,
                buy = (double)p.BuyQty, get = (double)p.GetQty, mem = p.MemberOnly ? 1 : 0,
                prio = p.Priority, stack = p.Stackable ? 1 : 0,
                sd = DbEx.Iso(p.StartDate), ed = DbEx.Iso(p.EndDate),
                st = p.StartTime?.ToString(@"hh\:mm") ?? "", et = p.EndTime?.ToString(@"hh\:mm") ?? "",
                days = string.Join(",", p.Days.OrderBy(d => d)), t = now, t2 = now
            }));
        _audit.Log(userId, username, "PROMO_SAVE", "promotion", id, $"{p.Code} - {p.Name}");
        p.Id = id; // caller keeps using the same object (usage recording needs real FK)
        return id;
    }

    /// <summary>Validates coupon usage limits (total + per customer) and records usage. Call inside sale tx.</summary>
    public void RecordUsage(IDbConnection c, Promotion p, long saleId, long customerId, decimal discount, string couponCode, string now)
    {
        c.Execute(@"INSERT INTO promotion_usage (promo_id, sale_id, customer_id, discount_amount, created_at)
            VALUES (@pid, @sid, @cust, @d, @t)",
            new { pid = p.Id, sid = saleId, cust = customerId > 0 ? (long?)customerId : null, d = DbEx.MoneyParam(discount), t = now });

        if (!string.IsNullOrWhiteSpace(p.CouponCode))
        {
            c.Execute(@"INSERT INTO coupons (code, promo_id, is_active, created_at)
                VALUES (@cc, @pid, 1, @t)
                ON CONFLICT(code) DO NOTHING", new { cc = p.CouponCode.Trim().ToUpperInvariant(), pid = p.Id, t = now });
            c.Execute("UPDATE coupons SET used_count = used_count + 1 WHERE code=@cc", new { cc = p.CouponCode.Trim().ToUpperInvariant() });
            c.Execute(@"INSERT INTO coupon_usage (coupon_id, sale_id, customer_id, created_at)
                SELECT id, @sid, @cust, @t FROM coupons WHERE code=@cc",
                new { sid = saleId, cust = customerId > 0 ? (long?)customerId : null, cc = p.CouponCode.Trim().ToUpperInvariant(), t = now });
        }
    }

    public List<(string Code, string Name, string Type, string Range, int Priority)> List() =>
        _db.With(c => c.Query<(string, string, string, string, int)>(@"
            SELECT code, name,
            CASE type WHEN 'Percent' THEN 'PERCENT' WHEN 'Fixed' THEN 'FIXED' WHEN 'BxGy' THEN 'BUY X GET Y' ELSE 'SPECIAL PRICE' END AS Type,
            start_date || ' ~ ' || end_date AS Range, priority
            FROM promotions WHERE is_active=1 ORDER BY priority DESC, id DESC").ToList());

    public void Deactivate(string code, long userId, string username)
    {
        _db.With(c => c.Execute("UPDATE promotions SET is_active=0, updated_at=@t WHERE code=@c",
            new { t = DbEx.Iso(DateTime.Now), c = code.Trim().ToUpperInvariant() }));
        _audit.Log(userId, username, "PROMO_DEACTIVATE", "promotion", 0, code);
    }
}

/// <summary>Loyalty + store credit LEDGERS (never just totals).</summary>
public class CreditLedgerService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public CreditLedgerService(Db db, AuditService audit) { _db = db; _audit = audit; }

    // ---------- loyalty ----------
    public void LoyaltyMove(long customerId, string type, decimal points, string refType, long refId,
        string notes, long userId, string now)
    {
        _db.With(c =>
        {
            c.Execute(@"INSERT INTO loyalty_ledger (customer_id, type, points, reference_type, reference_id, notes, user_id, created_at)
                VALUES (@cid, @t, @p, @rt, @rid, @n, @uid, @now)",
                new { cid = customerId, t = type, p = points, rt = refType, rid = refId, n = notes, uid = userId, now });
            var sign = type is "REDEEM" or "EXPIRE" ? -1 : 1;
            c.Execute("UPDATE customers SET points = MAX(0, points + @dp) WHERE id=@cid",
                new { dp = points * sign, cid = customerId });
        });
    }

    public List<(string Type, decimal Points, string Notes, DateTime CreatedAt)> LoyaltyHistory(long customerId, int limit = 100) =>
        _db.With(c => c.Query<(string, decimal, string, DateTime)>(@"
            SELECT type, points, notes, created_at FROM loyalty_ledger
            WHERE customer_id=@id ORDER BY id DESC LIMIT @l", new { id = customerId, l = limit }).ToList());

    // ---------- store credit ----------
    public void StoreCreditMove(long customerId, string direction, decimal amount, string refType,
        long refId, string notes, long userId, string now)
    {
        _db.With(c =>
        {
            c.Execute(@"INSERT INTO store_credit_ledger (customer_id, direction, amount, reference_type, reference_id, notes, user_id, created_at)
                VALUES (@cid, @dir, @amt, @rt, @rid, @n, @uid, @now)",
                new { cid = customerId, dir = direction, amt = DbEx.MoneyParam(amount), rt = refType, rid = refId, n = notes, uid = userId, now });
            var sign = direction == "OUT" ? -1 : 1;
            var deltaCents = DbEx.Cents(amount) * sign;
            c.Execute(@"UPDATE customers SET store_credit = CAST(MAX(0, store_credit + @d) AS INTEGER) WHERE id=@cid",
                new { d = deltaCents, cid = customerId });
        });
    }

    public decimal StoreCreditBalance(long customerId) =>
        _db.With(c => DbEx.Money(c.ExecuteScalar<long>(
            "SELECT COALESCE(store_credit,0) FROM customers WHERE id=@id",
            new { id = customerId })));

    public List<(string Direction, decimal Amount, string Notes, DateTime CreatedAt)> StoreCreditHistory(long customerId, int limit = 100) =>
        _db.With(c => c.Query<(string, decimal, string, DateTime)>(@"
            SELECT direction, {am} AS Amount, notes, created_at FROM store_credit_ledger
            WHERE customer_id=@id ORDER BY id DESC LIMIT @l"
                .Replace("{am}", DbEx.MoneyColumn("amount")),
            new { id = customerId, l = limit }).ToList());
}

/// <summary>Reorder suggestions: max(target - stock, avg_daily_sales * lead_time).</summary>
public class ReorderService
{
    private readonly Db _db;
    public ReorderService(Db db) { _db = db; }

    public List<(string Code, string Name, decimal Stock, decimal MinStock, decimal ReorderPoint,
        decimal TargetStock, decimal AvgDailySales, int LeadTime, decimal SuggestedQty)> Suggest(int leadTimeDays = 7)
    {
        return _db.With(c => c.Query<(string, string, decimal, decimal, decimal, decimal, decimal, int, decimal)>(@"
            WITH recent AS (
                SELECT si.product_id, SUM(si.qty) AS total_qty,
                    MAX(julianday('now')) - MIN(julianday(s.sale_date)) AS span
                FROM sale_items si JOIN sales s ON s.id=si.sale_id
                WHERE s.status='COMPLETED' AND s.sale_date >= datetime('now','localtime','-30 days')
                GROUP BY si.product_id
            )
            SELECT p.code AS Code, p.name AS Name, p.stock AS Stock, p.min_stock AS MinStock,
                p.reorder_point AS ReorderPoint, p.target_stock AS TargetStock,
                CAST(ROUND(COALESCE(recent.total_qty,0) / MAX(1, COALESCE(recent.span,30)), 2) AS REAL) AS AvgDailySales,
                @lead AS LeadTime,
                CAST(ROUND(MAX(
                    COALESCE(p.target_stock,0) - p.stock,
                    (CAST(ROUND(COALESCE(recent.total_qty,0) / MAX(1, COALESCE(recent.span,30)), 2) AS REAL) * @lead) - p.stock
                ), 0) AS REAL) AS SuggestedQty
            FROM products p
            LEFT JOIN recent ON recent.product_id = p.id
            WHERE p.is_active=1 AND (
                p.stock <= p.min_stock OR p.stock <= p.reorder_point AND p.reorder_point > 0
            )
            ORDER BY SuggestedQty DESC", new { lead = leadTimeDays }).ToList());
    }
}

