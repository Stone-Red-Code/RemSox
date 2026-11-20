using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace RemSox.Shared.Cryptography;

/// <summary>
/// Turns a password into a 256-bit key with PBKDF2-HMAC-SHA256. BouncyCastle's managed
/// implementation is used rather than the BCL's, which needs native crypto Cosmos does not have.
/// </summary>
public static class Pkcs5S2PasswordKeyDeriver
{
    /// <summary>Size of the derived key in bytes.</summary>
    public const int KeySize = 32;

    /// <summary>
    /// Derives the key. Deliberately slow (<paramref name="iterations"/> HMAC rounds) so that
    /// trying passwords against a recorded handshake is slow too.
    /// </summary>
    public static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        Pkcs5S2ParametersGenerator generator = new(new Sha256Digest());
        generator.Init(PbeParametersGenerator.Pkcs5PasswordToUtf8Bytes(password.ToCharArray()), salt, iterations);

        return ((KeyParameter)generator.GenerateDerivedMacParameters(KeySize * 8)).GetKey();
    }
}
