using RemSox.Utils;

namespace RemSox.Networking;

internal class AesPacketCrypto(byte[] key) : IPacketCrypto
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
