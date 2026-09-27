using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>A view-owned instance of the common bottom explanation panel.</summary>
public partial class GameMessageBar : Control
{
    public TextureScope Textures { get; init; } = null!;
    private GameRichTextLabel _label = null!;
    private string _text = "";
    private bool _enabled = true;
    public string Text { get => _text; set => SetMessage(value); }

    public override void _Ready()
    {
        Textures.RequestPrefixes("message-");
        Position = new(0,736); Size = new(1024,32); MouseFilter = MouseFilterEnum.Ignore;
        _label = new GameRichTextLabel { Position = new(0,4), Size = new(1024,24), Textures = Textures };
        AddChild(_label);
        Refresh();
    }
    public void SetMessage(string text, bool enabled = true)
    {
        if (_text == text && _enabled == enabled) return;
        _text = text; _enabled = enabled;
        if (IsNodeReady()) Refresh();
    }
    private void Refresh()
    {
        _label.Present("[center]" + _text + "[/center]", GameTextStyles.Message(_enabled));
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (string.IsNullOrEmpty(_text)) return;
        var left = Textures["message-left"]; var middle = Textures["message-middle"]; var right = Textures["message-right"];
        int capWidth = left.GetWidth() + right.GetWidth();
        // The text and its padding occupy the middle; the fading caps sit outside it.
        int middleWidth = Math.Min(_label.GetContentWidth() + 8, Math.Max(0, 1024 - capWidth));
        int width = capWidth + middleWidth;
        int leftX = (1024 - width) / 2;
        DrawTexture(left,new(leftX,0));
        float start = leftX + left.GetWidth(), end = start + middleWidth;
        for (float x = start; x < end; x += middle.GetWidth())
        {
            var size = new Vector2(Math.Min(middle.GetWidth(),end-x),middle.GetHeight());
            DrawTextureRectRegion(middle,new(new Vector2(x,0),size),new(Vector2.Zero,size));
        }
        DrawTexture(right,new(end,0));
    }
}
