using Godot;

namespace San9.Client.Godot;

public partial class ScenarioCategoryButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string Highlight { get; init; } = "";
    public string Title { get; init; } = "";
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; QueueRedraw(); }
    }
    public override void _Ready()
    {
        var label = new GameRichTextLabel { Textures = Textures, Position = new(0, 7), Size = new(144, 32) };
        AddChild(label);
        label.Present($"[center]{San9.Content.Text.MessageMarkup.Escape(Title)}[font_size=16]剧本[/font_size][/center]",
            new(24, new Color("f8d860"), GameTextEffect.Outline, new Color("603000")));
    }
    public override void _Draw()
    {
        if (Selected && Textures.TryGetReady(Highlight, this, out var texture)) DrawTexture(texture, Vector2.Zero);
    }
}
