using System.Text.RegularExpressions;
using Godot;

namespace San9.Client.Godot;

/// <summary>Native BBCode renderer for package-local icons and modern theme fonts.</summary>
public partial class GameRichTextLabel : RichTextLabel
{
    private static readonly Regex Images = new(@"\[font_size=(?<size>\d+)\]|(?<end>\[/font_size\])|\[img(?<options>[^\]]*)\]icons/(?<id>[a-z0-9-]+)\[/img\]", RegexOptions.CultureInvariant);
    private string _markup = "";
    public string Markup => _markup;
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public GameTextStyle Style { get; private set; }

    public GameRichTextLabel()
    {
        BbcodeEnabled = true; ScrollActive = false;
        MouseFilter = MouseFilterEnum.Ignore;
        AutowrapMode = TextServer.AutowrapMode.Off;
    }

    public void Present(string markup, GameTextStyle style)
    {
        if (style.Effect == GameTextEffect.Shadow && markup.Contains("[outline_size=", StringComparison.Ordinal))
            throw new InvalidDataException("Inline outlines cannot replace a host shadow in native RichTextLabel; choose an explicit presentation policy.");
        _markup = markup; Style = style;
        AddThemeFontOverride("normal_font", GetThemeFont("font"));
        AddThemeFontSizeOverride("normal_font_size", style.FontSize);
        AddThemeColorOverride("default_color", style.Foreground);
        AddThemeConstantOverride("outline_size", GameTextEffects.OutlineSize(style));
        AddThemeColorOverride("font_outline_color", GameTextEffects.OutlineColor(style));
        AddThemeColorOverride("font_shadow_color", GameTextEffects.ShadowColor(style));
        AddThemeConstantOverride("shadow_offset_x", (int)GameTextEffects.ShadowOffset(style).X);
        AddThemeConstantOverride("shadow_offset_y", (int)GameTextEffects.ShadowOffset(style).Y);
        AddThemeConstantOverride("shadow_outline_size", 0);
        var sizes = new Stack<int>();
        sizes.Push(style.FontSize);
        Text = Images.Replace(markup, match =>
        {
            if (match.Groups["size"].Success)
            {
                if (!int.TryParse(match.Groups["size"].Value, out int value) || value is < 1 or > 4096)
                    throw new InvalidDataException("Invalid inline font size.");
                sizes.Push(value); return match.Value;
            }
            if (match.Groups["end"].Success) { if (sizes.Count > 1) sizes.Pop(); return match.Value; }
            int size = sizes.Peek();
            string prefix = "text-icon-" + match.Groups["id"].Value + "-";
            var candidates = Textures.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(p => (Texture: p.Value, Size: int.Parse(p.Key[prefix.Length..])))
                .OrderBy(p => Math.Abs(p.Size - size)).ThenByDescending(p => p.Size).ToArray();
            if (candidates.Length == 0) throw new InvalidDataException($"Missing text icon: {match.Groups["id"].Value}");
            var texture = candidates[0].Texture;
            // A cached resource name, not a disk file. Ownership remains with the loaded skin textures.
            string path = $"res://message-images/{texture.GetInstanceId()}.tres";
            if (texture.ResourcePath != path) texture.TakeOverPath(path);
            string options = match.Groups["options"].Value;
            if (options.Length == 0)
            {
                int width = (int)Math.Round((double)texture.GetWidth() * size / texture.GetHeight());
                options = $" width={width} height={size}";
            }
            return $"[img{options}]{path}[/img]";
        });
    }
}
