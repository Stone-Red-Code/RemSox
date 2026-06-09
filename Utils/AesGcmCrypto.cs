using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

using System.Security.Cryptography;
using System.Text;

namespace RemSox.Utils;

public static class AesGcmCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 128;

    public static byte[] Encrypt(byte[] plainData, byte[] key)
    {
        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        GcmBlockCipher cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), TagSize, nonce));

        byte[] output = new byte[cipher.GetOutputSize(plainData.Length)];

        int len = cipher.ProcessBytes(plainData, 0, plainData.Length, output, 0);
        _ = cipher.DoFinal(output, len);

        byte[] result = new byte[nonce.Length + output.Length];

        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(output, 0, result, nonce.Length, output.Length);

        return result;
    }

    public static string Encrypt(string text, byte[] key)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(Encrypt(data, key));
    }

    public static byte[] Decrypt(byte[] encryptedData, byte[] key)
    {
        byte[] nonce = new byte[NonceSize];
        Buffer.BlockCopy(encryptedData, 0, nonce, 0, NonceSize);

        int cipherLength = encryptedData.Length - NonceSize;
        byte[] cipherBytes = new byte[cipherLength];

        Buffer.BlockCopy(encryptedData, NonceSize, cipherBytes, 0, cipherLength);

        GcmBlockCipher cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(new KeyParameter(key), TagSize, nonce));

        byte[] plain = new byte[cipher.GetOutputSize(cipherBytes.Length)];

        int len = cipher.ProcessBytes(cipherBytes, 0, cipherBytes.Length, plain, 0);
        _ = cipher.DoFinal(plain, len);

        return plain;
    }

    public static string Decrypt(string cipherText, byte[] key)
    {
        byte[] data = Convert.FromBase64String(cipherText);
        byte[] plain = Decrypt(data, key);
        return Encoding.UTF8.GetString(plain);
    }
}