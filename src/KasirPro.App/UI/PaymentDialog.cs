using KasirPro.Core.Domain;

namespace KasirPro.App.UI;

public class PaymentLine
{
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
}

/// <summary>
/// Payment dialog: Cash / QRIS / Debit / Transfer with split payment support.
/// QRIS is recorded as a payment method only (no gateway, fully offline).
/// When store credit (piutang) is enabled and a real customer is chosen,
/// the unpaid remainder can be assigned as receivable.
/// </summary>
public class PaymentDialog : Form
{
    private readonly decimal _grandTotal;
    private readonly bool _allowCredit;
    private readonly decimal _maxRedeemable;
    private readonly List<PaymentLine> _lines = new();
    private readonly FlowLayoutPanel _linesPanel;
    private readonly Label _lblRemaining;
    private readonly Label _lblPaid;
    private readonly Label _lblChange;
    private readonly Button _btnConfirm;

    public List<PaymentLine> ResultPayments => _lines;
    public decimal CreditAmount { get; private set; }
    /// <summary>Points redemption value (rupiah) applied to this sale.</summary>
    public decimal PointsRedeemed { get; private set; }
    /// <summary>Max redeemable value (rupiah) offered by loyalty settings.</summary>
    public decimal PointsRedeemable => _maxRedeemable;

    public PaymentDialog(decimal grandTotal, bool allowCredit = false, decimal maxRedeemable = 0)
    {
        _grandTotal = grandTotal;
        _allowCredit = allowCredit;
        _maxRedeemable = Math.Min(maxRedeemable, grandTotal);
        Text = "Pembayaran";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 480);
        BackColor = Theme.Bg;
        Font = Theme.FontBase;

        var totalLbl = Theme.Label("GRAND TOTAL", 11, true);
        totalLbl.Location = new Point(20, 12);
        var totalVal = Theme.Label(Money.Format(grandTotal), 18, true, Theme.Accent);
        totalVal.Location = new Point(20, 36);

        _linesPanel = new FlowLayoutPanel
        {
            Location = new Point(20, 84),
            Size = new Size(520, 180),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Card
        };

        var addCash = Theme.SecondaryButton("+ Cash", 100);
        addCash.Location = new Point(20, 272);
        addCash.Click += (s, e) => AddLine(PaymentMethod.Cash);
        var addQris = Theme.SecondaryButton("+ QRIS", 100);
        addQris.Location = new Point(126, 272);
        addQris.Click += (s, e) => AddLine(PaymentMethod.Qris);
        var addDebit = Theme.SecondaryButton("+ Debit", 100);
        addDebit.Location = new Point(232, 272);
        addDebit.Click += (s, e) => AddLine(PaymentMethod.Debit);
        var addTransfer = Theme.SecondaryButton("+ Transfer", 100);
        addTransfer.Location = new Point(338, 272);
        addTransfer.Click += (s, e) => AddLine(PaymentMethod.Transfer);
        var removeLine = Theme.DangerButton("Hapus", 90);
        removeLine.Location = new Point(448, 272);
        removeLine.Click += (s, e) => { if (_lines.Count > 0) { _lines.RemoveAt(_lines.Count - 1); RenderLines(); } };

        _lblPaid = Theme.Label("Dibayar:  Rp 0", 10, true);
        _lblPaid.Location = new Point(20, 330);
        _lblRemaining = Theme.Label("Remaining:  Rp 0", 10, true, Theme.Danger);
        _lblRemaining.Location = new Point(20, 354);
        _lblChange = Theme.Label("Kembalian:  Rp 0", 10, true, Theme.Success);
        _lblChange.Location = new Point(20, 378);

        _btnConfirm = Theme.SuccessButton("BAYAR", 200, 46);
        _btnConfirm.Location = new Point(340, 396);
        _btnConfirm.Click += OnConfirm;

        var cancel = Theme.SecondaryButton("Batal", 90, 46);
        cancel.Location = new Point(20, 396);
        cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

        var addButtons = new List<Control> { addCash, addQris, addDebit, addTransfer, removeLine };

        if (_allowCredit)
        {
            var addCredit = Theme.WarningButton("+ PIUTANG", 120, 34);
            addCredit.Location = new Point(20, 300);
            addCredit.Click += (s, e) => AssignCredit();
            var creditNote = Theme.Label("Sisa biaya menjadi piutang atas nama pelanggan.", 8, false, Theme.Muted);
            creditNote.Location = new Point(148, 306);
            addButtons.Add(addCredit);
            addButtons.Add(creditNote);
        }

        if (_maxRedeemable > 0)
        {
            var usePoints = Theme.SuccessButton("PAKAI POIN", 120, 34);
            usePoints.Location = new Point(_allowCredit ? 290 : 20, 300);
            usePoints.Click += (s, e) =>
            {
                var usable = Math.Min(_maxRedeemable - PointsRedeemed, Remaining());
                if (usable <= 0) { UiHelpers.Warn("Tidak ada poin yang bisa dipakai untuk sisa ini."); return; }
                PointsRedeemed = Money.Round(PointsRedeemed + usable);
                UpdateSummary();
            };
            var clearPoints = Theme.SecondaryButton("Reset Poin", 100, 34);
            clearPoints.Location = new Point(_allowCredit ? 418 : 148, 300);
            clearPoints.Click += (s, e) => { PointsRedeemed = 0; UpdateSummary(); };
            addButtons.Add(usePoints);
            addButtons.Add(clearPoints);
        }

        Controls.AddRange(new Control[]
        {
            totalLbl, totalVal, _linesPanel,
            _lblPaid, _lblRemaining, _lblChange, _btnConfirm, cancel
        });
        foreach (var b in addButtons) Controls.Add(b);

        // pre-fill with a single cash line for fast cashiering
        AddLine(PaymentMethod.Cash, grandTotal);
    }

    private void AddLine(PaymentMethod method, decimal? amount = null)
    {
        _lines.Add(new PaymentLine
        {
            Method = method,
            Amount = amount ?? Remaining()
        });
        RenderLines();
    }

    private decimal PaidSoFar() => ChangeCalculator.PaidSoFar(_lines.Select(l => (l.Method, l.Amount)));

    private decimal Remaining() => ChangeCalculator.Remaining(_grandTotal, _lines.Select(l => (l.Method, l.Amount)));

    private void RenderLines()
    {
        _linesPanel.Controls.Clear();
        foreach (var line in _lines)
        {
            var row = new Panel { Width = 490, Height = 34, BackColor = Theme.Card };
            var method = Theme.Combo(110);
            method.Location = new Point(2, 4);
            method.Items.AddRange(new object[] { "Cash", "Qris", "Debit", "Transfer" });
            method.SelectedIndex = (int)line.Method;
            method.SelectedIndexChanged += (s, e) =>
            {
                line.Method = (PaymentMethod)method.SelectedIndex;
                UpdateSummary();
            };

            var amount = Theme.TextBox(140);
            amount.Location = new Point(120, 5);
            amount.TextAlign = HorizontalAlignment.Right;
            amount.Text = Money.FormatPlain(line.Amount);
            amount.TextChanged += (s, e) =>
            {
                if (decimal.TryParse(amount.Text.Replace(".", "").Replace(",", ""), out var v))
                    line.Amount = v;
                UpdateSummary();
            };

            var quickFill = Theme.SecondaryButton("Sisa", 70, 26);
            quickFill.Location = new Point(268, 3);
            quickFill.Font = new Font("Segoe UI", 8f);
            quickFill.Click += (s, e) =>
            {
                line.Amount = Remaining() + line.Amount;
                amount.Text = Money.FormatPlain(line.Amount);
            };

            row.Controls.AddRange(new Control[] { method, amount, quickFill });
            _linesPanel.Controls.Add(row);
        }
        UpdateSummary();
    }

    private void AssignCredit()
    {
        var remaining = Remaining();
        if (remaining <= 0) { UiHelpers.Warn("Tidak ada sisa yang bisa dijadikan piutang."); return; }
        CreditAmount = remaining;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var paid = PaidSoFar();
        var effective = paid + CreditAmount + PointsRedeemed;
        var remaining = Money.Round(_grandTotal - effective);
        if (remaining < 0) remaining = 0;
        var change = Money.Round(effective - _grandTotal);
        _lblPaid.Text = "Dibayar:  Rp " + Money.FormatPlain(paid) +
                        (PointsRedeemed > 0 ? $"  (+ poin {Money.FormatPlain(PointsRedeemed)})" : "") +
                        (CreditAmount > 0 ? $"  (+ piutang {Money.FormatPlain(CreditAmount)})" : "");
        _lblRemaining.Text = "Remaining:  Rp " + Money.FormatPlain(remaining);
        _lblRemaining.ForeColor = remaining > 0 ? Theme.Danger : Theme.Success;
        _lblChange.Text = "Kembalian:  Rp " + Money.FormatPlain(change > 0 ? change : 0);

        _btnConfirm.Enabled = (remaining == 0 && (paid > 0 || PointsRedeemed > 0)) ||
                              (CreditAmount > 0 && CreditAmount >= remaining);
        _btnConfirm.BackColor = _btnConfirm.Enabled ? Theme.Success : Theme.Muted;
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var paid = PaidSoFar();
        var effective = paid + PointsRedeemed;
        if (CreditAmount > 0)
        {
            if (Money.Round(paid + PointsRedeemed + CreditAmount) < _grandTotal)
            {
                UiHelpers.Warn("Piutang belum mencakup seluruh sisa.");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        if (effective < _grandTotal)
        {
            UiHelpers.Warn("Jumlah pembayaran kurang dari total. " +
                $"Kurang {Money.Format(_grandTotal - effective)}.\n" +
                "Tambahkan metode pembayaran lain (split payment).");
            return;
        }
        if (_lines.Any(l => l.Amount < 0))
        {
            UiHelpers.Warn("Nominal pembayaran tidak valid.");
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
