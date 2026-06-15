using RemSox.Networking;

namespace RemSox.UI.GUI.Rendering;

public sealed class NetworkRenderSource(TcpRpcServer server) : IRenderSource
{
    private const string MessageType = "RenderCmd";

    public void Render(IEnumerable<RenderCommand> commands)
    {
        foreach (RenderCommand cmd in commands)
        {
            byte[] data = cmd.ToBytes();
            _ = server.SendRawToAll(MessageType, data);
        }
    }

    public void Composite()
    {
    }
}
