# SECURITY - LICENSING

> **PRIVATE MASTER KEY NEVER SHIPS WITH CUSTOMER APPLICATION.**

## Model

```
MASTER KEYGEN (developer)                     POS APP (pelanggan)
--------------------------                    --------------------
Keys/private.key                              EmbeddedPublicKey (PUBLIC key only,
DPAPI(CurrentUser) + AES(passphrase)          PEM, di KasirPro.Licensing)
+ keygen.db (riwayat)
        |                                             |
        | sign(payload, privKey)                      |
        +------------------ KPR1.<payload>.<sig> ---->| verify(token, pubKey)
                                                      | + machineId check + expiry check
```

Algoritma: **ECDSA P-256 + SHA-256** (`System.Security.Cryptography`, standar .NET).
Tidak ada kriptografi buatan sendiri. Tidak ada `MD5(machineId)` atau
`SHA256(machineId + secret-di-app)` sebagai sistem lisensi utama.

## Yang TIDAK boleh berada di aplikasi pelanggan

- Private master key (`Keys/private.key`, isi kapan pun bentuknya)
- Passphrase master key
- Keygen database (`keygen.db`)
- Algoritma signing (hanya verifikasi)

Public key BOLEH dan HARUS di-commit / di-embed - itu memang untuk umum.

## Token

Format: `KPR1.<payload-base64url>.<signature-base64url>`

Payload (JSON, ditandatangani utuh):

```json
{
  "version": 1,
  "product": "KasirPro",
  "customer": "Toko Makmur",
  "machineId": "KP-A3AC-810D-9E20",
  "licenseType": "Lifetime",
  "licenseId": "LIC-BB093629625B",
  "issuedAt": "2026-09-02T21:56:25.69Z",
  "expiresAt": null
}
```

- `product` wajib `KasirPro` - token untuk produk lain ditolak.
- `machineId` dicocokkan dengan komputer lokal.
- `expiresAt` null = lifetime; selain itu dibandingkan dengan UTC.

## Machine ID

Dihitung dari kombinasi identifier stabil Windows:

- `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`
- System UUID (`Win32_ComputerSystemProduct.UUID`) bila tersedia dan valid

Normalisasi (uppercase + trim) -> SHA-256 -> format pendek `KP-XXXX-XXXX-XXXX`
(12 hex pertama) atau full hash 64 hex. Tidak memakai IP address; MAC address
tidak dipakai sebagai satu-satunya identifier; bila identifier sekunder tidak
tersedia, Machine ID tetap dihasilkan dari MachineGuid saja.

## Verifikasi setiap startup

`license.dat` (di samping exe) berisi envelope JSON + token. Setiap kali
aplikasi dijalankan:

1. Baca license.dat - hilang -> Activation Screen.
2. Parse token - format buruk -> "Corrupted License".
3. Verifikasi signature dengan PUBLIC key - gagal -> "Invalid License".
4. Cek product - beda -> "Invalid License".
5. Cek machineId - beda -> "License for Different Computer".
6. Cek expiry - lewat -> "Expired License".
7. Semua lolos -> aplikasi berjalan.

Tidak ada flag/registry `IsActivated` yang dipercaya. Menambah/kurang/mengubah
satu karakter pada license.dat membuat signature tidak valid lagi.

## Private key protection (Keygen)

`Keys/private.key` = PKCS#8 private key yang:

1. Dienkripsi AES-256 dengan key turunan PBKDF2 (150k iterasi, SHA-256)
   dari passphrase master.
2. Hasilnya dilindungi Windows DPAPI `CurrentUser` sebagai lapis kedua.

Akibatnya: file private key tidak berguna di komputer lain (DPAPI berbeda)
atau tanpa passphrase. Kehilangan passphrase = master key tidak dapat dipakai
(generate key pair baru, redistribute public key).

## Rotasi

Untuk mengganti master key: buat key pair baru di Keygen, export public key
baru, embed ulang di `KasirPro.Licensing`, build ulang KasirPro.App, lalu
generate ulang lisensi semua pelanggan.

## Checklist rilis

- [ ] `tools/**/Keys/` dan `tools/**/Data/` TIDAK ikut package pelanggan
- [ ] `license.dat`, `*.license`, `*.pem`, `*.db` tidak di-commit (lihat .gitignore)
- [ ] Embedded public key di KasirPro.Licensing cocok dengan private key Keygen aktif
- [ ] Publish pelanggan hanya dari `src/KasirPro.App`
