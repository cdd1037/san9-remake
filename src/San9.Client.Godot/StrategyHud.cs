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
    public San9.Application.GameSession Session { get; init; } = null!;
    public San9.Application.Presentation.StrategySnapshot Scenario => Session.View;
    public TextCatalog Texts { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public IReadOnlyDictionary<string, PngImage> SkinImages { get; init; } = null!;
    private readonly Dictionary<string, ImageTexture> _textures = [];
    private readonly List<(string Asset, Vector2 Position)> _frames = [];
    private Label _footer = null!;
    private GameMessageBar _message = null!;
    private Label _footerCity = null!;
    private readonly List<Label> _regionLabels = [];
    private bool _regionVisible;
    private Vector2? _hoverPosition;
    private StrategyRosterList _rosterList = null!;
    private PopupMenu _contextMenu = null!;
    private StrategyMinimap? _minimap;
    public StrategyOverview Overview { get; } = new();
    public InformationPanel Information { get; private set; } = null!;
    private StrategyOverviewPanel _overviewPanel = null!;
    private bool _built;
    private long _stateRevision = -1;
    private readonly Dictionary<string,Label> _resourceValues = [];
    public string DisplayedMoney => _resourceValues.TryGetValue("资金", out var label) ? label.Text : "";
    private Label _year = null!, _date = null!;
    private TextureButton? _large, _small, _territory, _facilities, _officers;
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        Name = "StrategyHud"; Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        World.MapReady += Build;
        GetWindow().MouseExited += ClearHover;
        _message = new GameMessageBar { Textures = _textures, Text = "正在准备190年地图…" };
        AddChild(_message);
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
        var force = p.Forces.Single(f => f.Id == p.PlayerForceId);
        _year = LabelAt($"{p.Year}年", new(104, 11), new(66, 32), 24);
        _date = LabelAt($"{p.Month}月上旬", new(182, 11), new(89, 32), 24);
        LabelAt(Texts.Resolve(force.NameKey), new(280, 11), new(92, 32), 24, HorizontalAlignment.Left);
        AddResourceField("信望", force.Trust.ToString(), 385, 425, 32);
        AddResourceField("资金", force.Money.ToString(), 465, 505, 56);
        AddResourceField("兵粮", force.Food.ToString(), 569, 609, 56);
        _text.Apply(LabelAt("春", new(26, 40), new(16, 16), 16), GameTextStyles.For(GameTextRole.Spring));
        _territory = AddButton("疆界", "button-territory", new(694, 10), new(64, 36), true, () => World.ShowBoundaries = !World.ShowBoundaries);
        _territory.ToggleMode = true; _territory.SetPressedNoSignal(World.ShowBoundaries);
        AddButton("情报", "button-information", new(842, 7), new(80, 40), true, () => Information.OpenMenu());
        AddButton("功能", "button-functions", new(929, 7), new(80, 40), false);
        AddButton("?", "button-help", new(957, 50), new(56, 40), false);
        AddButton("部队", "button-units", new(956, 208), new(56, 32), false);
        _officers = AddButton("武将", "button-officers", new(927, 238), new(56, 32), true, () => SelectRosterPage(RosterPage.Officers));
        _facilities = AddButton("设施", "button-facilities", new(899, 268), new(56, 32), true, () => SelectRosterPage(RosterPage.Facilities));
        _officers.ToggleMode = true; _facilities.ToggleMode = true; _facilities.SetPressedNoSignal(true);
        _large = AddButton("大", "button-map-mode", new(900, 627), new(44, 44), true, Overview.Close);
        _large.ToggleMode = true;
        _small = AddButton("小", "button-map-mode", new(957, 606), new(44, 44), true, Overview.Open);
        _small.ToggleMode = true;
        AddButton("进行", "button-advance", new(943, 664), new(60, 60), false);
        _minimap = new StrategyMinimap { World = World, Session = Session, Skin = Skin, Position = new(643, 31), Size = new(320, 160) };
        AddChild(_minimap);
        foreach (var (text, x, y) in new[] { ("东", 880, 154), ("西", 710, 52), ("南", 710, 154), ("北", 880, 52) })
            _text.Apply(LabelAt(text, new(x, y), new(16, 16), 16), GameTextStyles.For(GameTextRole.Compass));
        _contextMenu = new PopupMenu();
        AddChild(_contextMenu);
        foreach (string title in new[] { "进行", "情报", "储存", "载入" })
        {
            _contextMenu.AddItem(title == "情报" ? title : title + "（尚未实现）");
            _contextMenu.SetItemDisabled(_contextMenu.ItemCount - 1, title != "情报");
        }
        _contextMenu.IdPressed += id => { if (id == 1) Information.OpenMenu(); };
        _rosterList = new StrategyRosterList { Position = new(892, 303), Textures = _textures,
            Texts = Texts, Skin = Skin, Activated = ActivateRosterEntry, ContextRequested = ShowContextMenu };
        AddChild(_rosterList);
        _rosterList.Refresh(Scenario, Scenario.PlayerForceId);
        _overviewPanel = new StrategyOverviewPanel { State = Overview, World = World, Session = Session,
            Skin = Skin, Texts = Texts, Textures = _textures, Visible = false };
        GetParent().AddChild(_overviewPanel);
        GetParent().MoveChild(_overviewPanel, GetIndex());
        Information = new InformationPanel { Session = Session, Texts = Texts, Textures = _textures, Locate = id => { Overview.Close(); World.FocusBuilding(id); } };
        GetParent().AddChild(Information);
        UpdateSelection(); QueueRedraw();
    }

    private void AddResourceField(string title, string value, int titleX, int valueX, int valueWidth)
    {
        // Center the font's line metrics in the background, not in an original bitmap glyph cell.
        LabelAt(title, new(titleX, 15), new(32, 24), 16, HorizontalAlignment.Left);
        _resourceValues.Add(title, LabelAt(value, new(valueX, 15), new(valueWidth, 24), 16, HorizontalAlignment.Right));
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
                "button-map-mode" => GameButtonTextRole.MapMode,
                "button-advance" => GameButtonTextRole.Advance,
                "button-facilities" => GameButtonTextRole.Facilities,
                "button-officers" => GameButtonTextRole.Officers,
                "button-units" => GameButtonTextRole.Units,
                _ => throw new ArgumentOutOfRangeException(nameof(asset))
            },
            Frames = Enumerable.Range(0, asset == "button-territory" ? 6 : 4)
                .Select(frame => (Texture2D)_textures[frame == 0 ? asset : $"{asset}-{frame}"]).ToArray(),
            StretchMode = TextureButton.StretchModeEnum.Scale, Disabled = !enabled,
            HintText = Texts.Resolve(StrategyHintText.Key(title switch
            {
                "大" => "map-large", "小" => "map-small",
                _ => asset["button-".Length..]
            })), MouseFilter = MouseFilterEnum.Stop };
        if (action is not null) button.Pressed += () => action();
        AddChild(button);
        return button;
    }

    public bool IsBlockedPoint(Vector2 point)
    {
        if (Information?.State.IsOpen == true) return true;
        if (point.Y >= 740 && point.Y < 768 && point.X >= 0 && point.X < 1024) return true;
        foreach (var (asset, position) in _frames)
        {
            var pixels = SkinImages[Skin.Roles[asset]];
            int x = (int)Math.Floor(point.X - position.X), y = (int)Math.Floor(point.Y - position.Y);
            if (x >= 0 && y >= 0 && x < pixels.Width && y < pixels.Height && pixels.Pixels[(y * pixels.Width + x) * 4 + 3] > 0) return true;
        }
        return _built && (_rosterList.GetRect().HasPoint(point) || _minimap!.GetRect().HasPoint(point) ||
            GetChildren().OfType<TextureButton>().Any(button => button.GetRect().HasPoint(point)));
    }
    public override bool _HasPoint(Vector2 point) => IsBlockedPoint(point);
    public override void _GuiInput(InputEvent @event)
    {
        if (_built && @event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right }) ShowContextMenu();
        AcceptEvent();
    }
    private void ShowContextMenu()
    {
        var point = GetViewport().GetScreenTransform() * GetGlobalMousePosition();
        _contextMenu.Position = new((int)point.X, (int)point.Y);
        _contextMenu.Popup();
    }
    private void SelectRosterPage(RosterPage page)
    {
        _rosterList.SelectPage(page);
        _facilities!.SetPressedNoSignal(page == RosterPage.Facilities);
        _officers!.SetPressedNoSignal(page == RosterPage.Officers);
    }
    private void ActivateRosterEntry(RosterKey key)
    {
        if (_rosterList.Roster.Find(key) is { } entry) World.FocusBuilding(entry.SettlementId);
    }
    public void FocusFacility(int id) => World.FocusBuilding(id);
    private void UpdateSelection() => UpdateMessage();
    public string MessageText => _message.Text;
    private void UpdateMessage()
    {
        if (World.LoadError is not null) { _message.Text = "地图加载失败 · F3 查看详情"; return; }
        if (!_built) return;
        if (Information?.State.IsOpen == true)
        {
            var menu = Information.HoverMenuButton;
            _message.SetMessage(menu?.HintText ?? "", menu is null || !menu.Disabled);
            return;
        }
        var button = GetChildren().OfType<StrategyButton>().FirstOrDefault(b => b.IsHovered())
            ?? (Overview.IsOpen ? _overviewPanel.HoverButton : null);
        _message.SetMessage(button?.HintText ?? (Overview.IsOpen ? "" : "请选择命令起点。"), button is null || !button.Disabled);
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
            if (Overview.IsOpen)
            {
                if (_overviewPanel.TrySquare(mouse, out int x, out int y))
                { UpdateRegion(x, y); visible = true; }
            }
            else
            {
                var world = World.ViewportToWorld(mouse);
                if (MapGeometry.TrySquare(world.X, world.Y, out int x, out int y))
                { UpdateRegion(x, y); visible = true; }
            }
        }
        _regionVisible = visible;
        foreach (var label in _regionLabels) label.Visible = visible;
    }
    public override void _Process(double delta)
    {
        if (World.LoadError is not null) _message.Text = "地图加载失败 · F3 查看详情";
        if (_built && _stateRevision != Session.Snapshot.Revision)
        {
            _stateRevision = Session.Snapshot.Revision;
            var view=Session.View; var force=view.Forces.Single(f => f.Id==view.PlayerForceId);
            _resourceValues["资金"].Text=force.Money.ToString(); _resourceValues["兵粮"].Text=force.Food.ToString(); _resourceValues["信望"].Text=force.Trust.ToString();
            _year.Text=$"{view.Year}年"; _date.Text=$"{view.Month}月{(view.Day==1?"上旬":view.Day==11?"中旬":"下旬")}";
            _rosterList.Refresh(view, view.PlayerForceId);
            UpdateSelection();
        }
        UpdateHover(_hoverPosition ?? GetGlobalMousePosition());
        UpdateMessage();
        _large?.SetPressedNoSignal(!Overview.IsOpen); _small?.SetPressedNoSignal(Overview.IsOpen);
        if (_territory is not null) _territory.Disabled = Overview.IsOpen;
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
        if (_regionVisible) DrawTexture(_textures["region-frame"], new(824, 738));
    }
    public override void _ExitTree()
    {
        World.MapReady -= Build;
        GetWindow().MouseExited -= ClearHover;
        foreach (var texture in _textures.Values) texture.Dispose();
        _text.Dispose();
    }

}
