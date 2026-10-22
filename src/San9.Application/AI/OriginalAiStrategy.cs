using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Strategy preparation from public force facts, direct-corps aggregates and declared geography.</summary>
internal static class OriginalAiStrategy
{
    internal static bool IsOpponent(WorldSnapshot world, int owner, int target)
    {
        if (target == owner || !world.Forces.Any(f => f.Id == target && !f.IsDormant && !f.IsEliminated && !f.IsBarbarian)) return false;
        var alliance = world.Simulation.Alliance;
        if (alliance?.MemberForceIds.Contains(owner) == true)
        {
            if (alliance.TargetForceId == target) return true;
            if (alliance.MemberForceIds.Contains(target)) return false;
        }
        if (world.Simulation.AttackRequests.Any(r => r.RequestedForceId == owner && r.RequesterForceId == target ||
            r.RequestedForceId == target && r.RequesterForceId == owner)) return false;
        return (world.Relations.FirstOrDefault(r => r.FirstId == Math.Min(owner, target) && r.SecondId == Math.Max(owner, target))?.Value ?? 50) / 20 < 3;
    }
    internal static OriginalAiState PrepareGlobal(WorldSnapshot world, OriginalAiState state)
    {
        state = state.Reconcile(world);
        if (state.PreparedClock == world.Clock) return state;
        var valid = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated).ToArray();
        bool gate = world.Clock.DayNumber % 45 < 10;
        var order = state.DiplomaticOrder;
        if (state.RankedClock is null || gate)
            order = valid.OrderByDescending(f => (long)f.Trust +
                (world.Officers.FirstOrDefault(o => o.Id == f.LeaderId)?.BloodlineId is int family
                    ? state.Policy.FamilyRankBonuses.GetValueOrDefault(family) : 0)).Select(f => f.Id).ToImmutableArray();
        var groups = state.Forces;
        if (gate)
        {
            var players = world.Simulation.Controls.Where(c => c.ControllerId == "player").Select(c => c.ForceId).ToHashSet();
            var ordinary = valid.Where(f => !f.IsBarbarian && !players.Contains(f.Id)).ToArray();
            long Troops(ForceState f) => world.Units.Where(u => u.OwnerId == f.Id).Sum(u => (long)u.Soldiers);
            var chosen = new Dictionary<int, OriginalAiGroup>();
            foreach (var f in valid) chosen[f.Id] = OriginalAiGroup.Unassigned;
            if (valid.Count(f => !f.IsBarbarian) >= 4)
            {
                foreach (var f in valid.Where(f => f.IsBarbarian)) chosen[f.Id] = OriginalAiGroup.Favored;
                foreach (var f in ordinary.Where(f => f.LeaderId is int leader && state.Policy.PreferredStrategyRulerIds.Contains(leader)))
                    chosen[f.Id] = OriginalAiGroup.Favored;
                var remaining = ordinary.Where(f => chosen[f.Id] == OriginalAiGroup.Unassigned).OrderByDescending(Troops).ToArray();
                for (int i = 0; i < remaining.Length; i++)
                    chosen[remaining[i].Id] = i < remaining.Length / 2 ? OriginalAiGroup.Favored : OriginalAiGroup.ReducedActivity;
                if (!chosen.Values.Contains(OriginalAiGroup.ReducedActivity) && ordinary.OrderBy(Troops).FirstOrDefault() is { } weakest)
                    chosen[weakest.Id] = OriginalAiGroup.ReducedActivity;
            }
            groups = groups.Select(f => chosen.TryGetValue(f.ForceId, out var group) ? f with { Group = group } : f).ToImmutableArray();
        }
        return state with { PreparedClock = world.Clock, RankedClock = state.RankedClock is null || gate ? world.Clock : state.RankedClock,
            DiplomaticOrder = order, Forces = groups };
    }

    internal static (long Economy, long Military) Readiness(IEnumerable<SettlementState> places)
    {
        var all = places.ToArray();
        var cities = all.Where(s => s.Kind == SettlementKind.City).ToArray();
        static long Ratio(long value, long maximum) => maximum > 0 ? 100 * value / maximum : 100;
        return ((Ratio(cities.Sum(s => (long)s.Cultivation), cities.Sum(s => (long)s.MaxCultivation)) +
            Ratio(cities.Sum(s => (long)s.Commerce), cities.Sum(s => (long)s.MaxCommerce)) +
            Ratio(cities.Sum(s => (long)s.PublicOrder), 1000L * cities.Length)) / 3,
            (Ratio(all.Sum(s => (long)s.Durability), all.Sum(s => (long)s.MaxDurability)) +
                Ratio(all.Sum(s => (long)s.Morale), 100L * all.Length)) / 2);
    }

    internal static OriginalAiCorpsState PrepareCorps(WorldSnapshot world, CorpsState corps, OriginalAiState state,
        OriginalAiGeography geography, OriginalAiRandom random)
    {
        var previous = state.Corps.FirstOrDefault(c => c.CorpsId == corps.Id) ?? new(corps.Id, OriginalAiMode.General, null, null);
        var owner = world.Forces.Single(f => f.Id == corps.OwnerId);
        var valid = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated).ToArray();
        bool Valid(int id) => valid.Any(f => f.Id == id);
        var direct = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == corps.OwnerId &&
            (s.CorpsId == corps.Id || s.CorpsId is null && corps.IsRoot)).ToArray();
        var metrics = Readiness(direct);
        bool Player(int id) => world.Simulation.Controls.Any(c => c.ForceId == id && c.ControllerId == "player");
        int Relation(int from, int to) => world.Relations.FirstOrDefault(r => r.FirstId == Math.Min(from, to) && r.SecondId == Math.Max(from, to))?.Value ?? 50;
        bool AllianceMember(int id) => world.Simulation.Alliance?.MemberForceIds.Contains(id) == true;
        bool Opponent(int id) => IsOpponent(world, owner.Id, id);
        var frontier = direct.Where(s => geography.Frontier(s.Id, owner.Id)).ToArray();
        var neighbors = direct.SelectMany(s => geography.Neighbors(s.Id)).Select(p => p.OwnerId).OfType<int>()
            .Where(Opponent).Distinct().ToArray();
        int? allianceTarget = AllianceMember(owner.Id) && world.Simulation.Alliance is { } alliance && Valid(alliance.TargetForceId)
            ? alliance.TargetForceId : null;
        int? requestTarget = world.Simulation.AttackRequests.Where(r => r.RequestedForceId == owner.Id)
            .Select(r => (int?)r.TargetSettlementId).FirstOrDefault(id => world.Settlements.Any(s => s.Id == id && !s.IsDormant && !s.IsDestroyed && s.OwnerId != owner.Id));
        bool update = corps.Delegation is not GeneralDelegation || previous.Mode switch
        {
            OriginalAiMode.General => true,
            OriginalAiMode.Domestic => metrics.Economy > 50 || random.Percent(8),
            OriginalAiMode.MilitaryPreparation => metrics.Military > 50 || random.Percent(8),
            OriginalAiMode.TargetForce => previous.TargetForceId is not int target || !Opponent(target) ||
                valid.Any(f => Opponent(f.Id) && Relation(f.Id, owner.Id) / 20 < Relation(target, owner.Id) / 20) ||
                allianceTarget is int desired && desired != target || random.Percent(8),
            OriginalAiMode.TargetSettlement => !world.Settlements.Any(s => s.Id == previous.TargetSettlementId && !s.IsDestroyed && !s.IsDormant && s.OwnerId != owner.Id) ||
                requestTarget is int requested && requested != previous.TargetSettlementId || random.Percent(8),
            OriginalAiMode.NoFrontier => frontier.Length > 0 || random.Percent(8),
            _ => throw new InvalidOperationException("Unsupported original AI mode."),
        };
        if (!update) return previous;
        OriginalAiCorpsState Mode(OriginalAiMode mode, int? force = null, int? settlement = null) => new(corps.Id, mode, force, settlement);
        OriginalAiCorpsState Fallback() => Mode(metrics.Economy < 50 ? OriginalAiMode.Domestic : metrics.Military >= 50
            ? OriginalAiMode.General : OriginalAiMode.MilitaryPreparation);
        int Classify(int target)
        {
            int value = Relation(target, owner.Id);
            if (!Opponent(target)) return Math.Max(value / 20, 3);
            int ambition = world.Officers.FirstOrDefault(o => o.Id == corps.LeaderId)?.Ambition ?? 0;
            return Math.Min(4, Math.Max(0, value - ambition - random.Next(ambition) - (Player(target) ? 10 : 0)) / 20);
        }
        if (!owner.IsBarbarian)
        {
            switch (corps.Delegation)
            {
                case DomesticDelegation: return Mode(OriginalAiMode.Domestic);
                case DefeatForceDelegation target when Valid(target.ForceId) && target.ForceId != owner.Id:
                    return Mode(OriginalAiMode.TargetForce, target.ForceId);
                case CaptureSettlementDelegation target when world.Settlements.Any(s => s.Id == target.SettlementId && !s.IsDormant && !s.IsDestroyed && s.OwnerId != owner.Id):
                    return Mode(OriginalAiMode.TargetSettlement, settlement: target.SettlementId);
            }
            if (allianceTarget is int ally) return Mode(OriginalAiMode.TargetForce, ally);
        }
        if (requestTarget is int request) return Mode(OriginalAiMode.TargetSettlement, settlement: request);
        if (owner.IsBarbarian)
        {
            if (neighbors.OrderBy(id => Relation(owner.Id, id)).Select(id => (int?)id).FirstOrDefault() is not int target)
                return Mode(OriginalAiMode.General);
            int band = Relation(owner.Id, target) / 20 + (Player(target) ? 0 : 1);
            return band <= 1 || band == 2 && random.Percent(5 * Math.Max(0, 60 - Relation(owner.Id, target)))
                ? Mode(OriginalAiMode.TargetForce, target) : Mode(OriginalAiMode.General);
        }
        if (frontier.Length == 0) return Mode(OriginalAiMode.NoFrontier);
        var regular = valid.Where(f => !f.IsBarbarian).ToArray();
        if (regular.Length == 2 && regular.FirstOrDefault(f => f.Id != owner.Id) is { } other)
            return Classify(other.Id) <= 2 ? Mode(OriginalAiMode.TargetForce, other.Id) : Fallback();
        var nearCities = direct.Where(s => geography.Priority(s.Id, owner.Id))
            .Select(s => geography.Places.FirstOrDefault(p => p.Id == s.Id)).OfType<OriginalAiGeography.Place>()
            .SelectMany(p => geography.NearbyCities(p.CityId)).Distinct().ToHashSet();
        var nearby = geography.Places.Where(p => nearCities.Contains(p.CityId)).ToArray();
        var counts = nearby.Where(p => p.OwnerId is int id && id != owner.Id && Valid(id)).GroupBy(p => p.OwnerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        // The original requires both force and region statistics. Missing authored region
        // classification is unavailable information, so use its existing metric fallback.
        if (counts.Count == 0 || !nearby.Any(p => p.ProvinceId is not null && p.OwnerId is int id && id != owner.Id) || neighbors.Length == 0)
            return Fallback();
        var ranked = neighbors.Where(id => state.DiplomaticOrder.Contains(id)).ToArray();
        if (neighbors.Length > 1 && ranked.Length == 0) return Fallback();
        int selected = neighbors.Length == 1 ? neighbors[0] : ranked.OrderByDescending(id =>
            50L * (counts.GetValueOrDefault(id) + 250L * (2 * Math.Max(0, 5 - Relation(id, owner.Id) / 20) + (Player(id) ? 1 : 0))) +
            Math.Max(0, 50 - (state.DiplomaticOrder.Length - state.DiplomaticOrder.IndexOf(id)))).First();
        int classification = Classify(selected);
        return classification <= 1 ? Mode(OriginalAiMode.TargetForce, selected) : classification <= 3 ? Fallback() : previous;
    }
}
