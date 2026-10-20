using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot.Persistence;

public static class SaveSlotSummaryFactory
{
    public static SaveSlotSummary Create(GameSession session, ScenarioInitialState definition, TextCatalog texts, string note)
        => Create(session.View, definition, texts, note);

    public static SaveSlotSummary Create(StrategySnapshot view, ScenarioInitialState definition, TextCatalog texts, string note)
    {
        var force = view.Forces.Single(f => f.Id == view.PlayerForceId);
        var ruler = view.Officers.SingleOrDefault(o => o.Id == force.LeaderId);
        var simulation = view.World.Simulation;
        string xun = view.Day <= 10 ? "上旬" : view.Day <= 20 ? "中旬" : "下旬";
        return new(texts.Resolve(ruler?.NameKey ?? force.NameKey), ruler is null ? null : $"information-person-{ruler.Id}",
            $"{definition.Year,3}年{definition.Month,2}月", texts.Resolve(TextCatalog.ScenarioTitleKey), $"{view.Year,3}年{view.Month,2}月{xun}", DateTimeOffset.Now,
            Math.Max(1, simulation.Controls.Count(c => c.Kind == ControllerKind.Player)), null,
            simulation.ComputerDamageBonusPercent == 0 ? "标准" : "强化", note);
    }
}
