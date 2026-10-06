using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace CvPlatform.Application.Integration;

/// <summary>
/// Creates and verifies the opaque per-position API tokens. Only the SHA-256 hash of a token is
/// stored, so the plaintext secret exists just once (the moment it is generated).
/// </summary>
public static class PositionApiTokenSecret
{
    public const string Prefix = "cvp_";

    private const int SecretBytes = 32;

    public static string Create() =>
        Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    public static bool HasTokenPrefix(string token) =>
        token.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
