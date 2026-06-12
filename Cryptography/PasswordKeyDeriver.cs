using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace RemSox.Cryptography;

public static class Pkcs5S2PasswordKeyDeriver
{
    private const int KeySize = 32; // 256-bit key
    private const int Iterations = 150_000; // PBKDF2 cost factor

    public static byte[] DeriveKey(string password, byte[] salt)
    {
        Pkcs5S2ParametersGenerator generator = new Pkcs5S2ParametersGenerator(new Sha256Digest());

        generator.Init(
            PbeParametersGenerator.Pkcs5PasswordToBytes(password.ToCharArray()),
            salt,
            Iterations
        );

        return ((KeyParameter)generator.GenerateDerivedMacParameters(KeySize * 8))
            .GetKey();
    }
}