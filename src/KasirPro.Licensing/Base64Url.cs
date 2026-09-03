using System.Text;

namespace KasirPro.Licensing;

public static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Decode(string data)
    {
        var s = data.Trim().Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Invalid Base64Url length");
        }
        return Convert.FromBase64String(s);
    }

    public static string EncodeText(string text) => Encode(Encoding.UTF8.GetBytes(text));

    public static string DecodeText(string data) => Encoding.UTF8.GetString(Decode(data));
}
