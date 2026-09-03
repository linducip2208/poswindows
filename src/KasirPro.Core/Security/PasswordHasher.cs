using System.Security.Cryptography;

namespace KasirPro.Core.Security;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 60000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToHexString(key), Convert.ToHexString(salt));
    }

    public static bool Verify(string password, string hashHex, string saltHex)
    {
        if (string.IsNullOrEmpty(hashHex) || string.IsNullOrEmpty(saltHex)) return false;
        try
        {
            var salt = Convert.FromHexString(saltHex);
            var expected = Convert.FromHexString(hashHex);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
