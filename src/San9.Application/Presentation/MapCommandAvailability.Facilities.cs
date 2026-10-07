using San9.Core.Domain;

namespace San9.Application.Presentation;

internal sealed partial class MapCommandAvailability
{
    private static bool IsDomestic(string command) => command is
        "patrol" or "commerce" or "cultivate" or "repair" or "recruit" or "train" or "buy" or "sell";

    private string? Domestic(SettlementState home, string command)
    {
        var treasury = Treasury(home.CorpsId);
        // RE-0095/0096/0097: the first failed condition determines the visible explanation.
        return command switch
        {
            "patrol" or "commerce" or "cultivate" or "repair" => treasury.Money < 50 ? "funds" : command switch
            {
                "patrol" => home.PatrolUsed ? "already-used" : home.PublicOrder >= 1000 ? "public-order-max" : null,
                "commerce" => home.CommerceUsed ? "already-used" : home.Commerce >= home.MaxCommerce ? "commerce-max" : null,
                "cultivate" => home.CultivationUsed ? "already-used" : home.Cultivation >= home.MaxCultivation ? "cultivation-max" : null,
                _ => home.RepairUsed ? "already-used" : home.Durability >= home.MaxDurability ? "durability-max" : null
            },
            "recruit" => home.Soldiers >= home.SoldierCapacity ? "soldiers-max"
                : home.RecruitUsed ? "recruit-used" : home.DraftPopulation == 0 ? "no-recruits"
                : treasury.Money == 0 ? "recruit-no-funds" : null,
            "train" => home.Soldiers == 0 ? "no-soldiers" : home.TrainUsed ? "already-used"
                : home.Morale >= 100 ? "morale-max" : null,
            "buy" => treasury.Money == 0 ? "no-funds" : treasury.Food >= 1000000 ? "food-max"
                : !home.MerchantPresent ? "merchant-absent" : home.FoodTradeUsed ? "trade-used" : null,
            "sell" => treasury.Food == 0 ? "no-food" : treasury.Food < view.World.Simulation.FoodExchangeRate ? "insufficient-food"
                : treasury.Money >= 1000000 ? "money-max" : !home.MerchantPresent ? "merchant-absent"
                : home.FoodTradeUsed ? "trade-used" : null,
            _ => "unavailable"
        };
    }

    private IEnumerable<FacilityRule> BuildableTypes()
    {
        if (view.MilitaryRules is not { } rules) return [];
        return rules.Facilities.Values.Where(f => f.Buildable && !ConstructionLocations.Read(view, Owner, f.Id).IsEmpty);
    }
    private int MinimumConstructionCost(SettlementState home)
    {
        var actors = view.World.Officers.Where(p => people.TryGetValue(p.Id, out var officer) && officer.IsAvailable &&
            p.OwnerId == Owner && p.SettlementId == home.Id).ToArray();
        var treasures = new TreasureRulebook(view.Information.Treasures.Select(t => t.ToRule()));
        return BuildableTypes().Select(type => type.Cost(type.CostReductionTacticId is int tactic ?
            actors.Count(p => TacticAvailability.HasForMembers(tactic, [p], Force, view.World.Treasures, treasures)) : 0)).DefaultIfEmpty(int.MaxValue).Min();
    }
}
