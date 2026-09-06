using Godot;

namespace San9.Client.Godot;

public enum GameTextRole { Header, Resource, Building, Message, Region, Spring, Compass, Roster }
public enum GameButtonTextRole { Information, Functions, Help, Territory, MapMode, Advance, Facilities, Officers, Units }

public enum GameTextEffect { None, NeighborOutline, Shadow }

public readonly record struct GameTextStyle(int FontSize, Color Foreground,
    GameTextEffect Effect, Color EffectColor);

public static class GameTextEffects
{
    private static readonly Vector2[] Neighbors =
    [new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)];

    public static int PassCount(GameTextStyle style) => style.Effect switch
    {
        GameTextEffect.None => 0,
        GameTextEffect.NeighborOutline => Neighbors.Length,
        GameTextEffect.Shadow => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };

    public static Vector2 Offset(GameTextStyle style, int pass)
    {
        if ((uint)pass >= PassCount(style)) throw new ArgumentOutOfRangeException(nameof(pass));
        return style.Effect == GameTextEffect.Shadow ? Vector2.One : Neighbors[pass];
    }

    public static Color Color(GameTextStyle style) =>
        new(style.EffectColor, style.EffectColor.A * (style.Effect == GameTextEffect.NeighborOutline ? .25f : 1f));
}

public static class GameTextStyles
{
    private static readonly Color White = new("f0f0f0");
    private static GameTextStyle Outlined(int size, Color foreground, string edge = "000000") =>
        new(size, foreground, GameTextEffect.NeighborOutline, new Color(edge));
    private static GameTextStyle Plain(int size, Color foreground) =>
        new(size, foreground, GameTextEffect.None, Colors.Transparent);

    public static GameTextStyle For(GameTextRole role) => role switch
    {
        GameTextRole.Building => Outlined(16, White),
        GameTextRole.Message => new(16, White, GameTextEffect.Shadow, Colors.Black),
        GameTextRole.Spring => Outlined(16, White, "770000"),
        GameTextRole.Compass => Outlined(16, new Color("988058")),
        GameTextRole.Header => Plain(24, White),
        GameTextRole.Resource => Plain(16, White),
        GameTextRole.Region => Outlined(16, new Color("f8f8b0"), "005028"),
        GameTextRole.Roster => Plain(16, White),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static GameTextStyle Message(bool enabled) => For(GameTextRole.Message) with
    {
        Foreground = enabled ? White : new Color("f06850")
    };

    public static GameTextStyle CatalogRow => Outlined(16, White);

    public static GameTextStyle CatalogHeader(bool hovered, bool pressed) =>
        Outlined(16, new Color(hovered && !pressed ? "f0f0f0" : "d8d8d8"),
            hovered && !pressed ? "005028" : "000000");

    public static GameTextStyle InformationMenu(bool hovered, bool pressed, bool disabled) =>
        Outlined(16, disabled ? new Color(hovered ? "000000" : "a4a4a4") : pressed ? new Color("f8f850") : White,
            disabled ? hovered ? "f0f0f0" : "000000" : pressed ? "005028" : "000000");

    public static GameTextStyle Button(GameButtonTextRole role, bool hovered, bool selected, bool disabled)
    {
        bool command = role is GameButtonTextRole.Information or GameButtonTextRole.Functions or GameButtonTextRole.Help or GameButtonTextRole.Advance;
        Color foreground = disabled ? new Color(hovered ? "000000" : "a4a4a4")
            : hovered || (command && selected) ? White : new Color("d8d8d8");
        string activeEdge = role switch
        {
            GameButtonTextRole.Information or GameButtonTextRole.Advance or GameButtonTextRole.Facilities => "005028",
            GameButtonTextRole.Functions or GameButtonTextRole.Units => "5828c8",
            GameButtonTextRole.Officers => "800090",
            _ => "603000"
        };
        return Outlined(24, foreground, disabled ? (hovered ? "f0f0f0" : "000000")
            : hovered || selected ? activeEdge : "000000");
    }
}

// One adapter owns immutable shared settings; map text uses the same style values.
public sealed class GameTextPresenter : IDisposable
{
    private readonly Dictionary<(GameTextStyle Style, Font Font), LabelSettings> _settings = [];
    public void Apply(Label label, GameTextStyle style)
    {
        var key = (style, label.GetThemeFont("font"));
        if (!_settings.TryGetValue(key, out var settings))
        {
            settings = new LabelSettings { Font = key.Item2, FontSize = style.FontSize, FontColor = style.Foreground,
                OutlineSize = 0, OutlineColor = Colors.Transparent, ShadowColor = Colors.Transparent,
                ShadowSize = 0, StackedShadowCount = GameTextEffects.PassCount(style) };
            for (int pass = 0; pass < settings.StackedShadowCount; pass++)
            {
                // Godot draws stacked shadows from the last index to the first.
                int layer = settings.StackedShadowCount - 1 - pass;
                settings.SetStackedShadowOffset(layer, GameTextEffects.Offset(style, pass));
                settings.SetStackedShadowColor(layer, GameTextEffects.Color(style));
                settings.SetStackedShadowOutlineSize(layer, 0);
            }
            _settings.Add(key, settings);
        }
        label.LabelSettings = settings;
    }

    public static void Apply(Button button, GameTextStyle style)
    {
        if (style.Effect != GameTextEffect.None) throw new ArgumentException("Effect text requires a Label presenter.", nameof(style));
        button.AddThemeFontSizeOverride("font_size", style.FontSize);
        button.AddThemeConstantOverride("outline_size", 0);
        button.AddThemeColorOverride("font_outline_color", Colors.Transparent);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color" })
            button.AddThemeColorOverride(state, style.Foreground);
    }

    public static void Draw(CanvasItem canvas, Font font, string text, Vector2 point, GameTextStyle style,
        HorizontalAlignment alignment = HorizontalAlignment.Left, float width = -1)
    {
        for (int pass = 0; pass < GameTextEffects.PassCount(style); pass++)
            canvas.DrawString(font, point + GameTextEffects.Offset(style, pass), text, alignment, width,
                style.FontSize, GameTextEffects.Color(style));
        canvas.DrawString(font, point, text, alignment, width, style.FontSize, style.Foreground);
    }

    public void Dispose() { foreach (var settings in _settings.Values) settings.Dispose(); _settings.Clear(); }
}
