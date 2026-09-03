using System.Globalization;
using System.Text;
using KasirPro.Infrastructure;

namespace KasirPro.App.UI;

public static class UiHelpers
{
    public static string Money(decimal v) => KasirPro.Core.Domain.Money.Format(v);

    public static string MoneyPlain(decimal v) => KasirPro.Core.Domain.Money.FormatPlain(v);

    public static void Info(string msg, string title = "KasirPro") =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Warn(string msg, string title = "KasirPro") =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Error(string msg, string title = "KasirPro") =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static bool Confirm(string msg, string title = "Konfirmasi") =>
        MessageBox.Show(msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static void ShowChildError(Exception ex) =>
        Error("Terjadi kesalahan: " + ex.Message + Environment.NewLine +
              "Detail teknis disimpan di folder Logs.");

    public static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            Warn(ex.Message);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLogger.Instance.Error("UI error", ex);
            ShowChildError(ex);
        }
    }

    public static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException ex)
        {
            Warn(ex.Message);
            return default!;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLogger.Instance.Error("UI error", ex);
            ShowChildError(ex);
            return default!;
        }
    }

    public static string DateId(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

