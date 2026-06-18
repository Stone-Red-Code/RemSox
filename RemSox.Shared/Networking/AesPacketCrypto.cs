using RemSox.Shared.Cryptography;

namespace RemSox.Shared.Networking;

public class AesPacketCrypto(byte[] key) : IPacketCrypto
{
    public byte[] Decrypt(byte[] data)
    {
        return AesGcmCrypto.Decrypt(data, key);
    }

    public byte[] Encrypt(byte[] data)
    {
        return AesGcmCrypto.Encrypt(data, key);
    }
}
