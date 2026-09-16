using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum CompensationKind { Free, Prisoner, Money, Treasure, Refuse }

public sealed record CompensationOffer(
    [property: JsonRequired] CompensationKind Kind,
    [property: JsonRequired] uint Money,
    [property: JsonRequired] int? PrisonerId,
    [property: JsonRequired] int? TreasureId)
{
    [JsonIgnore] public bool IsValid => Kind switch
    {
        CompensationKind.Free or CompensationKind.Refuse => Money == 0 && PrisonerId is null && TreasureId is null,
        CompensationKind.Prisoner => Money == 0 && PrisonerId is >= 0 && TreasureId is null,
        CompensationKind.Money => Money is > 0 and <= 1000000 && PrisonerId is null && TreasureId is null,
        CompensationKind.Treasure => Money == 0 && PrisonerId is null && TreasureId is >= 0,
        _ => false
    };
}

internal static class PrisonerCompensation
{
    internal static int? Captor(WorldMutation world, OfficerState person) => person.Status != OfficerStatus.Captive ? null :
        person.SettlementId is int place && world.Settlements.TryGetValue(place, out var settlement) ? settlement.OwnerId :
        person.UnitId is int unitId && world.Units.TryGetValue(unitId, out var unit) ? unit.OwnerId : null;

    internal static bool IsHeld(WorldMutation world, int personId, int allegiance, int captor) =>
        allegiance != captor && world.Officers.TryGetValue(personId, out var person) && person.OwnerId == allegiance &&
        Captor(world, person) == captor;

    internal static int? ExchangeCandidate(WorldMutation world, int requester, int target) =>
        world.Officers.Values.Where(p => IsHeld(world, p.Id, target, requester))
            .OrderByDescending(p => Total(world.Abilities(p))).ThenBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefault();

    private static long Total(OfficerAbilities value) => (long)value.War + value.Intelligence + value.Politics + value.Leadership;

    internal static uint Value(WorldMutation world, int requester, int envoy, int target, int prisoner) =>
        DiplomacyMath.PrisonerCompensation(world.Abilities(world.Officers[prisoner]),
            world.Abilities(world.Officers[envoy]).Politics, MilitaryDiplomacy.Relation(world, requester, target));

    internal static CompensationOffer Quote(WorldMutation world, int requester, int envoy, int target, int prisoner,
        TreasureRulebook treasures)
    {
        var refused = new CompensationOffer(CompensationKind.Refuse, 0, null, null);
        if (!IsHeld(world, prisoner, requester, target) || !world.Forces.TryGetValue(requester, out var source) || source.IsEliminated ||
            !world.Forces.TryGetValue(target, out var other) || other.IsEliminated ||
            !world.Officers.TryGetValue(envoy, out var actor) || !actor.IsServing || actor.OwnerId != requester)
            return refused;
        int? exchange = ExchangeCandidate(world, requester, target);
        if (exchange is null && MilitaryDiplomacy.Relation(world, requester, target) >= 80)
            return new(CompensationKind.Free, 0, null, null);
        if (exchange is int id) return new(CompensationKind.Prisoner, 0, id, null);
        return PaymentQuote(world, requester, envoy, target, prisoner, treasures);
    }

    internal static CompensationOffer PaymentQuote(WorldMutation world, int requester, int envoy, int target, int prisoner,
        TreasureRulebook treasures)
    {
        var source = world.Forces[requester];
        var other = world.Forces[target];
        var refused = new CompensationOffer(CompensationKind.Refuse, 0, null, null);
        uint amount = Value(world, requester, envoy, target, prisoner);
        if (DiplomacyMath.CanQuotePrisonerRansom(source.Money, amount))
            return new(CompensationKind.Money, amount, null, null);
        if (source.LeaderId is not int sourceRuler || other.LeaderId is not int targetRuler ||
            !world.Officers[sourceRuler].IsServing || !world.Officers[targetRuler].IsServing) return refused;
        var candidates = DiplomacyTreasures.Candidates(world.Treasures.Values, treasures, sourceRuler, targetRuler);
        return candidates.IsEmpty ? refused : new(CompensationKind.Treasure, 0, null, candidates[0]);
    }

    // Validate the actual offer against current ownership and payment ability. This is separate
    // from the AI's willingness to accept and from its automatic choice of an offer.
    internal static bool Payable(WorldMutation world, int requester, int target, CompensationOffer offer,
        TreasureRulebook treasures)
    {
        if (!offer.IsValid || requester == target || !world.Forces.TryGetValue(requester, out var source) || source.IsEliminated ||
            !world.Forces.TryGetValue(target, out var recipient) || recipient.IsEliminated) return false;
        return offer.Kind switch
        {
            CompensationKind.Free => true,
            CompensationKind.Prisoner => IsHeld(world, offer.PrisonerId!.Value, target, requester),
            CompensationKind.Money => source.Money >= offer.Money,
            CompensationKind.Treasure => source.LeaderId is int ruler && recipient.LeaderId is int targetRuler &&
                world.Officers[ruler].IsServing && world.Officers[targetRuler].IsServing &&
                world.Treasures.TryGetValue(offer.TreasureId!.Value, out var item) && item.Discovered && item.HolderId == ruler &&
                treasures.Items.ContainsKey(item.Id),
            _ => false
        };
    }
}
