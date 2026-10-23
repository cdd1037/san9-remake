using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot.Persistence;

public static class SaveSlotSummaryFactory
{
    public static SaveSlotSummary Create(GameSession session, ScenarioInitialState definition, TextCatalog texts,
        IReadOnlyDictionary<string, string> illustrationRoles, string note)
        => Create(session.View, definition, texts, illustrationRoles, note);

    public static SaveSlotSummary Create(StrategySnapshot view, ScenarioInitialState definition, TextCatalog texts,
        IReadOnlyDictionary<string, string> illustrationRoles, string note)
    {
        var force = view.Forces.SingleOrDefault(f => f.Id == view.PlayerForceId);
        var ruler = view.Officers.SingleOrDefault(o => o.Id == force?.LeaderId);
        var simulation = view.World.Simulation;
        string xun = view.Day <= 10 ? "上旬" : view.Day <= 20 ? "中旬" : "下旬";
        string? portrait = ruler is null ? null : illustrationRoles[$"information-person-{ruler.Id}"];
        return new(definition.ScenarioId, force is null ? simulation.Controls.Any(c => c.Kind == ControllerKind.Player) ? "玩家势力" : "无玩家势力" : texts.Resolve(ruler?.NameKey ?? force.NameKey), portrait,
            $"{definition.Year,3}年{definition.Month,2}月", texts.Resolve(TextCatalog.ScenarioTitleKey), $"{view.Year,3}年{view.Month,2}月{xun}", DateTimeOffset.Now,
            simulation.Controls.Count(c => c.Kind == ControllerKind.Player), simulation.NewGame.Mode == San9.Core.Domain.GameMode.Historical,
            simulation.NewGame.Difficulty == San9.Core.Domain.GameDifficulty.Advanced ? "高级" : "初级", note);
    }
}
