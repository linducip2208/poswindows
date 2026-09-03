namespace KasirPro.Core.Domain;

public enum PaymentMethod
{
    Cash = 0,
    Qris = 1,
    Debit = 2,
    Transfer = 3,
    Credit = 4,
    Points = 5
}

public static class TaxMode
{
    /// <summary>Follow store default.</summary>
    public const string Default = "";
    public const string None = "NONE";
    public const string Inclusive = "INCLUSIVE";
    public const string Exclusive = "EXCLUSIVE";

    public static bool IsValid(string mode) =>
        mode == Default || mode == None || mode == Inclusive || mode == Exclusive;
}

public enum SaleStatus
{
    Completed = 0,
    Voided = 1
}

public enum StockDirection
{
    In = 1,
    Out = -1
}

public static class StockRef
{
    public const string Purchase = "PURCHASE";
    public const string Sale = "SALE";
    public const string SaleReturn = "SALE_RETURN";
    public const string PurchaseReturn = "PURCHASE_RETURN";
    public const string Adjustment = "ADJUSTMENT";
    public const string Opname = "OPNAME";
    public const string ManualIn = "MANUAL_IN";
    public const string ManualOut = "MANUAL_OUT";
    public const string Opening = "OPENING_STOCK";
}

public enum StockOpnameStatus
{
    Draft = 0,
    Posted = 1
}

public enum CashMovementType
{
    Opening = 0,
    Sale = 1,
    SaleReturn = 2,
    CashIn = 3,
    CashOut = 4,
    Closing = 5
}

public enum CashSessionStatus
{
    Open = 0,
    Closed = 1
}

public enum PurchaseStatus
{
    Completed = 0,
    Voided = 1
}

public static class AuditAction
{
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string Sale = "SALE";
    public const string SaleVoid = "SALE_VOID";
    public const string SaleReturn = "SALE_RETURN";
    public const string Purchase = "PURCHASE";
    public const string PurchaseReturn = "PURCHASE_RETURN";
    public const string StockAdjustment = "STOCK_ADJUSTMENT";
    public const string StockIn = "STOCK_IN";
    public const string StockOut = "STOCK_OUT";
    public const string StockOpname = "STOCK_OPNAME";
    public const string Backup = "BACKUP";
    public const string Restore = "RESTORE";
    public const string UserChange = "USER_CHANGE";
    public const string SettingChange = "SETTING_CHANGE";
    public const string ProductChange = "PRODUCT_CHANGE";
    public const string CustomerChange = "CUSTOMER_CHANGE";
    public const string SupplierChange = "SUPPLIER_CHANGE";
    public const string DebtSettle = "DEBT_SETTLE";
}
