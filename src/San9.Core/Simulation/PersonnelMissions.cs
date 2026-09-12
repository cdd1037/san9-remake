using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RecruitOfficerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetOfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record MoveOfficersCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> OfficerIds, int DestinationId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record SearchCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int DestinationId)
    : GameCommand(CommandId, IssuerId, Sequence)
{
    public bool AcceptRecruitment { get; init; } = true;
}
public enum SearchResultKind { None, Person, Treasure, Money }
public sealed record SearchResolvedEvent(GameClock Clock, int OfficerId, int PlaceId, int? DiscoveredOfficerId, bool Recruited) : DomainEvent
{
    public SearchResultKind Result { get; init; }
    public int? TreasureId { get; init; }
    public long Money { get; init; }
}
public sealed record RecruitmentResolvedEvent(GameClock Clock, int OfficerId, int TargetOfficerId, int Threshold, int Roll, bool Success) : DomainEvent;
public sealed record PersonAffiliationChangedEvent(GameClock Clock, int OfficerId, int? PreviousForceId, int? ForceId, int PlaceId) : DomainEvent;
public sealed record OfficerMovedEvent(GameClock Clock, int OfficerId, int OriginId, int DestinationId) : DomainEvent;
public sealed record ForceRelationChangedEvent(GameClock Clock, int FirstId, int SecondId, int Previous, int Current) : DomainEvent;
public sealed record OfficerAbilityGrownEvent(GameClock Clock, int OfficerId, string Ability, int Previous, int Current) : DomainEvent;

internal static class PersonnelMissions
{
    internal const string RecruitKind = "personnel-recruit";
    internal const string MoveKind = "personnel-move";
    internal const string SearchKind = "personnel-search";

    internal static PersonnelJourneyKind JourneyKind(string kind) => kind switch
    {
        RecruitKind => PersonnelJourneyKind.Recruit, MoveKind => PersonnelJourneyKind.Move, SearchKind => PersonnelJourneyKind.Search,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static bool CanAct(WorldMutation world, int force, int id) =>
        world.Officers.TryGetValue(id, out var person) && person.IsServing && person.OwnerId == force &&
        person.SettlementId is int origin && world.Settlements.TryGetValue(origin, out var home) && home.OwnerId == force && home.CanHostPersons && home.Durability > 0 &&
        !person.Acted && !world.Simulation.Tasks.Any(t => t.OfficerId == id);

    internal static bool Recruitable(WorldMutation world, int force, OfficerState target) =>
        target.OwnerId != force && (target.Status is OfficerStatus.Free or OfficerStatus.Serving or OfficerStatus.Strategist or OfficerStatus.Governor ||
            target.Status == OfficerStatus.Captive && target.SettlementId is int prison && world.Settlements[prison].OwnerId == force) &&
        PersonnelDefection.CanTransfer(world, target) &&
        !world.Forces.Values.Any(f => f.LeaderId == target.Id) &&
        !world.Simulation.Tasks.Any(t => t.OfficerId == target.Id && t.Journey is not null);

    internal static CommandRejection StartRecruit(WorldMutation world, CommandContext context, RecruitOfficerCommand command,
        PersonnelRulebook rules, List<DomainEvent> events)
    {
        if (!CanAct(world, command.IssuerId, command.OfficerId)) return CommandRejection.PersonUnavailable;
        if (!world.Officers.TryGetValue(command.TargetOfficerId, out var target)) return CommandRejection.UnknownPerson;
        if (!Recruitable(world, command.IssuerId, target)) return CommandRejection.PersonUnavailable;
        if (world.Forces[command.IssuerId].LeaderId is null) return CommandRejection.PersonUnavailable;
        if (target.OwnerId is int other && !world.Relations.ContainsKey((Math.Min(other, command.IssuerId), Math.Max(other, command.IssuerId))))
            return CommandRejection.InvalidTask;
        int origin = world.Officers[command.OfficerId].SettlementId!.Value;
        if ((target.SettlementId ?? target.RegionId) is not int destination || !rules.Places.ContainsKey(origin) || !rules.Places.ContainsKey(destination))
            return CommandRejection.InvalidTask;
        int roll = (int)(world.NextRandom("personnel-recruit-submission") % 100);
        Add(world, context, command, command.CommandId, command.OfficerId, origin,
            new(destination, target.Id, roll), RecruitKind, rules, events);
        return CommandRejection.None;
    }

    internal static CommandRejection StartSearch(WorldMutation world, CommandContext context, SearchCommand command,
        PersonnelRulebook rules, List<DomainEvent> events)
    {
        if (!CanAct(world, command.IssuerId, command.OfficerId) || world.Forces[command.IssuerId].LeaderId is null)
            return CommandRejection.PersonUnavailable;
        int origin = world.Officers[command.OfficerId].SettlementId!.Value;
        if (!rules.Places.ContainsKey(origin) || !CanSearch(world, command.IssuerId, command.DestinationId, rules))
            return CommandRejection.InvalidTask;
        int roll = (int)(world.NextRandom("personnel-search-submission") % 100);
        Add(world, context, command, command.CommandId, command.OfficerId, origin,
            new(command.DestinationId, null, roll) { AcceptRecruitment = command.AcceptRecruitment }, SearchKind, rules, events);
        return CommandRejection.None;
    }

    internal static bool CanSearch(WorldMutation world, int forceId, int destinationId, PersonnelRulebook rules)
    {
        if (!rules.Places.ContainsKey(destinationId)) return false;
        if (world.Settlements.TryGetValue(destinationId, out var target) && target.OwnerId is int owner)
            return owner == forceId;
        return !rules.RegionCities.TryGetValue(destinationId, out int cityId) ||
            world.Settlements.TryGetValue(cityId, out var city) && (city.OwnerId is null || city.OwnerId == forceId);
    }

    internal static CommandRejection StartMove(WorldMutation world, CommandContext context, MoveOfficersCommand command,
        PersonnelRulebook rules, List<DomainEvent> events)
    {
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        if (!world.Settlements.TryGetValue(command.DestinationId, out var destination) ||
            destination.OwnerId != command.IssuerId || !destination.CanHostPersons || destination.Durability == 0 || !rules.Places.ContainsKey(destination.Id))
            return CommandRejection.InvalidTask;
        if (command.OfficerIds.Any(id => !CanAct(world, command.IssuerId, id) ||
            world.Officers[id].SettlementId == destination.Id || !rules.Places.ContainsKey(world.Officers[id].SettlementId!.Value) ||
            world.Corps.Values.Any(c => c.LeaderId == id && c.Treasury is null && destination.CorpsId != c.Id) ||
            !GovernanceMaintenance.CanRemove(world, id)))
            return CommandRejection.PersonUnavailable;
        var ids = command.OfficerIds.OrderBy(id => world.Officers[id].SettlementId).ThenBy(id => id).ToArray();
        for (int index = 0; index < ids.Length; index++)
        {
            int id = ids[index];
            Guid taskId = index == 0 ? command.CommandId : new Guid(Convert.FromHexString(CanonicalJson.Hash(new { command.CommandId, OfficerId = id })).AsSpan(0, 16));
            Add(world, context, command, taskId, id, world.Officers[id].SettlementId!.Value,
                new(destination.Id, null, null), MoveKind, rules, events);
        }
        return CommandRejection.None;
    }

    private static void Add(WorldMutation world, CommandContext context, GameCommand command, Guid id, int officer, int origin,
        PersonnelJourney journey, string kind, PersonnelRulebook rules, List<DomainEvent> events)
    {
        var due = world.Clock;
        for (int day = 0; day < rules.ResolutionDayOffset(origin, journey.DestinationPlaceId, JourneyKind(kind)); day++) due = due.NextDay();
        var task = new PersistentTask(id, kind, context.ControllerId, command.IssuerId, officer, origin,
            world.Clock, due, 0, command.Sequence) { Journey = journey };
        world.Replace(world.Officers[officer] with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
    }

    internal static void GrowPolitics(WorldMutation world, int officerId, List<DomainEvent> events)
    {
        if (world.NextRandom("personnel-politics-growth") % 100 >= 3) return;
        var person = world.Officers[officerId];
        if (person.BaseAbilities.Politics >= 100) return;
        int next = person.BaseAbilities.Politics + 1;
        world.Replace(person with { BaseAbilities = person.BaseAbilities with { Politics = next } });
        events.Add(new OfficerAbilityGrownEvent(world.Clock, person.Id, "politics", person.BaseAbilities.Politics, next));
    }
}

internal sealed class PersonnelMissionRule(PersonnelRulebook rules, EconomyRulebook economy, GovernanceRulebook governance,
    TreasureRulebook treasures, string kind) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is { ReturnLeg: null } journey && DurationDays(task) >= 0 &&
        (kind == PersonnelMissions.MoveKind ? journey.TargetOfficerId is null && journey.SuccessRoll is null && journey.AcceptRecruitment is null :
         kind == PersonnelMissions.SearchKind ? journey.TargetOfficerId is null && journey.SuccessRoll is >= 0 and < 100 && journey.AcceptRecruitment is not null :
         journey.TargetOfficerId is >= 0 && journey.SuccessRoll is >= 0 and < 100 && journey.AcceptRecruitment is null);

    internal override int DurationDays(PersistentTask task) => task.Journey is { } journey &&
        rules.Places.ContainsKey(task.OriginSettlementId) && rules.Places.ContainsKey(journey.DestinationPlaceId)
        ? rules.ResolutionDayOffset(task.OriginSettlementId, journey.DestinationPlaceId, PersonnelMissions.JourneyKind(kind)) : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task)
    {
        if (task.Journey is not { } journey || DurationDays(task) < 0) return false;
        if (kind == PersonnelMissions.SearchKind)
            return journey.TargetOfficerId is null && journey.SuccessRoll is >= 0 and < 100 && world.Forces[task.ForceId].LeaderId is not null &&
                PersonnelMissions.CanSearch(world, task.ForceId, journey.DestinationPlaceId, rules);
        if (kind == PersonnelMissions.MoveKind)
            return journey.TargetOfficerId is null && journey.SuccessRoll is null &&
                world.Settlements.TryGetValue(journey.DestinationPlaceId, out var destination) && destination.OwnerId == task.ForceId &&
                destination.CanHostPersons && destination.Durability > 0 &&
                !world.Corps.Values.Any(c => c.LeaderId == task.OfficerId && c.Treasury is null && destination.CorpsId != c.Id) &&
                GovernanceMaintenance.CanRemove(world, task.OfficerId);
        return journey.SuccessRoll is >= 0 and < 100 && journey.TargetOfficerId is int targetId &&
            world.Forces[task.ForceId].LeaderId is not null && world.Officers.TryGetValue(targetId, out var target) &&
            PersonnelMissions.Recruitable(world, task.ForceId, target) && (target.SettlementId ?? target.RegionId) == journey.DestinationPlaceId;
    }

    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        var journey = task.Journey!;
        if (kind == PersonnelMissions.SearchKind)
        {
            Search(world, task, events);
            PersonnelReturns.Start(world, task, rules, events, true);
            return;
        }
        if (kind == PersonnelMissions.MoveKind)
        {
            int? oldCorpsId = world.Settlements[task.OriginSettlementId].CorpsId;
            PersonnelTransitions.Move(world, task.OfficerId, journey.DestinationPlaceId, events, rules, true);
            if (oldCorpsId is int corpsId && world.Settlements[journey.DestinationPlaceId].CorpsId != corpsId)
            {
                var person = world.Officers[task.OfficerId];
                if (person.Status == OfficerStatus.Governor) world.Replace(person with { Status = OfficerStatus.Serving });
                if (GovernanceMaintenance.RepairCorps(world, corpsId, governance, events) != CommandRejection.None)
                    throw new InvalidOperationException("Validated corps transfer failed.");
            }
            GovernanceMaintenance.RepairCommander(world, task.OriginSettlementId, governance, events);
            GovernanceMaintenance.RepairCommander(world, journey.DestinationPlaceId, governance, events);
            EconomicCycle.UpdateTrust(world, economy, events);
            return;
        }
        var target = world.Officers[journey.TargetOfficerId!.Value];
        int threshold = RecruitmentRules.Threshold(world, task.OfficerId, target.Id);
        int roll = journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        if (success)
        {
            bool transferredPlace = PersonnelDefection.Recruit(world, task, rules, governance, events);
            if (!transferredPlace) PersonnelReturns.Start(world, task, rules, events, true, target.Id);
            PersonnelMissions.GrowPolitics(world, task.OfficerId, events);
            EconomicCycle.UpdateTrust(world, economy, events);
        }
        else RecruitmentRules.Failure(world, task.ForceId, target.Id, threshold, events);
        events.Add(new RecruitmentResolvedEvent(world.Clock, task.OfficerId, target.Id, threshold, roll, success));
        PersonnelReturns.Start(world, task, rules, events, true);
    }

    private void Search(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        int destination = task.Journey!.DestinationPlaceId;
        var people = world.Officers.Values.Where(p => p.Status == OfficerStatus.Undiscovered &&
            (p.SettlementId ?? p.RegionId) == destination).OrderBy(p => p.Id).ToArray();
        int politics = world.Abilities(world.Officers[task.OfficerId]).Politics;
        long score = Math.Max(1, Math.Min(politics, 60) / 2) + Math.Max(0, Math.Min(politics, 80) - 60)
            + 2L * Math.Max(0, Math.Min(politics, 90) - 80) + 3L * Math.Max(0, politics - 90);
        long root = (long)Math.Sqrt(100L * people.Length);
        long threshold = 2 * score * root / 30;
        if (people.Length == 0 || task.Journey.SuccessRoll >= threshold)
        {
            ResolveOtherSearchResults(world, task, score, events);
            return;
        }
        var target = people[(int)(world.NextRandom("personnel-search-candidate") % (ulong)people.Length)];
        var leader = world.Officers[world.Forces[task.ForceId].LeaderId!.Value];
        int loyaltyGain = 10 + (50 - PersonnelRulebook.AffinityDistance(target, leader)) / 5;
        RecruitmentRules.SetLoyalty(world, target.Id, task.ForceId, Math.Min(255, target.LoyaltyTo(task.ForceId) + loyaltyGain), "search-recruit-interest", events);
        int recruitThreshold = RecruitmentRules.Threshold(world, task.OfficerId, target.Id);
        bool recruited = task.Journey.AcceptRecruitment == true &&
            (int)(world.NextRandom("personnel-search-recruitment") % 100) < recruitThreshold;
        if (recruited)
        {
            PersonnelTransitions.Recruit(world, target.Id, task.ForceId, task.OriginSettlementId, events, rules, true);
            PersonnelReturns.Start(world, task, rules, events, true, target.Id);
            EconomicCycle.UpdateTrust(world, economy, events);
        }
        else
        {
            PersonnelTransitions.Discover(world, target.Id, destination, events, rules, true);
            if (task.Journey.AcceptRecruitment == true)
                RecruitmentRules.SetLoyalty(world, target.Id, task.ForceId, 49, "search-recruit-failed", events);
        }
        PersonnelMissions.GrowPolitics(world, task.OfficerId, events);
        events.Add(new SearchResolvedEvent(world.Clock, task.OfficerId, destination, target.Id, recruited) { Result = SearchResultKind.Person });
    }

    private void ResolveOtherSearchResults(WorldMutation world, PersistentTask task, long politicsScore, List<DomainEvent> events)
    {
        int destination = task.Journey!.DestinationPlaceId;
        int? corpsId = world.Settlements[task.OriginSettlementId].CorpsId;
        bool direct = world.Simulation.Controls.Any(c => c.ForceId == task.ForceId && c.Kind == ControllerKind.Player) &&
            (corpsId is null || world.Corps[corpsId.Value].ControlMode == CorpsControlMode.Direct);
        if (direct)
        {
            var items = world.Treasures.Values.Where(t => t.Discovery == TreasureDiscovery.Buried && t.HolderId is null &&
                t.DiscoveryRegionId == destination && treasures.Items.ContainsKey(t.Id)).OrderBy(t => t.Id).ToArray();
            long threshold = politicsScore * (long)Math.Sqrt(100L * items.Length) / 90;
            if (items.Length > 0 && task.Journey.SuccessRoll < threshold)
            {
                var item = items[world.NextRandom("personnel-search-treasure") % (ulong)items.Length];
                int rulerId = world.Forces[task.ForceId].LeaderId!.Value;
                world.Replace(item with { Discovery = TreasureDiscovery.Discovered, HolderId = rulerId });
                PersonnelMissions.GrowPolitics(world, task.OfficerId, events);
                EconomicCycle.UpdateTrust(world, economy, events);
                events.Add(new SearchResolvedEvent(world.Clock, task.OfficerId, destination, null, false)
                    { Result = SearchResultKind.Treasure, TreasureId = item.Id });
                return;
            }
        }
        if ((long)(world.NextRandom("personnel-search-funds") % 100) < Math.Max(1, politicsScore / 2))
        {
            long funds = politicsScore * (1 + (long)(world.NextRandom("personnel-search-funds") % 3)) +
                (long)(world.NextRandom("personnel-search-funds") % 10);
            var account = ResourceAccounts.Resolve(world, task.ForceId, corpsId);
            uint before = ResourceAccounts.Balance(world, account, ResourceKind.Money);
            uint after = ResourceAccounts.Change(world, account, ResourceKind.Money, funds, "search-funds", destination, events);
            events.Add(new SearchResolvedEvent(world.Clock, task.OfficerId, destination, null, false)
                { Result = SearchResultKind.Money, Money = (long)after - before });
            return;
        }
        events.Add(new SearchResolvedEvent(world.Clock, task.OfficerId, destination, null, false) { Result = SearchResultKind.None });
    }
}
