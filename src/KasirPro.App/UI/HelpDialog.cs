namespace KasirPro.App.UI;

/// <summary>F1 quick help: shortcut list + workflow cheatsheet.</summary>
public class HelpDialog : Form
{
    public HelpDialog()
    {
        Text = "Bantuan KasirPro";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(680, 600);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var header = Theme.PageHeader("Bantuan", "Shortcut keyboard & alur kerja cepat");
        var body = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f),
            BackColor = Theme.Card,
            BorderStyle = BorderStyle.None
        };
        body.Text =
@"KEYBOARD SHORTCUTS
==================
F2   Fokus barcode / buka POS
F3   Fokus pencarian produk
F4   Fokus pilih pelanggan
F5   Refresh halaman aktif
F6   Hold (parkir) transaksi
F7   Recall transaksi terparkir
F8   Fokus diskon
F9   Pembayaran
F10  Buka cash drawer (butuh izin DRAWER.OPEN)
F11  Fullscreen
F1   Bantuan (halaman ini)
ESC  Tutup dialog / kembali

ALUR KASIR CEPAT
================
1. F2 -> scan barcode (Enter otomatis menambah ke keranjang)
2. Keranjang benar? -> F9
3. Input pembayaran (split boleh: + Cash + QRIS, dst)
4. BAYAR -> Payment Success -> Print Receipt / New Sale

TUKAR BARANG (EXCHANGE)
=======================
Data > Transaction > Exchange: cari invoice, centang barang lama,
tambah barang baru, selisih otomatis dihitung.

RETUR
=====
Data > Transaction > Sales Return: cari invoice, centang item + qty,
pilih refund (tanpa/cash/store credit), proses.

PIUTANG
=======
Aktifkan di Store Settings, set limit kredit pelanggan.
Saat bayar kurang dari total -> tombol + PIUTANG.
Pelunasan: Data > Transaction > Debts (Piutang).

PO / PEMBELIAN
==============
Simple: Data > Transaction > Purchase (langsung stok masuk)
Advanced: Transaction > Purchase Orders (draft -> approve -> receive parsial)

STOK
====
Stock In/Out/Adjustment: Data > Inventory
Opname: hitung fisik -> posting (stok menyesuaikan)
Transfer: Inventory > Gudang & Transfer Stok
Saran belanja: Inventory > Purchase Suggestion -> Buat Draft PO

PRINTER & HARDWARE
==================
Tools > Hardware Test Center: tes scanner/printer/drawer/timbangan
Tools > Barcode & Labels: generate barcode internal + cetak label
Printer role terpisah: struk/label/A4 di Printer Setup

LAPORAN
=======
Semua laporan: Data > Reports (filter tanggal, Print, Export PDF/CSV)
X-Report = ringkasan shift berjalan; Z-Report = tutup shift final

TIPS
====
- Semua transaksi atomic: kalau gagal, otomatis rollback.
- Backup otomatis saat tutup shift & keluar (bisa diatur).
- Detail teknis error tersimpan di folder Logs.
";

        var close = Theme.PrimaryButton("TUTUP", 110, 36);
        close.Location = new Point(540, 540);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (s, e) => Close();

        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(close);
    }
}
