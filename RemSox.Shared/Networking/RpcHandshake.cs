using System.Buffers.Binary;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Utilities;

using RemSox.Shared.Cryptography;

namespace RemSox.Shared.Networking;

/// <summary>
/// The password login that starts every RPC connection, before any message is accepted. Both
/// sides prove they know the password without sending it, and derive fresh keys for the
/// connection's <see cref="AesGcmSession"/>.
/// </summary>
/// <remarks>
/// <para>Three unencrypted packets, framed like any other (an int32 length, then the body):</para>
/// <list type="number">
/// <item>Hello, server to client: version, iteration count and salt of the stored
/// <see cref="PasswordHash"/>, server nonce. The server keeps only that hash, never the
/// password.</item>
/// <item>Login, client to server: version, client nonce, client proof.</item>
/// <item>Result, server to client: 1 and the server proof, or 0 for a wrong password (the server
/// then closes the connection).</item>
/// </list>
/// <para>
/// The password key is PBKDF2(password, salt, iterations), the key of the stored hash. Every
/// other value is an HMAC-SHA256 under it of a label and the transcript (version, iterations,
/// salt and both nonces), so each connection gets keys of its own and a proof recorded from one
/// connection is useless in another. Whoever records a handshake, or poses as the server, can
/// still try passwords offline at PBKDF2's cost: a long password is what keeps that from
/// working. And since the hash's key is what logs in, a stolen hash logs in as well as the
/// password does; it only keeps the password itself (reused elsewhere) secret.
/// </para>
/// </remarks>
public static class RpcHandshake
{
    /// <summary>The protocol version; a server and a viewer of different versions refuse each other.</summary>
    public const byte Version = 1;

    /// <summary>PBKDF2 rounds for a new password: about a tenth of a second on a PC, one or two seconds on RemSox in QEMU.</summary>
    public const int DefaultIterations = 100_000;

    /// <summary>
    /// The fewest rounds a client accepts, so a fake server cannot make its proof cheap to
    /// guess passwords against; and the most, so it cannot make the client spin for minutes.
    /// </summary>
    public const int MinIterations = 50_000;

    /// <inheritdoc cref="MinIterations"/>
    public const int MaxIterations = 10_000_000;

    /// <summary>
    /// The largest packet accepted before the login has succeeded, so a client that has not
    /// logged in cannot make the server allocate much.
    /// </summary>
    public const int MaxPacketSize = 256;

    /// <summary>How long either side waits for the other's next handshake packet.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const int NonceSize = 32;
    private const int ProofSize = 32;

    private const byte Accepted = 1;
    private const byte Rejected = 0;

    /// <summary>A random nonce for one side of one handshake.</summary>
    public static byte[] CreateNonce()
    {
        return RandomNumberGenerator.GetBytes(NonceSize);
    }

    /// <summary>[version][iterations int32][salt][server nonce]</summary>
    public static byte[] CreateHello(PasswordHash password, byte[] serverNonce)
    {
        byte[] iterations = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(iterations, password.Iterations);
        return [Version, .. iterations, .. password.Salt, .. serverNonce];
    }

    /// <summary>
    /// Reads the server's hello and derives the password key from it: the slow part of the
    /// client's login.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a hello of this version, or one asking for an unreasonable iteration count.</exception>
    public static (PasswordKey Key, byte[] ServerNonce) ReadHello(byte[] packet, string password)
    {
        CheckPacket(packet, 1 + 4 + PasswordHash.SaltSize + NonceSize, "hello");

        int iterations = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(1));
        if (iterations is < MinIterations or > MaxIterations)
        {
            throw new InvalidDataException($"The server asks for {iterations} password rounds; refusing anything outside {MinIterations}-{MaxIterations}.");
        }

        byte[] salt = packet[5..(5 + PasswordHash.SaltSize)];
        PasswordKey key = new(iterations, salt, Pkcs5S2PasswordKeyDeriver.DeriveKey(password, salt, iterations));
        return (key, packet[(5 + PasswordHash.SaltSize)..]);
    }

    /// <summary>The server's side of the password key: the one in the stored hash.</summary>
    public static PasswordKey GetPasswordKey(PasswordHash password)
    {
        return new PasswordKey(password.Iterations, password.Salt, password.Key);
    }

    /// <summary>[version][client nonce][client proof]</summary>
    public static byte[] CreateLogin(byte[] clientNonce, SessionKeys keys)
    {
        return [Version, .. clientNonce, .. keys.ClientProof];
    }

    /// <exception cref="InvalidDataException">Not a login of this version.</exception>
    public static (byte[] ClientNonce, byte[] ClientProof) ReadLogin(byte[] packet)
    {
        CheckPacket(packet, 1 + NonceSize + ProofSize, "login");
        return (packet[1..(1 + NonceSize)], packet[(1 + NonceSize)..]);
    }

    /// <summary>The server's answer: accepted with its proof, or rejected without one.</summary>
    public static byte[] CreateResult(SessionKeys? keys)
    {
        return keys is null ? [Rejected] : [Accepted, .. keys.ServerProof];
    }

    /// <summary>Checks that the server accepted the login and proved it knows the password too.</summary>
    /// <exception cref="AuthenticationException">The password was wrong, or the server's proof is.</exception>
    /// <exception cref="InvalidDataException">Not a result packet.</exception>
    public static void CheckResult(byte[] packet, SessionKeys keys)
    {
        if (packet is [Rejected])
        {
            throw new AuthenticationException("Wrong password.");
        }

        if (packet.Length != 1 + ProofSize || packet[0] != Accepted)
        {
            throw new InvalidDataException("The server sent an invalid login result.");
        }

        if (!Arrays.FixedTimeEquals(packet[1..], keys.ServerProof))
        {
            throw new AuthenticationException("The server could not prove it knows the password.");
        }
    }

    /// <summary>Compares a client's proof without leaking how much of it matched.</summary>
    public static bool IsValidClientProof(byte[] clientProof, SessionKeys keys)
    {
        return Arrays.FixedTimeEquals(clientProof, keys.ClientProof);
    }

    /// <summary>Derives the proofs and the connection's keys from the password key and the transcript.</summary>
    public static SessionKeys DeriveSessionKeys(PasswordKey passwordKey, byte[] serverNonce, byte[] clientNonce)
    {
        byte[] iterations = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(iterations, passwordKey.Iterations);
        byte[] transcript = [Version, .. iterations, .. passwordKey.Salt, .. serverNonce, .. clientNonce];

        return new SessionKeys(
            Mac(passwordKey.Key, "client proof", transcript),
            Mac(passwordKey.Key, "server proof", transcript),
            Mac(passwordKey.Key, "client to server", transcript),
            Mac(passwordKey.Key, "server to client", transcript));
    }

    private static void CheckPacket(byte[] packet, int length, string name)
    {
        if (packet.Length == 0 || packet[0] != Version)
        {
            throw new InvalidDataException($"The other side speaks a different protocol version (expected {Version}).");
        }

        if (packet.Length != length)
        {
            throw new InvalidDataException($"Invalid {name} packet.");
        }
    }

    private static byte[] Mac(byte[] key, string label, byte[] transcript)
    {
        HMac hmac = new(new Sha256Digest());
        hmac.Init(new KeyParameter(key));

        byte[] labelBytes = Encoding.ASCII.GetBytes("RemSox RPC " + label);
        hmac.BlockUpdate(labelBytes, 0, labelBytes.Length);
        hmac.BlockUpdate(transcript, 0, transcript.Length);

        byte[] result = new byte[hmac.GetMacSize()];
        _ = hmac.DoFinal(result, 0);
        return result;
    }

    /// <summary>The PBKDF2 key of the password with what it was derived with, which both sides put in the transcript.</summary>
    public sealed record PasswordKey(int Iterations, byte[] Salt, byte[] Key);

    /// <summary>What one handshake derives.</summary>
    public sealed record SessionKeys(byte[] ClientProof, byte[] ServerProof, byte[] ClientToServer, byte[] ServerToClient);
}
