using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>The original catalog → object editor → catalog navigation owns one isolated draft.</summary>
public partial class WorldEditorView : ModalView
{
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public WorldEditorDraft Draft { get; init; } = null!;
    public Func<int, Func<StrategySnapshot>, Control> MapPreview { get; init; } = null!;
    public Func<WorldEditOperation, SessionResult> Apply { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private Control? _page;
    private Control? _modal;
    private bool _mapMode;
    public bool IsCatalog => _page is CatalogView;
    public string Title => Draft.Kind switch { WorldEditorKind.Officers => "编辑武将", WorldEditorKind.Corps => "编辑军团",
        WorldEditorKind.Cities => "编辑都市", WorldEditorKind.Facilities => "编辑设施", _ => "编辑障碍物" };
    private string CatalogTitle => Draft.Kind switch { WorldEditorKind.Officers => "武将一览", WorldEditorKind.Corps => "势力一览",
        WorldEditorKind.Cities => "都市一览", WorldEditorKind.Facilities => "工作物一览", _ => "障碍物一览" };
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        ShowCatalog();
    }
    private void ShowCatalog()
    {
        _mapMode = false;
        Replace(new CatalogView
        {
            Service = Service, Textures = Textures, TableState = Draft.Catalog, Pages = Draft.Pages,
            Title = CatalogTitle, HintText = "请选择欲编辑的" + Title[2..] + "。",
            ReadRows = Draft.ReadRows, CloseTitle = "关闭", Confirmed = Commit, Closed = RequestClose,
            Portrait = Draft.Kind == WorldEditorKind.Officers ? id => $"catalog-person-{id}" : null,
            ShowMap = Draft.Kind is WorldEditorKind.Corps or WorldEditorKind.Cities ? ShowMap : null,
            Activated = ShowDetail
        });
    }
    private void ShowMap()
    {
        _mapMode = true;
        Replace(new InformationMapView
        {
            ReadView = () => Draft.Preview, Texts = Texts, Service = Service, Skin = Skin, Textures = Textures,
            Category = Draft.Category, ReadCandidates = () => Draft.ReadRows(Draft.Catalog.Page).Select(r => r.Id).ToArray(),
            Title = CatalogTitle, HintText = "请选择欲编辑的" + (Draft.Kind == WorldEditorKind.Corps ? "军团" : "都市") + "。",
            ShowList = ShowCatalog, Activated = ShowDetail, Closed = RequestClose, Confirmed = Commit
        });
    }
    public void ShowDetail(int rowId)
    {
        Draft.Catalog.SelectId(rowId);
        Replace(new WorldEditorDetailView
        {
            Draft = Draft, CurrentId = Draft.TargetFromRow(rowId), Title = Title, Textures = Textures, Service = Service, Texts = Texts,
            Candidates = Draft.Catalog.Rows.Select(r => Draft.TargetFromRow(r.Id)).ToArray(),
            MapPreview = id => MapPreview(id, () => Draft.Preview), Closed = () => { if (_mapMode) ShowMap(); else ShowCatalog(); },
            CurrentChanged = id => Draft.Catalog.SelectId(Draft.Kind == WorldEditorKind.Corps
                ? Draft.Preview.World.Corps.Single(c => c.Id == id).OwnerId : id),
            NumberRequested = (field, refresh) =>
            {
                OpenModal(new NumberInputDialog { Textures = Textures, Subject = field.Label,
                    State = new(field.Minimum, field.Maximum, field.Value), Closed = CloseModal,
                    Confirmed = value => { field.Set(value); CloseModal(); refresh(); } });
            }
        });
    }
    private void RequestClose()
    {
        if (_modal is not null) return;
        if (!Draft.HasChanges) { Closed(); return; }
        var dialog = new GameDialog { Textures = Textures, Size = Size };
        dialog.SetRichMessage(Texts.ResolveRich(WorldEditorText.ConfirmDiscardKey));
        dialog.Confirmed += () => { CloseModal(); Closed(); };
        dialog.Dismissed += CloseModal;
        OpenModal(dialog);
        dialog.FocusDefaultButton();
    }
    private void Commit()
    {
        if (!Draft.HasChanges) { Closed(); return; }
        var result = Apply(Draft.Operation);
        if (result.Disposition == SessionDisposition.Accepted) { Closed(); return; }
        var notice = new GameDialog { Textures = Textures, Size = Size };
        notice.Closed += CloseModal; OpenModal(notice);
        notice.SetNotice($"编辑未能执行：{result.Rejection}\n草稿仍保留。"); notice.FocusDefaultButton();
    }
    private void OpenModal(Control page)
    {
        CloseModal(); _modal = OverlayPage.Create(page, Colors.Transparent); AddChild(_modal);
    }
    private void CloseModal()
    {
        if (_modal is null) return;
        RemoveChild(_modal); _modal.QueueFree(); _modal = null;
    }
    private void Replace(Control page)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = page; AddChild(page);
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
}
