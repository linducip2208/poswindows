using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Loyalty points: earn on completed sales, redeem as Points payment method.</summary>
public class LoyaltyService
{
    private readonly Db _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;

    public LoyaltyService(Db db, SettingsService settings, AuditService audit)
    { _db = db; _settings = settings; _audit = audit; }

    public bool Enabled => _settings.Get("loyalty_enabled", "0") == "1";
    public decimal EarnPer1000 => decimal.TryParse(_settings.Get("loyalty_earn_per_1000", "1"),
        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var e) ? Math.Max(0, e) : 1;
    /// <summary>1 point = this many rupiah when redeeming.</summary>
    public decimal PointValue => decimal.TryParse(_settings.Get("loyalty_point_value", "100"),
        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Max(1, v) : 100;

    /// <summary>Points earned for a net sale amount.</summary>
    public decimal EarnFor(decimal netTotal) =>
        Enabled ? Math.Floor(netTotal / 1000m * EarnPer1000) : 0;

    /// <summary>Redeemable rupiah value of a customer's points.</summary>
    public decimal RedeemableValue(Customer c) =>
        Enabled && c.Id > 1 ? Money.Round(c.Points * PointValue) : 0;

    /// <summary>Applies earning + redemption for a completed sale, atomically, with ledger rows.</summary>
    public void ApplyForSale(long saleId, long customerId, decimal netTotal, decimal redeemedValue, long userId, string username)
    {
        if (!Enabled || customerId <= 1) return;
        var now = DbEx.Iso(DateTime.Now);
        var earned = EarnFor(netTotal);
        var redeemedPoints = redeemedValue > 0 ? Math.Ceiling(redeemedValue / PointValue) : 0;

        _db.With(c =>
        {
            if (redeemedPoints > 0)
                c.Execute("UPDATE customers SET points = points - @p, updated_at=@t WHERE id=@id",
                    new { p = redeemedPoints, t = now, id = customerId });
            if (earned > 0)
                c.Execute("UPDATE customers SET points = points + @p, updated_at=@t WHERE id=@id",
                    new { p = earned, t = now, id = customerId });
            _audit.InTx(c, userId, username, "LOYALTY", "customer", customerId,
                $"Sale #{saleId}: earn {earned} poin, redeem {redeemedPoints} poin ({Money.Format(redeemedValue)})");
        });
    }

    public decimal GetPoints(long customerId) =>
        _db.With(c => c.ExecuteScalar<decimal>("SELECT points FROM customers WHERE id=@id", new { id = customerId }));
}

/// <summary>Extra report queries: by hour, by payment method, by category.</summary>
public class AnalyticsService
{
    private readonly Db _db;
    public AnalyticsService(Db db) { _db = db; }

    public List<(int Hour, int Count, decimal Total)> ByHour(DateTime from, DateTime to) =>
        _db.With(c => c.Query<(int, int, decimal)>(@"
            SELECT CAST(strftime('%H', sale_date) AS INTEGER) AS Hour, COUNT(*) AS Count,
            CAST(ROUND(SUM(total)/100.0,2) AS REAL) AS Total
            FROM sales WHERE status='COMPLETED' AND sale_date >= @f AND sale_date <= @t2
            GROUP BY Hour ORDER BY Hour",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    public List<(string Method, int Count, decimal Total)> ByPayment(DateTime from, DateTime to) =>
        _db.With(c => c.Query<(string, int, decimal)>(@"
            SELECT sp.method AS Method, COUNT(DISTINCT s.id) AS Count,
            CAST(ROUND(SUM(sp.amount)/100.0,2) AS REAL) AS Total
            FROM sale_payments sp JOIN sales s ON s.id=sp.sale_id
            WHERE s.status='COMPLETED' AND s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY sp.method ORDER BY Total DESC",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());

    public List<(string Category, int Qty, decimal Total)> ByCategory(DateTime from, DateTime to) =>
        _db.With(c => c.Query<(string, int, decimal)>(@"
            SELECT COALESCE(cat.name,'(tanpa kategori)') AS Category,
            CAST(ROUND(SUM(si.qty),0) AS INTEGER) AS Qty,
            CAST(ROUND(SUM(si.subtotal)/100.0,2) AS REAL) AS Total
            FROM sale_items si JOIN sales s ON s.id=si.sale_id
            LEFT JOIN products pr ON pr.id=si.product_id
            LEFT JOIN categories cat ON cat.id=pr.category_id
            WHERE s.status='COMPLETED' AND s.sale_date >= @f AND s.sale_date <= @t2
            GROUP BY cat.id ORDER BY Total DESC",
            new { f = DbEx.Iso(from.Date), t2 = DbEx.Iso(to.Date.AddDays(1).AddSeconds(-1)) }).ToList());
}
