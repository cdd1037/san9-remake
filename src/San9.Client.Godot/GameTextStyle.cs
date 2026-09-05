using Godot;

namespace San9.Client.Godot;

public enum GameTextRole { Header, Resource, Building, Message, Region, Spring, Compass, Facility }
public enum GameButtonTextRole { Information, Functions, Help, Territory, Zoom, Advance, Facilities, Officers, Units }

public readonly record struct GameTextStyle(int FontSize, Color Foreground, int OutlineSize,
    Color Outline, Color Shadow, Vector2 ShadowOffset);

public static class GameTextStyles
{
    private static readonly Color White = new("f0f0f0");
    private static Color Effect(string rgb) => new(new Color(rgb), .65f);
    private static GameTextStyle Outlined(int size, Color foreground, string edge = "000000") =>
        new(size, foreground, 1, Effect(edge), Colors.Transparent, Vector2.Zero);
    private static GameTextStyle Plain(int size, Color foreground) =>
        new(size, foreground, 0, Colors.Transparent, Colors.Transparent, Vector2.Zero);

    public static GameTextStyle For(GameTextRole role) => role switch
    {
        GameTextRole.Building => Outlined(16, White),
        GameTextRole.Message => Outlined(16, White) with { Shadow = Effect("000000"), ShadowOffset = Vector2.One },
        GameTextRole.Spring => Outlined(16, White, "770000"),
        GameTextRole.Compass => Outlined(16, new Color("988058")),
        GameTextRole.Header => Plain(24, White),
        GameTextRole.Resource => Plain(16, White),
        GameTextRole.Region => Outlined(16, new Color("f8f8b0"), "005028"),
        GameTextRole.Facility => Plain(16, White),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

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
                OutlineSize = style.OutlineSize, OutlineColor = style.Outline,
                ShadowColor = style.Shadow, ShadowOffset = style.ShadowOffset, ShadowSize = style.Shadow.A > 0 ? 1 : 0 };
            _settings.Add(key, settings);
        }
        label.LabelSettings = settings;
    }

    public static void Apply(Button button, GameTextStyle style)
    {
        button.AddThemeFontSizeOverride("font_size", style.FontSize);
        button.AddThemeConstantOverride("outline_size", style.OutlineSize);
        button.AddThemeColorOverride("font_outline_color", style.Outline);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color" })
            button.AddThemeColorOverride(state, style.Foreground);
    }

    public static void Draw(CanvasItem canvas, Font font, string text, Vector2 point, GameTextStyle style)
    {
        if (style.Shadow.A > 0) canvas.DrawString(font, point + style.ShadowOffset, text, fontSize: style.FontSize, modulate: style.Shadow);
        if (style.OutlineSize > 0) canvas.DrawStringOutline(font, point, text, fontSize: style.FontSize, size: style.OutlineSize, modulate: style.Outline);
        canvas.DrawString(font, point, text, fontSize: style.FontSize, modulate: style.Foreground);
    }

    public void Dispose() { foreach (var settings in _settings.Values) settings.Dispose(); _settings.Clear(); }
}
