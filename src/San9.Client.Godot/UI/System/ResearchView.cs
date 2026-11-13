using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Research selection and confirmation use the same catalog and dialog as other commands.</summary>
public partial class ResearchView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public int? InitialSettlementId { get; init; }
    private Control? _page;
    private int? _place;
    private static readonly CatalogPage[] Pages = [new("研究", [
        new("name", "项目", 20, CatalogAlignment.Left), new("cost", "资金", 10, CatalogAlignment.Right),
        new("days", "日数", 8, CatalogAlignment.Right), new("state", "状态／条件", 30, CatalogAlignment.Left)])];

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        int? forceId = Session.View.PlayerForceId;
        var world = Session.Snapshot;
        var seat = world.Simulation.Controls.FirstOrDefault(c => c.ForceId == forceId && c.Kind == ControllerKind.Player);
        var valid = seat is null ? [] : world.Settlements.Where(p => !p.IsDormant && !p.IsDestroyed && p.CanHostPersons &&
            CorpsControl.CanControlSettlement(world, seat, new CommandContext("player"), p.Id)).OrderBy(p => p.Id).Select(p => p.Id).ToArray();
        _place = InitialSettlementId is int place && valid.Contains(place) ? place : valid.Select(p => (int?)p).FirstOrDefault();
        ShowCatalog();
    }

    private void Replace(Control page)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = page; AddChild(page);
    }

    private void ShowCatalog()
    {
        var projects = Session.ResearchProjects.ToArray();
        var force = Session.Snapshot.Forces.FirstOrDefault(f => f.Id == Session.View.PlayerForceId);
        string State(int index)
        {
            var project = projects[index];
            if (force is null || _place is null) return "无可操作据点";
            if (force.CompletedResearch.Contains(project.Id)) return "已完成";
            if (force.ActiveResearch is { } active)
                return active.ProjectId == project.Id ? $"进行中，剩余{active.DaysRemaining}日" : "其他研究进行中";
            var missing = project.Prerequisites.Where(id => !force.CompletedResearch.Contains(id)).Select(Session.ResearchProjectName).ToArray();
            return missing.Length == 0 ? "可研究" : "需要 " + string.Join("、", missing);
        }
        var placeName = _place is int id ? Session.View.Settlements.Single(p => p.Id == id).ResolveName(Texts) : "无";
        var rows = projects.Select((p, i) => new CatalogRow(i, [Session.ResearchProjectName(p.Id), p.Cost, p.Days, State(i)])).ToArray();
        Replace(new CatalogView { Title = "军事研究", HintText = $"执行设施　{placeName}。选择研究项目查看条件。",
            CloseTitle = "中止", Textures = Textures, Service = Service, Pages = Pages, ReadRows = _ => rows, Closed = Closed,
            Activated = index =>
            {
                var project = projects[index]; string state = State(index);
                if (state != "可研究") { Notice(state); return; }
                Confirm($"研究 {Session.ResearchProjectName(project.Id)}？\n资金 {project.Cost}　{project.Days} 日。", () => Start(project.Id));
            } });
    }

    private void Start(string projectId)
    {
        if (_place is not int place || Session.View.PlayerForceId is not int force) { Notice("当前没有可操作的据点。"); return; }
        var result = Session.Execute(new("player"), new StartResearchCommand(Guid.NewGuid(), force,
            Session.Snapshot.Simulation.NextSequence, place, projectId));
        if (result.Submission.Accepted) { ShowCatalog(); return; }
        Notice(result.Submission.Rejection == CommandRejection.InsufficientFunds ? "研究失败，资金不足。" : "研究失败，请检查当前回合、据点控制权和研究条件。");
    }
    private void Confirm(string message, Action yes)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Confirmed += yes; dialog.Dismissed += ShowCatalog; dialog.Closed += ShowCatalog; dialog.SetMessage(message);
    }
    private void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Closed += ShowCatalog; dialog.SetNotice(message);
    }
}
