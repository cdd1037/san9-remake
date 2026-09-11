using Godot;

namespace San9.Client.Godot.Diagnostics;

/// <summary>Actual engine comparison of the three adapters, including disabled state contrast.</summary>
public partial class TextStyleSheet : Control
{
    public (string Name, GameTextStyle Style)[] Samples { get; init; } = [];
    public int FontSize { get; init; }
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    private readonly GameTextPresenter _presenter = new();

    public override void _Ready()
    {
        AddChild(new ColorRect { Size = Size, Color = new("182025") });
        AddChild(new Label { Position = new(18, 8), Text = $"{FontSize}px    Role/state             Label                         RichTextLabel + icon                  Canvas" });
        for (int i = 0; i < Samples.Length; i++)
        {
            var (name, source) = Samples[i];
            var style = source with { FontSize = FontSize };
            float y = 48 + i * 38;
            // Split backgrounds expose both dark and light foreground states.
            AddChild(new ColorRect { Position = new(210, y), Size = new(800, 34), Color = new("71817b") });
            var title = new Label { Position = new(18, y), Text = name };
            AddChild(title); _presenter.Apply(title, GameTextStyles.For(GameTextRole.Resource) with { FontSize = 12 });
            var label = new Label { Position = new(220, y), Size = new(230, 34), Text = "洛阳 32000 士兵" };
            AddChild(label); _presenter.Apply(label, style);
            var rich = new GameRichTextLabel { Position = new(470, y), Size = new(270, 34), Textures = Textures };
            AddChild(rich); rich.Present("[img]icons/city[/img]洛阳 32000 士兵", style);
            if (label.LabelSettings.OutlineSize != rich.GetThemeConstant("outline_size") ||
                label.LabelSettings.ShadowColor != rich.GetThemeColor("font_shadow_color"))
                throw new InvalidDataException("Text adapters disagree on native effects.");
        }
        // Draw map samples on a foreground child so their backgrounds cannot cover them.
        AddChild(new MapSamples { Samples = Samples, FontSize = FontSize, Size = Size });
    }

    public override void _ExitTree() => _presenter.Dispose();

    public partial class MapSamples : Control
    {
        public (string Name, GameTextStyle Style)[] Samples { get; init; } = [];
        public int FontSize { get; init; }
        public override void _Draw()
        {
            var font = GetThemeFont("font");
            for (int i = 0; i < Samples.Length; i++)
                GameTextPresenter.Draw(this, font, "洛阳 32000 士兵", new(770, 48 + i * 38 + font.GetAscent(FontSize)),
                    Samples[i].Style with { FontSize = FontSize });
        }
    }
}
