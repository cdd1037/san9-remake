using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class FormationTeachingView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public int SettlementId { get; init; }
    private Control? _page;
    private int _teacher, _student;
    private static readonly CatalogPage[] PeoplePage = [new("武将", [new("name", "武将", 16, CatalogAlignment.Left),
        new("status", "状态", 28, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] FormationPage = [new("阵形", [new("name", "阵形", 16, CatalogAlignment.Left),
        new("teacher", "教师熟练", 12, CatalogAlignment.Right), new("status", "状态", 24, CatalogAlignment.Left)])];
    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; ShowTeachers(); }
    private void Replace(Control page) { if (_page is not null) { RemoveChild(_page); _page.QueueFree(); } _page = page; AddChild(page); }
    private string Person(int id) => Texts.Resolve(Session.View.MessageSpeaker(id).NameKey);
    private string Formation(int id) => Session.View.Information.Formations.FirstOrDefault(f => f.Id == id)?.Name ?? $"阵形{id}";
    private OfficerState[] People() => Session.Snapshot.Officers.Where(p => !p.IsDormant && p.IsServing &&
        p.OwnerId == Session.View.PlayerForceId && p.SettlementId == SettlementId && p.UnitId is null).OrderBy(p => p.Id).ToArray();
    private string State(OfficerState person)
    {
        if (person.FormationTeaching is { } lesson) return $"学习{Formation(lesson.FormationId)}，剩{lesson.DaysRemaining}日";
        var pupil = Session.Snapshot.Officers.FirstOrDefault(p => p.FormationTeaching?.TeacherId == person.Id);
        if (pupil?.FormationTeaching is { } teaching) return $"教授{Person(pupil.Id)}，剩{teaching.DaysRemaining}日";
        return person.Acted ? "已行动" : "可行动";
    }
    private void ShowTeachers()
    {
        var people = People(); var rows = people.Select(p => new CatalogRow(p.Id, [Person(p.Id), State(p)])).ToArray();
        Replace(new CatalogView { Title = "阵形学习", HintText = "选择已掌握阵形的教师；教学持续30日。", CloseTitle = "中止",
            Textures = Textures, Service = Service, Pages = PeoplePage, ReadRows = _ => rows, Closed = Closed,
            Activated = id => { var p = people.Single(x => x.Id == id); if (State(p) != "可行动") { Notice(State(p), ShowTeachers); return; }
                _teacher = id; ShowStudents(); } });
    }
    private void ShowStudents()
    {
        var people = People().Where(p => p.Id != _teacher).ToArray();
        var rows = people.Select(p => new CatalogRow(p.Id, [Person(p.Id), State(p)])).ToArray();
        Replace(new CatalogView { Title = "选择学生", HintText = $"教师：{Person(_teacher)}。选择同据点武将。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = PeoplePage, ReadRows = _ => rows, Closed = ShowTeachers,
            Activated = id => { var p = people.Single(x => x.Id == id); if (State(p) != "可行动") { Notice(State(p), ShowStudents); return; }
                _student = id; ShowFormations(); } });
    }
    private void ShowFormations()
    {
        var teacher = Session.Snapshot.Officers.Single(p => p.Id == _teacher);
        var skills = teacher.FormationProficiencies ?? System.Collections.Immutable.ImmutableSortedDictionary<int, int>.Empty;
        var rows = skills.Select(k =>
        {
            var result = Session.Validate(new("player"), Command(k.Key));
            return new CatalogRow(k.Key, [Formation(k.Key), k.Value, result.Accepted ? "可学习" : "已掌握或不符合条件"]);
        }).ToArray();
        Replace(new CatalogView { Title = "选择阵形", HintText = $"{Person(_teacher)}教授{Person(_student)}；完成后熟练度为0。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = FormationPage, ReadRows = _ => rows, Closed = ShowStudents,
            Activated = id =>
            {
                if (!Session.Validate(new("player"), Command(id)).Accepted) { Notice("无法学习：已掌握或人员、阵形条件不符。", ShowFormations); return; }
                var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
                dialog.Confirmed += () => Start(id); dialog.Dismissed += ShowFormations; dialog.Closed += ShowFormations;
                dialog.SetMessage($"由{Person(_teacher)}教授{Person(_student)}{Formation(id)}？\n需要30日。");
            } });
    }
    private StartFormationTeachingCommand Command(int formation) => new(Guid.NewGuid(), Session.View.PlayerForceId!.Value,
        Session.Snapshot.Simulation.NextSequence, SettlementId, _teacher, _student, formation);
    private void Start(int formation)
    {
        var result = Session.Execute(new("player"), Command(formation));
        if (!result.Submission.Accepted) { Notice("无法开始教学，请检查当前回合及人员状态。", ShowTeachers); return; }
        ShowTeachers();
    }
    private void Notice(string text, Action back) { var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog); dialog.Closed += back; dialog.SetNotice(text); }
}
