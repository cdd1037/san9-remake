using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Shared command chrome and reversible child navigation; no command submission occurs here.</summary>
public abstract partial class CommandPreparationView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Submit { get; init; } = null!;
    public Action<CommandTargetRequest> ChooseMap { get; init; } = null!;
    protected Control Body = null!;
    protected Control? Child;
    private bool invalidated;
    private bool rightPressed;
    protected readonly GameTextPresenter Text = new();
    protected abstract bool IsCurrent { get; }
    protected abstract void Build();
    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; Rebuild(); }
    public override void _Process(double delta)
    {
        if (IsCurrent || invalidated) return;
        invalidated = true;
        var dialog = new GameDialog { Textures = Textures, Size = Size };
        dialog.SetNotice(CommandText.Resolve(Texts, "failure.stale-preparation")); dialog.Closed += Closed; ReplaceChild(dialog);
    }
    protected void Rebuild()
    {
        if (Body is not null) { RemoveChild(Body); Body.QueueFree(); }
        Body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(Body); Build();
    }
    protected void ReplaceChild(Control child, bool keepBodyVisible = false)
    {
        if (Child is not null) { RemoveChild(Child); Child.QueueFree(); }
        Body.Visible = keepBodyVisible; Child = child; AddChild(child);
    }
    protected void CloseChild()
    {
        if (Child is not null) { RemoveChild(Child); Child.QueueFree(); Child = null; }
        Rebuild();
    }
    protected void SelectMapTargets(CommandTargetSelection selection, string title, IReadOnlyList<CatalogPage> pages,
        Func<int, CatalogRow[]> readRows, Action<int> selected, Action canceled, int? current = null)
    {
        if (selection.Candidates.IsEmpty) { Notice(CommandText.Resolve(Texts, "failure.no-target")); return; }
        var state = new CatalogTableState();
        ChooseMap(new(this, selection, title, (pick, showMap) =>
        {
            Show();
            void Map() { CloseChild(); showMap(); }
            ReplaceChild(new CatalogView
            {
                Title = title, HintText = CommandText.Resolve(Texts, "hint.target"), CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Service = Service, Textures = Textures,
                Pages = pages, TableState = state, InitialCurrentId = state.SelectedId ?? current,
                ReadRows = readRows, Activated = pick, ShowMap = Map, Closed = Map
            });
        }, selected, canceled));
    }
    protected void SelectAdministrative(GameSession session, StrategySnapshot view, InformationCategory category,
        IEnumerable<int> candidates, string title, Action<int>? selected, CatalogSelection? selection = null,
        Action<IReadOnlySet<int>>? confirmed = null, int? current = null,
        IReadOnlyList<CatalogPage>? pages = null, Func<int, CatalogRow[]>? readRows = null,
        string? hintText = null, Action? canceled = null)
    {
        var ids = candidates.ToArray();
        var available = ids.ToHashSet();
        if (ids.Length == 0) { Notice(CommandText.Resolve(Texts, "failure.no-target")); return; }
        Textures.RequestPrefixes("overview-", "information-", "detail-", "catalog-");
        var query = new InformationQuery(session,Texts);
        var state = new CatalogTableState();
        string hint = hintText ?? (category == InformationCategory.Cities ? CommandText.Resolve(Texts, "hint.city") : CommandText.Resolve(Texts, "hint.force"));
        Action cancel = canceled ?? CloseChild;
        void Show(bool map)
        {
            if (map)
                ReplaceChild(new InformationMapView
                {
                    ReadView = () => view, Texts = Texts, Skin = Skin, Service = Service, Textures = Textures,
                    Category = category, ReadCandidates = () => ids, Title = title, HintText = hint, CloseTitle = CommandText.Resolve(Texts, "action.cancel"),
                    Activated = id => selected?.Invoke(id), Selection = selection, ConfirmSelection = confirmed,
                    ShowList = () => Show(false), Closed = cancel
                });
            else
                ReplaceChild(new CatalogView
                {
                    Title = title, HintText = hint, CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Service = Service, Textures = Textures,
                    Pages = pages ?? AdministrativeCatalog.Pages(category), TableState = state, InitialCurrentId = state.SelectedId ?? current,
                    ReadRows = readRows ?? (page => query.Read(category,page).Rows.Where(r => available.Contains(r.Key.Id))
                        .Select(r => new CatalogRow(r.Key.Id,r.Cells.ToArray())).ToArray()),
                    Activated = selected, Selection = selection, ConfirmSelection = confirmed,
                    ShowMap = () => Show(true), Closed = cancel
                });
        }
        Show(true);
    }
    protected void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size };
        dialog.Closed += CloseChild; dialog.SetNotice(message); ReplaceChild(dialog);
    }
    public void ShowFailure(string message) => Notice(message);
    protected void Label(string value, Vector2 at, Vector2 size)
    {
        var label = new Label { Text = value, Position = at, Size = size, ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        Body.AddChild(label); Text.Apply(label,GameTextStyles.CatalogRow with { Foreground = new Color("f0f0f0") });
    }
    protected void Value(string value, Vector2 at, Vector2 size)
    {
        Body.AddChild(new CommandValueField { Textures = Textures, Position = at, Size = size });
        Label(value, at, size);
    }
    protected void Caption(string value, Vector2 at, Vector2 size)
    {
        Body.AddChild(new CommandLabelField { Textures = Textures, Position = at, Size = size });
        Label(value, at, size);
    }
    protected void Person(StrategySnapshot view, int? id, Vector2 at)
    {
        var person = view.Officers.SingleOrDefault(p => p.Id == id);
        Body.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = at,
            Officer = person is null ? null : new(Texts.Resolve(person.NameKey), $"catalog-person-{id}", person.Abilities) });
    }
    protected void Properties(Vector2 at, params OfficerProperty[] values)
        => WideProperties(at,48,values);
    protected void WideProperties(Vector2 at, int labelWidth, params OfficerProperty[] values)
        => SpacedProperties(at,labelWidth,8,values);
    protected void SpacedProperties(Vector2 at, int labelWidth, int valueGap, params OfficerProperty[] values)
    {
        var properties = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = at,
            LabelWidth = labelWidth, ValueGap = valueGap, RowHeight = 24 };
        properties.SetProperties(values); Body.AddChild(properties);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            if (mouse.Pressed) rightPressed = Child is null;
            else
            {
                bool close = rightPressed && Child is null;
                rightPressed = false;
                if (close) Closed();
            }
        }
        AcceptEvent();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Child is null && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() => Text.Dispose();
}

/// <summary>The static command label plate, separate from interactive selector buttons.</summary>
internal partial class CommandLabelField : Control
{
    public TextureScope Textures { get; init; } = null!;
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => CommandPanelArt.Strip(this, Textures, "deployment-label", new(Vector2.Zero, Size));
}

internal partial class CommandValueField : Control
{
    public TextureScope Textures { get; init; } = null!;
    private StyleBoxTexture field = null!;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; field = CommandPanelArt.Field(Textures); }
    public override void _Draw()
    {
        DrawStyleBox(field,new(Vector2.Zero,Size));
        SkinPainter.Tile(this,Textures["deployment-field-rim"],new(0,0,Size.X,4));
        SkinPainter.Tile(this,Textures["deployment-field-rim"],new(0,Size.Y-4,Size.X,4));
    }
    public override void _ExitTree() => field.Dispose();
}
