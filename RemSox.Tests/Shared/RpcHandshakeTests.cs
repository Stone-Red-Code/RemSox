using System.Security.Authentication;

using RemSox.Shared.Cryptography;
using RemSox.Shared.Networking;

namespace RemSox.Tests.Shared;

/// <summary>
/// The RPC login, run as client and server would without a socket: both sides must derive the
/// same keys from the right password and nothing usable from anything else.
/// </summary>
public sealed class RpcHandshakeTests
{
    private const string User = "bob";
    private const string Password = "correct horse";

    // One stored hash for the whole class: PBKDF2 at the real iteration count is slow on purpose.
    private static readonly PasswordHash Stored = PasswordHash.Create(Password, RpcHandshake.DefaultIterations);

    [Fact]
    public void RightPasswordGivesBothSidesTheSameKeys()
    {
        (RpcHandshake.SessionKeys client, RpcHandshake.SessionKeys server, byte[] clientProof) = RunHandshake(User, Password);

        Assert.True(RpcHandshake.IsValidClientProof(clientProof, server));
        Assert.Equal(server.ClientToServer, client.ClientToServer);
        Assert.Equal(server.ServerToClient, client.ServerToClient);
        Assert.NotEqual(server.ClientToServer, server.ServerToClient);

        // The client accepts the server's result, which proves the server knows the key too.
        RpcHandshake.CheckResult(RpcHandshake.CreateResult(server), client);
    }

    [Fact]
    public void WrongPasswordIsRejected()
    {
        (_, RpcHandshake.SessionKeys server, byte[] clientProof) = RunHandshake(User, "wrong");

        Assert.False(RpcHandshake.IsValidClientProof(clientProof, server));
    }

    [Fact]
    public void NameIsComparedWithoutCase()
    {
        (_, RpcHandshake.SessionKeys server, byte[] clientProof) = RunHandshake("BOB", Password, serverUser: User);

        Assert.True(RpcHandshake.IsValidClientProof(clientProof, server));
    }

    [Fact]
    public void ProofFromOneConnectionIsUselessInAnother()
    {
        (_, _, byte[] recordedProof) = RunHandshake(User, Password);
        (_, RpcHandshake.SessionKeys otherServer, _) = RunHandshake(User, Password);

        Assert.False(RpcHandshake.IsValidClientProof(recordedProof, otherServer));
    }

    [Fact]
    public void ClientRefusesAServerThatCannotProveItKnowsThePassword()
    {
        (RpcHandshake.SessionKeys client, _, _) = RunHandshake(User, Password);
        RpcHandshake.SessionKeys impostor = new(new byte[32], new byte[32], new byte[32], new byte[32]);

        _ = Assert.Throws<AuthenticationException>(() => RpcHandshake.CheckResult(RpcHandshake.CreateResult(impostor), client));
    }

    [Fact]
    public void RejectionAndRefusalReachTheClient()
    {
        (RpcHandshake.SessionKeys client, _, _) = RunHandshake(User, Password);

        AuthenticationException rejected = Assert.Throws<AuthenticationException>(() => RpcHandshake.CheckResult(RpcHandshake.CreateResult(null), client));
        Assert.Contains("Wrong name or password", rejected.Message);

        AuthenticationException refused = Assert.Throws<AuthenticationException>(() => RpcHandshake.CheckResult(RpcHandshake.CreateRefusal("Too many failed logins; try again later."), client));
        Assert.Equal("Too many failed logins; try again later.", refused.Message);
    }

    [Fact]
    public void DecoyChallengeLooksTheSameForTheSameNameEveryTime()
    {
        byte[] secret = new byte[32];

        RpcHandshake.PasswordKey first = RpcHandshake.CreateDecoyKey(secret, "nobody");
        RpcHandshake.PasswordKey again = RpcHandshake.CreateDecoyKey(secret, "NOBODY");
        RpcHandshake.PasswordKey other = RpcHandshake.CreateDecoyKey(secret, "someone");

        Assert.Equal(first.Salt, again.Salt);
        Assert.NotEqual(first.Salt, other.Salt);
        Assert.Equal(RpcHandshake.DefaultIterations, first.Iterations);

        // Random each time, so no proof can match it.
        Assert.NotEqual(first.Key, again.Key);
    }

    [Theory]
    [InlineData(RpcHandshake.MinIterations - 1)]
    [InlineData(RpcHandshake.MaxIterations + 1)]
    public void ClientRefusesUnreasonableIterationCounts(int iterations)
    {
        RpcHandshake.PasswordKey key = new(iterations, new byte[PasswordHash.SaltSize], new byte[32]);
        byte[] challenge = RpcHandshake.CreateChallenge(key, RpcHandshake.CreateNonce());

        _ = Assert.Throws<InvalidDataException>(() => RpcHandshake.ReadChallenge(challenge, Password));
    }

    [Fact]
    public void HelloCarriesTheName()
    {
        Assert.Equal("bob", RpcHandshake.ReadHello(RpcHandshake.CreateHello("bob")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a-name-that-is-far-too-long-for-the-hello-packet-of-the-rpc-login!")]
    public void HelloRefusesEmptyAndLongNames(string name)
    {
        _ = Assert.Throws<ArgumentException>(() => RpcHandshake.CreateHello(name));
    }

    [Fact]
    public void HelloOfAnotherVersionIsRefused()
    {
        byte[] hello = RpcHandshake.CreateHello("bob");
        hello[0] = RpcHandshake.Version + 1;

        _ = Assert.Throws<InvalidDataException>(() => RpcHandshake.ReadHello(hello));
    }

    [Fact]
    public void HelloWithAWrongLengthIsRefused()
    {
        byte[] hello = [.. RpcHandshake.CreateHello("bob"), (byte)'x'];

        _ = Assert.Throws<InvalidDataException>(() => RpcHandshake.ReadHello(hello));
    }

    /// <summary>Runs the four packets between a client using <paramref name="password"/> and a server holding <see cref="Stored"/>.</summary>
    private static (RpcHandshake.SessionKeys Client, RpcHandshake.SessionKeys Server, byte[] ClientProof) RunHandshake(string user, string password, string? serverUser = null)
    {
        // Server: reads the hello, answers with the stored hash's salt and a nonce.
        string named = RpcHandshake.ReadHello(RpcHandshake.CreateHello(user));
        RpcHandshake.PasswordKey serverKey = RpcHandshake.GetPasswordKey(Stored);
        byte[] serverNonce = RpcHandshake.CreateNonce();
        byte[] challenge = RpcHandshake.CreateChallenge(serverKey, serverNonce);

        // Client: derives the key from its password and the challenge, sends its proof.
        (RpcHandshake.PasswordKey clientKey, byte[] receivedNonce) = RpcHandshake.ReadChallenge(challenge, password);
        byte[] clientNonce = RpcHandshake.CreateNonce();
        RpcHandshake.SessionKeys client = RpcHandshake.DeriveSessionKeys(user, clientKey, receivedNonce, clientNonce);
        (byte[] sentNonce, byte[] proof) = RpcHandshake.ReadLogin(RpcHandshake.CreateLogin(clientNonce, client));

        // Server: derives the same from the stored key.
        RpcHandshake.SessionKeys server = RpcHandshake.DeriveSessionKeys(serverUser ?? named, serverKey, serverNonce, sentNonce);
        return (client, server, proof);
    }
}
