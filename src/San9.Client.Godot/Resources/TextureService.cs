using Godot;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class TextureService : Node
{
    private Dictionary<string, PortraitDefinition> _portraits = [];
    private TextureCache<ImageTexture> _cache = null!;
    private readonly Dictionary<ImageAsset, AlphaMask> _masks = [];
    private readonly Dictionary<ImageFile, ImageAsset[]> _maskSources = [];
    private readonly HashSet<TextureScope> _scopes = [];
    private readonly List<(long Frame, Action Release)> _retired = [];
    private long _frame;
    private bool _closed;
    private double _frameUploadMilliseconds;
    private long _generatedUploadOverruns;
    public bool CanUpload => _frameUploadMilliseconds < 2;
    public IReadOnlyDictionary<string, ImageAsset> MapAssets { get; private set; } = null!;
    public IReadOnlyDictionary<string, ImageAsset> LibraryAssets { get; private set; } = null!;
    public IReadOnlyDictionary<string, San9.Content.Maps.MapAnimationClip> AnimationClips { get; private set; } = null!;
    public IReadOnlyDictionary<string, ImageAsset> Roles { get; private set; } = null!;
    public IReadOnlyDictionary<int, TargetMarkerOffset> BuildingTargetOffsets { get; private set; } = null!;
    public IReadOnlyDictionary<int, TargetMarkerOffset> BuildingArrowOffsets { get; private set; } = null!;
    public IReadOnlyDictionary<string, CursorDefinition> Cursors { get; private set; } = null!;
    public IReadOnlyDictionary<string, ImageAsset> CursorAssets { get; private set; } = null!;
    public TextureCache<ImageTexture> Cache => _cache;
    public OverviewMapRenderer OverviewMap { get; private set; } = null!;
    public ImageTexture MinimapTerrain { get; private set; } = null!;
    public void Initialize(ClientContentPackage package, string root)
    {
        Initialize(package, (file, token) => ClientImageFiles.LoadFile(root, file, token));
        var map = package.Map;
        OverviewMap = new(map.Width, map.Height, map.Squares, package.Scenario.RegionCityIds);
        MinimapTerrain = CreateTexture(MinimapRenderer.Compile(map.Width, map.Height, map.Squares, map.MinimapColors));
    }
    internal void Initialize(ClientContentPackage package, Func<ImageFile, CancellationToken, PngImage> load)
    {
        _portraits = package.Portraits.ToDictionary(p => p.Image.Id);
        MapAssets = package.Map.Assets.ToDictionary(a => a.Id);
        LibraryAssets = package.Textures.Assets.ToDictionary(a => a.Id);
        AnimationClips = package.Map.Animations.Clips.ToDictionary(c => c.Id);
        var ui = package.Skin.Assets.ToDictionary(a => a.Id);
        Roles = package.Skin.Roles.ToDictionary(p => p.Key, p => ui[p.Value]);
        BuildingTargetOffsets = package.Skin.BuildingTargetOffsets;
        BuildingArrowOffsets = package.Skin.BuildingArrowOffsets;
        Cursors = package.Skin.Cursors;
        CursorAssets = Cursors.ToDictionary(p => p.Key, p => ui[p.Value.Asset]);
        var unitClips = package.Map.Units.Bodies.SelectMany(body => body.DirectionClips
            .Concat(body.Attacks.SelectMany(a => a.Actions).SelectMany(a => a.DirectionClips))).ToHashSet();
        var unitFrames = package.Map.Animations.Clips.Where(clip => unitClips.Contains(clip.Id) ||
                clip.Id.StartsWith("unit-flag-", StringComparison.Ordinal))
            .SelectMany(clip => clip.Frames.Concat(clip.SeasonFrames.Values.SelectMany(frames => frames)))
            .ToHashSet();
        var hitImages = MapAssets.Values.Where(a => a.Id.StartsWith("building-", StringComparison.Ordinal) || unitFrames.Contains(a.Id))
            .Concat(Roles.Where(p => p.Key is "top-frame" or "radar-frame" or "sidebar-frame" or "ruler-frame" or "target-footer" || p.Key.StartsWith("target-title-", StringComparison.Ordinal) || p.Key.StartsWith("button-overview-", StringComparison.Ordinal)).Select(p => p.Value));
        foreach (var group in hitImages.Distinct().GroupBy(a => a.File)) _maskSources[group.Key] = group.ToArray();
        _cache = new(load, Upload, t => t.Dispose());
        ProcessPriority = -100;
    }
    private ImageTexture Upload(ImageFile file, PngImage pixels)
    {
        if (_maskSources.TryGetValue(file, out var assets))
            foreach (var asset in assets)
                if (!_masks.ContainsKey(asset)) _masks.Add(asset, new(pixels, asset, asset.Id.StartsWith("building-", StringComparison.Ordinal) ? (byte)128 : (byte)1));
        return CreateTexture(pixels);
    }
    private static ImageTexture CreateTexture(PngImage pixels)
    {
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false,
            pixels.Channels switch { 1 => Image.Format.L8, 3 => Image.Format.Rgb8, _ => Image.Format.Rgba8 }, pixels.Pixels);
        return ImageTexture.CreateFromImage(image);
    }
    public ImageTexture UploadGenerated(PngImage pixels)
    {
        if (!CanUpload) throw new InvalidOperationException("The frame upload budget is exhausted.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var texture = CreateTexture(pixels);
        double elapsed = watch.Elapsed.TotalMilliseconds;
        _frameUploadMilliseconds += elapsed; if (elapsed > 2) _generatedUploadOverruns++;
        return texture;
    }
    public Rect2 HitBounds(ImageAsset asset)
    {
        if (!_masks.TryGetValue(asset, out var mask)) return default;
        var bounds = mask.Bounds;
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
    public bool Hit(ImageAsset asset, int x, int y) => _masks.TryGetValue(asset, out var mask) && mask.Contains(x, y);
    public Bitmap? ClickMask(ImageAsset asset)
    {
        if (!_masks.TryGetValue(asset, out var mask)) return null;
        var bitmap = new Bitmap(); bitmap.Create(new(mask.Width, mask.Height));
        for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++) bitmap.SetBit(x, y, mask.Contains(x, y));
        return bitmap;
    }
    public TextureScope CreatePortraitScope(string role, PortraitView view)
    {
        var image = Roles[role];
        if (_portraits.TryGetValue(image.Id, out var portrait)) image = portrait.GetView(view);
        return CreateScope(new Dictionary<string, ImageAsset> { [role] = image });
    }
    public TextureScope CreateScope(IReadOnlyDictionary<string, ImageAsset>? assets = null, bool prefetch = false)
    {
        var scope = new TextureScope(this, assets ?? Roles, prefetch);
        _scopes.Add(scope); return scope;
    }
    public bool ActiveReady => _scopes.All(s => s.IsReady);
    public Exception? ActiveError => _scopes.Select(s => s.Error).FirstOrDefault(e => e is not null);
    public void Defer(Action release) { if (_closed) release(); else _retired.Add((_frame + 2, release)); }
    internal void Retire(TextureScope scope, Action release)
    {
        _scopes.Remove(scope);
        if (_closed) release(); else _retired.Add((_frame + 2, release));
    }
    public override void _Process(double delta)
    {
        _frame++;
        foreach (var item in _retired.Where(r => r.Frame <= _frame).ToArray()) { item.Release(); _retired.Remove(item); }
        double previousUploads = _cache.UploadMilliseconds;
        _cache.Pump();
        _frameUploadMilliseconds = _cache.UploadMilliseconds - previousUploads;
        foreach (var scope in _scopes.ToArray()) scope.Refresh();
    }
    public string Statistics => $"GPU估算 {_cache.Bytes / 1048576.0:F1} MiB（固定 {_cache.PinnedBytes / 1048576.0:F1} / 闲置 {(_cache.Bytes - _cache.PinnedBytes) / 1048576.0:F1}）\n" +
        $"纹理 {_cache.Count} 命中 {_cache.Hits} 缺失 {_cache.Misses} 淘汰 {_cache.Evictions} 待上传 {_cache.PendingBytes / 1048576.0:F1} MiB\n" +
        $"解码 {_cache.DecodeMilliseconds:F1} ms 上传 {_cache.UploadMilliseconds:F1} ms 单次超时 {_cache.UploadOverruns + _generatedUploadOverruns}";
    public override void _ExitTree()
    {
        _closed = true;
        foreach (var scope in _scopes.ToArray()) scope.Dispose();
        foreach (var item in _retired) item.Release(); _retired.Clear();
        MinimapTerrain?.Dispose();
        _cache.Dispose(); _masks.Clear();
    }
}
