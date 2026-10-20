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
    public MapMessageLog RecordsLog { get; init; } = null!;
    public San9.Application.Presentation.StrategySnapshot Scenario => World.View;
    public TextCatalog Texts { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextureService Textures { get; init; } = null!;
    public ISceneMedia SceneMedia { get; init; } = new SilentSceneMedia();
    public Action? StartNewGame { get; init; }
    private TextureScope _textures = null!;
    public SceneMusicPlayer Music { get; init; } = null!;
    private readonly List<(string Asset, Vector2 Position)> _frames = [];
    private Label _footer = null!;
    private GameMessageBar _message = null!;
    private GameDialog _notice = null!;
    private Label _footerCity = null!;
    private readonly List<Label> _regionLabels = [];
    private bool _regionVisible;
    private (bool Strategy, bool Targeting, bool Command, bool Region)? _drawState;
    private Vector2? _hoverPosition;
    private StrategyRosterList _rosterList = null!;
    private MapContextPopup _mapContext = null!;
    private MapCommandPopup _commandMenu = null!;
    public StrategyOverlayHost OverlayHost { get; init; } = null!;
    public Node BackdropHost { get; init; } = null!;
    private MapObjectTarget? _commandTarget;
    private StrategyMinimap? _minimap;
    public StrategyOverview Overview { get; } = new();
    public InformationPanel Information { get; private set; } = null!;
    public SystemMenuPanel SystemMenu { get; private set; } = null!;
    public Action? StrategyReady { get; init; }
    public Persistence.SlotSaveStore SaveStore { get; init; } = null!;
    public Func<string, Persistence.SaveSlotSummary> SaveSummary { get; init; } = null!;
    public Action<San9.Application.GameSession> ActivateSession { get; init; } = null!;
    public bool SoundEnabled => ReadSettings().SoundEnabled;
    public Func<Settings.UserSettings> ReadSettings { get; init; } = () => Settings.UserSettings.Default;
    public Action<Settings.UserSettings> ApplySettings { get; init; } = _ => { };
    private TextureButton _functionsButton = null!;
    private StrategyOverviewPanel _overviewPanel = null!;
    private bool _built;
    private MapHoverCard _hoverCard = null!;
    private long _hoverRevision = -1;
    private MapInformationContext _hoverContext;
    private long _stateRevision = -1;
    private readonly Dictionary<string,Label> _resourceValues = [];
    public string DisplayedMoney => _resourceValues.TryGetValue("资金", out var label) ? label.Text : "";
    private Label _year = null!, _date = null!, _season = null!;
    private TextureButton? _large, _small, _territory, _facilities, _officers, _units;
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        _textures = Textures.CreateScope();
        _textures.RequestPrefixes("button-", "top-", "radar-", "sidebar-", "ruler-", "field-", "region-",
            "season-", "turn-", "target-", "hover-", "icon-", "minimap-",
            "roster-", "scroll-", "message-", "dialog-", "narration-", "command-", "information-menu-", "shortcut-");
        Name = "StrategyHud"; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Stop;
        BuildLayout();
        World.MapReady += Build;
        GetWindow().MouseExited += ClearHover;
        _message = new GameMessageBar { Textures = _textures, Text = "正在准备190年地图…" };
        _bottomBar.AddChild(_message);
        _message.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _notice = new GameDialog { Textures = _textures, ZIndex = 60, Visible = false };
        _notice.Closed += () => _notice.Hide();
        AddChild(_notice);
        _notice.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        World.EdgeScrollModeChanged += ShowScrollModeNotice;
        Label RegionLabel(string value, int x, int width)
        {
            var label = LabelAt(value, new(x, 744), new(width, 16), 16, parent: _bottomRight);
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
        Turn = new(Session)
        {
            StrategyReady = StrategyReady,
            ExecutionTitleStarted = () => _sceneSounds.Play(CommandPresentation.MapAudio!.ExecutionTitleSound, SoundEnabled),
            StrategyTitleStarted = () =>
            {
                if (CommandPresentation.Progress is { } progress) _sceneSounds.Play(progress.StrategyTitleSound, SoundEnabled);
            },
            CanContinue = publication => _executionPublication == publication &&
                Interaction.CanContinue,
            PresentSeasonChange = PresentSeasonChange,
            CanPlayDay = () => Interaction.CanPlay && _textures.IsReady && _speechTextures.IsReady && _battleView.Prepare(Turn.Day) && ReferenceEquals(World.Day, Turn.Day) && World.IsDayReady && _executionPanels.IsExpanded,
            PresentDay = PresentBattleDay,
            RetryPresentation = () => _battleView.RetryResources(),
            CreateDay = (before, after, events) => new(before, after, events, new MapDayPlan(World.Package!, before, events),
                battle: BattlePresentationPlan.Create(before, after, events, Session.Messages(Texts, before.World), Texts, CommandPresentation,
                    World.Package!.Buildings.ToDictionary(b => b.Id, b => b.FixedType), World.Package,
                    ReadSettings().TacticScenesEnabled, ReadSettings().DuelScenesEnabled))
            {
                CanPresentAttack = attack => World.Display?.Presented is { } frame &&
                    MapDayPlan.IsAttackVisible(attack, frame.ViewportBounds.Position.X, frame.ViewportBounds.Position.Y,
                        frame.ViewportBounds.Size.X, frame.ViewportBounds.Size.Y)
            }
        };
        var p = Scenario;
        _frames.Add(("top-frame", Vector2.Zero));
        _frames.Add(("radar-frame", new(624, 0)));
        _frames.Add(("sidebar-frame", new(836, 167)));
        _frames.Add(("ruler-frame", new(275, 11)));
        var force = p.Forces.Single(f => f.Id == p.PlayerForceId);
        _year = LabelAt($"{p.Year}年", new(104, 11), new(66, 32), 24);
        _date = LabelAt($"{p.Month}月上旬", new(182, 11), new(89, 32), 24);
        _strategicHeader.Add(LabelAt(Texts.Resolve(force.NameKey), new(280, 11), new(92, 32), 24, HorizontalAlignment.Left));
        AddResourceField("信望", force.Trust.ToString(), 385, 425, 32);
        AddResourceField("资金", (Scenario.PlayerCorps?.Treasury.Money ?? 0).ToString(), 465, 505, 56);
        AddResourceField("兵粮", (Scenario.PlayerCorps?.Treasury.Food ?? 0).ToString(), 569, 609, 56);
        UpdateResources(p);
        _season = LabelAt("春", new(32, 38), new(16, 16), 16);
        _text.Apply(_season, GameTextStyles.For(GameTextRole.Spring));
        _territory = AddButton("疆界", "button-territory", new(694, 10), new(64, 36), true, () => World.ShowBoundaries = !World.ShowBoundaries, parent: _topRight);
        _territory.ToggleMode = true; _territory.SetPressedNoSignal(World.ShowBoundaries);
        _informationButton = AddButton("情报", "button-information", new(842, 7), new(80, 40), true, () => Information.OpenMenu(), parent: _topRight);
        _functionsButton = AddButton("功能", "button-functions", new(929, 7), new(80, 40), true, () => SystemMenu.OpenMenu(), parent: _topRight);
        AddButton("?", "button-help", new(957, 50), new(56, 40), false, parent: _topRight);
        _units = AddButton("部队", "button-units", new(956, 208), new(56, 32), true, () => SelectRosterPage(RosterPage.Units), parent: _sidebarMotion);
        _units.ToggleMode = true;
        _officers = AddButton("武将", "button-officers", new(927, 238), new(56, 32), true, () => SelectRosterPage(RosterPage.Officers), parent: _sidebarMotion);
        _facilities = AddButton("设施", "button-facilities", new(899, 268), new(56, 32), true, () => SelectRosterPage(RosterPage.Facilities), parent: _sidebarMotion);
        _officers.ToggleMode = true; _facilities.ToggleMode = true; _facilities.SetPressedNoSignal(true);
        _large = AddButton("大", "button-map-mode", new(900, 627), new(44, 44), true, Overview.Close, parent: _sidebarMotion);
        _large.ToggleMode = true;
        _small = AddButton("小", "button-map-mode", new(957, 606), new(44, 44), true, Overview.Open, parent: _sidebarMotion);
        _small.ToggleMode = true;
        _advanceButton = AddButton("进行", "button-advance", new(943, 664), new(60, 60), true, RequestAdvance, parent: _sidebarMotion);
        _minimap = new StrategyMinimap { World = World, Session = Session, Skin = Skin, Textures = _textures, Position = new(643, 31), Size = new(320, 160) };
        _topRight.AddChild(_minimap);
        foreach (var (text, x, y) in new[] { ("东", 880, 154), ("西", 710, 52), ("南", 710, 154), ("北", 880, 52) })
            _text.Apply(LabelAt(text, new(x, y), new(16, 16), 16, parent: _topRight), GameTextStyles.For(GameTextRole.Compass));
        _rosterList = new StrategyRosterList
        {
            Position = new(892, 303),
            Textures = _textures,
            Texts = Texts,
            Skin = Skin,
            Activated = ActivateRosterEntry,
            ContextRequested = ShowContextMenu
        };
        _sidebarMotion.AddChild(_rosterList);
        _rosterList.Refresh(Scenario);
        _overviewPanel = new StrategyOverviewPanel
        {
            State = Overview,
            World = World,
            Session = Session,
            Skin = Skin,
            Texts = Texts,
            Service = Textures,
            Visible = false
        };
        BackdropHost.AddChild(_overviewPanel);
        Information = new InformationPanel
        {
            Session = Session,
            Texts = Texts,
            Service = Textures,
            MenuAnchor = _informationButton,
            World = World,
            Skin = Skin,
            RecordsLog = RecordsLog,
            Chronicle = CommandPresentation.Chronicle ?? throw new InvalidDataException("Missing chronicle presentation."),
            ChangeReportMode = mode => ApplyUserSettings(ReadSettings() with { ProgressReport = mode }),
            Locate = id => { Overview.Close(); World.FocusBuilding(id); }
        };
        OverlayHost.AddChild(Information);
        SystemMenu = new SystemMenuPanel
        {
            Service = Textures,
            MenuAnchor = _functionsButton,
            Texts = Texts,
            Store = SaveStore,
            Session = Session,
            CreateSummary = SaveSummary,
            ActivateSession = ActivateSession,
            ReadSettings = ReadSettings,
            ApplySettings = ApplyUserSettings
        };
        OverlayHost.AddChild(SystemMenu);
        SystemMenu.VisibilityChanged += () => FocusBehaviorRecursive = SystemMenu.Visible
            ? FocusBehaviorRecursiveEnum.Disabled : FocusBehaviorRecursiveEnum.Inherited;
        _mapContext = new MapContextPopup { Textures = _textures };
        OverlayHost.AddChild(_mapContext);
        _commandMenu = new MapCommandPopup { Obstacles = MapObstacles, Textures = _textures, Texts = Texts, Activated = ActivateCommand };
        OverlayHost.AddChild(_commandMenu);
        World.BuildingContextRequested += ShowSettlementContext;
        World.UnitContextRequested += ShowUnitContext;
        World.EmptyContextRequested += ShowShortcutMenu;
        World.CommandMenuRequested += ShowCommandMenu;
        _hoverCard = new MapHoverCard { Obstacles = MapObstacles, World = World, Textures = _textures, UiFont = GetThemeFont("font"), Visible = false };
        AddChild(_hoverCard);
        BuildTurnControls();
        BuildTargetControls();
        // Adding controls can trigger hit testing before the HUD is fully constructed.
        _built = true;
        BuildSidebar();
        UpdateSelection(); QueueRedraw();
    }

    private void AddResourceField(string title, string value, int titleX, int valueX, int valueWidth)
    {
        // Center the font's line metrics in the background, not in an original bitmap glyph cell.
        _strategicHeader.Add(LabelAt(title, new(titleX, 15), new(32, 24), 16, HorizontalAlignment.Left));
        var label = LabelAt(value, new(valueX, 15), new(valueWidth, 24), 16, HorizontalAlignment.Right);
        _strategicHeader.Add(label);
        _resourceValues.Add(title, label);
    }

    private void UpdateResources(StrategySnapshot view)
    {
        var economy = view.PlayerEconomicOutlook;
        var money = _resourceValues["资金"];
        var food = _resourceValues["兵粮"];
        money.Text = (view.PlayerCorps?.Treasury.Money ?? 0).ToString();
        food.Text = (view.PlayerCorps?.Treasury.Food ?? 0).ToString();
        _text.Apply(money, GameTextStyles.Resource(economy?.Money.Sufficient ?? true));
        _text.Apply(food, GameTextStyles.Resource(economy?.Food.Sufficient ?? true));
        _resourceValues["信望"].Text = (view.Forces.SingleOrDefault(f => f.Id == view.PlayerForceId)?.Trust ?? 0).ToString();
    }

    private Label LabelAt(string text, Vector2 position, Vector2 size, int fontSize, HorizontalAlignment alignment = HorizontalAlignment.Center, Control? parent = null)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = size,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        (parent ?? _topLeft).AddChild(label);
        _text.Apply(label, GameTextStyles.For(fontSize == 24 ? GameTextRole.Header : GameTextRole.Resource));
        return label;
    }

    private TextureButton AddButton(string title, string asset, Vector2 position, Vector2 size, bool enabled, Action? action = null, Control? parent = null)
    {
        var button = new StrategyButton
        {
            Title = title,
            Position = position,
            Size = size,
            IgnoreTextureSize = true,
            Territory = asset == "button-territory",
            TextRole = asset switch
            {
                "button-information" => GameButtonTextRole.Information,
                "button-functions" => GameButtonTextRole.Functions,
                "button-help" => GameButtonTextRole.Help,
                "button-territory" => GameButtonTextRole.Territory,
                "button-map-mode" or "button-pause" => GameButtonTextRole.MapMode,
                "button-advance" => GameButtonTextRole.Advance,
                "button-facilities" => GameButtonTextRole.Facilities,
                "button-officers" => GameButtonTextRole.Officers,
                "button-units" => GameButtonTextRole.Units,
                _ => throw new ArgumentOutOfRangeException(nameof(asset))
            },
            Frames = Enumerable.Range(0, asset == "button-territory" ? 6 : 4)
                .Select(frame => (Texture2D)_textures.Get(frame == 0 ? asset : $"{asset}-{frame}", this)).ToArray(),
            StretchMode = TextureButton.StretchModeEnum.Scale,
            Disabled = !enabled,
            HintText = Texts.ResolveRich(StrategyHintText.Key(title switch
            {
                "大" => "map-large",
                "小" => "map-small",
                _ => asset["button-".Length..]
            })),
            MouseFilter = MouseFilterEnum.Stop
        };
        if (action is not null) button.Pressed += () => action();
        (parent ?? _topLeft).AddChild(button);
        _hudButtons.Add(button);
        return button;
    }

    private void ApplyUserSettings(Settings.UserSettings draft)
    {
        ApplySettings(draft);
        if (!draft.SoundEnabled) { _sceneSounds.Stop(); _recordSounds.Stop(); }
        _executionPanels.SetReportMode(draft.ProgressReport);
    }

    private StrategyInteraction Interaction => StrategyInteraction.Resolve(_built, Turn?.Screen,
        Turn?.CanAdvance == true, _commandTargets is not null ? CommandTargetStage : _personnel is null ? _flow?.Stage ?? MilitaryCommandStage.None : PersonnelStage, Overview.IsOpen,
        OverlayHost.IsInsideTree() && OverlayHost.HasModal || _notice?.Visible == true,
        OverlayHost.Workflows.Blocked, SidebarMoving, _battleView?.IsSceneActive == true || HasMapPopup,
        _pendingNarrations.Count != 0);
    private bool IsMapInteractionBlocked => !Interaction.CanUseMap;
    public bool CanNavigateMap => Interaction.CanNavigate;
    private bool HasMapPopup => _commandMenu?.Visible == true || _mapContext?.Visible == true;

    // Navigation passes through passive artwork; object clicks retain the artwork hit masks.
    public bool IsNavigationBlocked(Vector2 point) => !CanNavigateMap || IsInteractivePoint(point);

    private bool IsInteractivePoint(Vector2 point) => _built &&
        ((_rosterList.Visible && HudRect(_rosterList).HasPoint(point)) || HudRect(_minimap!).HasPoint(point) ||
        _hudButtons.Any(button => button.IsVisibleInTree() && HudRect(button).HasPoint(point)));

    public bool IsBlockedPoint(Vector2 point)
        => IsMapInteractionBlocked || IsCoveredPoint(point);

    private bool IsCoveredPoint(Vector2 point)
    {
        if (_battleView?.IsSceneActive == true) return true;
        if (HasMapPopup) return true;
        if (_built && _executionPanels.Covers(point)) return true;
        if (point.Y >= Size.Y - 28 && point.Y < Size.Y && point.X >= 0 && point.X < Size.X) return true;
        foreach (var (asset, position) in VisibleFrames())
        {
            var pixels = Textures.Roles[asset];
            int x = (int)Math.Floor(point.X - position.X), y = (int)Math.Floor(point.Y - position.Y);
            if (Textures.Hit(pixels, x, y)) return true;
        }
        return IsInteractivePoint(point);
    }
    public override bool _HasPoint(Vector2 point) => IsBlockedPoint(point);
    public override void _GuiInput(InputEvent @event)
    {
        if (_built && @event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right }) ShowContextMenu();
        AcceptEvent();
    }
    private MapObjectTarget? _contextTarget;
    private MapObjectTarget? _hoverTarget;
    private long _contextRevision;
    private void ShowCommandMenu(MapObjectTarget target, Vector2 point)
    {
        if (!CanIssueOrders) return;
        var entries = target.IsUnit ? MapCommandMenu.Unit(Scenario, target.Id) : MapCommandMenu.Settlement(Scenario, target.Id);
        if (entries.Count == 0) return;
        _contextTarget = null;
        _commandTarget = target;
        _contextRevision = Session.Snapshot.Revision;
        if (target.IsUnit) Selection.Clear(); else Selection.SelectBuilding(target.Id);
        _mapContext.Close();
        _commandMenu.Open(entries, target.IsUnit ? MapCommandSummary.Unit(Scenario, Texts, target.Id) : MapCommandSummary.Settlement(Scenario, Texts, target.Id), point);
        World.HoverObject(null); _hoverCard.ShowInfo(null, point); _hoverTarget = null;
    }
    private void ShowSettlementContext(int id, Vector2 point)
    {
        if (!CanIssueOrders) return;
        _contextTarget = new(id, false);
        _contextRevision = Session.Snapshot.Revision;
        _commandMenu.Close();
        _mapContext.Open(ContextEntries(SettlementContextMenu.Read(Scenario, id)), point);
        World.HoverObject(null); _hoverCard.ShowInfo(null, point); _hoverTarget = null;
    }

    private void ActivateCommand(string command)
    {
        if (_commandTarget is not { } target || !CanIssueOrders) return;
        if ((target.IsUnit && command is ("attack" or "march" or "redirect-transport") || !target.IsUnit && command is ("sortie" or "build" or "transport")) &&
            CommandPresentation.Military.Count == 0)
        { _notice.SetNotice("内容包没有提供军事指令表现定义。"); _notice.Show(); return; }
        if (target.IsUnit)
        {
            if (command is not ("attack" or "march" or "redirect-transport")) return;
            _commandMenu.Close(); Overview.Close();
            _flow.OpenUnit(target.Id, command switch
            {
                "attack" => UnitCommandKind.Attack,
                "redirect-transport" => UnitCommandKind.Transport,
                _ => UnitCommandKind.March
            });
            return;
        }
        if (command is not ("sortie" or "patrol" or "build" or "transport" or "demolish" or "delegate") && !MissionDraft.Supports(command) && !FacilityCommandDraft.Supports(command) && !AppointmentDraft.Supports(command) && !PersonnelDraft.Supports(command)) return;
        _commandMenu.Close();
        Overview.Close();
        if (command == "patrol") OpenPatrol(target.Id);
        else if (command == "demolish") OpenDemolition(target.Id);
        else if (command == "delegate") OpenCorpsManagement();
        else if (MissionDraft.Supports(command)) OpenMission(target.Id,command);
        else if (PersonnelDraft.Supports(command)) OpenPersonnel(target.Id,command);
        else if (command is "build" or "transport") _flow.OpenSpecial(target.Id, command == "build" ? SpecialDeploymentKind.Construction : SpecialDeploymentKind.Transport);
        else if (AppointmentDraft.Supports(command)) OpenAppointment(target.Id, command);
        else if (FacilityCommandDraft.Supports(command)) OpenFacilityCommand(target.Id,command);
        else _flow.Open(target.Id);
    }

    private void ShowUnitContext(int id, Vector2 point)
    {
        if (!CanIssueOrders) return;
        _contextTarget = new(id, true);
        _contextRevision = Session.Snapshot.Revision;
        _commandMenu.Close();
        _mapContext.Open(ContextEntries(UnitMapInteraction.Menu(Scenario, id)), point);
        World.HoverObject(null); _hoverCard.ShowInfo(null, point); _hoverTarget = null;
    }
    private void OpenMapContextEntry(MapContextEntry entry)
    {
        if (_contextTarget is not {} target) return;
        var entries = target.IsUnit ? UnitMapInteraction.Menu(Scenario, target.Id) : SettlementContextMenu.Read(Scenario, target.Id);
        if (entries.Contains(entry) && entry.Enabled) Information.OpenContext(entry);
    }
    private MapPopupEntry[] ContextEntries(IReadOnlyList<MapContextEntry> entries) => entries
        .Select(entry => new MapPopupEntry(entry.Title, $"查看{entry.Title}。", entry.Enabled, () => OpenMapContextEntry(entry))).ToArray();
    private void ShowContextMenu() => ShowShortcutMenu(GetGlobalMousePosition());
    private void ShowShortcutMenu(Vector2 point)
    {
        if (!CanIssueOrders) return;
        _contextTarget = null;
        _contextRevision = Session.Snapshot.Revision;
        _commandMenu.Close();
        var query = new InformationQuery(Session, Texts, CommandPresentation.Chronicle);
        var information = InformationQuery.MenuCategories.Select(category => new MapPopupEntry(
            InformationQuery.MenuTitle(category), Texts.ResolveRich(query.MenuHintKey(category)), query.Available(category),
            () => Information.Open(category))).ToArray();
        _mapContext.Open([
            new("进行", Texts.ResolveRich(StrategyHintText.Key("advance")), Turn.CanAdvance, RequestAdvance, SkinPrefix: "shortcut-advance"),
            new("情报", Texts.ResolveRich(StrategyHintText.Key("information")), true, Children: information, SeparatorBefore: true),
            new("储存", Texts.ResolveRich(StrategyHintText.Key("functions-save")), true, () => SystemMenu.Open(Persistence.SaveLoadMode.Save)),
            new("载入", Texts.ResolveRich(StrategyHintText.Key("functions-load")), true, () => SystemMenu.Open(Persistence.SaveLoadMode.Load))
        ], point, 40);
        World.HoverObject(null); _hoverCard.ShowInfo(null, point); _hoverTarget = null;
    }
    private void SelectRosterPage(RosterPage page)
    {
        _rosterList.SelectPage(page);
        _facilities!.SetPressedNoSignal(page == RosterPage.Facilities);
        _officers!.SetPressedNoSignal(page == RosterPage.Officers);
        _units!.SetPressedNoSignal(page == RosterPage.Units);
    }
    private void ActivateRosterEntry(RosterKey key)
    {
        _rosterList.Refresh(Scenario);
        switch (_rosterList.Roster.Find(key)?.Location)
        {
            case RosterSettlementLocation settlement: World.FocusBuilding(settlement.Id); break;
            case RosterUnitLocation unit: World.FocusUnit(unit.Id); break;
        }
    }
    public void FocusFacility(int id) => World.FocusBuilding(id);
    private void UpdateSelection() => UpdateMessage();
    public string MessageText => _message.Text;
    private void UpdateMessage()
    {
        if (World.LoadError is not null) { _message.Text = "地图加载失败 · F3 查看详情"; return; }
        if (!_built) return;
        if (Turn.Screen != TurnScreen.Strategy) { _message.Text = ""; return; }
        if (SystemMenu?.Visible == true)
        {
            var menu = SystemMenu.HoverMenuButton;
            _message.SetMessage(menu?.HintText ?? "", menu is null || !menu.Disabled);
            return;
        }
        if (Information?.State.IsOpen == true)
        {
            var menu = Information.HoverMenuButton;
            _message.SetMessage(menu?.HintText ?? "", menu is null || !menu.Disabled);
            return;
        }
        if (_commandMenu.Visible) { _message.SetMessage(_commandMenu.Hint, _commandMenu.HintEnabled); return; }
        if (_mapContext.Visible) { _message.SetMessage(_mapContext.Hint, _mapContext.HintEnabled); return; }
        if (_personnelTargeting) { _message.SetMessage(_personnel?.Draft.Kind == "search" ? "请选择目标地域。" : "请选择目标设施。",true); return; }
        if (_commandTargets is not null) { _message.SetMessage("请选择目标。",true); return; }
        if (_targets is not null)
        {
            string initial = _targets is SpecialDeploymentTargets { IsConstruction:true }
                ? Texts.ResolveRich(San9.Content.Interface.SpecialDeploymentText.Key("site")) : "请选择目标。";
            _message.SetMessage(_targets.Selected is null ? initial : "点击空地设置中继点，点击其他目标切换，再次点击当前目标设定方针。",true);
            return;
        }
        var button = _hudButtons.FirstOrDefault(b => b.Visible && b.IsHovered())
            ?? (Overview.IsOpen ? _overviewPanel.HoverButton : null);
        _message.SetMessage(button?.HintText ?? (Overview.IsOpen ? "" : "请选择命令起点。"), button is null || !button.Disabled);
    }
    private void UpdateRegion(int x, int y)
    {
        if (World.Package is not { } map) return;
        var info = RegionHoverInfo.Resolve(map.Squares[y * 200 + x], Scenario, Texts);
        _footer.Text = info.Region; _footerCity.Text = info.City;
    }

    private void ShowScrollModeNotice(EdgeScrollMode mode)
    {
        string key = CommandPresentation.Notices[mode == EdgeScrollMode.Hover ? "scroll-hover" : "scroll-click"];
        var evaluator = new MessageEvaluator(Texts.Messages);
        string message = evaluator.Evaluate(key, new(new Dictionary<string, MessageBinding>(),
            (_, _) => throw new InvalidDataException("Scroll mode notice cannot query world state."),
            _ => throw new InvalidDataException("Scroll mode notice cannot draw randomness.")));
        _notice.SetNotice(MessageMarkup.Plain(message));
        MoveChild(_notice, GetChildCount() - 1);
        _notice.Show();
        _notice.FocusDefaultButton();
    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion) _hoverPosition = motion.Position;
        HandleMapSpeechInput(@event);
    }
    private void ClearHover() => _hoverPosition = new Vector2(-1, -1);
    private void UpdateHover(Vector2 mouse)
    {
        var context = Interaction.InformationContext;
        bool visible = false;
        MapObjectTarget? hovered = null;
        if (_built && Interaction.CanInspectMap && World.IsInspectionBlocked?.Invoke(mouse) != true && !World.IsLoading && World.PresentedViewportBounds.HasPoint(mouse) && !IsCoveredPoint(mouse) && !IsSpeechPoint(mouse))
        {
            if (Overview.IsOpen)
            {
                if (_overviewPanel.TrySquare(_overviewPanel.GetGlobalTransform().AffineInverse() * mouse, out int x, out int y))
                { UpdateRegion(x, y); visible = true; }
            }
            else
            {
                hovered = World.ObjectAtViewport(mouse);
                var world = World.ViewportToWorld(mouse);
                if (MapGeometry.TrySquare(world.X, world.Y, out int x, out int y))
                { UpdateRegion(x, y); visible = true; }
            }
        }
        World.HoverObject(hovered);
        if (_built && (_hoverTarget != hovered || _hoverRevision != Session.Snapshot.Revision || _hoverContext != context))
        {
            _hoverRevision = Session.Snapshot.Revision;
            _hoverContext = context;
            if (_hoverTarget != hovered) _hoverCard.ShowInfo(null, mouse);
            _hoverTarget = hovered;
            _hoverCard.ShowInfo(hovered is {} target ? target.IsUnit ? UnitMapInteraction.Hover(Scenario, Texts, target.Id) : MapHoverInfo.Resolve(Scenario, Texts, target.Id, context) : null, mouse);
        }
        _regionVisible = visible;
        foreach (var label in _regionLabels) label.Visible = visible;
    }
    public override void _Process(double delta)
    {
        if (!_built && World.IsVisibleReady) Build();
        if (_built) UpdateTurn(delta);
        if (_commandTargets is {} request && request.Selection.View.World.Revision != Session.Snapshot.Revision) EndCommandTargets();
        if (_commandMenu?.Visible == true && _contextRevision != Session.Snapshot.Revision) _commandMenu.Close();
        if (_mapContext?.Visible == true && _contextRevision != Session.Snapshot.Revision) _mapContext.Close();
        if (World.LoadError is not null) _message.Text = "地图加载失败 · F3 查看详情";
        if (_built && _stateRevision != Session.Snapshot.Revision)
        {
            _stateRevision = Session.Snapshot.Revision;
            var view = Session.View;
            UpdateResources(view);
            _rosterList.Refresh(view);
            UpdateSelection();
        }
        if (_built)
        {
            _minimap!.SetCommandSource(_flow.Source ?? PatrolSource ?? FacilitySource ?? DemolitionSource ?? AppointmentSource ?? PersonnelSource ?? (_commandMenu!.Visible ? _commandTarget : null));
            UpdateSidebar(delta);
        }
        UpdateHover(_hoverPosition ?? GetGlobalMousePosition());
        UpdateMessage();
        _large?.SetPressedNoSignal(!Overview.IsOpen); _small?.SetPressedNoSignal(Overview.IsOpen);
        if (_territory is not null) _territory.Disabled = Overview.IsOpen;
        _territory?.SetPressedNoSignal(World.ShowBoundaries);
        var drawState = (Turn?.IsStrategy == true, SelectingTargets, _commandMenu?.Visible == true, _regionVisible);
        if (_drawState != drawState) { _drawState = drawState; QueueRedraw(); }
    }
    private void Strip(string role, Vector2 position, int width)
    {
        var left = _textures.Get(role + "-left", this);
        SkinPainter.Strip(this, left, _textures.Get(role + "-middle", this), _textures.Get(role + "-right", this),
            new(position, new(width, left.GetHeight())));
    }
    public override void _Draw()
    {
        if (!_built) return;
        foreach (var (asset, point) in VisibleFrames())
        {
            DrawTexture(_textures.Get(asset, this), point);
        }
        if (!SelectingTargets) DrawTexture(_textures.Get("season-" + _badgeSeason, this), new(26, 31),
            new Color(_badgeBrightness, _badgeBrightness, _badgeBrightness, 1));
        if (Turn.IsStrategy && !SelectingTargets) { Strip("field", new(377, 15), 88); Strip("field", new(457, 15), 112); Strip("field", new(561, 15), 112); }
        if (_regionVisible) DrawTexture(_textures.Get("region-frame", this), GroupPosition(_bottomRight) + new Vector2(824, 738));
    }
    public override void _ExitTree()
    {
        Music.SetMapTransitionGain(1);
        World.MapReady -= Build;
        World.EdgeScrollModeChanged -= ShowScrollModeNotice;
        World.BuildingContextRequested -= ShowSettlementContext;
        World.UnitContextRequested -= ShowUnitContext;
        World.EmptyContextRequested -= ShowShortcutMenu;
        World.CommandMenuRequested -= ShowCommandMenu;
        GetWindow().MouseExited -= ClearHover;
        _textures.Dispose();
        _speechTextures?.Dispose();
        _text.Dispose();
        _titleFlashMaterial.Dispose();
    }

}
