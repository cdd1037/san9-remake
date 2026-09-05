using Godot;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyHud : Control
{
    public MapWorld World { get; init; } = null!;
    public StrategySelection Selection { get; init; } = null!;
    public ScenarioSnapshot Scenario { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public IReadOnlyDictionary<string, PngImage> SkinImages { get; init; } = null!;
    private readonly Dictionary<string, ImageTexture> _textures = [];
    private readonly Dictionary<int, Button> _facilityButtons = [];
    private readonly List<(string Asset, Vector2 Position)> _frames = [];
    private Label _footer = null!, _message = null!;
    private Label _footerCity = null!;
    private readonly List<Label> _regionLabels = [];
    private bool _regionVisible;
    private Vector2? _hoverPosition;
    private ScrollContainer _scroll = null!;
    private StrategyMinimap? _minimap;
    private bool _built;
    private TextureButton? _large, _small, _territory;
    private readonly Dictionary<int, Color> _facilityColors = [];
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        Name = "StrategyHud"; Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        World.MapReady += Build;
        GetWindow().MouseExited += ClearHover;
        _message = LabelAt("正在准备190年地图…", new(312, 740), new(400, 24), 16);
        _text.Apply(_message, GameTextStyles.For(GameTextRole.Message));
        Label RegionLabel(string value, int x, int width)
        {
            var label = LabelAt(value, new(x, 744), new(width, 16), 16);
            _text.Apply(label, GameTextStyles.For(GameTextRole.Region));
            label.Visible = false; _regionLabels.Add(label); return label;
        }
        RegionLabel("地域", 856, 32); RegionLabel("：", 888, 16);
        _footer = RegionLabel("", 904, 48);
        RegionLabel("〈", 952, 16); _footerCity = RegionLabel("", 968, 32); RegionLabel("〉", 1000, 16);
        if (World.IsVisibleReady) Build();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        var p = Scenario;
        foreach (var pair in Skin.Roles)
        {
            var pixels = SkinImages[pair.Value];
            using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Pixels);
            _textures.Add(pair.Key, ImageTexture.CreateFromImage(image));
        }
        _frames.Add(("top-frame", Vector2.Zero));
        _frames.Add(("radar-frame", new(624, 0)));
        _frames.Add(("sidebar-frame", new(836, 167)));
        _frames.Add(("ruler-frame", new(275, 11)));
        _frames.Add(("scroll-track", new(992, 303)));
        var force = p.Forces.Single(f => f.Id == p.PlayerForceId);
        LabelAt($"{p.Year}年", new(104, 11), new(66, 32), 24);
        LabelAt($"{p.Month}月上旬", new(182, 11), new(89, 32), 24);
        LabelAt(Texts.Resolve(force.NameKey), new(280, 11), new(92, 32), 24, HorizontalAlignment.Left);
        AddResourceField("信望", force.Trust.ToString(), 385, 425, 32);
        AddResourceField("资金", force.Money.ToString(), 465, 505, 56);
        AddResourceField("兵粮", force.Food.ToString(), 569, 609, 56);
        _text.Apply(LabelAt("春", new(26, 40), new(16, 16), 16), GameTextStyles.For(GameTextRole.Spring));
        _territory = AddButton("疆界", "button-territory", new(694, 10), new(64, 36), true, () => World.ShowBoundaries = !World.ShowBoundaries);
        _territory.ToggleMode = true; _territory.SetPressedNoSignal(World.ShowBoundaries);
        AddButton("情报", "button-information", new(842, 7), new(80, 40), false);
        AddButton("功能", "button-functions", new(929, 7), new(80, 40), false);
        AddButton("?", "button-help", new(957, 50), new(56, 40), false);
        AddButton("部队", "button-units", new(956, 208), new(56, 32), false);
        AddButton("武将", "button-officers", new(927, 238), new(56, 32), false);
        var facilities = AddButton("设施", "button-facilities", new(899, 268), new(56, 32), true, () => _scroll.GrabFocus());
        facilities.ToggleMode = true; facilities.SetPressedNoSignal(true);
        _large = AddButton("大", "button-zoom", new(900, 627), new(44, 44), true, () => World.SetZoom(1));
        _large.ToggleMode = true;
        _small = AddButton("小", "button-zoom", new(957, 606), new(44, 44), true, () => World.SetZoom(.5f));
        _small.ToggleMode = true;
        AddButton("进行", "button-advance", new(943, 664), new(60, 60), false);
        _minimap = new StrategyMinimap { World = World, Scenario = Scenario, Skin = Skin, Position = new(643, 31), Size = new(320, 160) };
        AddChild(_minimap);
        foreach (var (text, x, y) in new[] { ("东", 880, 154), ("西", 710, 52), ("南", 710, 154), ("北", 880, 52) })
            _text.Apply(LabelAt(text, new(x, y), new(16, 16), 16), GameTextStyles.For(GameTextRole.Compass));
        _scroll = new ScrollContainer { Position = new(892, 303), Size = new(100, 280),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FocusMode = FocusModeEnum.All, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 0); _scroll.AddChild(list);
        foreach (var s in Selection.PlayerFacilities)
        {
            var b = new Button { Text = Texts.Resolve(s.NameKey), Flat = true, Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new(100, 20), MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = $"{Texts.Resolve(s.NameKey)} · 兵力 {s.Soldiers}\n耐久 {s.Durability}/{s.MaxDurability}" };
            var style = new StyleBoxEmpty();
            style.ContentMarginLeft = 16; style.ContentMarginTop = 0; style.ContentMarginBottom = 0; style.ContentMarginRight = 0;
            b.AddThemeStyleboxOverride("normal", style); b.AddThemeStyleboxOverride("hover", style); b.AddThemeStyleboxOverride("pressed", style); b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var color = new Color(Skin.Colors[s.HasAvailableOfficer ? "facility-available" : "facility-unavailable"]);
            _facilityColors.Add(s.Id, color);
            GameTextPresenter.Apply(b, GameTextStyles.For(GameTextRole.Facility) with { Foreground = color });
            if (s.CityMarkerColor is not null)
            {
                var icon = new TextureRect { Texture = _textures["city-prefix"], Position = new(0, 2), Size = new(16, 16), MouseFilter = MouseFilterEnum.Ignore };
                b.AddChild(icon);
            }
            b.Pressed += () => FocusFacility(s.Id);
            list.AddChild(b); _facilityButtons.Add(s.Id, b);
        }
        Selection.Changed += UpdateSelection;
        UpdateSelection(); QueueRedraw();
    }

    private void AddResourceField(string title, string value, int titleX, int valueX, int valueWidth)
    {
        // Center the font's line metrics in the background, not in an original bitmap glyph cell.
        LabelAt(title, new(titleX, 15), new(32, 24), 16, HorizontalAlignment.Left);
        LabelAt(value, new(valueX, 15), new(valueWidth, 24), 16, HorizontalAlignment.Right);
    }

    private Label LabelAt(string text, Vector2 position, Vector2 size, int fontSize, HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var label = new Label { Text = text, Position = position, Size = size, HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label);
        _text.Apply(label, GameTextStyles.For(fontSize == 24 ? GameTextRole.Header : GameTextRole.Resource));
        return label;
    }

    private TextureButton AddButton(string title, string asset, Vector2 position, Vector2 size, bool enabled, Action? action = null)
    {
        var button = new StrategyButton { Title = title, Position = position, Size = size, IgnoreTextureSize = true,
            Territory = asset == "button-territory",
            TextRole = asset switch
            {
                "button-information" => GameButtonTextRole.Information,
                "button-functions" => GameButtonTextRole.Functions,
                "button-help" => GameButtonTextRole.Help,
                "button-territory" => GameButtonTextRole.Territory,
                "button-zoom" => GameButtonTextRole.Zoom,
                "button-advance" => GameButtonTextRole.Advance,
                "button-facilities" => GameButtonTextRole.Facilities,
                "button-officers" => GameButtonTextRole.Officers,
                "button-units" => GameButtonTextRole.Units,
                _ => throw new ArgumentOutOfRangeException(nameof(asset))
            },
            Frames = Enumerable.Range(0, asset == "button-territory" ? 6 : 4)
                .Select(frame => (Texture2D)_textures[frame == 0 ? asset : $"{asset}-{frame}"]).ToArray(),
            StretchMode = TextureButton.StretchModeEnum.Scale, Disabled = !enabled,
            TooltipText = enabled ? title : $"{title}：首版尚未实现", MouseFilter = MouseFilterEnum.Stop };
        if (action is not null) button.Pressed += () => action();
        AddChild(button);
        return button;
    }

    public bool IsBlockedPoint(Vector2 point)
    {
        if (point.Y >= 740 && point.Y < 768 && point.X >= 0 && point.X < 1024) return true;
        foreach (var (asset, position) in _frames)
        {
            var pixels = SkinImages[Skin.Roles[asset]];
            int x = (int)Math.Floor(point.X - position.X), y = (int)Math.Floor(point.Y - position.Y);
            if (x >= 0 && y >= 0 && x < pixels.Width && y < pixels.Height && pixels.Pixels[(y * pixels.Width + x) * 4 + 3] > 0) return true;
        }
        return _built && (_scroll.GetRect().HasPoint(point) || _minimap!.GetRect().HasPoint(point) ||
            GetChildren().OfType<TextureButton>().Any(button => button.GetRect().HasPoint(point)));
    }
    public override bool _HasPoint(Vector2 point) => IsBlockedPoint(point);
    public override void _GuiInput(InputEvent @event) => AcceptEvent();
    public void FocusFacility(int id) { Selection.SelectBuilding(id); World.FocusBuilding(id); }
    private void UpdateSelection()
    {
        int? id = Selection.BuildingId;
        foreach (var pair in _facilityButtons)
        {
            GameTextPresenter.Apply(pair.Value, GameTextStyles.For(GameTextRole.Facility) with
                { Foreground = pair.Key == id ? new Color("fff19c") : _facilityColors[pair.Key] });
            if (pair.Key == id) _scroll.EnsureControlVisible(pair.Value);
        }
        _message.Text = "请选择命令起点。";
    }
    private void UpdateRegion(int x, int y)
    {
        if (World.Package is not { } map) return;
        var info = RegionHoverInfo.Resolve(map.Squares[y * 200 + x], Scenario, Texts);
        _footer.Text = info.Region; _footerCity.Text = info.City;
    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion) _hoverPosition = motion.Position;
    }
    private void ClearHover() => _hoverPosition = new Vector2(-1, -1);
    private void UpdateHover(Vector2 mouse)
    {
        bool visible = false;
        if (_built && !World.IsLoading && new Rect2(0, 0, 1024, 768).HasPoint(mouse) && !IsBlockedPoint(mouse))
        {
            var world = World.ViewportToWorld(mouse);
            if (MapGeometry.TrySquare(world.X, world.Y, out int x, out int y))
            { UpdateRegion(x, y); visible = true; }
        }
        _regionVisible = visible;
        foreach (var label in _regionLabels) label.Visible = visible;
    }
    public override void _Process(double delta)
    {
        if (World.LoadError is not null) _message.Text = "地图加载失败 · F3 查看详情";
        UpdateHover(_hoverPosition ?? GetGlobalMousePosition());
        _large?.SetPressedNoSignal(World.ZoomFactor >= .75f); _small?.SetPressedNoSignal(World.ZoomFactor < .75f);
        _territory?.SetPressedNoSignal(World.ShowBoundaries);
        QueueRedraw();
    }
    private void Strip(string role, Vector2 position, int width)
    {
        var left = _textures[role + "-left"]; var middle = _textures[role + "-middle"]; var right = _textures[role + "-right"];
        var color = Colors.White;
        DrawTexture(left, position, color);
        int start = left.GetWidth(), end = width - right.GetWidth();
        for (int x = start; x < end; x += middle.GetWidth())
        {
            int w = Math.Min(middle.GetWidth(), end - x);
            DrawTextureRectRegion(middle, new(position + new Vector2(x, 0), new Vector2(w, middle.GetHeight())), new(0, 0, w, middle.GetHeight()), color);
        }
        DrawTexture(right, position + new Vector2(end, 0), color);
    }
    public override void _Draw()
    {
        if (!_built) return;
        foreach (var (asset, point) in _frames) DrawTexture(_textures[asset], point);
        DrawTexture(_textures["season-spring"], new(26, 31));
        Strip("field", new(377, 15), 88); Strip("field", new(457, 15), 112); Strip("field", new(561, 15), 112);
        var bounds = MessagePanelGeometry.Bounds(_message.Text);
        Strip("message", new(bounds.X, 736), bounds.Width);
        if (_regionVisible) DrawTexture(_textures["region-frame"], new(824, 738));
    }
    public override void _ExitTree()
    {
        World.MapReady -= Build;
        GetWindow().MouseExited -= ClearHover;
        if (Selection is not null) Selection.Changed -= UpdateSelection;
        foreach (var texture in _textures.Values) texture.Dispose();
        _text.Dispose();
    }

}
