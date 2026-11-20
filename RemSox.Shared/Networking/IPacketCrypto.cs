namespace RemSox.Shared.Networking;

/// <summary>Protects the packets of one connection, in the order they are sent and received.</summary>
public interface IPacketCrypto
{
    /// <summary>Encrypts the next outgoing packet.</summary>
    byte[] Encrypt(byte[] data);

    /// <summary>Decrypts the next incoming packet.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The packet was altered, replayed, reordered or not made with this connection's key.
    /// </exception>
    byte[] Decrypt(byte[] data);
}
