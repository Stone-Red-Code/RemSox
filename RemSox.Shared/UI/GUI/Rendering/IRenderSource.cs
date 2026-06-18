namespace RemSox.Shared.UI.GUI.Rendering;

public interface IRenderSource
{
    void Render(IEnumerable<RenderCommand> commands);
    void Composite();
}
