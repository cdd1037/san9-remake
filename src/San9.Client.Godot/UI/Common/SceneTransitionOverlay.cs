using Godot;

namespace San9.Client.Godot;

/// <summary>A passive overlay; its owner supplies time, phase and input policy.</summary>
public partial class SceneTransitionOverlay : ColorRect
{
    public SceneTransitionOverlay()
    {
        Color = new Color(8 / 255f, 8 / 255f, 8 / 255f, 0);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 1;
        Size = new(1024, 768);
        Hide();
    }

    public void Sample(float opacity)
    {
        Color = new Color(Color.R, Color.G, Color.B, opacity);
        Visible = opacity > 0;
    }
}
