using Dapper;
using KasirPro.Core.Domain;

namespace KasirPro.Infrastructure.Services;

/// <summary>Park / recall shopping carts (per user).</summary>
public class HoldService
{
    private readonly Db _db;
    public HoldService(Db db) { _db = db; }

    public long Save(string label, long userId, long customerId, string customerName,
        decimal discount, List<CartLine> lines, long? existingId = null)
    {
        var totals = SaleCalculator.Calculate(lines, discount);
        var now = DbEx.Iso(DateTime.Now);
        var json = HoldSerializer.ToJson(lines);
        return _db.With(c =>
        {
            if (existingId.HasValue && existingId.Value > 0)
            {
                c.Execute(@"UPDATE holds SET label=@l, customer_id=@cust, customer_name=@cn, item_count=@ic,
                    subtotal=@sub, discount=@disc, total=@tot, items_json=@j, updated_at=@t WHERE id=@id",
                    new
                    {
                        l = label, cust = customerId > 0 ? (long?)customerId : null, cn = customerName,
                        ic = lines.Count(l => l.Qty > 0), sub = DbEx.MoneyParam(totals.Subtotal),
                        disc = DbEx.MoneyParam(totals.Discount), tot = DbEx.MoneyParam(totals.GrandTotal),
                        j = json, t = now, id = existingId.Value
                    });
                return existingId.Value;
            }
            return c.ExecuteScalar<long>(@"INSERT INTO holds (label, user_id, customer_id, customer_name, item_count,
                subtotal, discount, total, items_json, created_at, updated_at)
                VALUES (@l, @uid, @cust, @cn, @ic, @sub, @disc, @tot, @j, @t, @t2); SELECT last_insert_rowid();",
                new
                {
                    l = label, uid = userId, cust = customerId > 0 ? (long?)customerId : null, cn = customerName,
                    ic = lines.Count(x => x.Qty > 0), sub = DbEx.MoneyParam(totals.Subtotal),
                    disc = DbEx.MoneyParam(totals.Discount), tot = DbEx.MoneyParam(totals.GrandTotal),
                    j = json, t = now, t2 = now
                });
        });
    }

    public List<Hold> List(long userId, int limit = 50) =>
        _db.With(c => c.Query<Hold>(@"SELECT id AS Id, label AS Label, user_id AS UserId,
            COALESCE(customer_id,0) AS CustomerId, customer_name AS CustomerName,
            item_count AS ItemCount, {sub} AS Subtotal, {disc} AS Discount, {tot} AS Total, created_at AS CreatedAt
            FROM holds WHERE user_id=@uid ORDER BY updated_at DESC LIMIT @l"
                .Replace("{sub}", DbEx.MoneyColumn("subtotal"))
                .Replace("{disc}", DbEx.MoneyColumn("discount"))
                .Replace("{tot}", DbEx.MoneyColumn("total")),
            new { uid = userId, l = limit }).ToList());

    public Hold? Get(long id) =>
        _db.With(c =>
        {
            var h = c.QueryFirstOrDefault<Hold>(@"SELECT id AS Id, label AS Label, user_id AS UserId,
                COALESCE(customer_id,0) AS CustomerId, customer_name AS CustomerName,
                item_count AS ItemCount, {sub} AS Subtotal, {disc} AS Discount, {tot} AS Total,
                items_json AS Items2, created_at AS CreatedAt
                FROM holds WHERE id=@id"
                    .Replace("{sub}", DbEx.MoneyColumn("subtotal"))
                    .Replace("{disc}", DbEx.MoneyColumn("discount"))
                    .Replace("{tot}", DbEx.MoneyColumn("total")),
                new { id });
            if (h == null) return null;
            var raw = c.ExecuteScalar<string>("SELECT items_json FROM holds WHERE id=@id", new { id });
            h.Items = HoldSerializer.FromJson(raw);
            return h;
        });

    public void Delete(long id) =>
        _db.With(c => c.Execute("DELETE FROM holds WHERE id=@id", new { id }));
}

/// <summary>X (session running) and Z (shift close) reports with financial summary.</summary>
public class XZReportService
{
    private readonly Db _db;
    private readonly AuditService _audit;

    public XZReportService(Db db, AuditService audit) { _db = db; _audit = audit; }

    /// <summary>Builds and stores an X (live) or Z (final) report for a cash session.</summary>
    public XZReport Generate(long? cashSessionId, string type, long userId, string username, decimal? actualCash = null)
    {
        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(1).AddSeconds(-1);

        return _db.Transaction(c =>
        {
            var reportNo = InventoryService.NextNo(_db, type == "Z" ? "Z" : "X", "xz_reports", "report_no");
            var now = DbEx.Iso(DateTime.Now);

            var sessionFilter = cashSessionId > 0 ? "AND s.cash_session_id=@csid" : "";
            var p = new DynamicParameters();
            if (cashSessionId > 0) p.Add("csid", cashSessionId);
            p.Add("f", DbEx.Iso(from)); p.Add("t2", DbEx.Iso(to));

            var agg = c.QueryFirstOrDefault<(int Count, decimal Gross, decimal Disc, decimal Tax, decimal Net)>(
                $@"SELECT COUNT(*) AS Count,
                CAST(ROUND(SUM(subtotal)/100.0,2) AS REAL) AS Gross,
                CAST(ROUND(SUM(discount)/100.0,2) AS REAL) AS Disc,
                CAST(ROUND(SUM(tax)/100.0,2) AS REAL) AS Tax,
                CAST(ROUND(SUM(total)/100.0,2) AS REAL) AS Net
                FROM sales s WHERE s.status='COMPLETED' AND s.sale_date BETWEEN @f AND @t2 {sessionFilter}", p);

            var byPayment = c.Query<(string Method, decimal Amount)>(
                $@"SELECT sp.method AS Method, CAST(ROUND(SUM(sp.amount)/100.0,2) AS REAL) AS Amount
                FROM sale_payments sp JOIN sales s ON s.id=sp.sale_id
                WHERE s.status='COMPLETED' AND s.sale_date BETWEEN @f AND @t2 {sessionFilter}
                GROUP BY sp.method", p).ToDictionary(x => x.Method, x => x.Amount);

            var cashIn = cashSessionId > 0
                ? c.ExecuteScalar<decimal>("SELECT {v} FROM cash_sessions WHERE id=@csid".Replace("{v}", DbEx.MoneyColumn("cash_in")), new { csid = cashSessionId }) : 0;
            var cashOut = cashSessionId > 0
                ? c.ExecuteScalar<decimal>("SELECT {v} FROM cash_sessions WHERE id=@csid".Replace("{v}", DbEx.MoneyColumn("cash_out")), new { csid = cashSessionId }) : 0;
            var debtSettle = cashSessionId > 0
                ? c.ExecuteScalar<decimal>("SELECT {v} FROM cash_sessions WHERE id=@csid".Replace("{v}", DbEx.MoneyColumn("debt_payments")), new { csid = cashSessionId }) : 0;
            var refunds = c.ExecuteScalar<decimal>(
                $@"SELECT COALESCE(CAST(ROUND(SUM(total)/100.0,2) AS REAL),0) FROM sale_returns
                WHERE return_date BETWEEN @f AND @t2", p);
            var opening = cashSessionId > 0
                ? c.ExecuteScalar<decimal>("SELECT {v} FROM cash_sessions WHERE id=@csid".Replace("{v}", DbEx.MoneyColumn("opening_cash")), new { csid = cashSessionId }) : 0;

            var cashSales = byPayment.GetValueOrDefault("Cash");
            var expected = Money.Round(opening + cashSales + debtSettle + cashIn - cashOut);

            var id = c.ExecuteScalar<long>(@"INSERT INTO xz_reports (report_no, type, cash_session_id, user_id, generated_at,
                sales_count, gross_sales, discounts, tax, net_sales, payment_breakdown, cash_in, cash_out, debt_settlements,
                refunds, opening_cash, expected_cash, actual_cash, difference, created_at, updated_at)
                VALUES (@no, @type, @csid, @uid, @gen, @cnt, @gross, @disc, @tax, @net, @pb, @cin, @cout, @debt,
                @ref, @open, @exp, @act, @diff, @t, @t2); SELECT last_insert_rowid();",
                new
                {
                    no = reportNo, type, csid = cashSessionId > 0 ? (long?)cashSessionId : null,
                    uid = userId, gen = now, cnt = agg.Count, gross = DbEx.MoneyParam(agg.Gross),
                    disc = DbEx.MoneyParam(agg.Disc), tax = DbEx.MoneyParam(agg.Tax), net = DbEx.MoneyParam(agg.Net),
                    pb = System.Text.Json.JsonSerializer.Serialize(byPayment),
                    cin = DbEx.MoneyParam(cashIn), cout = DbEx.MoneyParam(cashOut),
                    debt = DbEx.MoneyParam(debtSettle), @ref = DbEx.MoneyParam(refunds),
                    open = DbEx.MoneyParam(opening), exp = DbEx.MoneyParam(expected),
                    act = DbEx.MoneyParam(actualCash ?? 0),
                    diff = DbEx.MoneyParam(actualCash.HasValue ? Money.Round(actualCash.Value - expected) : 0),
                    t = now, t2 = now
                });

            _audit.InTx(c, userId, username, type == "Z" ? "Z_REPORT" : "X_REPORT", "xz_report", id, reportNo);

            // build result from known values (reading inside the tx would miss uncommitted rows in other connections)
            return new XZReport
            {
                Id = id,
                ReportNo = reportNo,
                Type = type,
                CashSessionId = cashSessionId ?? 0,
                UserId = userId,
                GeneratedAt = DateTime.Now,
                SalesCount = agg.Count,
                GrossSales = agg.Gross,
                Discounts = agg.Disc,
                Tax = agg.Tax,
                NetSales = agg.Net,
                PaymentBreakdown = System.Text.Json.JsonSerializer.Serialize(byPayment),
                CashIn = cashIn,
                CashOut = cashOut,
                DebtSettlements = debtSettle,
                Refunds = refunds,
                OpeningCash = opening,
                ExpectedCash = expected,
                ActualCash = actualCash ?? 0,
                Difference = actualCash.HasValue ? Money.Round(actualCash.Value - expected) : 0
            };
        });
    }

    public XZReport? Get(long id) =>
        _db.With(c => c.QueryFirstOrDefault<XZReport>(@"SELECT id AS Id, report_no AS ReportNo, type AS Type,
            COALESCE(cash_session_id,0) AS CashSessionId, user_id AS UserId, generated_at AS GeneratedAt,
            sales_count AS SalesCount, {gross} AS GrossSales, {disc} AS Discounts, {tax} AS Tax, {net} AS NetSales,
            payment_breakdown AS PaymentBreakdown, {cin} AS CashIn, {cout} AS CashOut, {debt} AS DebtSettlements,
            {rf} AS Refunds, {open} AS OpeningCash, {exp} AS ExpectedCash, {act} AS ActualCash, {diff} AS Difference
            FROM xz_reports WHERE id=@id"
                .Replace("{gross}", DbEx.MoneyColumn("gross_sales"))
                .Replace("{disc}", DbEx.MoneyColumn("discounts"))
                .Replace("{tax}", DbEx.MoneyColumn("tax"))
                .Replace("{net}", DbEx.MoneyColumn("net_sales"))
                .Replace("{cin}", DbEx.MoneyColumn("cash_in"))
                .Replace("{cout}", DbEx.MoneyColumn("cash_out"))
                .Replace("{debt}", DbEx.MoneyColumn("debt_settlements"))
                .Replace("{rf}", DbEx.MoneyColumn("refunds"))
                .Replace("{open}", DbEx.MoneyColumn("opening_cash"))
                .Replace("{exp}", DbEx.MoneyColumn("expected_cash"))
                .Replace("{act}", DbEx.MoneyColumn("actual_cash"))
                .Replace("{diff}", DbEx.MoneyColumn("difference")),
            new { id }));

    public List<XZReport> History(int limit = 100) =>
        _db.With(c => c.Query<XZReport>(@"SELECT id AS Id, report_no AS ReportNo, type AS Type,
            COALESCE(cash_session_id,0) AS CashSessionId, user_id AS UserId, generated_at AS GeneratedAt,
            sales_count AS SalesCount, {net} AS NetSales, {diff} AS Difference
            FROM xz_reports ORDER BY id DESC LIMIT @l"
                .Replace("{net}", DbEx.MoneyColumn("net_sales"))
                .Replace("{diff}", DbEx.MoneyColumn("difference")),
            new { l = limit }).ToList());
}

public class XZReport
{
    public long Id { get; set; }
    public string ReportNo { get; set; } = "";
    public string Type { get; set; } = "X";
    public long CashSessionId { get; set; }
    public long UserId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int SalesCount { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal Tax { get; set; }
    public decimal NetSales { get; set; }
    public string PaymentBreakdown { get; set; } = "{}";
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal DebtSettlements { get; set; }
    public decimal Refunds { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal ActualCash { get; set; }
    public decimal Difference { get; set; }

    public Dictionary<string, decimal> Payments =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(PaymentBreakdown) ?? new();
}
