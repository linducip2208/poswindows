using Dapper;

namespace KasirPro.Infrastructure;

/// <summary>
/// Versioned SQLite migrations. Each entry runs once, inside a transaction,
/// recorded in table database_version.
/// </summary>
public class Migrator
{
    private readonly Db _db;

    public Migrator(Db db) { _db = db; }

    private static readonly string[] V1 = new[]
    {
        // ===== master data =====
        @"CREATE TABLE IF NOT EXISTS roles (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            salt TEXT NOT NULL,
            full_name TEXT NOT NULL DEFAULT '',
            role_id INTEGER NOT NULL REFERENCES roles(id),
            is_active INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL DEFAULT '',
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS customers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            phone TEXT NOT NULL DEFAULT '',
            address TEXT NOT NULL DEFAULT '',
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS suppliers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            phone TEXT NOT NULL DEFAULT '',
            address TEXT NOT NULL DEFAULT '',
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS categories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS units (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
            unit_id INTEGER REFERENCES units(id) ON DELETE SET NULL,
            purchase_price INTEGER NOT NULL DEFAULT 0,
            selling_price INTEGER NOT NULL DEFAULT 0,
            stock REAL NOT NULL DEFAULT 0,
            min_stock REAL NOT NULL DEFAULT 0,
            image_path TEXT NOT NULL DEFAULT '',
            notes TEXT NOT NULL DEFAULT '',
            is_active INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS product_barcodes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            barcode TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL
        );",
        // ===== sales =====
        @"CREATE TABLE IF NOT EXISTS sales (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            invoice_no TEXT NOT NULL UNIQUE,
            sale_date TEXT NOT NULL,
            customer_id INTEGER REFERENCES customers(id) ON DELETE SET NULL,
            user_id INTEGER NOT NULL REFERENCES users(id),
            cash_session_id INTEGER REFERENCES cash_sessions(id) ON DELETE SET NULL,
            subtotal INTEGER NOT NULL DEFAULT 0,
            discount INTEGER NOT NULL DEFAULT 0,
            total INTEGER NOT NULL DEFAULT 0,
            cost_total INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'COMPLETED',
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS sale_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sale_id INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            product_code TEXT NOT NULL DEFAULT '',
            product_name TEXT NOT NULL DEFAULT '',
            qty REAL NOT NULL DEFAULT 0,
            price INTEGER NOT NULL DEFAULT 0,
            cost INTEGER NOT NULL DEFAULT 0,
            discount INTEGER NOT NULL DEFAULT 0,
            subtotal INTEGER NOT NULL DEFAULT 0
        );",
        @"CREATE TABLE IF NOT EXISTS sale_payments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sale_id INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
            method TEXT NOT NULL,
            amount INTEGER NOT NULL DEFAULT 0,
            reference TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS sale_returns (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            return_no TEXT NOT NULL UNIQUE,
            sale_id INTEGER NOT NULL REFERENCES sales(id),
            return_date TEXT NOT NULL,
            user_id INTEGER NOT NULL REFERENCES users(id),
            total INTEGER NOT NULL DEFAULT 0,
            reason TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS sale_return_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sale_return_id INTEGER NOT NULL REFERENCES sale_returns(id) ON DELETE CASCADE,
            sale_item_id INTEGER NOT NULL REFERENCES sale_items(id),
            product_id INTEGER NOT NULL REFERENCES products(id),
            product_name TEXT NOT NULL DEFAULT '',
            qty REAL NOT NULL DEFAULT 0,
            price INTEGER NOT NULL DEFAULT 0,
            subtotal INTEGER NOT NULL DEFAULT 0
        );",
        // ===== purchases =====
        @"CREATE TABLE IF NOT EXISTS purchases (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchase_no TEXT NOT NULL UNIQUE,
            supplier_invoice_no TEXT NOT NULL DEFAULT '',
            purchase_date TEXT NOT NULL,
            supplier_id INTEGER REFERENCES suppliers(id) ON DELETE SET NULL,
            user_id INTEGER NOT NULL REFERENCES users(id),
            subtotal INTEGER NOT NULL DEFAULT 0,
            discount INTEGER NOT NULL DEFAULT 0,
            total INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'COMPLETED',
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS purchase_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchase_id INTEGER NOT NULL REFERENCES purchases(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            product_code TEXT NOT NULL DEFAULT '',
            product_name TEXT NOT NULL DEFAULT '',
            qty REAL NOT NULL DEFAULT 0,
            cost INTEGER NOT NULL DEFAULT 0,
            subtotal INTEGER NOT NULL DEFAULT 0
        );",
        @"CREATE TABLE IF NOT EXISTS purchase_returns (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            return_no TEXT NOT NULL UNIQUE,
            purchase_id INTEGER NOT NULL REFERENCES purchases(id),
            return_date TEXT NOT NULL,
            supplier_id INTEGER REFERENCES suppliers(id) ON DELETE SET NULL,
            user_id INTEGER NOT NULL REFERENCES users(id),
            total INTEGER NOT NULL DEFAULT 0,
            reason TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS purchase_return_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchase_return_id INTEGER NOT NULL REFERENCES purchase_returns(id) ON DELETE CASCADE,
            purchase_item_id INTEGER NOT NULL REFERENCES purchase_items(id),
            product_id INTEGER NOT NULL REFERENCES products(id),
            product_name TEXT NOT NULL DEFAULT '',
            qty REAL NOT NULL DEFAULT 0,
            cost INTEGER NOT NULL DEFAULT 0,
            subtotal INTEGER NOT NULL DEFAULT 0
        );",
        // ===== inventory ledger =====
        @"CREATE TABLE IF NOT EXISTS stock_movements (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id),
            reference_type TEXT NOT NULL,
            reference_id INTEGER NOT NULL DEFAULT 0,
            direction TEXT NOT NULL CHECK(direction IN ('IN','OUT')),
            qty REAL NOT NULL DEFAULT 0,
            stock_after REAL NOT NULL DEFAULT 0,
            notes TEXT NOT NULL DEFAULT '',
            user_id INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS stock_opnames (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            opname_no TEXT NOT NULL UNIQUE,
            opname_date TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'DRAFT',
            user_id INTEGER NOT NULL REFERENCES users(id),
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS stock_opname_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            stock_opname_id INTEGER NOT NULL REFERENCES stock_opnames(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            system_qty REAL NOT NULL DEFAULT 0,
            counted_qty REAL NOT NULL DEFAULT 0,
            difference REAL NOT NULL DEFAULT 0,
            adjusted INTEGER NOT NULL DEFAULT 0
        );",
        // ===== cash / shift =====
        @"CREATE TABLE IF NOT EXISTS cash_sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL REFERENCES users(id),
            opened_at TEXT NOT NULL,
            closed_at TEXT,
            opening_cash INTEGER NOT NULL DEFAULT 0,
            closing_cash_expected INTEGER NOT NULL DEFAULT 0,
            closing_cash_actual INTEGER NOT NULL DEFAULT 0,
            cash_sales INTEGER NOT NULL DEFAULT 0,
            cash_in INTEGER NOT NULL DEFAULT 0,
            cash_out INTEGER NOT NULL DEFAULT 0,
            difference INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'OPEN',
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS cash_movements (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            cash_session_id INTEGER NOT NULL REFERENCES cash_sessions(id) ON DELETE CASCADE,
            type TEXT NOT NULL,
            direction TEXT NOT NULL CHECK(direction IN ('IN','OUT')),
            amount INTEGER NOT NULL DEFAULT 0,
            reference_type TEXT NOT NULL DEFAULT '',
            reference_id INTEGER NOT NULL DEFAULT 0,
            notes TEXT NOT NULL DEFAULT '',
            user_id INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );",
        // ===== audit =====
        @"CREATE TABLE IF NOT EXISTS audit_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL DEFAULT 0,
            username TEXT NOT NULL DEFAULT '',
            action TEXT NOT NULL,
            entity TEXT NOT NULL DEFAULT '',
            entity_id INTEGER NOT NULL DEFAULT 0,
            description TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL
        );",
        // ===== performance indexes =====
        @"CREATE INDEX IF NOT EXISTS idx_products_name ON products(name);",
        @"CREATE INDEX IF NOT EXISTS idx_products_category ON products(category_id);",
        @"CREATE INDEX IF NOT EXISTS idx_products_active ON products(is_active);",
        @"CREATE INDEX IF NOT EXISTS idx_barcode_lookup ON product_barcodes(barcode);",
        @"CREATE INDEX IF NOT EXISTS idx_sales_date ON sales(sale_date);",
        @"CREATE INDEX IF NOT EXISTS idx_sales_user_date ON sales(user_id, sale_date);",
        @"CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id);",
        @"CREATE INDEX IF NOT EXISTS idx_sale_items_product ON sale_items(product_id);",
        @"CREATE INDEX IF NOT EXISTS idx_sale_payments_sale ON sale_payments(sale_id);",
        @"CREATE INDEX IF NOT EXISTS idx_purchases_date ON purchases(purchase_date);",
        @"CREATE INDEX IF NOT EXISTS idx_purchase_items_purchase ON purchase_items(purchase_id);",
        @"CREATE INDEX IF NOT EXISTS idx_movements_product_date ON stock_movements(product_id, created_at);",
        @"CREATE INDEX IF NOT EXISTS idx_movements_ref ON stock_movements(reference_type, reference_id);",
        @"CREATE INDEX IF NOT EXISTS idx_movements_created ON stock_movements(created_at);",
        @"CREATE INDEX IF NOT EXISTS idx_cash_mov_session ON cash_movements(cash_session_id);",
        @"CREATE INDEX IF NOT EXISTS idx_cash_sessions_status ON cash_sessions(status);",
        @"CREATE INDEX IF NOT EXISTS idx_audit_created ON audit_logs(created_at);",
        // ===== defaults & seed =====
        @"INSERT OR IGNORE INTO roles (id, name, created_at, updated_at) VALUES
            (1, 'Admin', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
            (2, 'Cashier', '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
        @"INSERT OR IGNORE INTO customers (id, code, name, created_at, updated_at) VALUES
            (1, 'WALKIN', 'Umum', '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
        @"INSERT OR IGNORE INTO units (id, name, created_at, updated_at) VALUES
            (1, 'PCS', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
            (2, 'BOX', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
            (3, 'KG', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
            (4, 'LITER', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
            (5, 'PAK', '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
        @"INSERT OR REPLACE INTO settings (key, value, updated_at) VALUES
            ('store_name', 'KasirPro Store', '2026-01-01 00:00:00'),
            ('store_address', '', '2026-01-01 00:00:00'),
            ('store_phone', '', '2026-01-01 00:00:00'),
            ('receipt_footer', 'Terima kasih atas kunjungan Anda', '2026-01-01 00:00:00'),
            ('receipt_paper', '80', '2026-01-01 00:00:00'),
            ('printer_name', '', '2026-01-01 00:00:00'),
            ('receipt_copies', '1', '2026-01-01 00:00:00'),
            ('auto_backup', '1', '2026-01-01 00:00:00'),
            ('backup_keep', '30', '2026-01-01 00:00:00'),
            ('invoice_prefix', 'INV', '2026-01-01 00:00:00'),
            ('setup_done', '0', '2026-01-01 00:00:00');"
    };

    private static readonly string[] V2 = new[]
    {
        // ===== credit / receivables (piutang) =====
        @"CREATE TABLE IF NOT EXISTS sale_debts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sale_id INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
            customer_id INTEGER REFERENCES customers(id) ON DELETE SET NULL,
            original_amount INTEGER NOT NULL DEFAULT 0,
            paid_amount INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'OUTSTANDING',
            due_date TEXT,
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE INDEX IF NOT EXISTS idx_debts_status ON sale_debts(status);",
        @"CREATE INDEX IF NOT EXISTS idx_debts_customer ON sale_debts(customer_id);",
        @"CREATE TABLE IF NOT EXISTS debt_payments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            debt_id INTEGER NOT NULL REFERENCES sale_debts(id) ON DELETE CASCADE,
            method TEXT NOT NULL,
            amount INTEGER NOT NULL DEFAULT 0,
            notes TEXT NOT NULL DEFAULT '',
            user_id INTEGER NOT NULL DEFAULT 0,
            cash_session_id INTEGER REFERENCES cash_sessions(id) ON DELETE SET NULL,
            created_at TEXT NOT NULL
        );",
        @"CREATE INDEX IF NOT EXISTS idx_debt_payments_debt ON debt_payments(debt_id);",
        @"ALTER TABLE cash_sessions ADD COLUMN debt_payments INTEGER NOT NULL DEFAULT 0;",
        @"INSERT OR REPLACE INTO settings (key, value, updated_at) VALUES
            ('allow_credit', '0', '2026-01-01 00:00:00'),
            ('language', 'id', '2026-01-01 00:00:00'),
            ('update_source', '', '2026-01-01 00:00:00');"
    };

    private static readonly string[] V3 = new[]
    {
        // ===== tax =====
        @"ALTER TABLE products ADD COLUMN tax_mode TEXT NOT NULL DEFAULT '';",
        @"ALTER TABLE sales ADD COLUMN tax INTEGER NOT NULL DEFAULT 0;",
        @"ALTER TABLE customers ADD COLUMN points INTEGER NOT NULL DEFAULT 0;",
        @"ALTER TABLE customers ADD COLUMN credit_limit INTEGER NOT NULL DEFAULT 0;",
        // ===== price tier (grosir) =====
        @"ALTER TABLE products ADD COLUMN wholesale_price INTEGER NOT NULL DEFAULT 0;",
        @"ALTER TABLE products ADD COLUMN wholesale_min_qty REAL NOT NULL DEFAULT 0;",
        @"CREATE TABLE IF NOT EXISTS unit_conversions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            unit_id INTEGER NOT NULL REFERENCES units(id),
            factor REAL NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            UNIQUE(product_id, unit_id)
        );",
        // ===== hold / park transaksi =====
        @"CREATE TABLE IF NOT EXISTS holds (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            label TEXT NOT NULL DEFAULT '',
            user_id INTEGER NOT NULL REFERENCES users(id),
            customer_id INTEGER REFERENCES customers(id) ON DELETE SET NULL,
            customer_name TEXT NOT NULL DEFAULT '',
            item_count INTEGER NOT NULL DEFAULT 0,
            subtotal INTEGER NOT NULL DEFAULT 0,
            discount INTEGER NOT NULL DEFAULT 0,
            total INTEGER NOT NULL DEFAULT 0,
            items_json TEXT NOT NULL DEFAULT '[]',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE INDEX IF NOT EXISTS idx_holds_user ON holds(user_id);",
        // ===== x/z report =====
        @"CREATE TABLE IF NOT EXISTS xz_reports (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            report_no TEXT NOT NULL UNIQUE,
            type TEXT NOT NULL,
            cash_session_id INTEGER REFERENCES cash_sessions(id) ON DELETE SET NULL,
            user_id INTEGER NOT NULL,
            generated_at TEXT NOT NULL,
            sales_count INTEGER NOT NULL DEFAULT 0,
            gross_sales INTEGER NOT NULL DEFAULT 0,
            discounts INTEGER NOT NULL DEFAULT 0,
            tax INTEGER NOT NULL DEFAULT 0,
            net_sales INTEGER NOT NULL DEFAULT 0,
            payment_breakdown TEXT NOT NULL DEFAULT '{}',
            cash_in INTEGER NOT NULL DEFAULT 0,
            cash_out INTEGER NOT NULL DEFAULT 0,
            debt_settlements INTEGER NOT NULL DEFAULT 0,
            refunds INTEGER NOT NULL DEFAULT 0,
            opening_cash INTEGER NOT NULL DEFAULT 0,
            expected_cash INTEGER NOT NULL DEFAULT 0,
            actual_cash INTEGER NOT NULL DEFAULT 0,
            difference INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        // ===== supplier payable (hutang belanja) =====
        @"ALTER TABLE purchases ADD COLUMN payment_status TEXT NOT NULL DEFAULT 'PAID';",
        @"ALTER TABLE purchases ADD COLUMN paid_amount INTEGER NOT NULL DEFAULT 0;",
        @"CREATE TABLE IF NOT EXISTS purchase_payments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchase_id INTEGER NOT NULL REFERENCES purchases(id) ON DELETE CASCADE,
            method TEXT NOT NULL,
            amount INTEGER NOT NULL DEFAULT 0,
            notes TEXT NOT NULL DEFAULT '',
            user_id INTEGER NOT NULL DEFAULT 0,
            cash_session_id INTEGER REFERENCES cash_sessions(id) ON DELETE SET NULL,
            created_at TEXT NOT NULL
        );",
        @"CREATE INDEX IF NOT EXISTS idx_purchase_payments_purchase ON purchase_payments(purchase_id);",
        @"CREATE INDEX IF NOT EXISTS idx_purchases_payment_status ON purchases(payment_status);",
        // ===== warehouses + transfer stok =====
        @"CREATE TABLE IF NOT EXISTS warehouses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            is_default INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"INSERT OR IGNORE INTO warehouses (id, name, is_default, created_at, updated_at) VALUES
            (1, 'TOKO', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
        @"CREATE TABLE IF NOT EXISTS warehouse_stock (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            warehouse_id INTEGER NOT NULL REFERENCES warehouses(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            qty REAL NOT NULL DEFAULT 0,
            UNIQUE(warehouse_id, product_id)
        );",
        @"CREATE INDEX IF NOT EXISTS idx_whstock_product ON warehouse_stock(product_id);",
        @"CREATE TABLE IF NOT EXISTS stock_transfers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            transfer_no TEXT NOT NULL UNIQUE,
            transfer_date TEXT NOT NULL,
            from_warehouse_id INTEGER NOT NULL REFERENCES warehouses(id),
            to_warehouse_id INTEGER NOT NULL REFERENCES warehouses(id),
            user_id INTEGER NOT NULL REFERENCES users(id),
            notes TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );",
        @"CREATE TABLE IF NOT EXISTS stock_transfer_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            transfer_id INTEGER NOT NULL REFERENCES stock_transfers(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            qty REAL NOT NULL DEFAULT 0
        );",
        @"CREATE INDEX IF NOT EXISTS idx_transfer_items_transfer ON stock_transfer_items(transfer_id);",
        // ===== settings =====
        @"INSERT OR REPLACE INTO settings (key, value, updated_at) VALUES
            ('tax_enabled', '0', '2026-01-01 00:00:00'),
            ('tax_rate_percent', '11', '2026-01-01 00:00:00'),
            ('tax_inclusive', '1', '2026-01-01 00:00:00'),
            ('loyalty_enabled', '0', '2026-01-01 00:00:00'),
            ('loyalty_earn_per_1000', '1', '2026-01-01 00:00:00'),
            ('loyalty_point_value', '100', '2026-01-01 00:00:00'),
            ('scale_enabled', '0', '2026-01-01 00:00:00'),
            ('scale_prefixes', '21,02', '2026-01-01 00:00:00'),
            ('scale_weight_divisor', '1000', '2026-01-01 00:00:00'),
            ('drawer_enabled', '0', '2026-01-01 00:00:00'),
            ('drawer_printer', '', '2026-01-01 00:00:00'),
            ('touch_mode', '0', '2026-01-01 00:00:00'),
            ('auto_logout_minutes', '0', '2026-01-01 00:00:00'),
            ('purchases_default_credit', '0', '2026-01-01 00:00:00');"
    };

    public int CurrentVersion
    {
        get
        {
            var exists = _db.With(c =>
            {
                var t = c.ExecuteScalar<long>(
                    "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='database_version'");
                return t > 0;
            });
            if (!exists) return 0;
            return (int)_db.With(c => c.ExecuteScalar<long>(
                "SELECT COALESCE(MAX(version),0) FROM database_version"));
        }
    }

    /// <summary>Applies pending migrations. Idempotent, transactional per version.</summary>
    public int Migrate()
    {
        var from = CurrentVersion;
        var applied = 0;
        if (from < 1)
        {
            _db.Transaction(conn =>
            {
                conn.Execute("CREATE TABLE IF NOT EXISTS database_version (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)");
                foreach (var sql in V1)
                    conn.Execute(sql);
                conn.Execute("INSERT INTO database_version (version, applied_at) VALUES (1, @applied_at)",
                    new { applied_at = DbEx.Iso(DateTime.Now) });
            });
            applied++;
        }
        if (from < 2)
        {
            _db.Transaction(conn =>
            {
                foreach (var sql in V2)
                    conn.Execute(sql);
                conn.Execute("INSERT INTO database_version (version, applied_at) VALUES (2, @applied_at)",
                    new { applied_at = DbEx.Iso(DateTime.Now) });
            });
            applied++;
        }
        if (from < 3)
        {
            _db.Transaction(conn =>
            {
                foreach (var sql in V3)
                    conn.Execute(sql);
                conn.Execute("INSERT INTO database_version (version, applied_at) VALUES (3, @applied_at)",
                    new { applied_at = DbEx.Iso(DateTime.Now) });
            });
            applied++;
        }
        return applied;
    }

    /// <summary>Returns the list of table names present in the database (used by tests / maintenance).</summary>
    public List<string> TableNames() =>
        _db.With(c => c.Query<string>("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").ToList());
}
