using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Shared.Networking;

namespace RemSox.Kernel.UI.GUI.Rendering;

public sealed class NetworkRenderSource(TcpRpcServer server) : IRenderSource
{
    private const string MessageType = "RenderCmd";

    public void Render(IEnumerable<RenderCommand> commands)
    {
        foreach (RenderCommand cmd in commands)
        {
            byte[] data = cmd.ToBytes();
            server.SendRawToAll(MessageType, data);
        }
    }

    public void Composite()
    {
    }
}
