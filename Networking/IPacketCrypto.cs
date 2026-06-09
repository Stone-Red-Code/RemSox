namespace RemSox.Networking;

public interface IPacketCrypto
{
    byte[] Encrypt(byte[] data);

    byte[] Decrypt(byte[] data);
}