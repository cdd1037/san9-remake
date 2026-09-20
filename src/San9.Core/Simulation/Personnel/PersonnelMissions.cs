using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RecruitOfficerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetOfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record MoveOfficersCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> OfficerIds, int DestinationId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record SearchCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int DestinationId)
    : GameCommand(CommandId, IssuerId, Sequence);
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PersonSearchEncounter), "person")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BeastSearchEncounter), "beast")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ScholarReturnEncounter), "scholar-return")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TreasureMonumentEncounter), "treasure-monument")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ShanyangGongEncounter), "shanyang-gong")]
public abstract record SearchEncounter;
public sealed record PersonSearchEncounter(int OfficerId) : SearchEncounter;
public sealed record BeastSearchEncounter(string EventId) : SearchEncounter;
public sealed record ScholarReturnEncounter(string EventId) : SearchEncounter;
public sealed record TreasureMonumentEncounter(string EventId) : SearchEncounter;
public sealed record ShanyangGongEncounter(bool ChoosingCountryTitle = false) : SearchEncounter;
public sealed record SearchRecruitmentPrompt(Guid TaskId, int Step, int ForceId, int SearchingOfficerId, int DiscoveredOfficerId, int PlaceId);
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
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Recruitment,
            world.Clock, command.OfficerId, command.TargetOfficerId);
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
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.SearchDiscovery,
            world.Clock, command.OfficerId, command.DestinationId);
        Add(world, context, command, command.CommandId, command.OfficerId, origin,
            new(command.DestinationId, null, roll), SearchKind, rules, events);
        return CommandRejection.None;
    }

    internal static bool CanSearch(WorldMutation world, int forceId, int destinationId, PersonnelRulebook rules)
    {
        if (!rules.Places.ContainsKey(destinationId) || world.AllSettlements.TryGetValue(destinationId, out var defined) && defined.IsDormant) return false;
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
        var ids = command.OfficerIds.Order().ToArray();
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
        journey = journey with { Route = PersonnelLocations.Route(world, rules, origin, journey.DestinationPlaceId) };
        int days = kind == SearchKind ? journey.Route.XunRoundedDays : journey.Route.Days;
        var due = world.Clock;
        for (int day = 0; day < Math.Max(0, days - 1); day++) due = due.NextDay();
        var task = new PersistentTask(id, kind, context.ControllerId, command.IssuerId, officer, origin,
            world.Clock, due, 0, command.Sequence) { Journey = journey };
        world.Replace(world.Officers[officer] with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
    }

    internal static void GrowPolitics(WorldMutation world, int officerId, List<DomainEvent> events)
    {
        OfficerAbilityGrowth.TryIncrease(world, officerId, OfficerAbility.Politics, "personnel-politics-growth", events);
    }
}

internal sealed class PersonnelMissionRule(SimulationRules simulationRules, string kind) : TaskRule
{
    private readonly PersonnelRulebook rules = simulationRules.Personnel;
    private readonly EconomyRulebook economy = simulationRules.Economy;
    private readonly GovernanceRulebook governance = simulationRules.Governance;
    private readonly TreasureRulebook treasures = simulationRules.Treasures;
    internal override bool SupportsResolutionStep(int step) => step == 0 || kind == PersonnelMissions.SearchKind && step is 1 or 2;
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is { ReturnLeg: null, Route: { FieldDestination: false }, UnitStrategy: null, Burn: null, Seize: null, Gossip: null } journey && DurationDays(task) >= 0 &&
        (task.ResolutionStep == 0 ? task.SearchEncounter is null : kind == PersonnelMissions.SearchKind &&
            (task.ResolutionStep == 1 && (task.SearchEncounter is PersonSearchEncounter { OfficerId: >= 0 } ||
                task.SearchEncounter is ScholarReturnEncounter scholar && rules.SearchEvents.GetValueOrDefault(scholar.EventId) is ScholarReturnSearchEventDefinition ||
                task.SearchEncounter is TreasureMonumentEncounter monument && rules.SearchEvents.GetValueOrDefault(monument.EventId) is TreasureMonumentSearchEventDefinition ||
                task.SearchEncounter is BeastSearchEncounter beast && rules.SearchEvents.GetValueOrDefault(beast.EventId) is BeastSearchEventDefinition) ||
             simulationRules.ShanyangGong is not null && task.SearchEncounter is ShanyangGongEncounter encounter &&
                task.ResolutionStep == (encounter.ChoosingCountryTitle ? 2 : 1))) &&
        (kind == PersonnelMissions.MoveKind ? journey.TargetOfficerId is null && journey.SuccessRoll is null :
         kind == PersonnelMissions.SearchKind ? journey.TargetOfficerId is null && journey.SuccessRoll is >= 0 and < 100 :
         journey.TargetOfficerId is >= 0 && journey.SuccessRoll is >= 0 and < 100);

    private static bool Direct(WorldMutation world, PersistentTask task) =>
        world.Simulation.Controls.Any(c => c.ForceId == task.ForceId && c.Kind == ControllerKind.Player) &&
        (world.Settlements[task.OriginSettlementId].CorpsId is not int corpsId || world.Corps[corpsId].ControlMode == CorpsControlMode.Direct);

    internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) =>
        task.SearchEncounter is not null && Direct(world, task) ? task.ForceId : null;

    internal override bool AcceptsResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response) =>
        response.Offer is null && (task.SearchEncounter is ShanyangGongEncounter encounter
            ? (encounter.ChoosingCountryTitle
                ? response.Accept && response.CountryTitleId is int title && ShanyangGong.Candidates(world, simulationRules, task.ForceId).Contains(title)
                : response.CountryTitleId is null)
            : response.CountryTitleId is null);

    internal override PersistentTask? ResolveResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response,
        List<DomainEvent> events) => task.SearchEncounter is ShanyangGongEncounter
        ? ShanyangGong.Resolve(world, task, simulationRules, response.Accept, response.CountryTitleId, events)
        : Resolve(world, task, response.Accept, events);

    internal override PersistentTask? Resolve(WorldMutation world, PersistentTask task, bool? accept, List<DomainEvent> events)
    {
        if (kind != PersonnelMissions.SearchKind) { Complete(world, task, events); return null; }
        if (task.SearchEncounter is ShanyangGongEncounter)
            return ShanyangGong.Resolve(world, task, simulationRules, accept ?? true, null, events);
        if (task.SearchEncounter is PersonSearchEncounter encounter)
            ResolvePerson(world, task, encounter.OfficerId, accept ?? true, events);
        else if (task.SearchEncounter is TreasureMonumentEncounter monument)
            TreasureMonument.Resolve(world, task, simulationRules, monument, accept ?? true, events);
        else if (task.SearchEncounter is ScholarReturnEncounter scholar)
            ScholarReturn.Resolve(world, task, rules, economy, scholar, accept ?? true, events);
        else if (task.SearchEncounter is BeastSearchEncounter beast)
            SearchEvents.ResolveBeast(world, task, rules, economy, beast, accept ?? true, events);
        else
        {
            if (Direct(world, task) && ThreeVisits.Search(world, task, simulationRules.ThreeVisits, rules, economy, events)) return null;
            if (Direct(world, task) && ShanyangGong.Eligible(world, task, simulationRules))
                return task with { SearchEncounter = new ShanyangGongEncounter() };
            if (Direct(world, task) && SearchEvents.Prepare(world, task, simulationRules, events, out var special))
            {
                if (special is not null) return task with { SearchEncounter = special };
                PersonnelReturns.Start(world, task, rules, events, true);
                return null;
            }
            var candidate = SelectPerson(world, task, out long score);
            if (candidate is not null) return task with { SearchEncounter = new PersonSearchEncounter(candidate.Id) };
            ResolveOtherSearchResults(world, task, score, events);
        }
        PersonnelReturns.Start(world, task, rules, events, true);
        return null;
    }

    internal override int DurationDays(PersistentTask task) => task.Journey is { Route: { } route } journey &&
        rules.Places.ContainsKey(task.OriginSettlementId) && rules.Places.ContainsKey(journey.DestinationPlaceId)
        ? Math.Max(0, (kind == PersonnelMissions.SearchKind ? route.XunRoundedDays : route.Days) - 1) : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task)
    {
        if (task.Journey is not { } journey || DurationDays(task) < 0) return false;
        if (kind == PersonnelMissions.SearchKind)
            return journey.TargetOfficerId is null && journey.SuccessRoll is >= 0 and < 100 && world.Forces[task.ForceId].LeaderId is not null &&
                (task.SearchEncounter switch
                {
                    null => true,
                    PersonSearchEncounter person => world.Officers.TryGetValue(person.OfficerId, out var discovered) &&
                        discovered.Status == OfficerStatus.Undiscovered && (discovered.SettlementId ?? discovered.RegionId) == journey.DestinationPlaceId,
                    ShanyangGongEncounter => ShanyangGong.Eligible(world, task, simulationRules),
                    ScholarReturnEncounter scholar => ScholarReturn.CanResolve(world, task, rules, scholar),
                    TreasureMonumentEncounter monument => TreasureMonument.CanResolve(world, task, simulationRules, monument),
                    BeastSearchEncounter beast => SearchEvents.CanResolveBeast(world, task, rules, beast),
                    _ => false
                }) &&
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

    private static OfficerState? SelectPerson(WorldMutation world, PersistentTask task, out long score)
    {
        int destination = task.Journey!.DestinationPlaceId;
        var people = world.Officers.Values.Where(p => p.Status == OfficerStatus.Undiscovered &&
            (p.SettlementId ?? p.RegionId) == destination).OrderBy(p => p.Id).ToArray();
        int politics = world.Abilities(world.Officers[task.OfficerId]).Politics;
        score = Math.Max(1, Math.Min(politics, 60) / 2) + Math.Max(0, Math.Min(politics, 80) - 60)
            + 2L * Math.Max(0, Math.Min(politics, 90) - 80) + 3L * Math.Max(0, politics - 90);
        long root = (long)Math.Sqrt(100L * people.Length);
        long threshold = 2 * score * root / 30;
        if (people.Length == 0 || task.Journey.SuccessRoll >= threshold)
            return null;
        return people[(int)(world.NextRandom("personnel-search-candidate") % (ulong)people.Length)];
    }

    private void ResolvePerson(WorldMutation world, PersistentTask task, int personId, bool accept, List<DomainEvent> events)
    {
        int destination = task.Journey!.DestinationPlaceId;
        var target = world.Officers[personId];
        var leader = world.Officers[world.Forces[task.ForceId].LeaderId!.Value];
        int loyaltyGain = 10 + (50 - PersonnelRulebook.AffinityDistance(target, leader)) / 5;
        RecruitmentRules.SetLoyalty(world, target.Id, task.ForceId, Math.Min(255, target.LoyaltyTo(task.ForceId) + loyaltyGain), "search-recruit-interest", events);
        int recruitThreshold = RecruitmentRules.Threshold(world, task.OfficerId, target.Id);
        bool recruited = accept &&
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
            if (accept)
                RecruitmentRules.SetLoyalty(world, target.Id, task.ForceId, 49, "search-recruit-failed", events);
        }
        PersonnelMissions.GrowPolitics(world, task.OfficerId, events);
        events.Add(new SearchResolvedEvent(world.Clock, task.OfficerId, destination, target.Id, recruited) { Result = SearchResultKind.Person });
    }

    private void ResolveOtherSearchResults(WorldMutation world, PersistentTask task, long politicsScore, List<DomainEvent> events)
    {
        int destination = task.Journey!.DestinationPlaceId;
        int? corpsId = world.Settlements[task.OriginSettlementId].CorpsId;
        if (Direct(world, task))
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
