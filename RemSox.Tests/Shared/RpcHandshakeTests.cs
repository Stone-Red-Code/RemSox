using System.Security.Authentication;

using RemSox.Shared.Cryptography;
using RemSox.Shared.Networking;

namespace RemSox.Tests.Shared;

/// <summary>
/// The RPC login, run as client and server would without a socket: both sides must derive the
/// same keys from the right password and nothing usable from anything else, and what the server
/// stores must not be enough to log in.
/// </summary>
public sealed class RpcHandshakeTests
{
    private const string User = "bob";
    private const string Password = "correct horse";

    // One stored hash for the whole class: PBKDF2 at the real iteration count is slow on purpose.
    private static readonly PasswordHash Stored = PasswordHash.Create(Password, RpcHandshake.DefaultIterations);

    private static readonly byte[] DecoySecret = new byte[32];

    [Fact]
    public void RightPasswordGivesBothSidesTheSameKeys()
    {
        (RpcHandshake.SessionKeys client, RpcHandshake.SessionKeys? server, byte[] clientProof) = RunHandshake(User, Password);

        Assert.NotNull(server);
        Assert.True(RpcHandshake.IsValidClientProof(clientProof, server));
        Assert.Equal(server.ClientToServer, client.ClientToServer);
        Assert.Equal(server.ServerToClient, client.ServerToClient);
        Assert.NotEqual(server.ClientToServer, server.ServerToClient);

        // The client accepts the server's result, which proves the server knows the verifier too.
        RpcHandshake.CheckResult(RpcHandshake.CreateResult(server), client);
    }

    [Fact]
    public void WrongPasswordIsRejected()
    {
        (_, RpcHandshake.SessionKeys? server, byte[] clientProof) = RunHandshake(User, "wrong");

        Assert.False(RpcHandshake.IsValidClientProof(clientProof, server!));
    }

    [Fact]
    public void NameIsComparedWithoutCase()
    {
        (_, RpcHandshake.SessionKeys? server, byte[] clientProof) = RunHandshake("BOB", Password, serverUser: User);

        Assert.True(RpcHandshake.IsValidClientProof(clientProof, server!));
    }

    [Fact]
    public void ProofFromOneConnectionIsUselessInAnother()
    {
        (_, _, byte[] recordedProof) = RunHandshake(User, Password);
        (_, RpcHandshake.SessionKeys? otherServer, _) = RunHandshake(User, Password);

        Assert.False(RpcHandshake.IsValidClientProof(recordedProof, otherServer!));
    }

    [Fact]
    public void StoredVerifierDoesNotLogIn()
    {
        // Whoever stole the hash has the salt, iterations and verifier; the best they can offer
        // in place of the password is one of those, which is not the password.
        (_, RpcHandshake.SessionKeys? server, byte[] clientProof) = RunHandshake(User, Convert.ToHexString(Stored.Verifier));

        Assert.False(RpcHandshake.IsValidClientProof(clientProof, server!));
    }

    [Fact]
    public void UnknownNameNeverLogsIn()
    {
        RpcHandshake.ServerLogin login = RpcHandshake.BeginServer("nobody", null, DecoySecret);
        (byte[] packet, _) = RpcHandshake.AnswerChallenge("nobody", Password, RpcHandshake.CreateChallenge(login));
        (byte[] a, byte[] proof) = RpcHandshake.ReadLogin(packet);

        RpcHandshake.SessionKeys? server = RpcHandshake.FinishServer(login, "nobody", a);

        Assert.False(login.IsKnownUser);
        Assert.False(server is not null && RpcHandshake.IsValidClientProof(proof, server));
    }

    [Fact]
    public void InvalidPublicValueFailsTheLogin()
    {
        RpcHandshake.ServerLogin login = RpcHandshake.BeginServer(User, Stored, DecoySecret);

        // A = 0 (or N) would make the secret 0 whatever the password.
        Assert.Null(RpcHandshake.FinishServer(login, User, new byte[Srp.NumberSize]));
        Assert.Null(RpcHandshake.FinishServer(login, User, Srp.ToBytes(Srp.Group.N)));
    }

    [Fact]
    public void ClientRefusesAnInvalidChallenge()
    {
        byte[] challenge = RpcHandshake.CreateChallenge(RpcHandshake.DefaultIterations, new byte[PasswordHash.SaltSize], new byte[Srp.NumberSize]);

        _ = Assert.Throws<InvalidDataException>(() => RpcHandshake.AnswerChallenge(User, Password, challenge));
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
        RpcHandshake.ServerLogin first = RpcHandshake.BeginServer("nobody", null, DecoySecret);
        RpcHandshake.ServerLogin again = RpcHandshake.BeginServer("NOBODY", null, DecoySecret);
        RpcHandshake.ServerLogin other = RpcHandshake.BeginServer("someone", null, DecoySecret);

        Assert.Equal(first.Salt, again.Salt);
        Assert.NotEqual(first.Salt, other.Salt);
        Assert.Equal(RpcHandshake.DefaultIterations, first.Iterations);
        Assert.Equal(RpcHandshake.CreateChallenge(first).Length, RpcHandshake.CreateChallenge(RpcHandshake.BeginServer(User, Stored, DecoySecret)).Length);
    }

    [Theory]
    [InlineData(RpcHandshake.MinIterations - 1)]
    [InlineData(RpcHandshake.MaxIterations + 1)]
    public void ClientRefusesUnreasonableIterationCounts(int iterations)
    {
        RpcHandshake.ServerLogin login = RpcHandshake.BeginServer(User, Stored, DecoySecret);
        byte[] challenge = RpcHandshake.CreateChallenge(iterations, login.Salt, login.B);

        _ = Assert.Throws<InvalidDataException>(() => RpcHandshake.AnswerChallenge(User, Password, challenge));
    }

    [Fact]
    public void PacketsFitTheHandshakeLimit()
    {
        RpcHandshake.ServerLogin login = RpcHandshake.BeginServer(User, Stored, DecoySecret);
        byte[] challenge = RpcHandshake.CreateChallenge(login);
        (byte[] packet, _) = RpcHandshake.AnswerChallenge(User, Password, challenge);

        Assert.True(challenge.Length <= RpcHandshake.MaxPacketSize);
        Assert.True(packet.Length <= RpcHandshake.MaxPacketSize);
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
    private static (RpcHandshake.SessionKeys Client, RpcHandshake.SessionKeys? Server, byte[] ClientProof) RunHandshake(string user, string password, string? serverUser = null)
    {
        // Server: reads the hello, answers with the stored hash's salt and its B.
        string named = RpcHandshake.ReadHello(RpcHandshake.CreateHello(user));
        RpcHandshake.ServerLogin login = RpcHandshake.BeginServer(serverUser ?? named, Stored, DecoySecret);

        // Client: derives the key from its password and the challenge, sends A and its proof.
        (byte[] packet, RpcHandshake.SessionKeys client) = RpcHandshake.AnswerChallenge(user, password, RpcHandshake.CreateChallenge(login));
        (byte[] a, byte[] proof) = RpcHandshake.ReadLogin(packet);

        // Server: derives the same from the verifier.
        RpcHandshake.SessionKeys? server = RpcHandshake.FinishServer(login, serverUser ?? named, a);
        return (client, server, proof);
    }
}
