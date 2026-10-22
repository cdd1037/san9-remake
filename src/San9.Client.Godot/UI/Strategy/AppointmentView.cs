using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Appointment preparation and catalog navigation; mutation belongs to the submit callback.</summary>
public partial class AppointmentView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public AppointmentDraft Draft { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Submit { get; init; } = null!;
    private readonly GameTextPresenter text = new();
    private Control? body, child;
    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; Rebuild(); }
    private void Rebuild()
    {
        if (body is not null) { RemoveChild(body); body.QueueFree(); }
        body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(body);
        if (Draft.Kind == "strategist") { BuildStrategist(); return; }
        if (Draft.Kind == "execute") { BuildExecution(); return; }
        if (Draft.Kind is "reward" or "exile") { BuildRosterAppointment(); return; }
        if (Draft.Kind is "bestow" or "confiscate") { BuildTreasureAppointment(); return; }
        BuildRank();
    }
    private void ReplaceChild(Control next)
    {
        if (child is not null) { RemoveChild(child); child.QueueFree(); }
        child = next; AddChild(next);
    }
    private void CloseChild()
    {
        if (child is not null) { RemoveChild(child); child.QueueFree(); child = null; }
        Rebuild();
    }
    private void Help()
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size };
        dialog.Closed += CloseChild; dialog.SetNotice(Texts.Resolve(CommandMenuSkin.DescriptionKey(Draft.Kind))); ReplaceChild(dialog);
    }
    private void SelectPeople()
    {
        if (Draft.Kind == "strategist") { SelectStrategist(); return; }
        if (Draft.Kind == "execute") { SelectExecutionTarget(); return; }
        if (Draft.Kind == "bestow") { SelectTreasureRecipient(); return; }
        if (Draft.Kind is "reward" or "exile") { SelectRosterAppointmentOfficers(); return; }
        SelectRankPeople(Draft.OfficePlan!.Fork(), new());
    }
    private void SelectSingleAppointmentOfficer((int Page,int Column)[] columns)
    {
        var tables = columns.Select(c => c.Page).Distinct().ToDictionary(page => page,
            page => OfficerCatalog.Read(Draft.View,Texts,page,null,null).Rows.ToDictionary(r => r.Key.Id));
        CatalogRow[] Rows(int page) => page == 0
            ? Draft.Candidates.Select(id => new CatalogRow(id,columns.Select(c => tables[c.Page][id].Cells[c.Column]).ToArray())).ToArray()
            : OfficerCatalog.Read(Draft.View,Texts,page-1,null,null).Rows.Where(r => Draft.Candidates.Contains(r.Key.Id))
                .OrderBy(r => Draft.Candidates.IndexOf(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id,r.Cells.ToArray())).ToArray();
        ReplaceChild(new CatalogView { Title = CommandText.Resolve(Texts, "select.officer"), CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Textures = Textures, Service = Service,
            HintText = Texts.Resolve(AppointmentText.Key(Draft.Kind,"select")),
            Pages = [new(CommandText.Resolve(Texts, "title." + Draft.Kind),columns.Select(c => OfficerCatalog.Pages[c.Page].Columns[c.Column]).ToArray()), .. OfficerCatalog.Pages],
            ReadRows = Rows, Portrait = id => $"catalog-person-{id}", InitialCurrentId = Draft.Members.IsEmpty ? null : Draft.Members[0],
            Activated = id => { if (Draft.SetMembers([id])) CloseChild(); }, Closed = CloseChild });
    }
    private void SelectTreasure()
    {
        ReplaceChild(new CatalogView { Title = CommandText.Resolve(Texts, "select.treasure"), CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Pages = TreasureCatalog.Pages, Textures = Textures, Service = Service,
            HintText = Texts.Resolve(AppointmentText.Key(Draft.Kind,"select-treasure")),
            ReadRows = _ => TreasureCatalog.Read(Draft.View, Texts).Rows.Where(r => Draft.TreasureIds.Contains(r.Key.Id))
                .OrderBy(r => Draft.TreasureIds.IndexOf(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells.ToArray())).ToArray(),
            InitialCurrentId = Draft.TreasureId, Closed = CloseChild,
            Activated = id => { if (Draft.SetTreasure(id)) CloseChild(); } });
    }
    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree() || child is not null) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            GetViewport().SetInputAsHandled();
            if (mouse.Pressed) Closed();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() => text.Dispose();
}
