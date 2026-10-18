using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public interface IInformationPage { void RefreshData(); }

public partial class InformationPanel : Control
{
    public InformationBrowser State { get; } = new();
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Control MenuAnchor { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public San9.Content.Interface.InterfaceSkin Skin { get; init; } = null!;
    public Action<int> Locate { get; init; } = null!;
    public San9.Content.Interface.ChroniclePresentationDefinition Chronicle { get; init; } = null!;
    public MapMessageLog RecordsLog { get; init; } = null!;
    public Action<Settings.ProgressReportMode> ChangeReportMode { get; init; } = null!;
    private InformationQuery _query = null!;
    private TextureScope? _textures;
    private Control? _page;
    private bool _dirty;
    public InformationMenuButton? HoverMenuButton => (_page as InformationMenuView)?.HoverButton;
    public override void _Ready()
    {
        Name = "InformationPanel"; Size = new(1024, 768); ZIndex = 20;
        MouseFilter = MouseFilterEnum.Stop; Visible = false;
        _query = new(Session, Texts, Chronicle); Session.Changed += OnChanged; RecordsLog.Changed += OnChanged;
    }
    private void OnChanged() => _dirty = true;
    public void OpenMenu() { State.OpenMenu(); Rebuild(); }
    public void Open(InformationCategory category) { State.Close(); State.Open(category); Rebuild(); }
    public void OpenContext(MapContextEntry entry)
    {
        State.OpenContext(entry);
        if (entry.DetailId is int id)
        {
            State.DetailSelection.SetVisibleRows(AdministrativeDetailView.VisibleRows(entry.Target.Category));
            State.DetailSelection.Open(_query.Read(State).Rows.Select(r => r.Key.Id), id);
        }
        Rebuild();
    }
    public void Back() { State.Back(); Rebuild(); }
    public override void _Process(double delta)
    {
        if (!_dirty || !State.IsOpen) return;
        _dirty = false;
        if (!ReconcileDetail()) { Rebuild(); return; }
        if (_page is IInformationPage page) page.RefreshData();
    }
    private bool ReconcileDetail()
    {
        if (!State.IsDetail) return true;
        var ids = _query.Read(State).Rows.Select(row => row.Key.Id).ToArray();
        if (ids.Length == 0) { State.Back(); return false; }
        int selected = State.SelectedId is int id && ids.Contains(id) ? id : ids[0];
        if (State.Category == InformationCategory.Officers)
        {
            if (State.OfficerDetail.Candidates.Length == 0) State.OfficerDetail.Open(ids, selected);
            else State.OfficerDetail.Refresh(ids);
            State.Select(State.OfficerDetail.CurrentId);
        }
        else
        {
            if (State.DetailSelection.Candidates.Count == 0) State.DetailSelection.Open(ids, selected);
            else State.DetailSelection.Refresh(ids);
            State.Select(State.DetailSelection.CurrentId);
        }
        return true;
    }
    private void Rebuild()
    {
        _dirty = false;
        ReconcileDetail();
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); _page = null; }
        _textures?.Dispose(); _textures = null;
        Visible = State.IsOpen;
        if (!Visible) return;
        _textures = Service.CreateScope(owner: this);
        if (State.IsMenu) _textures.RequestPrefixes("information-menu-");
        else if (State.Category is InformationCategory.Records or InformationCategory.Chronicle) _textures.RequestPrefixes("progress-record-", "detail-", "catalog-scroll-", "message-", "text-icon-");
        else if (State.IsDetail) _textures.RequestPrefixes("detail-", "formation-", "roster-", "catalog-scroll-", "message-");
        else if (State.IsMap) _textures.RequestPrefixes("overview-terrain", "information-", "catalog-help", "message-");
        else _textures.RequestPrefixes("information-", "catalog-", "roster-", "scroll-", "button-scroll-", "message-", "text-icon-");
        _page = State.IsMenu
            ? new InformationMenuView { AnchorControl = MenuAnchor, Query = _query, Texts = Texts, Textures = _textures,
                Open = category => { State.Open(category); Rebuild(); }, Back = Back }
            : new InformationPageFactory { State = State, Session = Session, Texts = Texts,
                Service = Service, Textures = _textures, World = World, Skin = Skin, Query = _query,
                RecordsLog = RecordsLog, ChangeReportMode = ChangeReportMode, Rebuild = Rebuild, Back = Back }.Create();
        AddChild(_page);
    }
    public override void _ExitTree() { Session.Changed -= OnChanged; RecordsLog.Changed -= OnChanged; _textures?.Dispose(); }
}
