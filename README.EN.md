# KasirPro — Professional Offline Retail POS for Windows

**🌐 Language / Bahasa / اللغة:**
[🇮🇩 Indonesia (README.md)](README.md) · [🇬🇧 English (README.EN.md)](README.EN.md) · [🇸🇦 العربية (README.AR.md)](README.AR.md)

---

A **100% offline** Windows desktop point-of-sale (POS) application for retail stores:
C# WinForms + local SQLite, .NET 10 — no internet, no server, no cloud required.
Fully portable — all data lives inside the application folder; just copy it to a flash drive.

**Built for:** minimarkets, grocery stores, fashion outlets, convenience stores, spare-part
shops, electronics stores, cosmetics, frozen food, small-to-medium building supply stores,
and general retail.

**Core character:** native Windows desktop · fast barcode scanning · keyboard
friendly · POS-hardware friendly · easy to back up · crash-safe database.

---

## ✨ Complete Feature List (v2.4.0)

### 🛒 Cashier (POS) Screen
- **Instant barcode scanning** — in-memory scan cache (all barcodes + SKUs loaded once),
  zero database query per scan; automatic DB fallback on cache miss
- **Split payment** — Cash / QRIS / Debit / Transfer / Receivable (credit) / Loyalty Points /
  Store Credit, unlimited payment lines
- **VAT/PPN tax** — inclusive or exclusive mode, configurable rate, per-product tax override
  (No Tax / Inclusive / Exclusive)
- **Hold / Park transaction** — park multiple carts per cashier, recall by label/customer
- **Promotions** — percentage discount, fixed discount, Buy X Get Y (cheapest items free),
  special price, happy-hour time windows, day-of-week schedules, member-only promos,
  minimum purchase/qty thresholds, stackable vs best-only policy
- **Coupons / vouchers** — offline codes (e.g. PROMO10), total-use limit, per-customer limit
- **Multi-level pricing** — Retail / Member / Wholesale 1-3 / Reseller / Distributor price
  levels with quantity break tiers; deterministic resolution order:
  Promotion → Customer price level → Quantity tier → Base price
- **Wholesale pricing** per product with minimum-quantity threshold (auto re-evaluated as
  quantity changes)
- **Scale barcode support** — EAN-13 embedded-weight barcodes (prefix + item code + weight),
  configurable prefixes and weight divisor
- **Payment Success dialog** — big TOTAL / PAID / CHANGE summary with Print Receipt /
  New Sale buttons
- **Printer fallback** — sale is always saved even if the printer fails (Retry / Skip)
- **Cash drawer kick** — ESC/POS pulse via Windows spooler, permission-protected
- **Keyboard shortcuts** — F1 help, F2 barcode focus, F5 refresh, F9 payment, F11 fullscreen,
  ESC cancel; auto-logout on idle

### 🔐 Security (RBAC)
- **4 roles** — Owner / Admin / Supervisor / Cashier with **35+ granular permissions**
  (POS.USE, SALE.VOID, PRODUCT.DELETE, USER.MANAGE, BACKUP.RESTORE, DRAWER.OPEN, …)
- Permissions are enforced **in the service layer** — hiding a menu is only cosmetic;
  calling a restricted method directly still fails and gets audited
- **Supervisor Authorization dialog** — sensitive actions (void, stock adjustment, manual
  drawer open, DB restore, large discounts) require a supervisor/admin PIN; every approval
  is stored in `approval_log` with requested_by / approved_by / reason / timestamp
- **Login lockout** — 5 failed attempts = 1-minute lock; passwords stored as PBKDF2-SHA256
  with constant-time comparison; no plaintext anywhere
- **Immutable Z-reports**; complete audit log (login, sale, void, return, exchange, discount
  override, drawer, cash in/out, adjustments, transfers, purchases, supplier payments,
  user changes, settings, backup, restore, license)

### 📦 Inventory
- **Stock ledger** — every stock change creates a `stock_movements` entry (product, direction,
  qty, before/after, reference, user, timestamp); product stock always equals ledger sum
- **Multi-warehouse** — main store + warehouses, per-warehouse stock table
- **Stock transfer** between warehouses (atomic, ledger-tracked)
- **Stock opname** with draft→count→review→posted workflow
- **Batch / lot / expiry tracking** — FEFO consumption (First Expired First Out), near-expiry
  report with configurable threshold (7/14/30/60/90 days), expired stock write-off
- **Serial / IMEI registry** — AVAILABLE/SOLD/RETURNED/DAMAGED lifecycle, duplicate guard
- **Reorder system** — reorder point, target stock, average daily sales, lead time;
  suggested order qty = max(target − stock, avg_daily_sales × lead_time)
- **Moving average cost** in integer cents, recalculated on every purchase
- **Fast / slow / dead stock** analytics (configurable no-sale window)
- **Inventory valuation** (stock × moving average cost)

### 🛬 Purchasing
- **Simple mode** — direct purchase + automatic stock-in (default)
- **Advanced workflow** — Purchase Order → Goods Receipt (partial receiving) → Supplier
  Invoice → Payment (PO Draft/Ordered/Received/Closed/Cancelled)
- **Supplier payables (AP)** — credit purchases tracked, partial payments, cash payments flow
  into the shift cash report
- **Purchase returns** — partial, cannot exceed received qty

### 👥 Customers & Loyalty
- Customer master: code, name, phone, email, address, birthday, tier
  (Regular/Silver/Gold/Platinum), price level, notes
- **Loyalty points ledger** — EARN / REDEEM / ADJUST / EXPIRE / REVERSAL; configurable earn
  rate (points per Rp 1,000) and redemption value; points can be used as payment
- **Store credit ledger** — IN (return without cash refund) / OUT (used on next purchase),
  full history, never just a total
- **Receivables (piutang)** — credit limit per customer, due dates, partial settlement,
  outstanding guard (sales with debt cannot be voided before settlement)

### 📊 Reports & Export
- 12+ live reports straight from SQLite: Sales, Purchases, Profit, Product Sales, Stock,
  Stock Movement, Low Stock, Cash, Cashier, Sales by Hour, by Payment Method, by Category
- **X-Report** (running shift) and **Z-Report** (immutable final close) with payment-method
  breakdown, cash expected/actual/difference, debt settlements, refunds
- **Export PDF** — zero-dependency built-in PDF writer (A4 landscape, pagination, headers)
- Export CSV, print via Windows printers (A4/A5)
- Cash report per shift with denominations support

### 💾 Offline Infrastructure
- **SQLite** — WAL journal mode, foreign_keys ON, synchronous NORMAL, busy_timeout 10s,
  automatic SQLITE_BUSY retry (3× backoff), light periodic maintenance
  (WAL checkpoint + PRAGMA optimize + quick_check)
- **Safe backup** — SQLite Backup API (never a raw file copy while running), integrity_check
  verification of every backup, automatic on close-shift/exit, configurable retention
- **Restore** — validate file → pre-restore backup → atomic swap → verify restored DB
  (Admin only, audited)
- **Offline updates** — update package = manifest.json + payload + **ECDSA signature**;
  SHA-256 per file; path-traversal protection; Data/Backup/Logs/license are never
  overwritten; applied on next launch (USB / network share, no internet)
- **Offline licensing** — ECDSA P-256 signed tokens, machine-locked, verified at every
  startup; separate Master Keygen application (private key DPAPI-protected, never ships)
- **Bilingual UI** — Bahasa Indonesia / English (centralized Strings catalog)

---

## 🏗 Architecture

```
KasirPro.App             WinForms UI (net10.0-windows)
KasirPro.Core            Domain: entities, Money (integer cents), calculators, promo engine
KasirPro.Infrastructure  SQLite (Dapper), services, migrations, printing, backup, updater
KasirPro.Licensing       Machine ID + ECDSA P-256 token verification (public key only)
KasirPro.Keygen          MASTER KEYGEN (developer only): DPAPI private key + keygen.db
KasirPro.Tests           186 xUnit automated tests
```

Layering: UI → Services → Domain → Infrastructure → SQLite.
**Money is stored as integer cents** (never floating point). All business operations are
transactional. Permissions are validated in services, not only in the UI.

## 🔨 Build & Test

Requires .NET 10 SDK:

```
dotnet restore
dotnet build -c Release
dotnet test tests/KasirPro.Tests -c Release
```

Or simply: `powershell scripts\build-release.ps1`

## 📦 Publish

```
# Portable (requires .NET Desktop Runtime 10 on the target PC)
powershell scripts\publish-portable.ps1

# Self-contained (no runtime needed, ~112 MB)
powershell scripts\publish-selfcontained.ps1
```

Output: `dist/KasirPro/KasirPro.exe` + `dist/DeveloperTools/KasirPro.Keygen/`
(private keys are NEVER included in the customer package).

## ▶ Run

```
KasirPro.exe                          # full GUI
KasirPro.exe --print-machine-id       # this computer's Machine ID
KasirPro.exe --verify-license <tok>   # activate via command line
KasirPro.exe --seed-demo              # demo data (development)
KasirPro.exe --reset-demo             # reset DB to demo state
KasirPro.exe --diag-license           # public-key fingerprint + license status
```

Startup order: license check → activation (if needed) → first-run wizard
(store info, admin PIN, printer) → login → dashboard.

## 🗄 Database (SQLite)

File: `<app folder>\Data\pos.db` (relative path — move the folder, the DB follows).
`foreign_keys=ON`, `journal_mode=WAL`, `synchronous=NORMAL`, busy_timeout 10s,
SQLITE_BUSY retry ×3, periodic light maintenance (checkpoint + optimize + quick_check).
Versioned migrations v1–v5 (transactional, idempotent) — old databases upgrade safely.

## 💾 Backup / Restore

- Manual: Tools → Backup Database
- Automatic: on shift close, on exit when data changed; retention configurable (3–100)
- Uses the SQLite Backup API + integrity_check of every produced backup
- Restore: validate → pre-restore backup → swap → verify (Admin only, audited)

## 🖨 Printers

- Separate printer roles: Receipt (58/80 mm), Label, A4 Invoice, Report, Drawer connector
- Thermal printing via Windows PrintDocument; Bluetooth/LAN printers installed in Windows
  appear automatically (Settings → Printers & scanners)
- Cash drawer: ESC p 0 pulse via Winspool RAW, requires DRAWER.OPEN permission + audit
- **Hardware Test Center**: Tools → Hardware Test Center (scanner / printers / drawer / scale)

## 📷 Barcode Scanner

- HID mode (default): the scanner acts as a keyboard, Enter suffix, auto-focus
- COM mode: choose the port in Hardware Test Center
- Test: scan on the Barcode Scanner tab → shows raw value, symbology, matched product,
  SKU, price, stock; "Barcode not registered" when unknown
- Scan cache: barcodes + SKUs are loaded once into memory (DB fallback on miss)

## 🏷 Barcode & Labels

- Tools → Barcode & Labels: generate internal EAN-13 (correct GS1 check digit),
  duplicate checker, GTIN validator, label designer (25×15 up to 50×40 mm),
  preview + bulk printing, built-in Code128 renderer (zero dependency)

## 🔑 Licensing / Keygen

ECDSA P-256 model. Private key lives in the Keygen (DPAPI + passphrase); the POS app
embeds only the public key. Token format `KPR1.<payload>.<signature>`, verified at
every startup. Machine-locked.

```
KasirPro.Keygen.exe --init <passphrase>
KasirPro.Keygen.exe --generate "Store Name" KP-XXXX-XXXX-XXXX lifetime <passphrase>
KasirPro.Keygen.exe --fingerprint
```

Compare fingerprints: `KasirPro.exe --diag-license` must match the Keygen's.

## 🔄 Offline Update

1. Developer: `UpdateService.BuildPackage(...)` → manifest.json + payload + manifest.sig
   (ECDSA, uses `Keys/update-private.pem`)
2. Copy the package folder to USB/share; set the folder in Store Settings
3. Tools → Check for Update (Offline): verifies signature + SHA-256 of every file +
   path-traversal protection → stages → applied on next launch
4. Data/, Backup/, Logs/, license.dat are NEVER overwritten

## 👮 Roles & Permissions

| Role | Can | Cannot |
|------|-----|--------|
| Owner | Everything | — |
| Admin | Everything | — |
| Supervisor | void/return/exchange, stock adjust, drawer, approvals | USER.MANAGE, ROLE.MANAGE, BACKUP.RESTORE |
| Cashier | POS, hold/recall, returns, discounts | void, user management, restore, settings, product delete |

Permissions are configurable via the `role_permissions` table. The menu hides what the
user may not use; the service layer still rejects direct calls (+ audit entry).

## 🧯 Troubleshooting

- **Printer not found** — install it in Windows first, click Refresh in the dialog
- **Bluetooth printer not printing** — pair it with Windows until it appears in the
  printer list; the app never connects BLE directly
- **Scanner not detected** — HID mode needs focus on the barcode field; COM mode needs
  the correct port; test in Hardware Test Center
- **Database locked** — auto-retries 3×; never share pos.db over the network
- **License invalid** — compare fingerprints (`--diag-license` vs `--fingerprint`)
- **Backup failed** — make sure the app folder is writable
- **COM port error** — the port is in use by another app; close it and refresh
- **Windows write permission** — run from a writable folder (not Program Files)

## 🧪 Testing

186 automated tests (xUnit): license sign/verify/tamper/expiry, RBAC matrix, promotions
(all types + schedules + stacking), multi-price resolution, EAN/GTIN check digits,
scale barcodes, split payments, loyalty & store credit ledgers, receivables + credit
limit, void/return/exchange, stock ledger integrity, warehouse transfers, batches FEFO,
serials, moving average, backup/restore, migrations, update signature + traversal,
performance (100 barcode lookups < 2 s), PDF export pagination.
