using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public enum GameTextRole { Header, Resource, Building, Message, Region, Spring, Compass, Roster, HelpBody, DetailBody, DialogTitle }
public enum GameButtonTextRole { Information, Functions, Help, Territory, MapMode, Advance, Facilities, Officers, Units, ConfirmYes, ConfirmNo }

public enum GameTextEffect { None, Outline, Shadow }

public readonly record struct GameTextStyle(int FontSize, Color Foreground,
    GameTextEffect Effect, Color EffectColor);

public static class GameTextEffects
{
    public static int OutlineSize(GameTextStyle style) => style.Effect == GameTextEffect.Outline ? TextOutlineStyle.Size : 0;
    public static Color OutlineColor(GameTextStyle style) => style.Effect == GameTextEffect.Outline
        ? new Color(style.EffectColor, style.EffectColor.A * TextOutlineStyle.Alpha) : Colors.Transparent;
    public static Color ShadowColor(GameTextStyle style) => style.Effect == GameTextEffect.Shadow
        ? style.EffectColor : Colors.Transparent;
    public static Vector2 ShadowOffset(GameTextStyle style) => style.Effect == GameTextEffect.Shadow
        ? Vector2.One : Vector2.Zero;
}

public static class GameTextStyles
{
    private static readonly Color White = new("f0f0f0");
    private static GameTextStyle Outlined(int size, Color foreground, string edge = "000000") =>
        new(size, foreground, GameTextEffect.Outline, new Color(edge));
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
        GameTextRole.HelpBody or GameTextRole.DetailBody => Outlined(16, White),
        GameTextRole.DialogTitle => Plain(18, Colors.White),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static GameTextStyle Message(bool enabled) => For(GameTextRole.Message) with
    {
        Foreground = enabled ? White : new Color("f06850")
    };

    public static GameTextStyle CatalogRow => Outlined(16, White);
    public static GameTextStyle CatalogDisabledRow => Outlined(16, new Color("a4a4a4"));

    public static GameTextStyle CatalogBulk(bool hovered, bool pressed, bool disabled) =>
        Outlined(16, new Color(disabled ? hovered ? "000000" : "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8"),
            disabled ? hovered ? "f0f0f0" : "000000" : hovered || pressed ? "603000" : "000000");

    public static GameTextStyle Building(bool highlighted) => For(GameTextRole.Building) with
    { Foreground = highlighted ? new Color("f8f850") : White };

    public static GameTextStyle CatalogTab(bool hovered, bool selected) =>
        Outlined(16, new Color(hovered ? "f0f0f0" : "d8d8d8"), hovered || selected ? "603000" : "000000");

    // Confirmed paired tables in help/training page updates; these are reusable before those screens exist.
    public static GameTextStyle HelpTopic(bool hovered, bool pressed, bool disabled, bool current)
    {
        var foreground = disabled ? new Color(current ? "000000" : "a4a4a4")
            : new Color(current || pressed ? "f0f0f0" : hovered ? "c0e0e8" : "d8d8d8");
        return !current && !hovered && !pressed && !disabled ? Plain(16, foreground)
            : Outlined(16, foreground, disabled ? "000000" : "5828c8");
    }

    public static GameTextStyle CatalogHeader(bool hovered, bool pressed) =>
        Outlined(16, new Color(hovered && !pressed ? "f0f0f0" : "d8d8d8"),
            hovered && !pressed ? "005028" : "000000");

    public static GameTextStyle InformationMenu(bool hovered, bool pressed, bool disabled) =>
        Outlined(16, disabled ? new Color(hovered ? "000000" : "a4a4a4") : pressed ? new Color("f8f850") : White,
            disabled ? hovered ? "f0f0f0" : "000000" : pressed ? "005028" : "000000");

    public static GameTextStyle Button(GameButtonTextRole role, bool hovered, bool selected, bool disabled)
    {
        bool command = role is GameButtonTextRole.Information or GameButtonTextRole.Functions or GameButtonTextRole.Help or GameButtonTextRole.Advance or GameButtonTextRole.ConfirmYes or GameButtonTextRole.ConfirmNo;
        Color foreground = disabled ? new Color(hovered ? "000000" : "a4a4a4")
            : hovered || (command && selected) ? White : new Color("d8d8d8");
        string activeEdge = role switch
        {
            GameButtonTextRole.Information or GameButtonTextRole.Advance or GameButtonTextRole.Facilities => "005028",
            GameButtonTextRole.Functions or GameButtonTextRole.Units => "5828c8",
            GameButtonTextRole.Officers => "800090",
            GameButtonTextRole.ConfirmYes => "002878",
            GameButtonTextRole.ConfirmNo => selected ? "b82000" : "000000",
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
                OutlineSize = GameTextEffects.OutlineSize(style), OutlineColor = GameTextEffects.OutlineColor(style),
                ShadowColor = GameTextEffects.ShadowColor(style), ShadowOffset = GameTextEffects.ShadowOffset(style),
                ShadowSize = 0 };
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
        if (style.Effect == GameTextEffect.Shadow)
            canvas.DrawString(font, point + GameTextEffects.ShadowOffset(style), text, alignment, width,
                style.FontSize, GameTextEffects.ShadowColor(style));
        if (style.Effect == GameTextEffect.Outline)
            canvas.DrawStringOutline(font, point, text, alignment, width, style.FontSize,
                GameTextEffects.OutlineSize(style), GameTextEffects.OutlineColor(style));
        canvas.DrawString(font, point, text, alignment, width, style.FontSize, style.Foreground);
    }

    public void Dispose() { foreach (var settings in _settings.Values) settings.Dispose(); _settings.Clear(); }
}
