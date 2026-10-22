using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Projects retained equipment and its formation compatibility, independently of automatic tactics.</summary>
internal static class UnitTacticDisplay
{
    internal static CatalogCell Read(StrategySnapshot view, MilitaryRulebook? rules, TextCatalog texts,
        UnitState unit, OfficerState officer)
    {
        if (unit.OwnerId != view.PlayerForceId && !view.World.Simulation.OtherForceTacticsVisible)
            return new("＊＊", IsUnavailable: true);
        if (rules?.TacticFormation(unit) is not { } formation)
            return CatalogCell.Unavailable;
        if (officer.EquippedTacticId is not int equipped) return new("");
        var tactic = view.Information.Tactics.First(t => t.Id == equipped);
        return new(texts.Resolve(tactic.NameKey),
            IsUnavailable: !rules.Tactics[equipped].CompatibleFormations.Contains(formation.Id));
    }
}
