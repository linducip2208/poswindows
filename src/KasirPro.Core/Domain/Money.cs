using System.Globalization;

namespace KasirPro.Core.Domain;

/// <summary>Money helper - always rounded to 2 decimals, integer arithmetic under the hood to avoid FP drift.</summary>
public static class Money
{
    public static readonly string Code = "Rp";

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Of(long cents) => cents / 100m;

    public static long ToCents(decimal value) => (long)Money.Round(value * 100m);

    public static string Format(decimal value) =>
        Code + " " + Money.Round(value).ToString("N0", CultureInfo.GetCultureInfo("id-ID"));

    public static string FormatPlain(decimal value) =>
        Money.Round(value).ToString("N0", CultureInfo.GetCultureInfo("id-ID"));
}
