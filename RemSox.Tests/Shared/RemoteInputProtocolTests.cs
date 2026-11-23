using RemSox.Shared.Networking;

namespace RemSox.Tests.Shared;

/// <summary>What the viewer sends of the mouse, read back as the server reads it.</summary>
public sealed class RemoteInputProtocolTests
{
    [Fact]
    public void MouseButtonCarriesItsModifiers()
    {
        byte[] sent = RemoteInputProtocol.SerializeMouseButton(12, -3, "Left", ConsoleModifiers.Control | ConsoleModifiers.Shift);

        (int x, int y, string button, ConsoleModifiers modifiers) = RemoteInputProtocol.DeserializeMouseButton(sent);

        Assert.Equal((12, -3, "Left"), (x, y, button));
        Assert.Equal(ConsoleModifiers.Control | ConsoleModifiers.Shift, modifiers);
    }

    [Fact]
    public void MouseButtonWithoutModifiersReadsAsNone()
    {
        // As an older viewer sent it: no modifier byte at all.
        byte[] sent = RemoteInputProtocol.SerializeMouseButton(1, 2, "Right");
        byte[] old = sent[..^1];

        Assert.Equal((ConsoleModifiers)0, RemoteInputProtocol.DeserializeMouseButton(sent).Modifiers);
        Assert.Equal((ConsoleModifiers)0, RemoteInputProtocol.DeserializeMouseButton(old).Modifiers);
    }
}
