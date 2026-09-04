namespace KasirPro.Infrastructure;

/// <summary>Permission catalog + role matrix, seeded by migration v4.</summary>
public static class PermissionSeed
{
    public static readonly (string Code, string Desc)[] Catalog =
    {
        ("POS.USE", "Gunakan layar kasir"),
        ("SALE.CREATE", "Buat transaksi penjualan"),
        ("SALE.HOLD", "Parkir transaksi"),
        ("SALE.RECALL", "Panggil transaksi terparkir"),
        ("SALE.VOID", "Batalkan transaksi"),
        ("SALE.RETURN", "Retur penjualan"),
        ("SALE.EXCHANGE", "Tukar barang"),
        ("SALE.REPRINT", "Cetak ulang struk"),
        ("DISCOUNT.APPLY", "Beri diskon"),
        ("PRICE.OVERRIDE", "Ubah harga saat jual"),
        ("DRAWER.OPEN", "Buka cash drawer manual"),
        ("SHIFT.OPEN", "Buka shift"),
        ("SHIFT.CLOSE", "Tutup shift"),
        ("PRODUCT.VIEW", "Lihat produk"),
        ("PRODUCT.CREATE", "Tambah produk"),
        ("PRODUCT.EDIT", "Ubah produk"),
        ("PRODUCT.DELETE", "Hapus produk"),
        ("STOCK.VIEW", "Lihat stok"),
        ("STOCK.ADJUST", "Penyesuaian stok"),
        ("STOCK.OPNAME", "Stock opname"),
        ("STOCK.POST_OPNAME", "Posting opname"),
        ("STOCK.TRANSFER", "Transfer stok antar gudang"),
        ("PURCHASE.CREATE", "Buat pembelian"),
        ("PURCHASE.APPROVE", "Approve PO"),
        ("SUPPLIER.MANAGE", "Kelola supplier"),
        ("CUSTOMER.MANAGE", "Kelola pelanggan"),
        ("REPORT.SALES", "Laporan penjualan"),
        ("REPORT.PROFIT", "Laporan laba"),
        ("REPORT.COST", "Lihat harga pokok"),
        ("USER.MANAGE", "Kelola pengguna"),
        ("ROLE.MANAGE", "Kelola role & permission"),
        ("SETTINGS.MANAGE", "Ubah pengaturan toko"),
        ("BACKUP.CREATE", "Buat backup"),
        ("BACKUP.RESTORE", "Restore database"),
        ("LICENSE.VIEW", "Lihat info lisensi"),
    };

    /// <summary>Role matrix: which role gets which permission.</summary>
    public static readonly (string Role, string[] Perms)[] Matrix =
    {
        ("Owner", Catalog.Select(c => c.Code).ToArray()),
        ("Admin", Catalog.Select(c => c.Code).ToArray()),
        ("Supervisor", new[]
        {
            "POS.USE", "SALE.CREATE", "SALE.HOLD", "SALE.RECALL", "SALE.VOID", "SALE.RETURN",
            "SALE.EXCHANGE", "SALE.REPRINT", "DISCOUNT.APPLY", "PRICE.OVERRIDE", "DRAWER.OPEN",
            "SHIFT.OPEN", "SHIFT.CLOSE", "PRODUCT.VIEW", "PRODUCT.CREATE", "PRODUCT.EDIT",
            "PRODUCT.DELETE", "STOCK.VIEW", "STOCK.ADJUST", "STOCK.OPNAME", "STOCK.POST_OPNAME",
            "STOCK.TRANSFER", "PURCHASE.CREATE", "PURCHASE.APPROVE", "SUPPLIER.MANAGE",
            "CUSTOMER.MANAGE", "REPORT.SALES", "REPORT.PROFIT", "REPORT.COST", "BACKUP.CREATE",
            "LICENSE.VIEW"
        }),
        ("Cashier", new[]
        {
            "POS.USE", "SALE.CREATE", "SALE.HOLD", "SALE.RECALL", "SALE.RETURN", "SALE.REPRINT",
            "DISCOUNT.APPLY", "SHIFT.OPEN", "PRODUCT.VIEW", "STOCK.VIEW", "CUSTOMER.MANAGE",
            "REPORT.SALES", "LICENSE.VIEW"
        }),
    };

    public static readonly string[] PermissionInserts =
        Catalog.Select(c =>
            $"INSERT OR IGNORE INTO permissions (code, description) VALUES ('{c.Code.Replace("'", "''")}', '{c.Desc.Replace("'", "''")}');")
        .ToArray();

    public static readonly string[] RolePermissionInserts =
        Matrix.SelectMany(m =>
            m.Perms.Select(p =>
                $"INSERT OR IGNORE INTO role_permissions (role_id, permission) " +
                $"SELECT r.id, '{p}' FROM roles r WHERE r.name = '{m.Role}';"))
        .ToArray();
}
