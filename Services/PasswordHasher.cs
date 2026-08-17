using System.Security.Cryptography;

namespace MeDan.Api.Services;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing for API-native staff accounts.
/// Format: <c>v1.{iterations}.{base64 salt}.{base64 hash}</c> — self-describing so the
/// work factor can be raised later without invalidating existing hashes.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 210_000; // OWASP guidance for PBKDF2-SHA256

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{DefaultIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    /// <summary>Constant-time verification. Returns false for malformed or null hashes.</summary>
    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return false;

        var parts = encoded.Split('.');
        if (parts.Length != 4 || parts[0] != "v1") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
