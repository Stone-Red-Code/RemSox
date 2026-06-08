namespace RemSox.UI.GUI.Rendering;

public interface IRenderSource
{
    public void Render(IEnumerable<RenderCommand> commands);
}