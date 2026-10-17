using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Projects formation capabilities without changing an officer's retained equipment.</summary>
internal static class UnitTacticDisplay
{
    internal static CatalogCell Read(StrategySnapshot view, MilitaryRulebook? rules, TextCatalog texts,
        UnitState unit, OfficerState officer)
    {
        if (unit.OwnerId != view.PlayerForceId && !view.World.Simulation.OtherForceTacticsVisible)
            return new("＊＊", IsUnavailable: true);
        if (rules?.TacticFormation(unit) is not { Tactics: { } profile } formation)
            return CatalogCell.Unavailable;
        var ids = profile.AutomaticTactics.IsEmpty
            ? officer.EquippedTacticId is int equipped ? new[] { equipped } : []
            : profile.AutomaticTactics.OrderByDescending(id => rules.Tactics[id].Power).ThenBy(id => id).ToArray();
        string[] names = ids.Where(id => rules.Tactics[id].CompatibleFormations.Contains(formation.Id))
            .Select(id => view.Information.Tactics.First(t => t.Id == id))
            .Select(t => texts.Resolve(t.NameKey)).ToArray();
        return names.Length == 0 ? new("") : new(string.Join("／", names));
    }
}
