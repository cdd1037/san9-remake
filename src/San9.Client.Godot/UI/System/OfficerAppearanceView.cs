using Godot;
using System.Collections.Immutable;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Player-authored appearances use the same lists, number entry and confirmation as other system pages.</summary>
public partial class OfficerAppearanceView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public ScenarioInitialState? Scenario { get; init; }
    public string ContentRoot { get; init; } = "";
    private Control? _page;
    private int _year, _age, _place;
    private int[] _officers = [];
    private string _search = "";
    private CatalogSelection? _selection;
    private Dictionary<int, OfficerAppearanceRecommendation> _recommendations = [];
    private Dictionary<string, string> _scenarioNames = [];
    private int[] _editTargets = [];
    private int? _editForce;
    private static readonly CatalogPage[] PeoplePage = [new("武将", [
        new("name", "武将", 12, CatalogAlignment.Left), new("status", "身分", 8, CatalogAlignment.Left),
        new("year", "登场", 6, CatalogAlignment.Right), new("leadership", "统率", 6, CatalogAlignment.Right),
        new("war", "武力", 6, CatalogAlignment.Right), new("intelligence", "智力", 6, CatalogAlignment.Right),
        new("politics", "政治", 6, CatalogAlignment.Right)])];
    private static readonly CatalogPage[] ChoicePage = [new("选择", [new("name", "项目", 24, CatalogAlignment.Left),
        new("info", "说明", 38, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] PlacesPage = [new("地点", [new("name", "地点", 22, CatalogAlignment.Left),
        new("owner", "势力", 22, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] PreviewPage = [new("预览", [new("name", "武将", 12, CatalogAlignment.Left),
        new("force", "势力", 12, CatalogAlignment.Left), new("place", "城市", 12, CatalogAlignment.Left),
        new("source", "依据／状态", 28, CatalogAlignment.Left)])];

    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; ShowPeople(); }
    private void Replace(Control next)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = next; AddChild(next);
    }
    private string PersonName(int id) => Texts.Resolve(Session.View.MessageSpeaker(id).NameKey);
    private void ShowPeople()
    {
        var people = Session.Snapshot.Officers.Where(p => !p.IsDormant &&
                p.Status is OfficerStatus.Dead or OfficerStatus.NotAppeared or OfficerStatus.Undiscovered)
            .OrderBy(p => p.Id).ToArray();
        _selection ??= new CatalogSelection(people.Select(p => p.Id), [], people.Length);
        var rows = people.Where(p => PersonName(p.Id).Contains(_search, StringComparison.OrdinalIgnoreCase))
            .Select(p => new CatalogRow(p.Id, [PersonName(p.Id), p.ManualAppearance is { } pending && pending.KeepPendingIfForceUnavailable && pending.Year <= Session.Snapshot.Clock.Year ? "待处理" : p.ManualAppearance is not null ? "已设置" : p.Status switch
            { OfficerStatus.Dead => "已死亡", OfficerStatus.NotAppeared => "未登场", _ => "未发现" },
            p.ManualAppearance is { } plan ? (CatalogCell)plan.Year : CatalogCell.Unavailable,
            p.BaseAbilities.Leadership, p.BaseAbilities.War, p.BaseAbilities.Intelligence, p.BaseAbilities.Politics])).ToArray();
        Replace(new CatalogView { Title = "武将登场", HintText = "可搜索姓名、多选武将；点击属性表头排序。", CloseTitle = "中止",
            Textures = Textures, Service = Service, Pages = PeoplePage, ReadRows = _ => rows, Closed = Closed, Selection = _selection,
            Search = ShowSearch, ConfirmSelection = ids => { _officers = ids.Order().ToArray(); _age = 30; ShowTime(); } });
    }
    private void ShowSearch() => Replace(new PeerageTitleEntryView { Textures = Textures, Heading = "搜索武将",
        Description = "输入姓名的一部分；留空显示全部。", InitialTitle = _search,
        Cancelled = ShowPeople, Confirmed = value => { _search = value ?? ""; ShowPeople(); } });
    private void ShowTime()
    {
        CatalogRow[] rows = [new(0, ["立即登场", "本旬按指定身份登场"]), new(1, ["指定年份", "该年一月登场"])];
        Replace(new CatalogView { Title = _officers.Length == 1 ? PersonName(_officers[0]) : $"{_officers.Length}名武将", HintText = "选择登场时间。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ChoicePage, ReadRows = _ => rows, Closed = ShowPeople,
            Activated = i => { if (i == 0) { _year = Session.Snapshot.Clock.Year; ShowAge(); } else ShowYear(); } });
    }
    private void ShowYear()
    {
        int current = Session.Snapshot.Clock.Year;
        if (current == int.MaxValue) { Notice("当前年份已达上限。", ShowTime); return; }
        Replace(new NumberInputDialog { Textures = Textures, Subject = "登场年份",
            State = new((uint)current + 1, int.MaxValue, (uint)current + 1), Closed = ShowTime,
            Confirmed = value => { _year = checked((int)value); ShowAge(); } });
    }
    private void ShowAge()
    {
        uint maximum = (uint)Math.Min(100, _year - 1);
        if (maximum == 0) { Notice("该年份不能设置武将年龄。", ShowTime); return; }
        _age = Math.Clamp(_age, 1, (int)maximum);
        Replace(new NumberInputDialog { Textures = Textures, Subject = "登场年龄",
            State = new(1, maximum, (uint)_age), Closed = ShowTime,
            Confirmed = value => { _age = (int)value; ShowMode(); } });
    }
    private void ShowMode()
    {
        CatalogRow[] rows = [new(0, ["统一指定", "全部使用同一地点和身份"]),
            new(1, ["按剧本归属", "参考其他剧本，逐人预览和调整"])];
        Replace(new CatalogView { Title = "分配方式", HintText = "选择统一指定或参考剧本归属。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ChoicePage, ReadRows = _ => rows, Closed = ShowAge,
            Activated = i => { if (i == 0) ShowPlaces(); else LoadRecommendations(); } });
    }
    private async void LoadRecommendations()
    {
        if (Scenario is null || string.IsNullOrWhiteSpace(ContentRoot)) { Notice("缺少剧本目录，请使用统一指定。", ShowMode); return; }
        var origin = _page;
        try
        {
            var catalog = await ContentCatalogFiles.LoadAsync(ContentRoot);
            var scenarios = await Task.WhenAll(catalog.Scenarios.Select(e => ScenarioContentFiles.LoadScenarioAsync(PackageFiles.Resolve(ContentRoot, e.Package))));
            var previews = await ContentCatalogFiles.LoadPreviewsAsync(ContentRoot, catalog);
            if (!IsInsideTree() || _page != origin) return;
            _scenarioNames = previews.ToDictionary(p => p.Id, p => p.Title, StringComparer.Ordinal);
            _recommendations = OfficerAppearanceRecommendations.Create(Scenario, Session.Snapshot, scenarios, _officers).ToDictionary(r => r.OfficerId);
            ShowPreview();
        }
        catch (Exception e)
        { if (IsInsideTree() && _page == origin) Notice("无法读取剧本依据：" + e.Message, ShowMode); }
    }
    private void ShowPreview()
    {
        var forceNames = Session.View.Forces.ToDictionary(f => f.Id, f => Texts.Resolve(f.NameKey));
        var placeNames = Session.View.Settlements.ToDictionary(p => p.Id, p => p.ResolveName(Texts));
        var rows = _officers.Select(id =>
        {
            var r = _recommendations[id];
            string force = r.Problem is not null ? "待处理" : r.ForceId is int f && forceNames.TryGetValue(f, out var fn) ? fn : "在野";
            string place = r.SettlementId is int p && placeNames.TryGetValue(p, out var pn) ? pn : "—";
            string source = r.Problem ?? (r.SourceScenarioId is null ? "手动指定" : $"{r.SourceYear}年 {_scenarioNames.GetValueOrDefault(r.SourceScenarioId, r.SourceScenarioId)}");
            return new CatalogRow(id, [PersonName(id), force, place, source]);
        }).ToArray();
        Replace(new CatalogView { Title = "登场预览", HintText = "选择武将调整；批量修改应用于全部已选武将。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = PreviewPage, ReadRows = _ => rows, Closed = ShowMode,
            Activated = id => { _editTargets = [id]; ShowEditForces(); },
            SearchTitle = "批量修改", Search = () => { _editTargets = _officers; ShowEditForces(); },
            Confirmed = () =>
            {
                if (_recommendations.Values.Any(r => r.Problem is not null || r.SettlementId is null))
                { Notice("仍有待处理的武将，请先指定势力和地点。", ShowPreview); return; }
                Confirm($"确定为{_officers.Length}名武将设置登场？\n{_year}年，登场年龄{_age}岁。", SubmitIndividual, ShowPreview);
            } });
    }
    private void ShowEditForces()
    {
        var forces = Session.Snapshot.Forces.Where(f => !f.IsDormant && !f.IsEliminated && f.LeaderId is not null)
            .Where(f => Session.Snapshot.Settlements.Any(s => s.OwnerId == f.Id && !s.IsDormant && !s.IsDestroyed && s.CanHostPersons && s.Durability > 0))
            .OrderBy(f => f.Id).ToArray();
        var names = Session.View.Forces.ToDictionary(f => f.Id, f => Texts.Resolve(f.NameKey));
        var rows = new[] { new CatalogRow(-1, ["在野", "选择任意可用地点"]) }.Concat(forces.Select(f =>
            new CatalogRow(f.Id, [names.GetValueOrDefault(f.Id, f.Id.ToString()), "加入该势力"])) ).ToArray();
        Replace(new CatalogView { Title = "选择势力", HintText = "选择归属，再选择地点。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ChoicePage, ReadRows = _ => rows, Closed = ShowPreview,
            Activated = id => { _editForce = id < 0 ? null : id; ShowEditPlaces(); } });
    }
    private void ShowEditPlaces()
    {
        var names = Session.View.Settlements.ToDictionary(p => p.Id, p => p.ResolveName(Texts));
        var forces = Session.View.Forces.ToDictionary(f => f.Id, f => Texts.Resolve(f.NameKey));
        var places = Session.Snapshot.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.CanHostPersons && s.Durability > 0 &&
            names.ContainsKey(s.Id) && (_editForce is null || s.OwnerId == _editForce)).OrderBy(s => s.Id).ToArray();
        var rows = places.Select(s => new CatalogRow(s.Id, [names[s.Id], s.OwnerId is int owner ? forces.GetValueOrDefault(owner, "无") : "无"])).ToArray();
        Replace(new CatalogView { Title = "选择地点", HintText = "所选武将将使用此势力和地点。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = PlacesPage, ReadRows = _ => rows, Closed = ShowEditForces,
            Activated = id => { foreach (int person in _editTargets) _recommendations[person] = new(person, _editForce, id, null, null, null); ShowPreview(); } });
    }
    private void SubmitIndividual()
    {
        var assignments = _officers.Select(id => { var r = _recommendations[id];
            return new OfficerAppearanceAssignment(id, new(_year, r.SettlementId!.Value, r.ForceId, _age) { KeepPendingIfForceUnavailable = true }); }).ToImmutableArray();
        var result = Session.Apply(Session.Request(new ConfigureIndividualAppearancesOperation(assignments)));
        if (result.Disposition == SessionDisposition.Rejected) { Notice("设置失败，请检查武将和势力状态。", ShowPreview); return; }
        _selection = null; Notice(_year == Session.Snapshot.Clock.Year ? "武将已登场。" : "登场安排已记录。", ShowPeople);
    }
    private void ShowPlaces()
    {
        var world = Session.Snapshot;
        var names = Session.View.Settlements.ToDictionary(p => p.Id, p => p.ResolveName(Texts));
        var forces = Session.View.Forces.ToDictionary(f => f.Id, f => Texts.Resolve(f.NameKey));
        var places = world.Settlements.Where(p => !p.IsDormant && !p.IsDestroyed && p.CanHostPersons && p.Durability > 0 && names.ContainsKey(p.Id))
            .OrderBy(p => p.Id).ToArray();
        var rows = places.Select((p, i) => new CatalogRow(i, [names[p.Id], p.OwnerId is int owner && forces.TryGetValue(owner, out var name) ? name : "无"])).ToArray();
        Replace(new CatalogView { Title = "选择登场地点", HintText = "选择可容纳武将的地点。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = PlacesPage, ReadRows = _ => rows, Closed = ShowMode,
            Activated = i => { _place = places[i].Id; ShowStatus(); } });
    }
    private void ShowStatus()
    {
        var place = Session.Snapshot.Settlements.Single(p => p.Id == _place);
        bool canJoin = Session.View.PlayerForceId is int force && place.OwnerId == force;
        CatalogRow[] rows = [new(0, ["在野", "在选定地点以在野身份登场"]),
            new(1, ["加入当前势力", canJoin ? "在当前势力所属地点出仕" : "地点不属于当前势力"])];
        Replace(new CatalogView { Title = "选择登场身份", HintText = "选择身份；地点易主时将尝试其他己方据点。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ChoicePage, ReadRows = _ => rows, Closed = ShowPlaces,
            Activated = i =>
            {
                if (i == 1 && !canJoin) { Notice("请选择当前势力所属地点。", ShowStatus); return; }
                int? owner = i == 1 ? Session.View.PlayerForceId : null;
                string name = Session.View.Settlements.Single(p => p.Id == _place).ResolveName(Texts);
                var plan = new ManualOfficerAppearance(_year, _place, owner, _age);
                string subject = _officers.Length == 1 ? PersonName(_officers[0]) : $"已选{_officers.Length}名武将";
                Confirm($"{subject}将在{_year}年、{_age}岁时于{name}{(owner is null ? "在野" : "加入当前势力")}。\n确定设置吗？", () => Submit(plan));
            } });
    }
    private void Submit(ManualOfficerAppearance plan)
    {
        var result = Session.Apply(Session.Request(new ConfigureOfficerAppearancesOperation(_officers.ToImmutableArray(), plan)));
        if (result.Disposition == SessionDisposition.Rejected) { Notice("设置失败，请重新检查武将、地点和当前回合。", ShowPeople); return; }
        _selection = null;
        Notice(plan.Year == Session.Snapshot.Clock.Year ? "武将已登场。" : "登场安排已记录。", ShowPeople);
    }
    private void Confirm(string text, Action yes, Action? back = null)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Confirmed += yes; dialog.Dismissed += back ?? ShowStatus; dialog.Closed += back ?? ShowStatus; dialog.SetMessage(text);
    }
    private void Notice(string text, Action back)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Closed += back; dialog.SetNotice(text);
    }
}
