using Godot;
using San9.Application.Authoring;
using San9.Content.Images;

namespace San9.Client.Godot;

public partial class TreasureImagePreview : TextureRect
{
    public TextureService? Service { get; init; }
    public TreasureIllustrationWorkspace? Illustrations { get; init; }
    public string? ImageKey { get; init; }
    private TextureScope? _scope;
    private ImageTexture? _owned;
    private readonly CancellationTokenSource _lifetime = new();

    public override async void _Ready()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.KeepAspectCentered;
        MouseFilter = MouseFilterEnum.Ignore;
        try
        {
            var custom = Illustrations?.Pack.Images.SingleOrDefault(i => i.Key == ImageKey);
            if (custom is not null)
            {
                var token = _lifetime.Token;
                var pixels = await Task.Run(() =>
                {
                    var source = Illustrations!.ReadImage(custom.Key);
                    using var original = Image.CreateFromData(source.Width, source.Height, false,
                        source.Channels == 4 ? Image.Format.Rgba8 : Image.Format.Rgb8, source.Pixels);
                    float scale = Math.Min(1, 240f / Math.Max(source.Width, source.Height));
                    if (scale < 1) original.Resize(Math.Max(1, (int)Math.Round(source.Width * scale)),
                        Math.Max(1, (int)Math.Round(source.Height * scale)), Image.Interpolation.Lanczos);
                    return new PngImage(original.GetWidth(), original.GetHeight(), source.Channels, original.GetData());
                }, token);
                token.ThrowIfCancellationRequested();
                using var image = Image.CreateFromData(pixels.Width, pixels.Height, false,
                    pixels.Channels == 4 ? Image.Format.Rgba8 : Image.Format.Rgb8, pixels.Pixels);
                _owned = ImageTexture.CreateFromImage(image); Texture = _owned;
            }
            else if (ImageKey is not null && Service is { } service && service.ImageAssets.TryGetValue(ImageKey, out var image))
            {
                _scope = service.CreateScope(new Dictionary<string, ImageAsset> { [ImageKey] = image });
                _scope.Request([ImageKey]); Texture = _scope.Get(ImageKey, this);
            }
            else ShowUnavailable(ImageKey is null ? "未设置图片" : "图片不可用");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { if (!_lifetime.IsCancellationRequested) { ShowUnavailable("图片不可用"); TooltipText = error.Message; } }
    }

    private void ShowUnavailable(string message)
    {
        var label = new Label { Text = message, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _ExitTree()
    {
        _lifetime.Cancel(); _lifetime.Dispose();
        Texture = null; _scope?.Dispose();
        if (_owned is not null)
        {
            if (Service is { } service) service.Defer(_owned.Dispose);
            else _owned.Dispose();
        }
    }
}
