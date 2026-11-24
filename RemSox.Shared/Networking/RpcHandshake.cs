using System.Buffers.Binary;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Utilities;

using RemSox.Shared.Cryptography;

namespace RemSox.Shared.Networking;

/// <summary>
/// The login that starts every RPC connection, before any message is accepted: a user name and
/// password, checked with SRP-6a (<see cref="Srp"/>). Both sides prove they know the password
/// without sending it, and derive fresh keys for the connection's <see cref="AesGcmSession"/>.
/// </summary>
/// <remarks>
/// <para>Four unencrypted packets, framed like any other (an int32 length, then the body):</para>
/// <list type="number">
/// <item>Hello, client to server: version, user name.</item>
/// <item>Challenge, server to client: version, iteration count and salt of the user's stored
/// <see cref="PasswordHash"/>, and the server's public value B. The server keeps only the
/// hash's verifier, never the password or its key. For a name that has no account it makes up
/// a salt that stays the same for that name and a random verifier
/// (<see cref="BeginServer"/>), so the answer does not tell which names exist.</item>
/// <item>Login, client to server: version, the client's public value A, client proof.</item>
/// <item>Result, server to client: 1 and the server proof; 0 for a wrong name or password; or 2
/// and a reason when the login is refused whatever the password: before it is checked, after
/// too many failed logins, or after it was right (the desktop is in use). The server closes the connection after a 0 or a 2.</item>
/// </list>
/// <para>
/// The client turns the password into the PBKDF2 key of the stored hash and that into SRP's
/// <c>x</c>; both sides then reach the same secret S only if the client's key matches the
/// server's verifier. Every other value is an HMAC-SHA256 under SHA-256(S) of a label and the
/// transcript (version, name, iterations, salt, A and B), so each connection gets keys of its
/// own and a proof recorded from one connection is useless in another. Recording a handshake
/// gives nothing to try passwords against; posing as the server, or stealing the verifier,
/// lets one try them offline at PBKDF2's cost, so a long password is still what keeps that from
/// working. Unlike the hash-proof login before it, the stored value no longer logs in.
/// </para>
/// <para>
/// The server's side (<see cref="BeginServer"/>, <see cref="FinishServer"/>) does big-number
/// arithmetic that takes noticeable time on RemSox, so its owner runs it off its loop.
/// </para>
/// </remarks>
public static class RpcHandshake
{
    /// <summary>
    /// The protocol version; a server and a viewer of different versions refuse each other. It
    /// covers everything after the login too (the render commands: 3 gave primitives a z-index;
    /// 4 is the SRP login; 5 typed primitives with clip rectangles; 6 the cursor shape; 7 the background; 8 the image primitive), so a change to either must
    /// raise it.
    /// </summary>
    public const byte Version = 8;

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
    public const int MaxPacketSize = 1024;

    /// <summary>The longest user name in bytes (UTF-8).</summary>
    public const int MaxNameSize = 64;

    /// <summary>How long either side waits for the other's next handshake packet.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const int ProofSize = 32;

    private const byte Rejected = 0;
    private const byte Accepted = 1;
    private const byte Refused = 2;

    /// <summary>[version][name length][name]</summary>
    /// <exception cref="ArgumentException">The name is empty or longer than <see cref="MaxNameSize"/> bytes.</exception>
    public static byte[] CreateHello(string user)
    {
        byte[] name = Encoding.UTF8.GetBytes(user);
        if (name.Length is 0 or > MaxNameSize)
        {
            throw new ArgumentException($"A user name is 1-{MaxNameSize} bytes.", nameof(user));
        }

        return [Version, (byte)name.Length, .. name];
    }

    /// <summary>Returns the user name the client wants to log in as.</summary>
    /// <exception cref="InvalidDataException">Not a hello of this version.</exception>
    public static string ReadHello(byte[] packet)
    {
        if (packet.Length < 2 || packet[0] != Version)
        {
            throw new InvalidDataException($"The other side speaks a different protocol version (expected {Version}).");
        }

        if (packet[1] is 0 or > MaxNameSize || packet.Length != 2 + packet[1])
        {
            throw new InvalidDataException("Invalid hello packet.");
        }

        return Encoding.UTF8.GetString(packet, 2, packet[1]);
    }

    /// <summary>
    /// The server's side of one login, from the hello to the result: the stored hash's iterations
    /// and salt, and SRP's state with its public value B.
    /// </summary>
    public sealed class ServerLogin
    {
        internal ServerLogin(int iterations, byte[] salt, Srp6Server srp, byte[] b, bool isKnownUser)
        {
            Iterations = iterations;
            Salt = salt;
            Srp = srp;
            B = b;
            IsKnownUser = isKnownUser;
        }

        public int Iterations { get; }

        public byte[] Salt { get; }

        internal Srp6Server Srp { get; }

        public byte[] B { get; }

        /// <summary>Gets whether the name has an account; a decoy never logs in, whatever it is sent.</summary>
        public bool IsKnownUser { get; }
    }

    /// <summary>
    /// Starts the server's side for <paramref name="user"/>, whose stored hash is
    /// <paramref name="stored"/>; null for a name without an account (or a password), which gets a
    /// decoy: a salt that is an HMAC of the name under the server's <paramref name="decoySecret"/>,
    /// so asking twice gives the same one, as for a real account, and a random verifier. Slow.
    /// </summary>
    public static ServerLogin BeginServer(string user, PasswordHash? stored, byte[] decoySecret)
    {
        if (stored is null)
        {
            byte[] salt = Mac(decoySecret, "decoy salt", Encoding.UTF8.GetBytes(user.ToLowerInvariant()))[..PasswordHash.SaltSize];
            (Srp6Server decoy, byte[] decoyB) = Srp.StartServer(Srp.CreateDecoyVerifier());
            return new ServerLogin(DefaultIterations, salt, decoy, decoyB, isKnownUser: false);
        }

        (Srp6Server server, byte[] b) = Srp.StartServer(stored.Verifier);
        return new ServerLogin(stored.Iterations, stored.Salt, server, b, isKnownUser: true);
    }

    /// <summary>[version][iterations int32][salt][B]</summary>
    public static byte[] CreateChallenge(ServerLogin login)
    {
        return CreateChallenge(login.Iterations, login.Salt, login.B);
    }

    /// <inheritdoc cref="CreateChallenge(ServerLogin)"/>
    public static byte[] CreateChallenge(int iterations, byte[] salt, byte[] b)
    {
        byte[] count = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, iterations);
        return [Version, .. count, .. salt, .. b];
    }

    /// <summary>
    /// The client's answer to the server's challenge: derives the password's key (the slow part
    /// of the client's login), SRP's A and the shared secret, and from them the connection's keys.
    /// </summary>
    /// <returns>The login packet to send, and the keys to check the result with and encrypt under.</returns>
    /// <exception cref="InvalidDataException">
    /// Not a challenge of this version, one asking for an unreasonable iteration count, or one
    /// whose B is not valid.
    /// </exception>
    public static (byte[] Login, SessionKeys Keys) AnswerChallenge(string user, string password, byte[] challenge)
    {
        CheckPacket(challenge, 1 + 4 + PasswordHash.SaltSize + Srp.NumberSize, "challenge");

        int iterations = BinaryPrimitives.ReadInt32LittleEndian(challenge.AsSpan(1));
        if (iterations is < MinIterations or > MaxIterations)
        {
            throw new InvalidDataException($"The server asks for {iterations} password rounds; refusing anything outside {MinIterations}-{MaxIterations}.");
        }

        byte[] salt = challenge[5..(5 + PasswordHash.SaltSize)];
        byte[] b = challenge[(5 + PasswordHash.SaltSize)..];

        (Srp6Client client, byte[] a) = Srp.StartClient(salt, Pkcs5S2PasswordKeyDeriver.DeriveKey(password, salt, iterations));
        byte[] secret;
        try
        {
            secret = Srp.ClientSecret(client, b);
        }
        catch (CryptoException ex)
        {
            throw new InvalidDataException("The server sent an invalid challenge: " + ex.Message, ex);
        }

        SessionKeys keys = DeriveSessionKeys(user, iterations, salt, a, b, secret);
        return ([Version, .. a, .. keys.ClientProof], keys);
    }

    /// <summary>Reads the client's login packet: its A and its proof.</summary>
    /// <exception cref="InvalidDataException">Not a login of this version.</exception>
    public static (byte[] A, byte[] ClientProof) ReadLogin(byte[] packet)
    {
        CheckPacket(packet, 1 + Srp.NumberSize + ProofSize, "login");
        return (packet[1..(1 + Srp.NumberSize)], packet[(1 + Srp.NumberSize)..]);
    }

    /// <summary>
    /// Finishes the server's side with the client's <paramref name="a"/>: the keys whose client
    /// proof the client must have sent, or null for an A that is not valid (the login fails). Slow.
    /// </summary>
    public static SessionKeys? FinishServer(ServerLogin login, string user, byte[] a)
    {
        byte[] secret;
        try
        {
            secret = Srp.ServerSecret(login.Srp, a);
        }
        catch (CryptoException)
        {
            return null;
        }

        return DeriveSessionKeys(user, login.Iterations, login.Salt, a, login.B, secret);
    }

    /// <summary>The server's answer: accepted with its proof, or rejected (wrong name or password) without one.</summary>
    public static byte[] CreateResult(SessionKeys? keys)
    {
        return keys is null ? [Rejected] : [Accepted, .. keys.ServerProof];
    }

    /// <summary>The server's answer when the login is refused whatever the password was (too many failed logins).</summary>
    public static byte[] CreateRefusal(string reason)
    {
        byte[] text = Encoding.UTF8.GetBytes(reason);
        return [Refused, .. text.Length > MaxPacketSize - 1 ? text[..(MaxPacketSize - 1)] : text];
    }

    /// <summary>Checks that the server accepted the login and proved it knows the password too.</summary>
    /// <exception cref="AuthenticationException">
    /// The name or password was wrong, the login was refused (the message says why), or the
    /// server's proof is wrong.
    /// </exception>
    /// <exception cref="InvalidDataException">Not a result packet.</exception>
    public static void CheckResult(byte[] packet, SessionKeys keys)
    {
        if (packet is [Rejected])
        {
            throw new AuthenticationException("Wrong name or password.");
        }

        if (packet.Length > 0 && packet[0] == Refused)
        {
            throw new AuthenticationException(Encoding.UTF8.GetString(packet, 1, packet.Length - 1));
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

    /// <summary>Derives the proofs and the connection's keys from SRP's shared secret and the transcript.</summary>
    private static SessionKeys DeriveSessionKeys(string user, int iterations, byte[] salt, byte[] a, byte[] b, byte[] secret)
    {
        byte[] name = Encoding.UTF8.GetBytes(user.ToLowerInvariant());
        byte[] count = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, iterations);
        byte[] transcript = [Version, (byte)name.Length, .. name, .. count, .. salt, .. a, .. b];
        // BouncyCastle's SHA-256, not the BCL's: that one needs native crypto Cosmos does not have.
        Sha256Digest digest = new();
        digest.BlockUpdate(secret, 0, secret.Length);
        byte[] key = new byte[digest.GetDigestSize()];
        _ = digest.DoFinal(key, 0);

        return new SessionKeys(
            Mac(key, "client proof", transcript),
            Mac(key, "server proof", transcript),
            Mac(key, "client to server", transcript),
            Mac(key, "server to client", transcript));
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

    /// <summary>What one handshake derives.</summary>
    public sealed record SessionKeys(byte[] ClientProof, byte[] ServerProof, byte[] ClientToServer, byte[] ServerToClient);
}
