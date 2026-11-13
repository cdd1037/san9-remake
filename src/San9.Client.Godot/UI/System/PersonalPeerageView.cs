using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Personal titles use the same catalog and confirmation windows as other personnel actions.</summary>
public partial class PersonalPeerageView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private Control? _page;
    private static readonly CatalogPage[] OfficersPage = [new("武将", [
        new("name", "武将", 16, CatalogAlignment.Left), new("peerage", "爵位", 16, CatalogAlignment.Left),
        new("loyalty", "忠诚", 8, CatalogAlignment.Right)]), new("战功", [
        new("name", "武将", 16, CatalogAlignment.Left), new("merit", "本势力战功", 16, CatalogAlignment.Right),
        new("loyalty", "忠诚", 8, CatalogAlignment.Right)]), new("政绩", [
        new("name", "武将", 16, CatalogAlignment.Left), new("merit", "本势力政绩", 16, CatalogAlignment.Right),
        new("loyalty", "忠诚", 8, CatalogAlignment.Right)])];
    private static readonly CatalogPage[] GradesPage = [new("爵位", [
        new("grade", "爵位", 16, CatalogAlignment.Left), new("limit", "名额", 12, CatalogAlignment.Right),
        new("salary", "爵俸", 8, CatalogAlignment.Right), new("state", "条件", 28, CatalogAlignment.Left)])];

    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; ShowOfficers(); }
    private void Replace(Control page)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = page; AddChild(page);
    }
    private static string GradeName(PersonalPeerageGrade grade) => grade switch
    {
        PersonalPeerageGrade.ImperialMarquis => "关内侯", PersonalPeerageGrade.VillageMarquis => "亭侯",
        PersonalPeerageGrade.DistrictMarquis => "乡侯", PersonalPeerageGrade.CountyMarquis => "县侯",
        PersonalPeerageGrade.Duke => "公", PersonalPeerageGrade.King => "王", _ => "—"
    };
    private static string Reason(CommandRejection rejection) => rejection switch
    {
        CommandRejection.None => "可授予", CommandRejection.PeerageUnchanged => "当前爵位",
        CommandRejection.PeerageRegimeUnavailable => "本年无可用爵制", CommandRejection.PeerageUnauthorized => "政治地位不足",
        CommandRejection.PeerageQuotaFull => "名额已满", CommandRejection.PeerageRoyalLineageRequired => "须为皇室宗亲",
        CommandRejection.PeerageEmperorRequired => "须由称帝势力授予", CommandRejection.PeerageCourtControlRequired => "须控制皇帝",
        CommandRejection.PersonUnavailable => "武将不具备资格", _ => "当前无法操作"
    };
    private void ShowOfficers()
    {
        var view = Session.View;
        if (view.PlayerForceId is not int force) { Closed(); return; }
        int? ruler = view.World.Forces.Single(f => f.Id == force).LeaderId;
        var people = view.World.Officers.Where(p => p.OwnerId == force && p.IsServing && p.Id != ruler).OrderBy(p => p.Id).ToArray();
        var names = view.Officers.ToDictionary(p => p.Id, p => Texts.Resolve(p.NameKey));
        var rows = people.Select((p, i) => new CatalogRow(i, [names[p.Id],
            p.PersonalPeerage is { } title ? title.Title ?? GradeName(title.Grade) : "无", OfficerCatalog.DisplayLoyalty(p.Loyalty)])).ToArray();
        var meritRows = people.Select((p, i) => new CatalogRow(i, [names[p.Id], p.BattleMeritFor(force),
            OfficerCatalog.DisplayLoyalty(p.Loyalty)])).ToArray();
        var serviceRows = people.Select((p, i) => new CatalogRow(i, [names[p.Id], p.ServiceMeritFor(force),
            OfficerCatalog.DisplayLoyalty(p.Loyalty)])).ToArray();
        Replace(new CatalogView { Title = "封爵", HintText = $"{view.PersonalPeerageRegimeName(force) ?? "爵制未配置"}　选择武将，再选择爵位。", CloseTitle = "中止",
            Textures = Textures, Service = Service, Pages = OfficersPage, ReadRows = page => page switch { 0 => rows, 1 => meritRows, _ => serviceRows }, Closed = Closed,
            Activated = index => ShowGrades(people[index].Id) });
    }
    private void ShowGrades(int officerId)
    {
        var view = Session.View;
        if (view.PlayerForceId is not int force) { Closed(); return; }
        var person = view.World.Officers.Single(p => p.Id == officerId);
        var grades = Enum.GetValues<PersonalPeerageGrade>();
        SetPersonalPeerageCommand Command(PersonalPeerage? title) => new(Guid.NewGuid(), force,
            view.World.Simulation.NextSequence, officerId, title);
        CommandRejection Check(PersonalPeerage? title) => Session.Validate(new("player"), Command(title)).Rejection;
        var options = grades.Select(g => (Grade: (PersonalPeerageGrade?)g, Title: (PersonalPeerage?)new PersonalPeerage(g, null)))
            .Append((Grade: null, Title: null)).ToArray();
        var rows = options.Select((option, i) =>
        {
            if (option.Grade is not { } grade)
                return new CatalogRow(i, ["撤爵", "—", "—", Check(null) is var revoke && revoke == CommandRejection.None ? "可撤销" : Reason(revoke)]);
            var rule = view.PersonalPeeragePolicy(force).Grades[grade];
            int count = view.World.Officers.Count(p => p.OwnerId == force && p.IsServing && p.PersonalPeerage?.Grade == grade);
            string limit = rule.Limit is int maximum ? $"{count}/{maximum}" : $"{count}/不限";
            string state = person.PersonalPeerage?.Grade == grade ? "当前爵位" : Reason(Check(option.Title));
            return new CatalogRow(i, [GradeName(grade), limit, rule.Salary, state]);
        }).ToArray();
        string name = Texts.Resolve(view.Officers.Single(p => p.Id == officerId).NameKey);
        Replace(new CatalogView { Title = name, HintText = "爵位由制度、政治地位、名额和宗室条件决定。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = GradesPage, ReadRows = _ => rows, Closed = ShowOfficers,
            Activated = index =>
            {
                var option = options[index];
                bool sameGrade = option.Grade is { } current && person.PersonalPeerage?.Grade == current;
                var rejection = sameGrade ? CommandRejection.None : Check(option.Title);
                if (rejection != CommandRejection.None) { Notice(Reason(rejection), () => ShowGrades(officerId)); return; }
                if (option.Grade is { } grade)
                {
                    Replace(new PeerageTitleEntryView { Textures = Textures, InitialTitle = sameGrade ? person.PersonalPeerage?.Title ?? "" : "",
                        Cancelled = () => ShowGrades(officerId), Confirmed = title =>
                        {
                            var peerage = new PersonalPeerage(grade, title);
                            var reason = Check(peerage);
                            if (reason != CommandRejection.None) { Notice(Reason(reason), () => ShowGrades(officerId)); return; }
                            Confirm($"授予{name} {title ?? GradeName(grade)}？", () => Execute(officerId, peerage), () => ShowGrades(officerId));
                        } });
                    return;
                }
                Confirm($"撤销{name}的爵位？", () => Execute(officerId, null), () => ShowGrades(officerId));
            } });
    }
    private void Execute(int officerId, PersonalPeerage? title)
    {
        if (Session.View.PlayerForceId is not int force) { Closed(); return; }
        var command = new SetPersonalPeerageCommand(Guid.NewGuid(), force, Session.Snapshot.Simulation.NextSequence, officerId, title);
        if (Session.Execute(new("player"), command).Submission.Accepted) ShowOfficers();
        else Notice("封爵失败，请检查当前回合及授爵条件。", () => ShowGrades(officerId));
    }
    private void Confirm(string message, Action yes, Action back)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Confirmed += yes; dialog.Dismissed += back; dialog.Closed += back; dialog.SetMessage(message);
    }
    private void Notice(string message, Action back)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Closed += back; dialog.SetNotice(message);
    }
}
