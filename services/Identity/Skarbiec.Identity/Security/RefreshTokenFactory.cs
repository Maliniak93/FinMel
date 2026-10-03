using System.Security.Cryptography;
using System.Text;

namespace Skarbiec.Identity.Security;

public readonly record struct GeneratedRefreshToken(string RawValue, string Hash);

// Only the hash is persisted, so a database read never discloses a replayable token.
public static class RefreshTokenFactory
{
    private const int RawValueByteLength = 32;

    public static GeneratedRefreshToken Create()
    {
        var rawValue = Base64Url(RandomNumberGenerator.GetBytes(RawValueByteLength));

        return new GeneratedRefreshToken(rawValue, Hash(rawValue));
    }

    public static string Hash(string rawValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawValue)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
