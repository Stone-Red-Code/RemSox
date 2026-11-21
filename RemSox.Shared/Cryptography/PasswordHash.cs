using System.Globalization;
using System.Security.Cryptography;

namespace RemSox.Shared.Cryptography;

/// <summary>
/// A password as it is stored: its PBKDF2-HMAC-SHA256 key with the salt and iteration count it
/// was made with, written as <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt hex&gt;$&lt;key hex&gt;</c>.
/// The password itself cannot be read back from it.
/// </summary>
public sealed class PasswordHash
{
    /// <summary>Size of the random salt in bytes.</summary>
    public const int SaltSize = 16;

    private const string Scheme = "pbkdf2-sha256";

    private PasswordHash(int iterations, byte[] salt, byte[] key)
    {
        Iterations = iterations;
        Salt = salt;
        Key = key;
    }

    /// <summary>Gets the number of PBKDF2 rounds.</summary>
    public int Iterations { get; }

    /// <summary>Gets the random salt, so equal passwords get different hashes.</summary>
    public byte[] Salt { get; }

    /// <summary>Gets the derived key.</summary>
    public byte[] Key { get; }

    /// <summary>Hashes <paramref name="password"/> with a new random salt. Deliberately slow.</summary>
    public static PasswordHash Create(string password, int iterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new PasswordHash(iterations, salt, Pkcs5S2PasswordKeyDeriver.DeriveKey(password, salt, iterations));
    }

    /// <summary>
    /// Returns whether <paramref name="password"/> is the password this is the hash of. As slow
    /// as <see cref="Create"/>; the comparison takes the same time whatever matches.
    /// </summary>
    public bool Matches(string password)
    {
        byte[] key = Pkcs5S2PasswordKeyDeriver.DeriveKey(password, Salt, Iterations);
        return Org.BouncyCastle.Utilities.Arrays.FixedTimeEquals(key, Key);
    }

    /// <summary>Reads a hash written by <see cref="ToString"/>; null when the text is not one.</summary>
    public static PasswordHash? Parse(string text)
    {
        string[] parts = text.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) || iterations <= 0)
        {
            return null;
        }

        try
        {
            byte[] salt = Convert.FromHexString(parts[2]);
            byte[] key = Convert.FromHexString(parts[3]);
            return salt.Length == SaltSize && key.Length == Pkcs5S2PasswordKeyDeriver.KeySize ? new PasswordHash(iterations, salt, key) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public override string ToString()
    {
        return $"{Scheme}${Iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToHexString(Salt)}${Convert.ToHexString(Key)}";
    }
}
