using KasirPro.Infrastructure;
using KasirPro.Infrastructure.Services;

namespace KasirPro.App;

/// <summary>Service locator for the single-instance desktop app.</summary>
public class AppServices
{
    public Db Db { get; }
    public AuditService Audit { get; }
    public UserService Users { get; }
    public SettingsService Settings { get; }
    public ProductService Products { get; }
    public InventoryService Inventory { get; }
    public SalesService Sales { get; }
    public PurchaseService Purchases { get; }
    public CashService Cash { get; }
    public ReportService Reports { get; }
    public AnalyticsService Analytics { get; }
    public DebtService Debts { get; }
    public HoldService Holds { get; }
    public XZReportService XZReports { get; }
    public LoyaltyService Loyalty { get; }
    public WarehouseService Warehouses { get; }
    public SupplierPaymentService SupplierPayments { get; }
    public PriceService Prices { get; }
    public PromotionService Promotions { get; }
    public CreditLedgerService Ledgers { get; }
    public ExchangeService Exchanges { get; }
    public BatchService Batches { get; }
    public SerialService Serials { get; }
    public MovementAnalyticsService MovementAnalytics { get; }
    public ReorderService Reorder { get; }
    public AuthorizationService Auth { get; }
    public UpdateService Updates { get; }
    public BackupService Backup { get; }
    public PrinterService Printer { get; }
    public DemoSeeder Seeder { get; }

    public AppServices(Db db, string? backupDir = null)
    {
        Db = db;
        Audit = new AuditService(db);
        Users = new UserService(db, Audit);
        Settings = new SettingsService(db, Audit);
        Auth = new AuthorizationService(db, Audit);
        Inventory = new InventoryService(db, Audit);
        Products = new ProductService(db, Audit, Inventory);
        Cash = new CashService(db, Audit);
        Loyalty = new LoyaltyService(db, Settings, Audit);
        Sales = new SalesService(db, Audit, Cash, Settings, Loyalty);
        Purchases = new PurchaseService(db, Audit);
        Reports = new ReportService(db);
        Analytics = new AnalyticsService(db);
        Debts = new DebtService(db, Audit, Cash);
        Holds = new HoldService(db);
        XZReports = new XZReportService(db, Audit);
        Warehouses = new WarehouseService(db, Audit);
        SupplierPayments = new SupplierPaymentService(db, Audit);
        Prices = new PriceService(db);
        Promotions = new PromotionService(db, Audit);
        Ledgers = new CreditLedgerService(db, Audit);
        Exchanges = new ExchangeService(db, Audit, Cash);
        Batches = new BatchService(db, Audit);
        Serials = new SerialService(db, Audit);
        MovementAnalytics = new MovementAnalyticsService(db);
        Reorder = new ReorderService(db);
        Updates = new UpdateService(Settings);
        Backup = new BackupService(db, Settings, Audit, backupDir);
        Printer = new PrinterService(Settings);
        Seeder = new DemoSeeder(db, Sales, Purchases, Users, Products);
    }
}
