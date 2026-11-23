using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities;

namespace RemSox.Shared.Cryptography;

/// <summary>
/// SRP-6a (RFC 5054's 2048-bit group, SHA-256), the augmented password exchange of the remote
/// desktop login: the server keeps only a verifier, <c>g^x mod N</c>, from which nobody can log
/// in, and each login proves knowledge of <c>x</c> without sending it.
/// </summary>
/// <remarks>
/// <c>x</c> comes from the password's slow PBKDF2 key rather than the password itself (BouncyCastle's
/// <c>H(salt | H(":" | key))</c>, with an empty identity so it does not depend on the account's
/// name), so trying passwords against a stolen verifier costs PBKDF2 per guess as before.
/// </remarks>
public static class Srp
{
    /// <summary>The group: RFC 5054's 2048-bit prime and generator.</summary>
    public static readonly Srp6GroupParameters Group = Srp6StandardGroups.rfc5054_2048;

    /// <summary>Size in bytes of a number in the group (a verifier, a public value).</summary>
    public static readonly int NumberSize = (Group.N.BitLength + 7) / 8;

    private static readonly byte[] Identity = [];

    /// <summary>Returns the verifier of a password's PBKDF2 <paramref name="key"/>, <see cref="NumberSize"/> bytes.</summary>
    public static byte[] ComputeVerifier(byte[] salt, byte[] key)
    {
        Srp6VerifierGenerator generator = new();
        generator.Init(Group, new Sha256Digest());
        return ToBytes(generator.GenerateVerifier(salt, Identity, key));
    }

    /// <summary>Starts the client's side: its public value A for the password's PBKDF2 <paramref name="key"/>.</summary>
    public static (Srp6Client Client, byte[] A) StartClient(byte[] salt, byte[] key)
    {
        Srp6Client client = new();
        client.Init(Group, new Sha256Digest(), new Org.BouncyCastle.Security.SecureRandom());
        return (client, ToBytes(client.GenerateClientCredentials(salt, Identity, key)));
    }

    /// <summary>Starts the server's side for a stored <paramref name="verifier"/>: its public value B.</summary>
    public static (Srp6Server Server, byte[] B) StartServer(byte[] verifier)
    {
        Srp6Server server = new();
        server.Init(Group, new BigInteger(1, verifier), new Sha256Digest(), new Org.BouncyCastle.Security.SecureRandom());
        return (server, ToBytes(server.GenerateServerCredentials()));
    }

    /// <summary>A verifier no password has, for a name without an account: below N, random.</summary>
    public static byte[] CreateDecoyVerifier()
    {
        BigInteger v = new BigInteger(Group.N.BitLength - 1, new Org.BouncyCastle.Security.SecureRandom()).Add(BigInteger.One);
        return ToBytes(v);
    }

    /// <summary>
    /// The shared secret S, from the other side's public value. Throws
    /// <see cref="Org.BouncyCastle.Crypto.CryptoException"/> for a public value that would make it
    /// guessable (0 mod N).
    /// </summary>
    public static byte[] ClientSecret(Srp6Client client, byte[] b)
    {
        return ToBytes(client.CalculateSecret(new BigInteger(1, b)));
    }

    /// <inheritdoc cref="ClientSecret"/>
    public static byte[] ServerSecret(Srp6Server server, byte[] a)
    {
        return ToBytes(server.CalculateSecret(new BigInteger(1, a)));
    }

    /// <summary>A number of the group as <see cref="NumberSize"/> big-endian bytes, so its size never tells its value.</summary>
    public static byte[] ToBytes(BigInteger value)
    {
        return BigIntegers.AsUnsignedByteArray(NumberSize, value);
    }
}
