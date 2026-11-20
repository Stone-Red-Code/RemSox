using System.Buffers.Binary;
using System.Security.Cryptography;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace RemSox.Shared.Networking;

/// <summary>
/// AES-256-GCM for one authenticated connection, with a key per direction (from
/// <see cref="RpcHandshake"/>). The nonce is the packet's number in its direction, so it is never
/// sent and never repeats under a key; a packet that is dropped, replayed or reordered then fails
/// to decrypt, like a forged one.
/// </summary>
/// <remarks>
/// BouncyCastle's managed AES, because Cosmos has no native crypto for the BCL's AesGcm. Both
/// counters assume one caller at a time per direction, which the RPC classes guarantee.
/// </remarks>
public sealed class AesGcmSession(byte[] sendKey, byte[] receiveKey) : IPacketCrypto
{
    /// <summary>Bytes each packet grows by: the authentication tag.</summary>
    public const int Overhead = TagSize / 8;

    private const int TagSize = 128;
    private const int NonceSize = 12;

    private ulong sent;
    private ulong received;

    public byte[] Encrypt(byte[] data)
    {
        GcmBlockCipher cipher = CreateCipher(forEncryption: true, sendKey, sent++);
        byte[] output = new byte[cipher.GetOutputSize(data.Length)];
        int length = cipher.ProcessBytes(data, 0, data.Length, output, 0);
        _ = cipher.DoFinal(output, length);
        return output;
    }

    public byte[] Decrypt(byte[] data)
    {
        GcmBlockCipher cipher = CreateCipher(forEncryption: false, receiveKey, received++);
        byte[] output = new byte[cipher.GetOutputSize(data.Length)];

        try
        {
            int length = cipher.ProcessBytes(data, 0, data.Length, output, 0);
            _ = cipher.DoFinal(output, length);
        }
        catch (InvalidCipherTextException ex)
        {
            throw new CryptographicException("Packet failed authentication.", ex);
        }

        return output;
    }

    private static GcmBlockCipher CreateCipher(bool forEncryption, byte[] key, ulong counter)
    {
        byte[] nonce = new byte[NonceSize];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce, counter);

        GcmBlockCipher cipher = new(new AesEngine());
        cipher.Init(forEncryption, new AeadParameters(new KeyParameter(key), TagSize, nonce));
        return cipher;
    }
}
