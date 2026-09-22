using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum PeerageChangeKind { Investiture, Promotion, Abdication, Proclamation, HanzhongKing, ShanyangGong }
public sealed record PeerageChangedEvent(GameClock Clock, int ForceId, int? PreviousRankId, int RankId,
    PeerageChangeKind Kind, int? CountryTitleId) : DomainEvent;
public sealed record CountryTitleOfferedEvent(GameClock Clock, int ForceId, ImmutableArray<int> CandidateIds) : DomainEvent;
public sealed record CountryTitlePrompt(int ForceId, PeerageChangeKind Kind, ImmutableArray<int> CandidateIds);
public sealed record CountryTitleEventSession(int ForceId, PeerageChangeKind ChangeKind, ImmutableArray<int> CandidateIds) : EventSession
{
    public override ResolutionStage Stage => ResolutionStage.QuarterPolitics;
    public override int RespondingForceId => ForceId;
}
public sealed record ChooseCountryTitleCommand(Guid CommandId, int IssuerId, long Sequence,
    [property: System.Text.Json.Serialization.JsonRequired] int CountryTitleId) : GameCommand(CommandId, IssuerId, Sequence);

internal static class PoliticalEvents
{
    internal static ImmutableArray<int> ControlledProvinces(WorldMutation world, PersonnelRulebook personnel, int forceId) =>
        personnel.CityProvinces.GroupBy(p => p.Value).Where(g => g.All(p => world.Settlements.TryGetValue(p.Key, out var city) &&
            city.Kind == SettlementKind.City && !city.IsDestroyed && city.OwnerId == forceId)).Select(g => g.Key).Order().ToImmutableArray();

    private static bool Eligible(WorldMutation world, SimulationRules rules, ForceState force) =>
        rules.Politics.Events is { } definition && !force.IsEliminated && force.LeaderId is not null &&
        force.RankId == rules.Politics.ProclamationRankId && force.Trust >= definition.ProclamationTrust &&
        (world.Simulation.ImperialCourtEnded || ImperialCourt.City(world, rules.Economy) is int city && world.Settlements[city].OwnerId == force.Id);

    private static HashSet<int> Occupied(WorldMutation world) => world.AllForces.Values
        .Where(f => !f.IsEliminated && f.CountryTitleId is not null).Select(f => f.CountryTitleId!.Value).ToHashSet();

    internal static void Begin(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Politics.Events is not { } definition || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        var forces = world.Forces.Values.Where(f => !f.IsEliminated && f.LeaderId is not null).OrderBy(f => f.Id).ToArray();
        var candidate = forces.FirstOrDefault(f => Eligible(world, rules, f));
        if (candidate is not null)
        {
            var kind = world.Simulation.ImperialCourtEnded ? PeerageChangeKind.Proclamation : PeerageChangeKind.Abdication;
            int? title = candidate.CountryTitleId;
            if (title is null)
            {
                var available = rules.Politics.ManualCandidates(Occupied(world));
                bool player = world.Simulation.Controls.Any(c => c.ForceId == candidate.Id && c.Kind == ControllerKind.Player);
                if (player && !available.IsEmpty)
                {
                    world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.AwaitingDecision,
                        PendingEvent = new CountryTitleEventSession(candidate.Id, kind, available) });
                    events.Add(new CountryTitleOfferedEvent(world.Clock, candidate.Id, available));
                    return;
                }
                int waitingPlayers = forces.Count(f => f.RankId != rules.Politics.ImperialRankId &&
                    world.Simulation.Controls.Any(c => c.ForceId == f.Id && c.Kind == ControllerKind.Player));
                if (player || available.Length <= waitingPlayers) title = AutomaticTitle(world, rules, candidate);
            }
            Commit(world, rules, candidate.Id, rules.Politics.ImperialRankId!.Value, title, kind, events);
            return;
        }
        foreach (var original in forces)
        {
            var force = world.Forces[original.Id];
            if (!world.Simulation.ImperialCourtEnded && (force.IsBarbarian || force.RankId == rules.Politics.ProclamationRankId ||
                force.CountryTitleId is int title && rules.Politics.Titles[title].PreventsInvestiture)) continue;
            var next = rules.Politics.NextRank(force.RankId);
            if (next is null || (force.RankId == rules.Politics.ProvinceEntryRankId
                ? ControlledProvinces(world, rules.Personnel, force.Id).IsEmpty : force.Trust < next.RequiredTrust)) continue;
            Commit(world, rules, force.Id, next.Id, force.CountryTitleId,
                world.Simulation.ImperialCourtEnded ? PeerageChangeKind.Promotion : PeerageChangeKind.Investiture, events);
        }
    }

    private static int? AutomaticTitle(WorldMutation world, SimulationRules rules, ForceState force)
    {
        var ruler = world.Officers[force.LeaderId!.Value];
        int? place = ruler.UnitId is int unit ? world.Units[unit].ServicePlaceId : ruler.SettlementId ?? ruler.RegionId;
        int? city = place is int home ? rules.Personnel.CityProvinces.ContainsKey(home) ? home :
            rules.Personnel.RegionCities.TryGetValue(home, out int related) ? related : null : null;
        int? province = city is int id && rules.Personnel.CityProvinces.TryGetValue(id, out int value) ? value : null;
        return rules.Politics.SelectAutomatic(ruler.Id, ruler.BloodlineId, province,
            ControlledProvinces(world, rules.Personnel, force.Id), Occupied(world), () => world.NextRandom("event-country-title"));
    }

    internal static CommandRejection Respond(WorldMutation world, SimulationRules rules, ChooseCountryTitleCommand command, List<DomainEvent> events)
    {
        if (world.Simulation.PendingEvent is not CountryTitleEventSession pending || pending.ForceId != command.IssuerId ||
            !pending.CandidateIds.Contains(command.CountryTitleId) || !Eligible(world, rules, world.Forces[pending.ForceId]) ||
            world.Forces[pending.ForceId].CountryTitleId is not null ||
            pending.ChangeKind != (world.Simulation.ImperialCourtEnded ? PeerageChangeKind.Proclamation : PeerageChangeKind.Abdication) ||
            !rules.Politics.ManualCandidates(Occupied(world)).Contains(command.CountryTitleId)) return CommandRejection.InvalidDecision;
        world.SetSimulation(world.Simulation with { PendingEvent = null, Boundary = SimulationBoundary.Stable });
        Commit(world, rules, pending.ForceId, rules.Politics.ImperialRankId!.Value, command.CountryTitleId, pending.ChangeKind, events);
        return CommandRejection.None;
    }

    private static void Commit(WorldMutation world, SimulationRules rules, int forceId, int rankId, int? titleId,
        PeerageChangeKind kind, List<DomainEvent> events)
    {
        int reward = kind == PeerageChangeKind.Abdication ? 100 : kind == PeerageChangeKind.Proclamation ? 0 : 10;
        if (kind == PeerageChangeKind.Abdication)
            world.SetSimulation(world.Simulation with { ImperialCourtEnded = true, ConsumedEventKeys = world.Simulation.ConsumedEventKeys
                .Append(rules.Politics.Events!.CourtEndedConsumptionKey).Distinct().OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray() });
        GrantRank(world, rules, forceId, rankId, titleId, kind, reward, events);
    }

    internal static void GrantRank(WorldMutation world, SimulationRules rules, int forceId, int rankId, int? titleId,
        PeerageChangeKind kind, int baseTrustBonus, List<DomainEvent> events, IReadOnlySet<int>? loyaltyRecipients = null)
    {
        var force = world.Forces[forceId];
        world.Replace(force with { RankId = rankId, CountryTitleId = titleId, BaseTrust = (int)Math.Min(1000L, (long)force.BaseTrust + baseTrustBonus) });
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId && p.Status != OfficerStatus.Ruler &&
            (loyaltyRecipients is null || loyaltyRecipients.Contains(p.Id))).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, forceId,
                (int)Math.Min(255, (long)person.LoyaltyTo(forceId) + rules.Politics.Ranks[rankId].LoyaltyBonus), "peerage", events);
        events.Add(new PeerageChangedEvent(world.Clock, forceId, force.RankId, rankId, kind, titleId));
    }

    internal static void ValidateRules(WorldMutation world, SimulationRules rules)
    {
        if (world.AllForces.Values.Any(f => f.CountryTitleId is int title && !rules.Politics.Titles.ContainsKey(title) ||
                (rules.Politics.Events is not null || rules.HanzhongKing is not null || rules.ShanyangGong is not null) && f.RankId is int rank && !rules.Politics.Ranks.ContainsKey(rank)) ||
            rules.Politics.Titles.Values.Any(t => t.PreferredRulerIds.Any(id => !world.AllOfficers.ContainsKey(id))) ||
            rules.Politics.ProvinceTitles.Keys.Any(id => !rules.Personnel.CityProvinces.ContainsValue(id)))
            throw new ArgumentException("Unknown political content reference.");
        if (world.Simulation.PendingEvent is CountryTitleEventSession pending &&
            (!Eligible(world, rules, world.Forces[pending.ForceId]) ||
             pending.ChangeKind != (world.Simulation.ImperialCourtEnded ? PeerageChangeKind.Proclamation : PeerageChangeKind.Abdication) ||
             !pending.CandidateIds.SequenceEqual(rules.Politics.ManualCandidates(Occupied(world)))))
            throw new ArgumentException("Invalid country title candidates.");
    }

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingEvent is not CountryTitleEventSession pending) return;
        if (!world.Clock.Boundary.Quarter || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30 ||
            world.Simulation.ActiveControl != world.Simulation.Controls.Length ||
            !world.Forces.TryGetValue(pending.ForceId, out var force) || force.IsEliminated || force.CountryTitleId is not null ||
            !world.Simulation.Controls.Any(c => c.ForceId == pending.ForceId && c.Kind == ControllerKind.Player) ||
            pending.ChangeKind is not (PeerageChangeKind.Abdication or PeerageChangeKind.Proclamation) || pending.CandidateIds.IsDefaultOrEmpty ||
            !pending.CandidateIds.SequenceEqual(pending.CandidateIds.Distinct().Order()) || pending.CandidateIds.Any(id => id < 0 || Occupied(world).Contains(id)))
            throw new ArgumentException("Invalid country title event session.");
    }
}
