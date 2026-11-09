using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RecruitOfficersCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> OfficerIds, int TargetOfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record MoveOfficersCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> OfficerIds, int DestinationId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record SearchCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> OfficerIds, int DestinationId)
    : GameCommand(CommandId, IssuerId, Sequence);
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PersonSearchEncounter), "person")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(MerchantInvestmentEncounter), "merchant-investment")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TigerSearchEncounter), "tiger")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BeastSearchEncounter), "beast")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ScholarReturnEncounter), "scholar-return")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TreasureMonumentEncounter), "treasure-monument")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ShanyangGongEncounter), "shanyang-gong")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(LongevityEncounter), "longevity")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ScholarInsultEncounter), "scholar-insult")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(MysticBookOfferEncounter), "mystic-book-offer")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(MysticBookReturnEncounter), "mystic-book-return")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CommunitySearchEncounter), "community")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ConflictSearchEncounter), "conflict")]
public abstract record SearchEncounter;
public sealed record CommunitySearchEncounter(string EventId) : SearchEncounter;
public sealed record ConflictSearchEncounter(string EventId, int? OtherOfficerId) : SearchEncounter;
public sealed record MysticBookOfferEncounter(string EventId) : SearchEncounter;
public sealed record MysticBookReturnEncounter(string EventId) : SearchEncounter;
public sealed record ScholarInsultEncounter(string EventId) : SearchEncounter;
public sealed record LongevityEncounter(string EventId) : SearchEncounter;
public sealed record PersonSearchEncounter(int OfficerId) : SearchEncounter;
public sealed record MerchantInvestmentEncounter(string EventId) : SearchEncounter;
public sealed record TigerSearchEncounter(string EventId) : SearchEncounter;
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
public sealed record RecruitmentResolvedEvent(GameClock Clock, int OfficerId, int TargetOfficerId, int Threshold, int Roll, bool Success) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired]
    public RecruitmentOccurrence Occurrence { get; init; } = null!;
}
public sealed record RecruitmentOccurrence(int ForceId, OfficerStatus PreviousStatus, PersonOccurrence Target,
    bool TransferredUnit, bool TransferredPlace, int? DetainingForceId);
public sealed record PersonAffiliationChangedEvent(GameClock Clock, int OfficerId, int? PreviousForceId, int? ForceId, int PlaceId) : DomainEvent;
public sealed record OfficerMovedEvent(GameClock Clock, int OfficerId, int OriginId, int DestinationId,
    PersonnelArrivalOccurrence Arrival) : DomainEvent;
public sealed record ForceRelationChangedEvent(GameClock Clock, int FirstId, int SecondId, int Previous, int Current) : DomainEvent;
public sealed record OfficerAbilityGrownEvent(GameClock Clock, int OfficerId, string Ability, int Previous, int Current) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired]
    public PersonOccurrence Occurrence { get; init; } = null!;
}

internal static class PersonnelMissions
{
    internal const string RecruitKind = "personnel-recruit";
    internal const string MoveKind = "personnel-move";
    internal const string SearchKind = "personnel-search";

    internal static bool CanAct(WorldMutation world, int force, int id) =>
        world.Officers.TryGetValue(id, out var person) && person.IsServing && person.OwnerId == force &&
        person.SettlementId is int origin && world.Settlements.TryGetValue(origin, out var home) && home.OwnerId == force && home.CanHostPersons && home.Durability > 0 &&
        !person.Acted && !world.IsBusy(id);

    internal static bool CanStartMission(WorldMutation world, int force, int id, MilitaryRulebook? military) =>
        CanAct(world, force, id) && SettlementOperations.Available(world, world.Settlements[world.Officers[id].SettlementId!.Value], military);

    internal static bool Recruitable(WorldMutation world, int force, OfficerState target) =>
        (target.OwnerId is not int owner || !world.Forces[owner].IsBarbarian) &&
        target.OwnerId != force && (target.Status is OfficerStatus.Free or OfficerStatus.Serving or OfficerStatus.Strategist or OfficerStatus.Governor ||
            target.Status == OfficerStatus.Captive && target.SettlementId is int prison && world.Settlements[prison].OwnerId == force) &&
        PersonnelDefection.CanTransfer(world, target) &&
        !world.Forces.Values.Any(f => f.LeaderId == target.Id);

    internal static CommandRejection RecruitDestination(WorldMutation world, OfficerState target, SimulationRules rules,
        out int destination, out UnitState? unit)
    {
        destination = -1;
        unit = null;
        if (target.UnitId is int unitId && (!world.Units.TryGetValue(unitId, out unit) ||
            unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null || !target.IsServing)) return CommandRejection.PersonUnavailable;
        int? place = unit is null ? target.SettlementId ?? target.RegionId : rules.Military?.RegionAt(unit.Position);
        if (place is not int id || !rules.Personnel.Places.ContainsKey(id)) return CommandRejection.InvalidTask;
        destination = id;
        return CommandRejection.None;
    }

    internal static CommandRejection StartRecruit(WorldMutation world, CommandContext context, RecruitOfficersCommand command,
        SimulationRules simulationRules, List<DomainEvent> events)
    {
        var rules = simulationRules.Personnel;
        var rejection = ValidateRecruit(world, command, simulationRules, out int destination, out UnitState? unit);
        if (rejection != CommandRejection.None) return rejection;
        var ids = command.OfficerIds.Order().ToArray();
        for (int index = 0; index < ids.Length; index++)
        {
            int officer = ids[index];
            int origin = world.Officers[officer].SettlementId!.Value;
            int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Recruitment,
                world.Clock, officer, command.TargetOfficerId);
            Guid taskId = index == 0 ? command.CommandId : world.AllocateTaskId();
            Add(world, context, command, taskId, officer, origin,
                new(destination, command.TargetOfficerId, roll) { RecruitmentUnitId = unit?.Id }, RecruitKind, rules, events, unit?.Position);
        }
        return CommandRejection.None;
    }

    internal static CommandRejection ValidateRecruit(WorldMutation world, RecruitOfficersCommand command,
        SimulationRules simulationRules, out int destination, out UnitState? unit)
    {
        destination = -1; unit = null;
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        foreach (int id in command.OfficerIds)
        {
            var rejection = ActorEligibility(world, command.IssuerId, id, simulationRules);
            if (rejection != CommandRejection.None) return rejection;
        }
        return RecruitTargetEligibility(world, command.IssuerId, command.TargetOfficerId, simulationRules, out destination, out unit);
    }

    internal static CommandRejection ActorEligibility(WorldMutation world, int forceId, int officerId, SimulationRules rules)
    {
        if (!CanStartMission(world, forceId, officerId, rules.Military)) return CommandRejection.PersonUnavailable;
        return rules.Personnel.Places.ContainsKey(world.Officers[officerId].SettlementId!.Value)
            ? CommandRejection.None : CommandRejection.InvalidTask;
    }

    internal static CommandRejection RecruitTargetEligibility(WorldMutation world, int forceId, int targetId, SimulationRules rules,
        out int destination, out UnitState? unit)
    {
        destination = -1; unit = null;
        if (!world.Officers.TryGetValue(targetId, out var target)) return CommandRejection.UnknownPerson;
        if (!Recruitable(world, forceId, target)) return CommandRejection.PersonUnavailable;
        if (world.Forces[forceId].LeaderId is null) return CommandRejection.PersonUnavailable;
        if (target.OwnerId is int other && !world.Relations.ContainsKey((Math.Min(other, forceId), Math.Max(other, forceId))))
            return CommandRejection.InvalidTask;
        return RecruitDestination(world, target, rules, out destination, out unit);
    }

    internal static CommandRejection StartSearch(WorldMutation world, CommandContext context, SearchCommand command,
        PersonnelRulebook rules, MilitaryRulebook? military, List<DomainEvent> events)
    {
        var rejection = ValidateSearch(world, command, rules, military);
        if (rejection != CommandRejection.None) return rejection;
        var ids = command.OfficerIds.Order().ToArray();
        for (int index = 0; index < ids.Length; index++)
        {
            int officer = ids[index];
            int origin = world.Officers[officer].SettlementId!.Value;
            int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.SearchDiscovery,
                world.Clock, officer, command.DestinationId);
            Guid taskId = index == 0 ? command.CommandId : world.AllocateTaskId();
            Add(world, context, command, taskId, officer, origin,
                new(command.DestinationId, null, roll), SearchKind, rules, events);
        }
        return CommandRejection.None;
    }

    internal static CommandRejection ValidateSearch(WorldMutation world, SearchCommand command, PersonnelRulebook rules, MilitaryRulebook? military)
    {
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        if (world.Forces[command.IssuerId].LeaderId is null || command.OfficerIds.Any(id => !CanStartMission(world, command.IssuerId, id, military)))
            return CommandRejection.PersonUnavailable;
        if (command.OfficerIds.Any(id => !rules.Places.ContainsKey(world.Officers[id].SettlementId!.Value)) ||
            !CanSearch(world, command.IssuerId, command.DestinationId, rules))
            return CommandRejection.InvalidTask;
        return CommandRejection.None;
    }

    internal static bool CanSearch(WorldMutation world, int forceId, int destinationId, PersonnelRulebook rules)
        => rules.CanSearch(world.AllSettlements, forceId, destinationId);

    internal static CommandRejection StartMove(WorldMutation world, CommandContext context, MoveOfficersCommand command,
        PersonnelRulebook rules, MilitaryRulebook? military, List<DomainEvent> events)
    {
        var rejection = ValidateMove(world, command, rules, military);
        if (rejection != CommandRejection.None) return rejection;
        var ids = command.OfficerIds.Order().ToArray();
        for (int index = 0; index < ids.Length; index++)
        {
            int id = ids[index];
            Guid taskId = index == 0 ? command.CommandId : world.AllocateTaskId();
            Add(world, context, command, taskId, id, world.Officers[id].SettlementId!.Value,
                new(command.DestinationId, null, null), MoveKind, rules, events);
        }
        return CommandRejection.None;
    }

    internal static CommandRejection ValidateMove(WorldMutation world, MoveOfficersCommand command, PersonnelRulebook rules, MilitaryRulebook? military)
    {
        if (command.OfficerIds.IsDefaultOrEmpty || command.OfficerIds.Distinct().Count() != command.OfficerIds.Length)
            return CommandRejection.InvalidTask;
        if (!world.Settlements.TryGetValue(command.DestinationId, out var destination) ||
            destination.OwnerId != command.IssuerId || !destination.CanHostPersons || destination.Durability == 0 || !rules.Places.ContainsKey(destination.Id))
            return CommandRejection.InvalidTask;
        if (command.OfficerIds.Any(id => !CanStartMission(world, command.IssuerId, id, military) ||
            world.Officers[id].SettlementId == destination.Id || !rules.Places.ContainsKey(world.Officers[id].SettlementId!.Value) ||
            world.Corps.Values.Any(c => c.LeaderId == id && c.IsRoot && destination.CorpsId != c.Id) ||
            !GovernanceMaintenance.CanRemove(world, id)))
            return CommandRejection.PersonUnavailable;
        return CommandRejection.None;
    }

    private static void Add(WorldMutation world, CommandContext context, GameCommand command, Guid id, int officer, int origin,
        PersonnelJourney journey, string kind, PersonnelRulebook rules, List<DomainEvent> events, WorldPosition? fieldDestination = null)
    {
        journey = journey with { Route = PersonnelLocations.Route(world, rules, origin, journey.DestinationPlaceId, fieldDestination) };
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
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is { ReturnLeg: null, Route: { } route, UnitStrategy: null, Burn: null, Seize: null, Gossip: null } journey && DurationDays(task) >= 0 &&
        (journey.RecruitmentUnitId is int unit ? unit >= 0 && kind == PersonnelMissions.RecruitKind && route.FieldDestination &&
            simulationRules.Military?.RegionAt(route.Destination) == journey.DestinationPlaceId : !route.FieldDestination) &&
        (task.ResolutionStep == 0 ? task.SearchEncounter is null : kind == PersonnelMissions.SearchKind &&
            (task.ResolutionStep == 1 && (task.SearchEncounter is PersonSearchEncounter { OfficerId: >= 0 } ||
                task.SearchEncounter is ScholarReturnEncounter scholar && rules.SearchEvents.GetValueOrDefault(scholar.EventId) is ScholarReturnSearchEventDefinition ||
                task.SearchEncounter is MysticBookOfferEncounter offer && rules.SearchEvents.GetValueOrDefault(offer.EventId) is MysticBookOfferSearchEventDefinition ||
                task.SearchEncounter is MysticBookReturnEncounter bookReturn && rules.SearchEvents.GetValueOrDefault(bookReturn.EventId) is MysticBookReturnSearchEventDefinition ||
                task.SearchEncounter is ScholarInsultEncounter insult && rules.SearchEvents.GetValueOrDefault(insult.EventId) is ScholarInsultSearchEventDefinition ||
                task.SearchEncounter is LongevityEncounter longevity && rules.SearchEvents.GetValueOrDefault(longevity.EventId) is LongevitySearchEventDefinition ||
                task.SearchEncounter is TreasureMonumentEncounter monument && rules.SearchEvents.GetValueOrDefault(monument.EventId) is TreasureMonumentSearchEventDefinition ||
                task.SearchEncounter is MerchantInvestmentEncounter merchant && rules.SearchEvents.GetValueOrDefault(merchant.EventId) is MerchantInvestmentDefinition ||
                task.SearchEncounter is CommunitySearchEncounter community && rules.SearchEvents.GetValueOrDefault(community.EventId) is CommunitySearchEventDefinition ||
                task.SearchEncounter is ConflictSearchEncounter conflict && rules.SearchEvents.GetValueOrDefault(conflict.EventId) is ConflictSearchEventDefinition ||
                task.SearchEncounter is TigerSearchEncounter tiger && rules.SearchEvents.GetValueOrDefault(tiger.EventId) is TigerSearchEventDefinition ||
                task.SearchEncounter is BeastSearchEncounter beast && rules.SearchEvents.GetValueOrDefault(beast.EventId) is BeastSearchEventDefinition) ||
             simulationRules.ShanyangGong is not null && task.SearchEncounter is ShanyangGongEncounter encounter &&
                task.ResolutionStep == (encounter.ChoosingCountryTitle ? 2 : 1))) &&
        (kind == PersonnelMissions.MoveKind ? journey.TargetOfficerId is null && journey.SuccessRoll is null :
         kind == PersonnelMissions.SearchKind ? journey.TargetOfficerId is null && journey.SuccessRoll is >= 0 and < 100 :
         journey.TargetOfficerId is >= 0 && journey.SuccessRoll is >= 0 and < 100);

    private static bool Direct(WorldMutation world, PersistentTask task) =>
        world.Simulation.Controls.Any(c => c.ForceId == task.ForceId && c.Kind == ControllerKind.Player) &&
        (PersonnelLocations.TaskHome(world, task).CorpsId is not int corpsId || CorpsControl.PlayerOperates(world.Corps[corpsId], world.Companion));

    internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) =>
        task.SearchEncounter is not null ? task.ForceId : null;

    internal override bool AcceptsResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response) =>
        response.Offer is null && (task.SearchEncounter is CommunitySearchEncounter community
            ? response.CountryTitleId is null && response.MerchantInvestment is null && response.SearchConflict is null && response.CommunitySearch is { } communityChoice &&
                response.Accept == CommunitySearch.IsAcceptance(communityChoice) && CommunitySearch.ValidResponse(world, task,
                    (CommunitySearchEventDefinition)rules.SearchEvents[community.EventId], communityChoice)
            : response.CommunitySearch is null && (task.SearchEncounter is ConflictSearchEncounter conflict
            ? response.CountryTitleId is null && response.MerchantInvestment is null && response.SearchConflict is { } conflictChoice &&
                response.Accept == SearchConflicts.IsAcceptance(conflictChoice) && SearchConflicts.ValidResponse(world, task,
                    (ConflictSearchEventDefinition)rules.SearchEvents[conflict.EventId], conflictChoice)
            : response.SearchConflict is null && (task.SearchEncounter is MerchantInvestmentEncounter merchant
            ? response.CountryTitleId is null && response.MerchantInvestment is { } choice && response.Accept == (choice.Choice == MerchantInvestmentChoice.Invest) &&
                MerchantInvestment.ValidResponse(world, task, (MerchantInvestmentDefinition)rules.SearchEvents[merchant.EventId], choice)
            : response.MerchantInvestment is null && (task.SearchEncounter is ShanyangGongEncounter encounter
            ? (encounter.ChoosingCountryTitle
                ? response.Accept && response.CountryTitleId is int title && ShanyangGong.Candidates(world, simulationRules, task.ForceId).Contains(title)
                : response.CountryTitleId is null)
            : response.CountryTitleId is null))));

    internal override PersistentTask? ResolveResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response,
        List<DomainEvent> events) => task.SearchEncounter is ShanyangGongEncounter
        ? ShanyangGong.Resolve(world, task, simulationRules, response.Accept, response.CountryTitleId, events)
        : task.SearchEncounter is MerchantInvestmentEncounter merchant
            ? ResolveMerchant(world, task, merchant, response.MerchantInvestment!, events)
            : task.SearchEncounter is CommunitySearchEncounter community
                ? ResolveCommunity(world, task, community, response.CommunitySearch!.Value, events)
            : task.SearchEncounter is ConflictSearchEncounter conflict
                ? ResolveConflict(world, task, conflict, response.SearchConflict!.Value, events)
                : Resolve(world, task, response.Accept, events);

    private PersistentTask? ResolveCommunity(WorldMutation world, PersistentTask task, CommunitySearchEncounter encounter,
        CommunitySearchChoice choice, List<DomainEvent> events)
    {
        CommunitySearch.Resolve(world, task, simulationRules, encounter, choice, events);
        PersonnelReturns.Start(world, task, rules, events, true);
        return null;
    }

    private PersistentTask? ResolveConflict(WorldMutation world, PersistentTask task, ConflictSearchEncounter encounter,
        SearchConflictChoice choice, List<DomainEvent> events)
    {
        SearchConflicts.Resolve(world, task, simulationRules, encounter, choice, events);
        PersonnelReturns.Start(world, task, rules, events, true);
        return null;
    }

    private PersistentTask? ResolveMerchant(WorldMutation world, PersistentTask task, MerchantInvestmentEncounter encounter,
        MerchantInvestmentResponse response, List<DomainEvent> events)
    {
        MerchantInvestment.Resolve(world, task, simulationRules, encounter, response, events);
        PersonnelReturns.Start(world, task, rules, events, true);
        return null;
    }

    internal override PersistentTask? Resolve(WorldMutation world, PersistentTask task, bool? accept, List<DomainEvent> events)
    {
        if (kind != PersonnelMissions.SearchKind) { Complete(world, task, events); return null; }
        if (task.SearchEncounter is ShanyangGongEncounter)
            return ShanyangGong.Resolve(world, task, simulationRules, accept ?? true, null, events);
        if (task.SearchEncounter is PersonSearchEncounter encounter)
            ResolvePerson(world, task, encounter.OfficerId, accept ?? true, events);
        else if (task.SearchEncounter is MysticBookOfferEncounter offer)
            MysticBook.Resolve(world, task, simulationRules, offer, accept ?? true, events);
        else if (task.SearchEncounter is MysticBookReturnEncounter bookReturn)
            MysticBook.Resolve(world, task, simulationRules, bookReturn, accept ?? true, events);
        else if (task.SearchEncounter is ScholarInsultEncounter insult)
            ScholarInsult.Resolve(world, task, simulationRules, insult, accept ?? false, events);
        else if (task.SearchEncounter is LongevityEncounter longevity)
            Longevity.Resolve(world, task, simulationRules, longevity, accept ?? true, events);
        else if (task.SearchEncounter is TreasureMonumentEncounter monument)
            TreasureMonument.Resolve(world, task, simulationRules, monument, accept ?? true, events);
        else if (task.SearchEncounter is ScholarReturnEncounter scholar)
            ScholarReturn.Resolve(world, task, rules, economy, scholar, accept ?? true, events);
        else if (task.SearchEncounter is MerchantInvestmentEncounter merchant)
            MerchantInvestment.Resolve(world, task, simulationRules, merchant, new(MerchantInvestmentChoice.Decline, 0), events);
        else if (task.SearchEncounter is CommunitySearchEncounter community)
            CommunitySearch.Resolve(world, task, simulationRules, community, CommunitySearchChoice.Decline, events);
        else if (task.SearchEncounter is ConflictSearchEncounter conflict)
            SearchConflicts.Resolve(world, task, simulationRules, conflict,
                SearchConflicts.Options(world, task, (ConflictSearchEventDefinition)rules.SearchEvents[conflict.EventId])[0].Choice, events);
        else if (task.SearchEncounter is TigerSearchEncounter tiger)
            TigerSearch.Resolve(world, task, simulationRules, tiger, accept ?? false, events);
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
                    MysticBookOfferEncounter offer => MysticBook.CanResolve(world, task, simulationRules, offer),
                    MysticBookReturnEncounter bookReturn => MysticBook.CanResolve(world, task, simulationRules, bookReturn),
                    ScholarInsultEncounter insult => ScholarInsult.CanResolve(world, task, simulationRules, insult),
                    LongevityEncounter longevity => Longevity.CanResolve(world, task, simulationRules, longevity),
                    TreasureMonumentEncounter monument => TreasureMonument.CanResolve(world, task, simulationRules, monument),
                    MerchantInvestmentEncounter merchant => MerchantInvestment.CanResolve(world, task, simulationRules, merchant),
                    CommunitySearchEncounter community => CommunitySearch.CanResolve(world, task, simulationRules, community),
                    ConflictSearchEncounter conflict => SearchConflicts.CanResolve(world, task, simulationRules, conflict),
                    TigerSearchEncounter tiger => TigerSearch.CanResolve(world, task, simulationRules, tiger),
                    BeastSearchEncounter beast => SearchEvents.CanResolveBeast(world, task, rules, beast),
                    _ => false
                }) &&
                PersonnelMissions.CanSearch(world, task.ForceId, journey.DestinationPlaceId, rules);
        if (kind == PersonnelMissions.MoveKind)
            return journey.TargetOfficerId is null && journey.SuccessRoll is null &&
                world.Settlements.TryGetValue(journey.DestinationPlaceId, out var destination) && destination.OwnerId == task.ForceId &&
                destination.CanHostPersons && destination.Durability > 0 &&
                !world.Corps.Values.Any(c => c.LeaderId == task.OfficerId && c.IsRoot && destination.CorpsId != c.Id) &&
                GovernanceMaintenance.CanRemove(world, task.OfficerId);
        return journey.SuccessRoll is >= 0 and < 100 && journey.TargetOfficerId is int targetId &&
            world.Forces[task.ForceId].LeaderId is not null && world.Officers.TryGetValue(targetId, out var target) &&
            PersonnelMissions.Recruitable(world, task.ForceId, target) &&
            PersonnelMissions.RecruitDestination(world, target, simulationRules, out _, out _) == CommandRejection.None;
    }

    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        var journey = task.Journey!;
        if (kind == PersonnelMissions.MoveKind)
        {
            int? oldCorpsId = PersonnelLocations.Corps(world, world.Officers[task.OfficerId]);
            PersonnelTransitions.Move(world, task, events, rules, true);
            if (oldCorpsId is int corpsId && world.Settlements[journey.DestinationPlaceId].CorpsId != corpsId)
            {
                var person = world.Officers[task.OfficerId];
                if (person.Status == OfficerStatus.Governor) world.Replace(person with { Status = OfficerStatus.Serving });
                if (GovernanceMaintenance.RepairCorps(world, corpsId, governance, events) != CommandRejection.None)
                    throw new InvalidOperationException("Validated corps transfer failed.");
            }
            return;
        }
        var target = world.Officers[journey.TargetOfficerId!.Value];
        // Capture before recruitment can change the target's affiliation and service place.
        task = PersonnelLocations.AtPerson(world, task, target, simulationRules);
        journey = task.Journey!;
        var occurrence = new RecruitmentOccurrence(task.ForceId, target.Status, PersonOccurrence.Capture(world, target),
            target.IsServing && target.UnitId is int targetUnit && world.Units[targetUnit].CommanderId == target.Id,
            target.IsServing && target.IsCommander && target.SettlementId is not null,
            target.Status == OfficerStatus.Captive ? target.SettlementId is int prison ? world.Settlements[prison].OwnerId
                : target.UnitId is int detention ? world.Units[detention].OwnerId : null : null);
        if (target.IsServing && target.OwnerId is int victim && victim != task.ForceId)
            events.Add(new SecretContactReportedEvent(world.Clock, victim, target.Id, task.ForceId, occurrence.Target));
        bool historicalSuccess = Guandu.Apply(world, task.OfficerId, target.Id, simulationRules, events);
        int threshold = historicalSuccess ? 100 : RecruitmentRules.Threshold(world, task.OfficerId, target.Id, simulationRules);
        int roll = journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        if (success)
        {
            bool transferredObject = PersonnelDefection.Recruit(world, task, simulationRules, events);
            if (!transferredObject) PersonnelReturns.Start(world, task, rules, events, true, target.Id);
            PersonnelMissions.GrowPolitics(world, task.OfficerId, events);
        }
        else RecruitmentRules.Failure(world, task.ForceId, target.Id, threshold, events);
        events.Add(new RecruitmentResolvedEvent(world.Clock, task.OfficerId, target.Id, threshold, roll, success) { Occurrence = occurrence });
        PersonnelReturns.Start(world, task, rules, events, true, arrived: true);
    }

    private static OfficerState? SelectPerson(WorldMutation world, PersistentTask task, out long score)
    {
        int destination = task.Journey!.DestinationPlaceId;
        var people = world.Officers.Values.Where(p => p.Status == OfficerStatus.Undiscovered &&
            (p.SettlementId ?? p.RegionId) == destination).OrderBy(p => p.Id).ToArray();
        int politics = world.Abilities(world.Officers[task.OfficerId]).Politics;
        score = PersonnelSearchChances.PoliticsScore(politics);
        long threshold = PersonnelSearchChances.Person(score, people.Length);
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
        int recruitThreshold = RecruitmentRules.Threshold(world, task.OfficerId, target.Id, simulationRules);
        bool recruited = accept &&
            (int)(world.NextRandom("personnel-search-recruitment") % 100) < recruitThreshold;
        if (recruited)
        {
            PersonnelTransitions.Recruit(world, target.Id, task.ForceId, PersonnelLocations.TaskHome(world, task).Id, events, rules, true);
            PersonnelReturns.Start(world, task, rules, events, true, target.Id);
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
        int? corpsId = PersonnelLocations.TaskHome(world, task).CorpsId;
        if (Direct(world, task))
        {
            var items = world.Treasures.Values.Where(t => t.Discovery == TreasureDiscovery.Buried && t.HolderId is null &&
                t.DiscoveryRegionId == destination && treasures.Items.ContainsKey(t.Id)).OrderBy(t => t.Id).ToArray();
            long threshold = PersonnelSearchChances.Treasure(politicsScore, items.Length);
            if (items.Length > 0 && task.Journey.SuccessRoll < threshold)
            {
                var item = items[world.NextRandom("personnel-search-treasure") % (ulong)items.Length];
                int rulerId = world.Forces[task.ForceId].LeaderId!.Value;
                world.Replace(item with { Discovery = TreasureDiscovery.Discovered, HolderId = rulerId });
                PersonnelMissions.GrowPolitics(world, task.OfficerId, events);
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
