using System.Security.Cryptography;

using RemSox.Shared.Networking;

namespace RemSox.Tests.Shared;

/// <summary>One connection's encryption: each side's send key is the other's receive key.</summary>
public sealed class AesGcmSessionTests
{
    private static readonly byte[] ClientToServer = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] ServerToClient = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void PacketsArriveAsSentInBothDirections()
    {
        (AesGcmSession client, AesGcmSession server) = CreatePair();

        for (int i = 0; i < 3; i++)
        {
            byte[] request = [1, 2, 3, (byte)i];
            byte[] reply = [9, 8, (byte)i];

            Assert.Equal(request, server.Decrypt(client.Encrypt(request)));
            Assert.Equal(reply, client.Decrypt(server.Encrypt(reply)));
        }
    }

    [Fact]
    public void EncryptedPacketGrowsByTheTagAndHidesTheData()
    {
        (AesGcmSession client, _) = CreatePair();
        byte[] data = new byte[64];

        byte[] sealedPacket = client.Encrypt(data);

        Assert.Equal(data.Length + AesGcmSession.Overhead, sealedPacket.Length);
        Assert.NotEqual(data, sealedPacket[..data.Length]);
    }

    [Fact]
    public void ChangedPacketFails()
    {
        (AesGcmSession client, AesGcmSession server) = CreatePair();
        byte[] sealedPacket = client.Encrypt([1, 2, 3]);
        sealedPacket[0] ^= 1;

        _ = Assert.Throws<CryptographicException>(() => server.Decrypt(sealedPacket));
    }

    [Fact]
    public void ReplayedPacketFails()
    {
        (AesGcmSession client, AesGcmSession server) = CreatePair();
        byte[] sealedPacket = client.Encrypt([1, 2, 3]);

        _ = server.Decrypt(sealedPacket);

        _ = Assert.Throws<CryptographicException>(() => server.Decrypt(sealedPacket));
    }

    [Fact]
    public void SkippedPacketMakesTheNextOneFail()
    {
        (AesGcmSession client, AesGcmSession server) = CreatePair();
        _ = client.Encrypt([1]);
        byte[] second = client.Encrypt([2]);

        _ = Assert.Throws<CryptographicException>(() => server.Decrypt(second));
    }

    [Fact]
    public void PacketCannotBeReflectedBackToItsSender()
    {
        (AesGcmSession client, _) = CreatePair();
        byte[] sealedPacket = client.Encrypt([1, 2, 3]);

        _ = Assert.Throws<CryptographicException>(() => client.Decrypt(sealedPacket));
    }

    private static (AesGcmSession Client, AesGcmSession Server) CreatePair()
    {
        return (new AesGcmSession(ClientToServer, ServerToClient), new AesGcmSession(ServerToClient, ClientToServer));
    }
}
