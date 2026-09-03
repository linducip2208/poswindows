using System.Globalization;

namespace KasirPro.App.UI;

/// <summary>
/// Lightweight i18n: Indonesian (default) + English. Missing keys fall back to Indonesian.
/// Language is stored in settings ("language") and applied on app start / menu rebuild.
/// </summary>
public static class Strings
{
    public static string Lang = "id";

    private static readonly Dictionary<string, string> Id = new()
    {
        // menu root
        ["menu_data"] = "Data",
        ["menu_transaction"] = "Transaction",
        ["menu_product"] = "Product",
        ["menu_inventory"] = "Inventory",
        ["menu_reports"] = "Reports",
        ["menu_tools"] = "Tools",
        // transaction
        ["menu_new_sale"] = "New Sale",
        ["menu_sales_history"] = "Sales History",
        ["menu_sales_return"] = "Sales Return",
        ["menu_debts"] = "Debts (Piutang)",
        ["menu_purchase"] = "Purchase",
        ["menu_purchase_history"] = "Purchase History",
        ["menu_purchase_return"] = "Purchase Return",
        ["menu_cash_in"] = "Cash In",
        ["menu_cash_out"] = "Cash Out",
        ["menu_open_shift"] = "Open Shift",
        ["menu_close_shift"] = "Close Shift",
        ["menu_shift_history"] = "Shift History",
        // product
        ["menu_product_list"] = "Product List",
        ["menu_add_product"] = "Add Product",
        ["menu_categories"] = "Categories",
        ["menu_units"] = "Units",
        ["menu_barcode"] = "Barcode",
        ["menu_price_update"] = "Price Update",
        // inventory
        ["menu_current_stock"] = "Current Stock",
        ["menu_stock_movement"] = "Stock Movement",
        ["menu_stock_in"] = "Stock In",
        ["menu_stock_out"] = "Stock Out",
        ["menu_stock_adjustment"] = "Stock Adjustment",
        ["menu_stock_opname"] = "Stock Opname",
        ["menu_low_stock"] = "Low Stock",
        // tools
        ["menu_store_settings"] = "Store Settings",
        ["menu_user_management"] = "User Management",
        ["menu_printer_setup"] = "Printer Setup",
        ["menu_receipt_setup"] = "Receipt Setup",
        ["menu_backup"] = "Backup Database",
        ["menu_restore"] = "Restore Database",
        ["menu_maintenance"] = "Database Maintenance",
        ["menu_check_update"] = "Check for Update (Offline)",
        ["menu_license_info"] = "License Information",
        ["menu_about"] = "About",
        // status bar
        ["status_user"] = "User",
        ["status_shift"] = "Shift",
        ["status_offline"] = "Offline POS",
        // dashboard
        ["dash_sales_today"] = "Sales Today",
        ["dash_transactions"] = "Transactions",
        ["dash_profit"] = "Profit",
        ["dash_low_stock"] = "Low Stock",
        ["dash_trend_title"] = "Sales Trend - Last 7 Days",
        ["dash_recent_title"] = "Recent Transactions",
        ["dash_top_title"] = "Top Selling Products - 7 Days",
        ["grid_time"] = "Time",
        ["grid_invoice"] = "Invoice",
        ["grid_customer"] = "Customer",
        ["grid_total"] = "Total",
        ["grid_payment"] = "Payment",
        ["grid_status"] = "Status",
        ["grid_product"] = "Product",
        ["grid_qty_sold"] = "Qty Sold",
        ["grid_revenue"] = "Revenue",
        // POS
        ["pos_barcode_hint"] = "Barcode / scan (Enter) - F2 untuk kembali ke sini",
        ["pos_search_hint"] = "Cari nama / kode produk...",
        ["pos_cart_title"] = "Shopping Cart",
        ["pos_pay"] = "PAY (F9)",
        ["pos_clear_cart"] = "Clear Cart",
        ["pos_discount_label"] = "Diskon Rp",
        ["pos_subtotal"] = "Subtotal",
        ["pos_discount"] = "Discount",
        ["pos_grand_total"] = "GRAND TOTAL",
        ["pos_empty_hint"] = "Belum ada item.\nScan barcode atau pilih produk di sebelah kiri.",
        ["pos_label_customer"] = "Pelanggan:",
        ["pos_all_categories"] = "Semua",
        // payment
        ["pay_title"] = "Pembayaran",
        ["pay_cash"] = "Cash",
        ["pay_qris"] = "Qris",
        ["pay_debit"] = "Debit",
        ["pay_transfer"] = "Transfer",
        ["pay_credit"] = "Piutang",
        ["pay_remove"] = "Hapus",
        ["pay_remove_suffix"] = "Hapus",
        ["pay_fill_remaining"] = "Sisa",
        ["pay_paid"] = "Dibayar",
        ["pay_remaining"] = "Remaining",
        ["pay_change"] = "Kembalian",
        ["pay_confirm"] = "BAYAR",
        ["pay_cancel"] = "Batal",
        // login
        ["login_title"] = "KASIRPRO",
        ["login_subtitle"] = "POS Retail - Kasir Login",
        ["login_user"] = "Pengguna:",
        ["login_password"] = "PIN / Password:",
        ["login_button"] = "LOGIN",
        // generic
        ["btn_close"] = "Tutup",
        ["btn_save"] = "SIMPAN",
        ["btn_cancel"] = "Batal",
        ["msg_yes"] = "Ya",
        ["msg_no"] = "Tidak",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["menu_data"] = "Data",
        ["menu_transaction"] = "Transaction",
        ["menu_product"] = "Product",
        ["menu_inventory"] = "Inventory",
        ["menu_reports"] = "Reports",
        ["menu_tools"] = "Tools",
        ["menu_new_sale"] = "New Sale",
        ["menu_sales_history"] = "Sales History",
        ["menu_sales_return"] = "Sales Return",
        ["menu_debts"] = "Debts (Receivables)",
        ["menu_purchase"] = "Purchase",
        ["menu_purchase_history"] = "Purchase History",
        ["menu_purchase_return"] = "Purchase Return",
        ["menu_cash_in"] = "Cash In",
        ["menu_cash_out"] = "Cash Out",
        ["menu_open_shift"] = "Open Shift",
        ["menu_close_shift"] = "Close Shift",
        ["menu_shift_history"] = "Shift History",
        ["menu_product_list"] = "Product List",
        ["menu_add_product"] = "Add Product",
        ["menu_categories"] = "Categories",
        ["menu_units"] = "Units",
        ["menu_barcode"] = "Barcode",
        ["menu_price_update"] = "Price Update",
        ["menu_current_stock"] = "Current Stock",
        ["menu_stock_movement"] = "Stock Movement",
        ["menu_stock_in"] = "Stock In",
        ["menu_stock_out"] = "Stock Out",
        ["menu_stock_adjustment"] = "Stock Adjustment",
        ["menu_stock_opname"] = "Stock Opname",
        ["menu_low_stock"] = "Low Stock",
        ["menu_store_settings"] = "Store Settings",
        ["menu_user_management"] = "User Management",
        ["menu_printer_setup"] = "Printer Setup",
        ["menu_receipt_setup"] = "Receipt Setup",
        ["menu_backup"] = "Backup Database",
        ["menu_restore"] = "Restore Database",
        ["menu_maintenance"] = "Database Maintenance",
        ["menu_check_update"] = "Check for Update (Offline)",
        ["menu_license_info"] = "License Information",
        ["menu_about"] = "About",
        ["status_user"] = "User",
        ["status_shift"] = "Shift",
        ["status_offline"] = "Offline POS",
        ["dash_sales_today"] = "Sales Today",
        ["dash_transactions"] = "Transactions",
        ["dash_profit"] = "Profit",
        ["dash_low_stock"] = "Low Stock",
        ["dash_trend_title"] = "Sales Trend - Last 7 Days",
        ["dash_recent_title"] = "Recent Transactions",
        ["dash_top_title"] = "Top Selling Products - 7 Days",
        ["grid_time"] = "Time",
        ["grid_invoice"] = "Invoice",
        ["grid_customer"] = "Customer",
        ["grid_total"] = "Total",
        ["grid_payment"] = "Payment",
        ["grid_status"] = "Status",
        ["grid_product"] = "Product",
        ["grid_qty_sold"] = "Qty Sold",
        ["grid_revenue"] = "Revenue",
        ["pos_barcode_hint"] = "Barcode / scan (Enter) - F2 to return here",
        ["pos_search_hint"] = "Search product name / code...",
        ["pos_cart_title"] = "Shopping Cart",
        ["pos_pay"] = "PAY (F9)",
        ["pos_clear_cart"] = "Clear Cart",
        ["pos_discount_label"] = "Discount Rp",
        ["pos_subtotal"] = "Subtotal",
        ["pos_discount"] = "Discount",
        ["pos_grand_total"] = "GRAND TOTAL",
        ["pos_empty_hint"] = "No items yet.\nScan a barcode or pick a product on the left.",
        ["pos_label_customer"] = "Customer:",
        ["pos_all_categories"] = "All",
        ["pay_title"] = "Payment",
        ["pay_cash"] = "Cash",
        ["pay_qris"] = "Qris",
        ["pay_debit"] = "Debit",
        ["pay_transfer"] = "Transfer",
        ["pay_credit"] = "Credit",
        ["pay_remove"] = "Remove",
        ["pay_remove_suffix"] = "Remove",
        ["pay_fill_remaining"] = "Rem.",
        ["pay_paid"] = "Paid",
        ["pay_remaining"] = "Remaining",
        ["pay_change"] = "Change",
        ["pay_confirm"] = "PAY",
        ["pay_cancel"] = "Cancel",
        ["login_title"] = "KASIRPRO",
        ["login_subtitle"] = "POS Retail - Cashier Login",
        ["login_user"] = "User:",
        ["login_password"] = "PIN / Password:",
        ["login_button"] = "LOGIN",
        ["btn_close"] = "Close",
        ["btn_save"] = "SAVE",
        ["btn_cancel"] = "Cancel",
        ["msg_yes"] = "Yes",
        ["msg_no"] = "No",
    };

    public static void Init(string language)
    {
        Lang = language == "en" ? "en" : "id";
        var culture = Lang == "en" ? "en-US" : "id-ID";
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; // money parsing stays invariant
    }

    public static string T(string key)
    {
        if (Lang == "en" && En.TryGetValue(key, out var en)) return en;
        if (Id.TryGetValue(key, out var id)) return id;
        return key;
    }

    public static string PayLabel(Core.Domain.PaymentMethod method) => method switch
    {
        Core.Domain.PaymentMethod.Cash => T("pay_cash"),
        Core.Domain.PaymentMethod.Qris => T("pay_qris"),
        Core.Domain.PaymentMethod.Debit => T("pay_debit"),
        Core.Domain.PaymentMethod.Transfer => T("pay_transfer"),
        Core.Domain.PaymentMethod.Credit => T("pay_credit"),
        _ => method.ToString()
    };
}
