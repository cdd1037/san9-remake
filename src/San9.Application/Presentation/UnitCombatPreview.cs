using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Shared by deployed-unit details and future deployment drafts; never mutates the world.</summary>
public static class UnitCombatPreview
{
    public static CatalogCell[] Read(UnitState unit, IEnumerable<OfficerView> members, MilitaryRulebook? rules, bool naval)
    {
        int? id = !naval ? unit.FormationId : unit.NavalFormationId;
        if (naval && id is null && unit.FormationId is int primary && rules?.Formations[primary].TravelMode == TravelMode.Water) id = primary;
        var formation = id is int key && rules is not null ? rules.Formations[key] : null;
        if (formation is null || (formation.TravelMode == TravelMode.Water) != naval)
            return [CatalogCell.Unavailable, CatalogCell.Unavailable, CatalogCell.Unavailable, CatalogCell.Unavailable, CatalogCell.Unavailable];
        else
        {
            var combat = unit.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed && rules!.UnformedFormationId is int unformed
                ? rules.Formations[unformed].Combat : formation.Combat;
            CatalogCell mobility = new(((decimal)formation.MovementBudget / MapCoordinates.UnitsPerCell).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), formation.MovementBudget);
            if (combat is null) return [CatalogCell.Unavailable, CatalogCell.Unavailable, CatalogCell.Unavailable, CatalogCell.Unavailable, mobility];
            else
            {
                int leadership = members.Where(o => o.IsServing && o.OwnerId == unit.OwnerId && o.Journey is null).Select(o => o.Abilities.Leadership).DefaultIfEmpty(0).Max();
                var strength = new CombatStrength(unit.Soldiers, unit.Morale, leadership, 1,
                    unit.Condition switch { UnitCondition.Confused => 8, UnitCondition.Routed => 6, _ => 10 });
                var power = CombatMath.FormationStrength(strength, combat);
                return [power.AgainstUnits, power.AgainstGarrison,
                    CombatMath.WallAttackStrength(unit.Soldiers, leadership, combat.AgainstWalls), power.Defense, mobility];
            }
        }
    }
}
