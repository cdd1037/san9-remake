using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Edits a local preparation; only the destination workflow submits a deployment.</summary>
public partial class DeploymentView : FixedPage
{
    private const int RowHeight = 40;
    private const int FormationRowY = 228;
    private const int ShipRowY = FormationRowY + RowHeight;
    private const int SoldiersRowY = ShipRowY + RowHeight;
    private const int MoneyRowY = SoldiersRowY + RowHeight;
    private const int MoraleRowY = MoneyRowY + RowHeight;
    private const int CombatHeaderY = MoraleRowY + 48;
    private const int CombatStartY = CombatHeaderY + 32;
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public DeploymentDraft Draft { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Proceed { get; init; } = null!;
    private Control? _body, _child;
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _fieldStyle = null!;
    private float _facilityWidth;
    private CommandWindow? _window;
    private DeploymentPresetBar? _presetBar;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        var field = Textures.Get("deployment-field", this);
        _fieldStyle = new StyleBoxTexture { Texture = field,
            TextureMarginLeft = 16, TextureMarginRight = 16, TextureMarginTop = 8, TextureMarginBottom = 8,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
        Rebuild();
    }
    private Label Label(string value, Rect2 bounds, int size = 16, HorizontalAlignment alignment = HorizontalAlignment.Center, GameTextStyle? style = null)
    {
        var label = new Label
        {
            Text = value,
            Position = bounds.Position,
            Size = bounds.Size,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true
        };
        _body!.AddChild(label); _text.Apply(label, style ?? GameTextStyles.CatalogRow with { FontSize = size }); return label;
    }
    private void Field(string value, Rect2 bounds) => Label(value, new(bounds.Position + new Vector2(4, 0), bounds.Size - new Vector2(8, 0)),
        alignment: HorizontalAlignment.Right, style: new GameTextStyle(16, new Color("f0f0f0"), GameTextEffect.None, Colors.Transparent));
    private InformationMenuButton Button(string title, string skin, Rect2 bounds, Action action, bool enabled = true)
    {
        var button = CommandWindow.CreateActionButton(Textures, title, skin, bounds, action);
        button.Disabled = !enabled;
        _body!.AddChild(button);
        return button;
    }
    private string Person(int? id) => id is int person ? Texts.Resolve(Draft.View.Officers.Single(o => o.Id == person).NameKey) : "";
    private string Formation(int? id) => id is int formation ? Draft.View.Information.Formations.Single(f => f.Id == formation).Name : "";
    private void Rebuild()
    {
        int presetScroll = _presetBar?.ScrollPosition ?? 0;
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        _presetBar = new DeploymentPresetBar
        {
            Draft = Draft, Presets = Session.DeploymentPresets, Texts = Texts, Textures = Textures,
            InitialScroll = presetScroll, Position = new(4, 112), Size = new(88, 600),
            Activated = ApplyPresetShortcut, SaveRequested = SavePresetShortcut, UpdateRequested = UpdatePresetShortcut
        };
        _body.AddChild(_presetBar);
        if (_window is not null) { RemoveChild(_window); _window.QueueFree(); }
        _window = new CommandWindow { Texts = Texts, Textures = Textures, Position = new(136, 100), Size = new(752, 568),
            Title = "出征", ShowBehindParent = true, Closed = Closed, Accepted = Proceed, CanAccept = Draft.CanProceed,
            Help = () => Notice("选择执行武将后，可选择阵形、船和士兵。\n拖拽武将牌交换阵位，点击兵法名称或双击武将选择兵法。\n阵形名称右侧的兵法菜单可选择按当前阵形或指定类别，选中后立即配置全队。\n选阵后自动配置由优化伴侣开关控制；灰色兵法不适配当前阵形、兵种或等级。\n执行进入选目标；中止会放弃本次设置。") };
        AddChild(_window);
        var (facility, facilityWidth) = CommandPanelArt.SourcePlate("执行设施　" + Draft.View.Settlements.Single(s => s.Id == Draft.Home.Id).ResolveName(Texts));
        _facilityWidth = facilityWidth;
        Label(facility, new(136, 100, _facilityWidth, 32));
        Button("执行武将", "edit", new(136, 148, 104, 40), SelectOfficers);
        Button("常用编队", "edit", new(248, 148, 104, 40), OpenPresets);
        bool selected = !Draft.Members.IsEmpty;
        Button("阵形", "edit", new(136, FormationRowY, 104, 40), () => SelectFormation(false), selected && Draft.Formations(false).Count > 1);
        AddTacticControls(selected);
        Button("船", "edit", new(136, ShipRowY, 104, 40), () => SelectFormation(true), selected && Draft.Formations(true).Count > 1);
        if (Draft.View.World.Simulation.NewGame.MixedTroopsEnabled)
            Button("兵种", "edit", new(296, ShipRowY, 72, 40), () => OpenChild(new DeploymentDetachmentsView
            {
                Draft = Draft, Texts = Texts, Textures = Textures, Service = Service, Closed = CloseChild
            }), selected && Draft.Detachments.Count == Draft.Members.Length);
        Button("士兵", "edit", new(136, SoldiersRowY, 104, 40), SelectSoldiers, selected && Draft.MaximumSoldiers > 0);
        Label("大将", new(136, 188, 96, 32)); Field(Person(Draft.CommanderId), new(240, 188, 80, 32));
        Field(Formation(Draft.FormationId), new(240, FormationRowY, 48, 32)); Field(Formation(Draft.NavalFormationId), new(240, ShipRowY, 48, 32));
        Field($"{(selected ? Draft.Soldiers.ToString() : "")}/{Draft.Home.Soldiers}", new(240, SoldiersRowY, 120, 32));
        Label("资金", new(136, MoneyRowY, 96, 32)); Field($"{(selected ? Draft.Cost.ToString() : "")}/{Draft.Money}", new(240, MoneyRowY, 120, 32));
        Label("士气", new(136, MoraleRowY, 96, 32)); Field(Draft.Home.Morale.ToString(), new(240, MoraleRowY, 40, 32));
        Label("陆上", new(240, CombatHeaderY, 48, 24)); Label("水上", new(296, CombatHeaderY, 48, 24));
        var land = Draft.Combat(false); var water = Draft.Combat(true);
        string[] labels = ["对部队", "对守兵", "对城壁", "守备力", "机动力"];
        for (int i = 0; i < labels.Length; i++)
        { int y = CombatStartY + i * RowHeight; Label(labels[i], new(136, y, 96, 32)); Field(selected ? land[i].Text : "", new(240, y, 40, 32)); Field(selected ? water[i].Text : "", new(296, y, 40, 32)); }
        var sectionStyle = GameTextStyles.CatalogRow with { EffectColor = new Color("005028") };
        Label("布阵", new(384, 166, 48, 24), alignment: HorizontalAlignment.Left, style: sectionStyle);
        Label("拖曳武将调整阵位；点击兵法名称更换。", new(440, 166, 432, 24), alignment: HorizontalAlignment.Left);
        if (selected)
        {
            _body.AddChild(new FormationBoardView
            {
                Data = Draft.Board(Texts),
                Textures = Textures,
                Position = new(427, 189),
                Size = new(432, 264),
                OfficerActivated = SelectTactic,
                OfficerMoved = (id, slot) => { if (Draft.Move(id, slot)) CallDeferred(nameof(RebuildDeferred)); }
            });
            _body.AddChild(new FormationVisionView { Data = Draft.Presentation(false), Position = new(469, 554), Size = new(144, 144) });
            _body.AddChild(new FormationVisionView { Data = Draft.Presentation(true), Position = new(727, 554), Size = new(144, 144) });
        }
        Label("兵法发动率：排在越前方的列队数值越高。\n狙击对象　：位于前锋及中锋的武将可能遭到狙击。\n单挑对战　：发生于敌我双方的前锋。", new(440, 465, 432, 56), alignment: HorizontalAlignment.Left);
        Label("陆上视野", new(384, 555, 80, 24), alignment: HorizontalAlignment.Left, style: sectionStyle);
        Label("水上视野", new(645, 555, 80, 24), alignment: HorizontalAlignment.Left, style: sectionStyle);
        _body.AddChild(new GameMessageBar { Textures = Textures, Text = Draft.Cost > Draft.Money ? "出征资金不足。" : "命令部队出征。" });
        QueueRedraw();
    }
    private void AddTacticControls(bool selected)
    {
        InformationMenuButton menu = null!;
        menu = Button("兵法", "edit", new(296, FormationRowY, 72, 40), () => OpenChild(new DeploymentTacticMenu
        {
            AnchorControl = menu, Textures = Textures,
            Selected = mode => { Draft.ConfigureTactics(mode); CloseChild(); }, Closed = CloseChild
        }), selected);
        menu.TooltipText = "选择按当前阵形或指定类别，立即配置全队兵法。";
    }
    private void RebuildDeferred() => Rebuild();
    private void OpenPresets() => OpenChild(new DeploymentPresetView
    {
        Draft = Draft, Session = Session, Texts = Texts, Textures = Textures, Closed = CloseChild
    });
    private void ApplyPresetShortcut(DeploymentPreset preset)
    {
        var result = Draft.ApplyPreset(preset);
        if (!result.CanApply) { Notice(result.Describe(Draft.View, Texts) + "。\n当前出征配置保持原样。 "); return; }
        Rebuild();
    }
    private void SavePresetShortcut()
    {
        if (!Draft.CanProceed) return;
        string name = DeploymentPresetStyle.DefaultName(Draft, Texts);
        var preset = Draft.CreatePreset(Guid.NewGuid(), name, DeploymentSoldierMode.Fixed);
        var result = Session.Apply(Session.Request(new SaveDeploymentPresetOperation(preset)));
        if (result.Disposition != SessionDisposition.Accepted) { Notice("当前无法保存编队，请稍后重试。 "); return; }
        Draft.MarkPresetSaved(preset);
        Rebuild();
    }
    private void UpdatePresetShortcut(DeploymentPreset source)
    {
        if (source.Id != Draft.SourcePresetId || !Draft.HasPresetChanges || !Draft.CanProceed) return;
        var current = Session.DeploymentPresets.FirstOrDefault(p => p.Id == source.Id);
        if (current is null) { Notice("来源编队已删除，当前配置仍可另存为新编队。 "); return; }
        var updated = Draft.CreatePresetUpdate(current);
        var result = Session.Apply(Session.Request(new SaveDeploymentPresetOperation(updated)));
        if (result.Disposition != SessionDisposition.Accepted) { Notice("当前无法更新编队，已保留本次修改。 "); return; }
        Draft.MarkPresetSaved(updated);
        Rebuild();
    }
    private void OpenChild(Control child)
    {
        _child = child;
        if (child is DeploymentPresetView) { _body?.Hide(); _window?.Hide(); QueueRedraw(); }
        AddChild(child);
    }
    private void CloseChild()
    { if (_child is not null) { RemoveChild(_child); _child.QueueFree(); _child = null; } Rebuild(); }
    private void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = new(1024, 768) };
        dialog.Closed += CloseChild; OpenChild(dialog); dialog.SetNotice(message);
    }
    private void SelectSoldiers() => OpenChild(new NumberInputDialog
    {
        State = new(1, Draft.MaximumSoldiers, Draft.Soldiers),
        Textures = Textures,
        Confirmed = value => { Draft.SetSoldiers(value); CloseChild(); },
        Closed = CloseChild
    });
    private void SelectOfficers()
    {
        var selection = new CatalogSelection(Draft.Candidates, Draft.Members, Draft.MemberLimit);
        var personalColumns = OfficerCatalog.CorePages[3].Columns;
        int PersonalIndex(string id) => Array.FindIndex(personalColumns, c => c.Id == id);
        int affinity = PersonalIndex("affinity"), office = PersonalIndex("office"),
            capacity = PersonalIndex("capacity"), personality = PersonalIndex("personality");
        var departure = new CatalogPage("出征", [OfficerCatalog.CorePages[0].Columns[0],
            personalColumns[affinity], personalColumns[office], personalColumns[capacity],
            .. OfficerCatalog.CorePages[0].Columns[1..5], OfficerCatalog.CorePages[1].Columns[^1], personalColumns[personality]]);
        var ability = OfficerCatalog.Read(Draft.View, Texts, 0, null, Draft.Home.Id).Rows.ToDictionary(r => r.Key.Id);
        var personal = OfficerCatalog.Read(Draft.View, Texts, 3, null, Draft.Home.Id).Rows.ToDictionary(r => r.Key.Id);
        var belonging = OfficerCatalog.Read(Draft.View, Texts, 1, null, Draft.Home.Id).Rows.ToDictionary(r => r.Key.Id);
        CatalogRow[] Rows(int page)
        {
            if (page > 0) return OfficerCatalog.Read(Draft.View, Texts, page - 1, null, Draft.Home.Id).Rows.Where(r => Draft.Candidates.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells.ToArray())).ToArray();
            return Draft.Candidates.Select(id => new CatalogRow(id, [Person(id),personal[id].Cells[affinity],personal[id].Cells[office],(long)Draft.CommandCapacity(id),
                ability[id].Cells[1],ability[id].Cells[2],ability[id].Cells[3],ability[id].Cells[4],belonging[id].Cells[^1],personal[id].Cells[personality]])).ToArray();
        }
        OpenChild(new CatalogView
        {
            Title = "选择武将",
            HintText = $"选择执行武将（最多{Draft.MemberLimit}人）。",
            Pages = [departure, .. OfficerCatalog.Pages(Draft.View, Texts)],
            ReadRows = Rows,
            Service = Service,
            Textures = Textures,
            Selection = selection,
            Portrait = id => $"catalog-person-{id}",
            ConfirmSelection = ids => { if (Draft.SetMembers(ids)) CloseChild(); },
            Closed = CloseChild
        });
    }
    private void SelectFormation(bool naval)
    {
        var category = naval ? InformationCategory.Ships : InformationCategory.Formations;
        var candidates = Draft.Formations(naval).Select(f => f.Id).ToHashSet();
        bool showProficiency = !naval && Draft.View.World.Simulation.NewGame.FormationKnowledgeEnabled && Draft.CommanderId is not null;
        var commander = Draft.CommanderId is int commanderId ? Draft.View.World.Officers.Single(o => o.Id == commanderId) : null;
        var pages = ForceCapabilityCatalog.Pages(category);
        if (showProficiency)
            pages = pages.Select(page => page with { Columns = [.. page.Columns, new("formation-proficiency", "熟练", 6, CatalogAlignment.Right)] }).ToArray();
        var view = new CatalogView
        {
            Title = naval ? "选择船" : "选择阵形",
            HintText = naval ? "请选择船。" : Draft.View.World.Simulation.NewGame.FormationKnowledgeEnabled
                ? "请选择主将掌握的阵形；武将熟练度影响各自分队。" : "请选择阵形。",
            CloseTitle = "中止",
            Pages = pages,
            Textures = Textures,
            Service = Service,
            PortraitSize = new(120, 120),
            PortraitFrameRole = "catalog-formation-frame",
            Portrait = id => $"catalog-formation-{id}",
            PortraitPlaceholder = (canvas, rect, id) =>
            {
                var presentation = Draft.View.Information.Formations.FirstOrDefault(f => f.Id == id)?.Presentation;
                if (presentation is null || presentation.Columns <= 0 || presentation.Rows <= 0) return;
                var cell = rect.Size / new Vector2(presentation.Columns, presentation.Rows);
                canvas.DrawRect(rect, new Color("22292b"));
                foreach (var slot in presentation.Slots)
                {
                    var position = rect.Position + cell * new Vector2(slot.Column, slot.Row);
                    canvas.DrawRect(new Rect2(position + new Vector2(2, 2), cell - new Vector2(4, 4)), new Color("8a3030"));
                }
                for (int x = 1; x < presentation.Columns; x++)
                    canvas.DrawLine(rect.Position + new Vector2(x * cell.X, 0), rect.Position + new Vector2(x * cell.X, rect.Size.Y), new Color("82918b"));
                for (int y = 1; y < presentation.Rows; y++)
                    canvas.DrawLine(rect.Position + new Vector2(0, y * cell.Y), rect.Position + new Vector2(rect.Size.X, y * cell.Y), new Color("82918b"));
            },
            InitialCurrentId = naval ? Draft.NavalFormationId : Draft.FormationId,
            ReadRows = page => ForceCapabilityCatalog.Read(Session, Texts, category, Draft.Force.Id, page, personalFormations: showProficiency).Rows
                .Where(r => candidates.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, showProficiency
                    ? [.. r.Cells, commander?.FormationProficiencies?.TryGetValue(r.Key.Id, out int value) == true ? (CatalogCell)value : CatalogCell.Unavailable]
                    : r.Cells.ToArray())).ToArray(),
            Activated = id => { if (Draft.SetFormation(id, naval)) CloseChild(); },
            Closed = CloseChild
        };
        OpenChild(view);
    }
    private void SelectTactic(int person)
    {
        var tactics = Draft.SelectableTactics(person).Select(t => t.Id).ToHashSet();
        OpenChild(new CatalogView
        {
            Title = "选择兵法",
            HintText = "仅显示武将掌握且当前兵种和等级可使用的兵法。",
            CloseTitle = "中止",
            Pages = ForceCapabilityCatalog.Pages(InformationCategory.Tactics),
            Textures = Textures,
            Service = Service,
            InitialCurrentId = Draft.Tactics[person],
            ReadRows = page => new[] { new CatalogRow(-1, ["无", .. Enumerable.Repeat<CatalogCell>("", ForceCapabilityCatalog.Pages(InformationCategory.Tactics)[page].Columns.Length - 1)]) }
                .Concat(ForceCapabilityCatalog.Read(Session, Texts, InformationCategory.Tactics, Draft.Force.Id, page).Rows.Where(r => tactics.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells.ToArray()))).ToArray(),
            Activated = id => { if (Draft.SetTactic(person, id < 0 ? null : id)) CloseChild(); },
            Closed = CloseChild
        });
    }
    public override void _Input(InputEvent input)
    {
        if (!Visible || _child is not null) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) { GetViewport().SetInputAsHandled(); Closed(); }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        { GetViewport().SetInputAsHandled(); if (mouse.Pressed) Closed(); }
    }
    private void Tile(string role, Rect2 rect)
    {
        var texture = Textures.Get("deployment-" + role, this);
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth()) for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        { var size = new Vector2(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y)); DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size)); }
    }
    private void Strip(string role, Rect2 rect)
    {
        var left = Textures.Get("deployment-" + role + "-left", this); var right = Textures.Get("deployment-" + role + "-right", this);
        Tile(role + "-middle", new(rect.Position + new Vector2(left.GetWidth(), 0), new(rect.Size.X - left.GetWidth() - right.GetWidth(), rect.Size.Y)));
        DrawTexture(left, rect.Position); DrawTexture(right, new(rect.End.X - right.GetWidth(), rect.Position.Y));
    }
    public override void _Draw()
    {
        if (_child is DeploymentPresetView) return;
        DrawTexture(Textures.Get("formation-panel-deployment", this), new(368, 148));
        Strip("facility", new(136, 100, _facilityWidth, 32));
        foreach (int x in new[] { 240, 296 })
        {
            // These labels crop eight pixels from the outer end caps in the original skin.
            var left = Textures.Get("deployment-mode-left", this); var right = Textures.Get("deployment-mode-right", this);
            float l = left.GetWidth() - 8, r = right.GetWidth() - 8;
            DrawTextureRectRegion(left, new(x, CombatHeaderY, l, 24), new(8, 0, l, 24));
            Tile("mode-middle", new(x + l, CombatHeaderY, 48 - l - r, 24));
            DrawTextureRectRegion(right, new(x + 48 - r, CombatHeaderY, r, 24), new(0, 0, r, 24));
        }
        foreach (int y in new[] { 188, MoneyRowY, MoraleRowY }) Strip("label", new(136, y, 96, 32));
        foreach (var rect in new[] { new Rect2(240, 188, 80, 32), new(240, FormationRowY, 48, 32), new(240, ShipRowY, 48, 32), new(240, SoldiersRowY, 120, 32), new(240, MoneyRowY, 120, 32), new(240, MoraleRowY, 40, 32) }) DrawField(rect);
        for (int i = 0; i < 5; i++)
        {
            int y = CombatStartY + i * RowHeight;
            Strip("label", new(136, y, 96, 32));
            DrawField(new(240, y, 40, 32)); DrawField(new(296, y, 40, 32));
        }
        DrawLine(new(136, 140), new(888, 140), new Color("755635"), 2);
    }
    private void DrawField(Rect2 rect)
    {
        DrawStyleBox(_fieldStyle, rect);
        Tile("field-rim", new(rect.Position, new(rect.Size.X - 2, 4)));
        Tile("field-rim", new(rect.Position + new Vector2(0, rect.Size.Y - 4), new(rect.Size.X - 2, 4)));
    }
    public override void _ExitTree() { _text.Dispose(); _fieldStyle.Dispose(); }
}
