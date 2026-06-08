using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

using System.Security.Cryptography;
using System.Text;

public static class BcAesCrypto
{
    private const int KeySize = 32;        // 256-bit AES
    private const int SaltSize = 16;       // 128-bit salt
    private const int NonceSize = 12;      // GCM standard
    private const int TagSize = 128;       // authentication tag (bits)
    private const int Iterations = 150_000; // PBKDF2 cost factor

    // -----------------------------
    // PBKDF2 Key Derivation
    // -----------------------------
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        Pkcs5S2ParametersGenerator generator = new(new Sha256Digest());

        generator.Init(
            PbeParametersGenerator.Pkcs5PasswordToBytes(password.ToCharArray()),
            salt,
            Iterations
        );

        KeyParameter keyParam = (KeyParameter)generator.GenerateDerivedMacParameters(KeySize * 8);
        return keyParam.GetKey();
    }

    // -----------------------------
    // Encrypt
    // -----------------------------
    public static string Encrypt(string plainText, string password)
    {
        byte[] salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        byte[] key = DeriveKey(password, salt);

        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        GcmBlockCipher cipher = new(new AesEngine());

        cipher.Init(true, new AeadParameters(
            new KeyParameter(key),
            TagSize,
            nonce
        ));

        byte[] input = Encoding.UTF8.GetBytes(plainText);
        byte[] output = new byte[cipher.GetOutputSize(input.Length)];

        int len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        _ = cipher.DoFinal(output, len);

        // Combine: salt + nonce + ciphertext
        byte[] result = new byte[salt.Length + nonce.Length + output.Length];

        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(nonce, 0, result, salt.Length, nonce.Length);
        Buffer.BlockCopy(output, 0, result, salt.Length + nonce.Length, output.Length);

        return Convert.ToBase64String(result);
    }

    // -----------------------------
    // Decrypt
    // -----------------------------
    public static string Decrypt(string cipherText, string password)
    {
        byte[] data = Convert.FromBase64String(cipherText);

        byte[] salt = new byte[SaltSize];
        byte[] nonce = new byte[NonceSize];

        Buffer.BlockCopy(data, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(data, SaltSize, nonce, 0, NonceSize);

        int cipherLength = data.Length - SaltSize - NonceSize;
        byte[] cipherBytes = new byte[cipherLength];

        Buffer.BlockCopy(data, SaltSize + NonceSize, cipherBytes, 0, cipherLength);

        byte[] key = DeriveKey(password, salt);

        GcmBlockCipher cipher = new(new AesEngine());

        cipher.Init(false, new AeadParameters(
            new KeyParameter(key),
            TagSize,
            nonce
        ));

        byte[] plain = new byte[cipher.GetOutputSize(cipherBytes.Length)];

        int len = cipher.ProcessBytes(cipherBytes, 0, cipherBytes.Length, plain, 0);
        _ = cipher.DoFinal(plain, len);

        return Encoding.UTF8.GetString(plain);
    }
}
