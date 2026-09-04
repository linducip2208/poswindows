namespace KasirPro.Core.Domain;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "Cashier";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public class Customer
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Loyalty points (1 point = loyalty_point_value rupiah when redeeming).</summary>
    public decimal Points { get; set; }
    /// <summary>Max outstanding debt allowed; 0 = no credit.</summary>
    public decimal CreditLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Supplier
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Unit
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Product
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public long UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Location { get; set; } = "";
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    /// <summary>Wholesale price; 0 = not offered.</summary>
    public decimal WholesalePrice { get; set; }
    /// <summary>Minimum qty to trigger wholesale price; 0 = not offered.</summary>
    public decimal WholesaleMinQty { get; set; }
    public decimal Stock { get; set; }
    public decimal MinStock { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal TargetStock { get; set; }
    /// <summary>"" = default; NONE / INCLUSIVE / EXCLUSIVE.</summary>
    public string TaxMode { get; set; } = "";
    public bool TrackBatch { get; set; }
    public bool TrackSerial { get; set; }
    public long DefaultSupplierId { get; set; }
    public string ImagePath { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Barcode { get; set; } = "";
}

public class ProductBarcode
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string Barcode { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
