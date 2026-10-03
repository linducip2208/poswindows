# KasirPro - Professional Offline Retail POS for Windows

**🌐 Bahasa / Language / اللغة:**
[🇮🇩 Indonesia (README.md)](README.md) · [🇬🇧 English (README.EN.md)](README.EN.md) · [🇸🇦 العربية (README.AR.md)](README.AR.md)

---

Aplikasi kasir (POS) desktop Windows **100% offline** untuk toko retail:
C# WinForms + SQLite lokal, .NET 10, tanpa internet, tanpa server, tanpa cloud.
Portable - seluruh data berada di folder aplikasi, cukup dipindahkan ke flashdisk.

## Fitur Utama (v2.4.0)

**Kasir (POS)**
- Barcode scan instan (in-memory scan cache, USB HID / scanner 2.4GHz / Bluetooth)
- Split payment: Cash / QRIS / Debit / Transfer / Piutang / Poin / Store Credit
- Pajak PPN include/exclude, tarif configurable, override per produk
- Hold/park transaksi + recall per kasir
- Promosi: diskon %, fixed, Buy X Get Y, harga spesial, coupon/voucher, happy hour, day-of-week
- Harga multi-level (Retail/Member/Wholesale1-3/Reseller/Distributor) + qty break
- Harga grosir per produk, barcode timbangan (prefix EAN-13 + berat)
- Payment success dialog, printer fallback, cash drawer kick (ESC/POS)
- F2-F11 keyboard shortcuts, auto-logout

**Keamanan (RBAC)**
- Role Owner/Admin/Supervisor/Cashier dengan 35+ permission granular
- Permission dievaluasi di **service layer** (bukan hanya menyembunyikan menu)
- Supervisor authorization dialog untuk void/adjust/drawer/restore (approval_log)
- Login lockout (5x gagal = lock 1 menit), PBKDF2 + constant-time compare
- Audit log lengkap, Z-report immutable

**Inventory**
- Stock ledger (setiap perubahan tercatat), multi gudang + transfer stok
- Stock opname + blind mode, batch/lot + expiry (FEFO, near-expiry, write-off)
- Serial/IMEI tracking, reorder point + purchase suggestion
- Moving average cost (integer cents), fast/slow/dead stock report

**Purchasing**
- Simple mode (langsung terima) + workflow PO -> Receipt -> Invoice -> Pay
- Hutang supplier (AP) + pembayaran (cash masuk laporan kas)
- Purchase return parsial

**Pelanggan**
- Member/tier, price level, poin loyalitas (ledger EARN/REDEEM/ADJUST/REVERSAL)
- Store credit (ledger IN/OUT), piutang + limit kredit + pelunasan

**Laporan & Ekspor**
- 12+ laporan live dari SQLite, Export **PDF** (zero-dependency) / CSV / Print A4
- X/Z report + history, sales by hour/payment/category, inventory valuation

**Offline Infrastruktur**
- SQLite WAL + foreign_keys + busy retry + light maintenance + quick_check
- Backup via SQLite Backup API (bukan copy file mentah), auto + retention
- Database Health Center: journal mode, integrity, schema version, ukuran WAL, dan ruang disk
- Folder backup eksternal yang dapat dipilih, validasi schema saat restore
- Pengeluaran operasional terhubung ke cash shift dan laporan laba
- Penutupan periode YYYY-MM untuk mencegah transaksi historis berubah
- Update offline via USB/share: manifest + SHA-256 + **ECDSA signature**
- Lisensi offline ECDSA P-256 + Master Keygen terpisah
- Dwi-bahasa: Indonesia / English

## Architecture

```
KasirPro.App        WinForms UI (net10.0-windows)
KasirPro.Core       Domain: entities, Money (integer cents), calculators, promo engine
KasirPro.Infrastructure  SQLite (Dapper), services, migrations, printing, backup, updater
KasirPro.Licensing  Machine ID + ECDSA P-256 token verify (public key only)
KasirPro.Keygen     MASTER KEYGEN (developer only): private key DPAPI + keygen.db
KasirPro.Tests      186 xUnit tests
```

Layer: UI -> Services -> Domain -> Infrastructure -> SQLite. Uang = **integer cents**
(never floating point). Semua operasi bisnis transactional. Permission divalidasi
di service (Auth.Require), menu hanya menyembunyikan.

## Build & Test

Butuh .NET SDK 10:

```
dotnet restore
dotnet build -c Release
dotnet test tests/KasirPro.Tests -c Release
```

Atau: `powershell scripts\build-release.ps1`

## Publish

```
# Portable (butuh .NET Desktop Runtime 10 di PC target)
powershell scripts\publish-portable.ps1

# Self-contained (tidak butuh runtime, ~112 MB)
powershell scripts\publish-selfcontained.ps1
```

Output: `dist/KasirPro/KasirPro.exe` + `dist/DeveloperTools/KasirPro.Keygen/`
(private key TIDAK pernah ikut package pelanggan).

## Run

```
KasirPro.exe                          # GUI lengkap
KasirPro.exe --print-machine-id       # Machine ID komputer ini
KasirPro.exe --verify-license <tok>   # aktivasi via CLI
KasirPro.exe --seed-demo              # data demo (dev)
KasirPro.exe --reset-demo             # reset DB ke demo
KasirPro.exe --diag-license           # fingerprint public key + status lisensi
```

## Database (SQLite)

File: `<folder app>\Data\pos.db` (relatif, portable).
`foreign_keys=ON`, `journal_mode=WAL`, `synchronous=NORMAL`, busy_timeout 10s,
retry SQLITE_BUSY 3x, light maintenance (checkpoint+optimize+quick_check).
Migrasi versioned v1-v6 (idempotent, transactional).

### Batas penggunaan SQLite

KasirPro menggunakan satu database SQLite lokal per perangkat. Jangan menaruh
`Data/pos.db` di folder network share untuk dipakai langsung oleh beberapa
komputer, karena SQLite bukan database multi-kasir melalui SMB. Untuk beberapa
terminal diperlukan satu host lokal/API atau mekanisme sinkronisasi khusus.

Folder backup dapat diarahkan ke USB atau drive lain melalui Store Settings.
Backup memakai SQLite Backup API dan dapat diperiksa dari menu Tools >
Kesehatan Database.

## Backup / Restore

- Manual: Tools > Backup Database
- Otomatis: tutup shift + exit + (retensi configurable 3-100 file)
- Menggunakan SQLite Backup API + integrity_check hasil backup
- Restore: validasi file -> pre-restore backup -> swap -> verify (Admin only)

## Printer

- Role printer terpisah: Receipt (58/80mm), Label, A4, Report, Drawer connector
- Thermal via Windows PrintDocument; Bluetooth/LAN printer yang terinstall di
  Windows otomatis tersedia (Settings > Printers & scanners)
- Cash drawer: ESC p 0 via Winspool RAW, permission DRAWER.OPEN + audit
- Hardware Test Center: Tools > Hardware Test Center (scanner/printer/drawer/scale)

## Barcode Scanner

- Mode HID (default): scanner = keyboard, suffix Enter, fokus otomatis
- Mode COM: pilih port di Hardware Test Center
- Test: scan di tab Barcode Scanner -> tampil raw value, tipe, produk matched
- Scan cache: barcode+SKU dimuat sekali ke memori (fallback query saat miss)

## Barcode & Labels

- Tools > Barcode & Labels: generate internal EAN-13 (check digit benar),
  duplicate checker, validasi EAN, label designer (25x15 s/d 50x40mm),
  preview + print massal, Code128 renderer

## Licensing / Keygen

Model ECDSA P-256. Private key di Keygen (DPAPI + passphrase), POS hanya
public key. Token `KPR1.<payload>.<sig>`. Verifikasi setiap startup.

```
KasirPro.Keygen.exe --init <passphrase>
KasirPro.Keygen.exe --generate "Toko" KP-XXXX-XXXX-XXXX lifetime <passphrase>
KasirPro.Keygen.exe --fingerprint
```

## Update Offline

1. Developer: `UpdateService.BuildPackage(...)` -> manifest.json + payload +
   manifest.sig (ECDSA, auto pakai Keys/update-private.pem)
2. Salin folder paket ke USB/share, set di Store Settings
3. Tools > Check for Update (Offline): verifikasi signature + SHA-256 semua
   file + path traversal protection -> stage -> applied on next launch
4. Data/, Backup/, Logs/, license.dat TIDAK PERNAH ditimpa

## Role & Permission

| Role | Contoh hak | Tidak boleh |
|------|-----------|-------------|
| Owner | Semua | - |
| Admin | Semua | - |
| Supervisor | void/return/exchange, stock adjust, drawer, approval | USER.MANAGE, ROLE.MANAGE |
| Cashier | POS, hold/recall, return, diskon | void, user mgmt, restore, settings, product delete |

Permission bisa diatur via tabel role_permissions. Menu menyembunyikan yang
tidak berhak; service menolak + audit bila dipanggil paksa.

## Troubleshooting

- **Printer not found**: pastikan terinstall di Windows, klik Refresh di dialog
- **Bluetooth printer tidak mencetak**: pairing dulu ke Windows sampai muncul
  di daftar printer; aplikasi tidak connect BLE langsung
- **Scanner not detected**: mode HID butuh fokus di field barcode; mode COM
  perlu port benar; test di Hardware Test Center
- **Database locked**: retry otomatis 3x; jangan share pos.db lewat jaringan
- **License invalid**: cocokkan fingerprint (`--diag-license` vs `--fingerprint`)
- **Backup failed**: pastikan folder aplikasi writable
- **COM port error**: port dipakai aplikasi lain, tutup lalu refresh
- **Windows write permission**: jalankan dari folder writable (bukan Program Files)

Fitur v1.1.0:
- POS kasir cepat (barcode scan instan dengan in-memory scan cache)
- Split payment: Cash / QRIS / Debit / Transfer (+ **Piutang/credit** opsional)
- Inventory ledger lengkap, stock opname, shift kasir + laporan selisih kas
- **Piutang**: daftar hutang pelanggan, cicilan/pelunasan, integrasi kas shift
- 9 laporan dengan **Export PDF** + Export CSV + print
- Backup/restore aman (SQLite Backup API) + otomatis
- Dwi-bahasa: **Bahasa Indonesia / English** (Store Settings)
- **Update offline** via folder share/USB (manifest + checksum SHA-256)
- Lisensi offline ECDSA P-256 + Master Keygen terpisah

## Architecture

```
UI (KasirPro.App - WinForms)
   |
Application Services (KasirPro.Infrastructure.Services)
   |
Domain / Core (KasirPro.Core - entities, Money, SaleCalculator, PasswordHasher)
   |
Infrastructure (KasirPro.Infrastructure - SQLite/Dapper, Migrator, AppPaths)
   |
SQLite (Data/pos.db, WAL mode, foreign_keys ON)

KasirPro.Licensing  : verifikasi lisensi offline (ECDSA P-256, public key only)
KasirPro.Keygen     : MASTER KEYGEN (developer only, private key DPAPI-encrypted)
KasirPro.Tests      : xUnit (lisensi, kalkulasi, stok, migrasi, backup)
```

Prinsip: uang disimpan sebagai INTEGER cents (tanpa floating point drift),
semua transaksi penjualan/pembelian/retur/adjustment atomic dalam satu
SQLite transaction, stok selalu konsisten dengan tabel ledger `stock_movements`.

## Folder Structure

```
KasirPro.sln
src/
  KasirPro.App/            UI WinForms (KasirPro.exe)
  KasirPro.Core/           Entities, enums, kalkulasi, hashing
  KasirPro.Infrastructure/ SQLite, migrasi, semua service bisnis
  KasirPro.Licensing/      Machine ID, token lisensi, aktivasi
tools/
  KasirPro.Keygen/         Master Keygen (JANGAN dibagikan ke pelanggan)
tests/
  KasirPro.Tests/          Automated tests
```

Runtime (folder aplikasi - semua relatif, otomatis dibuat):

```
KasirPro.exe
appsettings.json
license.dat            <- dibuat setelah aktivasi
Data/pos.db            <- database utama
Backup/                <- hasil backup (POS-yyyyMMdd-HHmmssfff.db)
Images/Products/       <- gambar produk (path relatif disimpan di DB)
Exports/Reports/       <- hasil export CSV laporan
Logs/                  <- kasirpro-yyyy-MM-dd.log
```

## How to Build

Project ini membutuhkan .NET SDK 10.0.101. Dari root repo:

```
dotnet restore
dotnet build -c Release
dotnet test tests/KasirPro.Tests
```

## How to Run

```
cd src/KasirPro.App/bin/Release/net10.0-windows
KasirPro.exe
```

Urutan startup:
1. Validasi lisensi (signature + machine ID, setiap kali startup)
2. Belum aktif -> layar Aktivasi Lisensi
3. Setup awal (info toko, buat PIN admin, printer opsional)
4. Login -> Dashboard

CLI tanpa UI:

```
KasirPro.exe --print-machine-id        # tampilkan Machine ID
KasirPro.exe --verify-license <token>  # aktivasi via command line
KasirPro.exe --seed-demo               # isi data demo (dev)
KasirPro.exe --reset-demo              # reset database ke data demo
```

## SQLite Location

`<folder aplikasi>\Data\pos.db` - relatif terhadap `AppContext.BaseDirectory`.
Pindahkan folder aplikasi ke drive lain (misal E:\KasirPro) maka database
otomatis mengikuti (E:\KasirPro\Data\pos.db). Tidak ada absolute path.

Konfigurasi SQLite: `foreign_keys=ON`, `journal_mode=WAL`,
`synchronous=NORMAL`, `busy_timeout=10s`. Migrasi versioned di tabel
`database_version` (idempotent, transactional).

## How Backup Works

- Manual: Data > Tools > Backup Database.
- Otomatis saat Close Shift dan saat aplikasi ditutup normal jika ada
  perubahan data (dapat dimatikan di Store Settings).
- Menggunakan SQLite Backup API (`SqliteConnection.BackupDatabase`) setelah
  WAL checkpoint - BUKAN copy file mentah, sehingga aman saat database aktif.
- Hasil diverifikasi `integrity_check` sebelum dinyatakan sukses.
- Nama file: `Backup/POS-yyyyMMdd-HHmmssfff.db`, retensi default 30 file.
- Restore: Data > Tools > Restore Database -> pilih file -> validasi ->
  database saat ini dibackup dulu -> swap -> reload. Juga ada file eksplorator
  untuk memilih backup dari luar folder Backup.

## How Thermal Printer Works

- Printer struk dipilih di Data > Tools > Printer Setup (58mm / 80mm) +
  tombol Print Test.
- Cetak memakai Windows `PrintDocument` (GDI) - tidak butuh SDK khusus.
  Printer thermal Windows biasanya tampil sebagai Generic/Text Only atau
  driver OEM; struk dicetak monospace sesuai lebar kertas.
- Template struk (nama toko, alamat, footer) diatur di Store Settings dan
  Receipt Setup.

## How Barcode Scanner Works

- Scanner USB dianggap keyboard HID: scan = ketik barcode + ENTER.
- POS memakai **scan cache**: semua barcode + kode produk aktif dimuat sekali
  ke memori, sehingga scan tanpa query database (fallback ke query bila cache
  miss). Cache di-refresh saat F5 / buka ulang halaman POS.
- Ditemukan -> tambah ke cart (increment jika sudah ada), fokus kembali ke
  field barcode. Tidak perlu SDK/driver khusus.

## Piutang (Receivables)

- Aktifkan di Store Settings ("Aktifkan piutang"), default mati.
- Saat pembayaran < total, tombol **+ PIUTANG** di dialog pembayaran mengubah
  sisa menjadi hutang atas nama pelanggan terdaftar (bukan Umum/Walk-in).
- Piutang dikelola di Data > Transaction > **Debts (Piutang)**: daftar sisa,
  jatuh tempo, cicilan/pelunasan parsial atau penuh (Cash/QRIS/Debit/Transfer).
- Pelunasan cash masuk ke shift kasir terbuka (kolom `debt_payments`), otomatis
  dihitung dalam "System Cash" saat tutup shift dan muncul di Cash Report.
- Sale dengan piutang outstanding tidak dapat di-void sebelum dilunasi.
- Semua pelunasan tercatat di audit log (DEBT_SETTLE).

## Export PDF Laporan

Setiap laporan punya tombol **Export PDF**. Generator PDF ditulis sendiri
(zero-dependency, PDF 1.4 + font core Helvetica, A4 landscape, paginasi +
footer nomor halaman) sehingga tidak menambah dependensi eksternal.

## Bahasa (i18n)

- Store Settings > "Bahasa (Language)": Bahasa Indonesia / English.
- Teks terpusat di `src/KasirPro.App/UI/Strings.cs` (kunci -> id/en);
  menambah bahasa/kunci = tambah entri kamus.
- Perubahan bahasa berlaku penuh setelah aplikasi dibuka ulang.

## Update Offline (tanpa internet)

Alur developer:

```
1. Build rilis baru, lalu buat paket (contoh via kode/helper test):
   UpdateService.BuildPackage(<folder bin rilis>, <folder paket>, "1.2.0", "catatan")
   -> menghasilkan manifest.json + salinan file + checksum SHA-256
   (folder Data/, Backup/, Logs/, license.dat otomatis dikecualikan)
2. Salin folder paket ke USB / network share.
```

Alur toko:

```
1. Data > Tools > Check for Update (Offline)
2. Isi folder update (tersimpan di Store Settings), Check.
3. Versi lebih baru + checksum cocok -> Apply.
4. Tutup aplikasi; saat dibuka lagi, UpdateStager menyalin file baru
   (Data/Backup/license tidak pernah ditimpa).
```

## How License Works

Model: **tanda tangan digital asimetris (ECDSA P-256)**.

- KasirPro.Keygen memegang PRIVATE key (DPAPI + passphrase encrypted).
- KasirPro.exe hanya membawa PUBLIC key (embedded di KasirPro.Licensing).
- Token lisensi: `KPR1.<payload-base64url>.<signature-base64url>`
- Payload berisi version, product, customer, machineId, licenseType,
  licenseId, issuedAt, expiresAt.
- Saat startup, KasirPro selalu memverifikasi ulang signature + machine ID +
  expiry dari `license.dat`. Tidak ada flag `IsActivated=true` yang dipercaya.
- license.dat dihapus -> layar aktivasi muncul kembali. Diubah -> verification
  gagal. Ditulis ulang untuk komputer lain -> ditolak.

Detail keamanan: lihat SECURITY-LICENSING.md.

## How to Initialize Master Key

Hanya developer (sekali):

```
cd tools/KasirPro.Keygen/bin/Debug/net10.0-windows
KasirPro.Keygen.exe --init <passphrase-kuat>
```

atau jalankan UI KasirPro.Keygen.exe -> GENERATE MASTER KEY PAIR.
Hasilnya: `Keys/private.key` (DPAPI+passphrase terenkripsi) dan
`Keys/public.key`. Passphrase hilang = master key tidak bisa dipakai.

## How to Generate License

```
# CLI
KasirPro.Keygen.exe --generate "Toko Makmur" KP-A3AC-810D-9E20 lifetime <passphrase>
KasirPro.Keygen.exe --generate "Toko X" KP-A3AC-810D-9E20 annual <passphrase>   # expired 1 tahun
KasirPro.Keygen.exe --generate "Toko X" KP-A3AC-810D-9E20 trial <passphrase>    # trial 14 hari
```

atau UI Keygen: isi Customer Name + Machine ID -> GENERATE LICENSE ->
COPY LICENSE. Riwayat lisensi tersimpan di `Data/keygen.db` (terpisah dari POS).

## How to Activate Customer POS

1. Di komputer pelanggan, jalankan `KasirPro.exe --print-machine-id`
   (atau baca Machine ID di layar aktivasi).
2. Kirim Machine ID ke developer.
3. Developer generate license (Keygen), kirim token `KPR1....` ke pelanggan.
4. Pelanggan paste token di layar Aktivasi (atau
   `KasirPro.exe --verify-license <token>`).
5. Valid -> tersimpan sebagai license.dat -> lanjut ke setup/login.

## How to Publish

```
# aplikasi pelanggan (framework-dependent, butuh .NET Desktop Runtime 6)
dotnet publish src/KasirPro.App -c Release -r win-x64 --self-contained false -o dist/KasirPro

# aplikasi pelanggan (self-contained, tidak butuh runtime terinstall)
dotnet publish src/KasirPro.App -c Release -r win-x64 --self-contained true -o dist/KasirPro

# developer tools (tidak ikut package pelanggan)
dotnet publish tools/KasirPro.Keygen -c Release -r win-x64 --self-contained false -o dist/DeveloperTools/KasirPro.Keygen
```

Atau gunakan publish profile: `dotnet publish /p:PublishProfile=KasirPro-x64`.

Hasil `dist/KasirPro/` berisi KasirPro.exe + runtime files. Folder Data/,
Backup/, dll. dibuat otomatis saat pertama dijalankan. Private key TIDAK
pernah ikut dalam package pelanggan.

## Tests

```
dotnet test tests/KasirPro.Tests
```

Mencakup: valid/invalid signature, wrong machine, corrupted payload,
expired, wrong product, aktivasi round-trip; kalkulasi total/diskon/change/
split payment; sale membuat stock movement; purchase menambah stok;
return mengembalikan stok; migrasi + idempotent; backup/restore round-trip;
uniqueness invoice.
