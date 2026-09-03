# KasirPro - POS Retail Windows Offline

Aplikasi kasir (POS) desktop Windows **100% offline** untuk toko retail:
C# WinForms + SQLite lokal, tanpa internet, tanpa server, tanpa cloud.
Portable - seluruh data berada di folder aplikasi, cukup dipindahkan ke flashdisk.

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

Butuh .NET SDK 6 (LTS). Dari root repo:

```
dotnet restore
dotnet build -c Release
dotnet test tests/KasirPro.Tests
```

## How to Run

```
cd src/KasirPro.App/bin/Release/net6.0-windows
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
cd tools/KasirPro.Keygen/bin/Debug/net6.0-windows
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
