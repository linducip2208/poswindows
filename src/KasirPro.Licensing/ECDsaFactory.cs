using System.Security.Cryptography;

namespace KasirPro.Licensing;

/// <summary>Test/dev helper: builds an ECDsa instance from a PEM public key string.</summary>
public static class ECDsaFactory
{
    public static ECDsa FromPem(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }
}
