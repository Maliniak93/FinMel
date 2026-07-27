using System.Security.Cryptography;
using System.Text;

namespace Skarbiec.Identity.Security;

public readonly record struct GeneratedRefreshToken(string RawValue, string Hash);

/// <summary>
/// The raw value is what goes in the httpOnly cookie; only its hash is persisted, so a DB
/// read never discloses a value an attacker could replay (ADR-005: rotation, no server-side
/// access-token blacklist — the refresh token store is the thing worth protecting).
/// </summary>
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
