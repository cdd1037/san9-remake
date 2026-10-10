using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Preparation only. The draft remains local until a future destination workflow commits it.</summary>
public partial class DeploymentView : Control
{
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
    public override void _Ready()
    {
        Size = new(1024, 768); ZIndex = 25; MouseFilter = MouseFilterEnum.Stop;
        var field = Textures["deployment-field"];
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
    private void Button(string title, string skin, Rect2 bounds, Action action, bool enabled = true)
    {
        var button = new InformationMenuButton { Title = title, SkinPrefix = "deployment-" + skin, Textures = Textures, Position = bounds.Position, Size = bounds.Size, Disabled = !enabled,
            TextStyle = (hovered, pressed, disabled) => new GameTextStyle(skin == "edit" ? 16 : 24,
                new Color(disabled ? hovered ? "000000" : "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8"),
                GameTextEffect.Outline, new Color(disabled ? hovered ? "f0f0f0" : "000000" : hovered || pressed
                    ? skin == "edit" ? "800090" : skin == "cancel" ? "983810" : "005028" : "000000")) };
        button.Pressed += action; _body!.AddChild(button);
    }
    private string Person(int? id) => id is int person ? Texts.Resolve(Draft.View.Officers.Single(o => o.Id == person).NameKey) : "";
    private string Formation(int? id) => id is int formation ? Texts.Resolve(Draft.View.Information.Formations.Single(f => f.Id == formation).NameKey) : "";
    private void Rebuild()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        if (_window is not null) { RemoveChild(_window); _window.QueueFree(); }
        _window = new CommandWindow { Texts = Texts, Textures = Textures, Position = new(136, 100), Size = new(752, 568),
            Title = "出征", ShowBehindParent = true, Closed = Closed, Accepted = Proceed, CanAccept = Draft.CanProceed,
            Help = () => Notice("选择执行武将后，可选择阵形、船和士兵。\n拖拽武将牌交换阵位，双击选择兵法。\n执行进入选目标；中止会放弃本次设置。") };
        AddChild(_window);
        string facility = "执行设施　" + Draft.View.Settlements.Single(s => s.Id == Draft.Home.Id).ResolveName(Texts);
        _facilityWidth = facility.Sum(c => c < 128 ? 8 : 16) + 32;
        Label(facility, new(136, 100, _facilityWidth, 32));
        Button("执行武将", "edit", new(136, 148, 104, 40), SelectOfficers);
        bool selected = !Draft.Members.IsEmpty;
        Button("阵形", "edit", new(136, 228, 104, 40), () => SelectFormation(false), selected && Draft.Formations(false).Count > 1);
        Button("船", "edit", new(136, 268, 104, 40), () => SelectFormation(true), selected && Draft.Formations(true).Count > 1);
        Button("士兵", "edit", new(136, 308, 104, 40), SelectSoldiers, selected && Draft.MaximumSoldiers > 0);
        Label("大将", new(136, 188, 96, 32)); Field(Person(Draft.CommanderId), new(240, 188, 80, 32));
        Field(Formation(Draft.FormationId), new(240, 228, 48, 32)); Field(Formation(Draft.NavalFormationId), new(240, 268, 48, 32));
        Field($"{(selected ? Draft.Soldiers.ToString() : "")}/{Draft.Home.Soldiers}", new(240, 308, 120, 32));
        Label("资金", new(136, 348, 96, 32)); Field($"{(selected ? Draft.Cost.ToString() : "")}/{Draft.Money}", new(240, 348, 120, 32));
        Label("士气", new(136, 388, 96, 32)); Field(Draft.Home.Morale.ToString(), new(240, 388, 40, 32));
        Label("陆上", new(240, 436, 48, 24)); Label("水上", new(296, 436, 48, 24));
        var land = Draft.Combat(false); var water = Draft.Combat(true);
        string[] labels = ["对部队", "对守兵", "对城壁", "守备力", "机动力"];
        for (int i = 0; i < labels.Length; i++)
        { int y = 468 + i * 40; Label(labels[i], new(136, y, 96, 32)); Field(selected ? land[i].Text : "", new(240, y, 40, 32)); Field(selected ? water[i].Text : "", new(296, y, 40, 32)); }
        var sectionStyle = GameTextStyles.CatalogRow with { EffectColor = new Color("005028") };
        Label("布阵", new(384, 166, 48, 24), alignment: HorizontalAlignment.Left, style: sectionStyle);
        Label("可拖曳武将以变更配置，双击鼠标可变更兵法。", new(440, 166, 432, 24), alignment: HorizontalAlignment.Left);
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
    private void RebuildDeferred() => Rebuild();
    private void OpenChild(Control child)
    { _child = child; AddChild(child); }
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
        var departure = new CatalogPage("出征", [OfficerCatalog.Pages[0].Columns[0],
            OfficerCatalog.Pages[3].Columns[1], OfficerCatalog.Pages[3].Columns[2],
            .. OfficerCatalog.Pages[0].Columns[1..6], OfficerCatalog.Pages[1].Columns[^1], OfficerCatalog.Pages[3].Columns[5]]);
        var ability = OfficerCatalog.Read(Draft.View, Texts, 0, null, Draft.Home.Id).Rows.ToDictionary(r => r.Key.Id);
        var personal = OfficerCatalog.Read(Draft.View, Texts, 3, null, Draft.Home.Id).Rows.ToDictionary(r => r.Key.Id);
        var belonging = OfficerCatalog.Read(Draft.View, Texts, 1, null, Draft.Home.Id).Rows.ToDictionary(r => r.Key.Id);
        CatalogRow[] Rows(int page)
        {
            if (page > 0) return OfficerCatalog.Read(Draft.View, Texts, page - 1, null, Draft.Home.Id).Rows.Where(r => Draft.Candidates.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells.ToArray())).ToArray();
            return Draft.Candidates.Select(id => new CatalogRow(id, [Person(id),personal[id].Cells[1],(long)Draft.CommandCapacity(id),
                ability[id].Cells[1],ability[id].Cells[2],ability[id].Cells[3],ability[id].Cells[4],ability[id].Cells[5],belonging[id].Cells[^1],personal[id].Cells[5]])).ToArray();
        }
        OpenChild(new CatalogView
        {
            Title = "选择武将",
            HintText = $"选择执行武将（最多{Draft.MemberLimit}人）。",
            Pages = [departure, .. OfficerCatalog.Pages],
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
        var view = new CatalogView
        {
            Title = naval ? "选择船" : "选择阵形",
            HintText = naval ? "请选择船。" : "请选择阵形。",
            CloseTitle = "中止",
            Pages = ForceCapabilityCatalog.Pages(category),
            Textures = Textures,
            Service = Service,
            PortraitSize = new(120, 120),
            Portrait = id => $"formation-diagram-{id}",
            InitialCurrentId = naval ? Draft.NavalFormationId : Draft.FormationId,
            ReadRows = page => ForceCapabilityCatalog.Read(Session, Texts, category, Draft.Force.Id, page).Rows.Where(r => candidates.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells.ToArray())).ToArray(),
            Activated = id => { if (Draft.SetFormation(id, naval)) CloseChild(); },
            Closed = CloseChild
        };
        OpenChild(view);
    }
    private void SelectTactic(int person)
    {
        var tactics = Draft.AvailableTactics(person).Select(t => t.Id).ToHashSet();
        if (tactics.Count == 0) { Notice("没学得兵法。 "); return; }
        OpenChild(new CatalogView
        {
            Title = "选择兵法",
            HintText = "选择武将使用的兵法。",
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
        var texture = Textures["deployment-" + role];
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth()) for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        { var size = new Vector2(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y)); DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size)); }
    }
    private void Strip(string role, Rect2 rect)
    {
        var left = Textures["deployment-" + role + "-left"]; var right = Textures["deployment-" + role + "-right"];
        Tile(role + "-middle", new(rect.Position + new Vector2(left.GetWidth(), 0), new(rect.Size.X - left.GetWidth() - right.GetWidth(), rect.Size.Y)));
        DrawTexture(left, rect.Position); DrawTexture(right, new(rect.End.X - right.GetWidth(), rect.Position.Y));
    }
    public override void _Draw()
    {
        DrawTexture(Textures["formation-panel-deployment"], new(368, 148));
        Strip("facility", new(136, 100, _facilityWidth, 32));
        foreach (int x in new[] { 240, 296 })
        {
            // These labels crop eight pixels from the outer end caps in the original skin.
            var left = Textures["deployment-mode-left"]; var right = Textures["deployment-mode-right"];
            float l = left.GetWidth() - 8, r = right.GetWidth() - 8;
            DrawTextureRectRegion(left, new(x, 436, l, 24), new(8, 0, l, 24));
            Tile("mode-middle", new(x + l, 436, 48 - l - r, 24));
            DrawTextureRectRegion(right, new(x + 48 - r, 436, r, 24), new(0, 0, r, 24));
        }
        foreach (int y in new[] { 188, 348, 388, 468, 508, 548, 588, 628 }) Strip("label", new(136, y, 96, 32));
        foreach (var rect in new[] { new Rect2(240, 188, 80, 32), new(240, 228, 48, 32), new(240, 268, 48, 32), new(240, 308, 120, 32), new(240, 348, 120, 32), new(240, 388, 40, 32) }) DrawField(rect);
        for (int i = 0; i < 5; i++) { DrawField(new(240, 468 + i * 40, 40, 32)); DrawField(new(296, 468 + i * 40, 40, 32)); }
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
