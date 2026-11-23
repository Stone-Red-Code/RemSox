using System.Globalization;
using System.Security.Cryptography;

namespace RemSox.Shared.Cryptography;

/// <summary>
/// A password as it is stored: the SRP verifier (<see cref="Srp"/>) of its PBKDF2-HMAC-SHA256
/// key, with the salt and iteration count it was made with, written as
/// <c>srp6-pbkdf2-sha256$&lt;iterations&gt;$&lt;salt hex&gt;$&lt;verifier hex&gt;</c>. Neither the
/// password nor anything that logs in can be read back from it: whoever steals it can only try
/// passwords against it, at PBKDF2's cost each.
/// </summary>
/// <remarks>
/// Older disks stored the PBKDF2 key itself (<c>pbkdf2-sha256$…$&lt;key hex&gt;</c>), which the
/// remote desktop login accepted in place of the password. <see cref="Parse"/> still reads those
/// and turns the key into its verifier (<see cref="WasUpgraded"/>), so they can be saved in the
/// new form without anyone's password.
/// </remarks>
public sealed class PasswordHash
{
    /// <summary>Size of the random salt in bytes.</summary>
    public const int SaltSize = 16;

    private const string Scheme = "srp6-pbkdf2-sha256";
    private const string KeyScheme = "pbkdf2-sha256";

    private PasswordHash(int iterations, byte[] salt, byte[] verifier, bool wasUpgraded = false)
    {
        Iterations = iterations;
        Salt = salt;
        Verifier = verifier;
        WasUpgraded = wasUpgraded;
    }

    /// <summary>Gets the number of PBKDF2 rounds.</summary>
    public int Iterations { get; }

    /// <summary>Gets the random salt, so equal passwords get different hashes.</summary>
    public byte[] Salt { get; }

    /// <summary>Gets the SRP verifier of the derived key, <see cref="Srp.NumberSize"/> bytes.</summary>
    public byte[] Verifier { get; }

    /// <summary>Gets whether this was read from the old form that stored the key itself, and should be saved again.</summary>
    public bool WasUpgraded { get; }

    /// <summary>Hashes <paramref name="password"/> with a new random salt. Deliberately slow.</summary>
    public static PasswordHash Create(string password, int iterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new PasswordHash(iterations, salt, Srp.ComputeVerifier(salt, Pkcs5S2PasswordKeyDeriver.DeriveKey(password, salt, iterations)));
    }

    /// <summary>
    /// Returns whether <paramref name="password"/> is the password this is the hash of. As slow
    /// as <see cref="Create"/>; the comparison takes the same time whatever matches.
    /// </summary>
    public bool Matches(string password)
    {
        byte[] verifier = Srp.ComputeVerifier(Salt, Pkcs5S2PasswordKeyDeriver.DeriveKey(password, Salt, Iterations));
        return Org.BouncyCastle.Utilities.Arrays.FixedTimeEquals(verifier, Verifier);
    }

    /// <summary>
    /// Reads a hash written by <see cref="ToString"/>, or one of the old form (computing its
    /// verifier); null when the text is neither.
    /// </summary>
    public static PasswordHash? Parse(string text)
    {
        string[] parts = text.Split('$');
        if (parts.Length != 4 || parts[0] is not (Scheme or KeyScheme)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) || iterations <= 0)
        {
            return null;
        }

        try
        {
            byte[] salt = Convert.FromHexString(parts[2]);
            byte[] value = Convert.FromHexString(parts[3]);
            if (salt.Length != SaltSize)
            {
                return null;
            }

            if (parts[0] == KeyScheme)
            {
                return value.Length == Pkcs5S2PasswordKeyDeriver.KeySize ? new PasswordHash(iterations, salt, Srp.ComputeVerifier(salt, value), wasUpgraded: true) : null;
            }

            return value.Length == Srp.NumberSize ? new PasswordHash(iterations, salt, value) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public override string ToString()
    {
        return $"{Scheme}${Iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToHexString(Salt)}${Convert.ToHexString(Verifier)}";
    }
}
