using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>A permitted, prepared alternative. No world, query callbacks or resolution dice cross this boundary.</summary>
public sealed record OpportunityOption(AllocationCandidate Candidate, CommandAdvice Advice, int Prior,
    int Days, string Key, OpportunityConditions Conditions);
/// <summary>Public evidence compared by value and retained only for an accepted task's cooldown.</summary>
public sealed record OpportunityConditions(int Accuracy, int? AdviserId, int Prior, OpportunitySubject? Subject,
    OpportunityActorBand ActorBand, int? Relation, int? GossipRelation);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpportunityPerson), "person")]
[JsonDerivedType(typeof(OpportunityUnit), "unit")]
[JsonDerivedType(typeof(OpportunityPlace), "place")]
[JsonDerivedType(typeof(OpportunitySearch), "search")]
[JsonDerivedType(typeof(OpportunitySurrender), "surrender")]
public abstract record OpportunitySubject;
public sealed record OpportunityPerson(int? OwnerId, OfficerStatus Status, int? SettlementId, int? UnitId, int? Loyalty) : OpportunitySubject;
public sealed record OpportunityUnit(int OwnerId, WorldPosition Position, uint Soldiers, int Morale, UnitCondition Condition) : OpportunitySubject;
public sealed record OpportunityPlace(int? OwnerId, uint Soldiers, int Durability) : OpportunitySubject;
public sealed record OpportunitySearch(int Posts) : OpportunitySubject;
public sealed record OpportunitySurrender(long OwnSoldiers, long TargetSoldiers) : OpportunitySubject;
public sealed record OpportunityActorBand(int Intelligence, int Politics);
public sealed record OpportunityObservation(GameClock Clock, int AdviserAccuracy,
    ImmutableArray<OpportunityOption> Options, bool Complete);
public sealed record OpportunityAttempt(int ForceId, string ControllerId, string Key, OpportunityConditions Conditions,
    Guid TaskId, int ActorId, GameClock Started, GameClock RetryAfter, string Outcome);

public static class OpportunityPlanner
{
    // Accuracy is evidence reliability, not a success probability. Use a visible-condition prior and
    // update it with an opinion's likelihood; a fully reliable opinion overrides that prior.
    public static int Estimate(int prior, AdviserOpinion opinion, int accuracy)
    {
        prior = Math.Clamp(prior, 1, 99);
        accuracy = Math.Clamp(accuracy, 50, 100);
        if (opinion is not (AdviserOpinion.Favorable or AdviserOpinion.Unfavorable)) return prior;
        long yes = opinion == AdviserOpinion.Favorable ? accuracy : 100 - accuracy;
        long no = 100 - yes;
        return (int)(100L * prior * yes / (prior * yes + (100 - prior) * no));
    }

    internal static ImmutableArray<AllocationCandidate> Plan(OpportunityObservation observation,
        ImmutableArray<OpportunityAttempt> attempts, AllocationWork work)
    {
        var result = ImmutableArray.CreateBuilder<AllocationCandidate>();
        foreach (var option in observation.Options)
        {
            if (!work.Spend()) break;
            if (attempts.Any(a => a.ControllerId == option.Candidate.ControllerId && a.Key == option.Key &&
                (a.Outcome == "pending" || a.RetryAfter.DayNumber > observation.Clock.DayNumber &&
                    a.Conditions == option.Conditions))) continue;
            int probability = Estimate(option.Prior, option.Advice.Opinion, observation.AdviserAccuracy);
            var candidate = option.Candidate;
            long net = candidate.Benefit * probability / 100 - candidate.MoneyCost - candidate.OfficerOpportunityCost;
            if (net <= 0) continue;
            result.Add(candidate with { Benefit = net, Opportunity = option });
        }
        return result.ToImmutable();
    }

    internal static ImmutableArray<OpportunityAttempt> Feedback(ImmutableArray<OpportunityAttempt> attempts,
        WorldSnapshot world, IEnumerable<DomainEvent> events)
    {
        var resolved = new Dictionary<Guid, bool>();
        foreach (var e in events)
        {
            (int Actor, string Key)? action = e switch
            {
                RecruitmentResolvedEvent r => (r.OfficerId, $"{nameof(RecruitOfficerCommand)}:{r.TargetOfficerId}"),
                SearchResolvedEvent r => (r.OfficerId, $"{nameof(SearchCommand)}:{r.PlaceId}"),
                UnitStrategyResolvedEvent r => (r.OfficerId, $"{nameof(UnitStrategyCommand)}:{r.TargetUnitId}:{r.Strategy}"),
                BurnResolvedEvent r => (r.OfficerId, $"{nameof(BurnCommand)}:{r.TargetSettlementId}"),
                SeizeResolvedEvent r => (r.OfficerId, $"{nameof(SeizeCommand)}:{r.TargetSettlementId}"),
                AlienateResolvedEvent r => (r.OfficerId, $"{nameof(AlienateCommand)}:{r.TargetOfficerId}"),
                GossipResolvedEvent r => (r.OfficerId, $"{nameof(GossipCommand)}:{r.FirstTargetForceId}:{r.SecondTargetForceId}"),
                _ => null
            };
            bool? success = e switch
            {
                RecruitmentResolvedEvent r => r.Success, UnitStrategyResolvedEvent r => r.Success,
                SearchResolvedEvent r => r.Result != SearchResultKind.None,
                BurnResolvedEvent r => r.Success, SeizeResolvedEvent r => r.Outcome != SeizeOutcomeKind.Failure,
                AlienateResolvedEvent r => r.Success, GossipResolvedEvent r => r.Success,
                GiftResolvedEvent r => r.Accepted, SurrenderResolvedEvent r => r.Accepted,
                PrisonerReturnResolvedEvent r => r.Accepted,
                AttackRequestResolvedEvent r => r.Outcome == AttackRequestOutcome.Accepted,
                TaskCancelledEvent => false, _ => null
            };
            Guid? task = e switch
            {
                GiftResolvedEvent r => r.TaskId, SurrenderResolvedEvent r => r.TaskId,
                PrisonerReturnResolvedEvent r => r.TaskId, AttackRequestResolvedEvent r => r.TaskId,
                TaskCancelledEvent r => r.TaskId, _ => null
            };
            if (action is { } performed)
                task = attempts.FirstOrDefault(a => a.Outcome == "pending" &&
                    a.ActorId == performed.Actor && a.Key == performed.Key)?.TaskId;
            if (task is Guid taskId && success is bool ok) resolved[taskId] = ok;
        }
        return attempts.Select(a => a.Outcome != "pending" ? a : resolved.TryGetValue(a.TaskId, out bool ok)
            ? a with { Outcome = ok ? "succeeded" : "failed", RetryAfter = After(world.Clock, ok ? 10 : 30) }
            : world.Simulation.Tasks.Any(t => t.Id == a.TaskId && t.Journey?.ReturnLeg is null) ? a
            : a with { Outcome = "finished", RetryAfter = After(world.Clock, 30) })
            .Where(a => a.Outcome == "pending" || a.RetryAfter.DayNumber > world.Clock.DayNumber)
            .OrderBy(a => a.ForceId).ThenBy(a => a.ControllerId, StringComparer.Ordinal).ThenBy(a => a.Key, StringComparer.Ordinal).ToImmutableArray();
    }

    internal static GameClock After(GameClock clock, int days)
    {
        for (int i = 0; i < days; i++) clock = clock.NextDay();
        return clock;
    }
}

internal static class OpportunityObservationCapture
{
    internal static OpportunityObservation Capture(SimulationEngine engine, ControlSeat active,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies, SupportObservation support,
        ImmutableArray<CampaignGoal> campaigns, MilitaryRulebook? rules, AllocationWork work,
        IReadOnlySet<string>? completed = null, ImmutableArray<OpportunityAttempt> attempts = default)
    {
        var world = engine.Snapshot;
        var abilities = engine.GetAbilities(world);
        var options = ImmutableArray.CreateBuilder<OpportunityOption>();
        int forceId = active.ForceId;
        // Eligibility probes do not create tasks. One unused identity covers this frozen observation;
        // candidate payloads need no serialization merely to pass the public command envelope.
        var queryId = SimulationIdentity.Command(world.Simulation.NextSequence, forceId);
        int accuracy = world.Forces.Single(f => f.Id == forceId).AdviserId is int adviser
            ? AdviserAccuracy.Percent(abilities[adviser].Intelligence) : 50;
        int Relation(int target) => world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(forceId, target) && r.SecondId == Math.Max(forceId, target))?.Value ?? 0;
        bool Enemy(int target) => target != forceId && MilitaryDiplomacy.AutomaticEnemy(world, forceId, target);
        if (!support.Complete && !support.DefenseComplete) return new(world.Clock, accuracy, [], false);
        var soldiersByForce = world.Settlements.Where(s => !s.IsDormant && s.OwnerId is not null)
            .Select(s => (Owner: s.OwnerId!.Value, Soldiers: (long)s.Soldiers))
            .Concat(world.Units.Select(u => (Owner: u.OwnerId, Soldiers: (long)u.Soldiers)))
            .GroupBy(s => s.Owner).ToDictionary(g => g.Key, g => g.Sum(s => s.Soldiers));
        var postsByPlace = world.Officers.Where(p => p.IsServing && p.OwnerId == forceId).ToLookup(p => p.SettlementId);
        var recruiting = world.Simulation.Tasks.Where(t => t.ForceId == forceId).Select(t => t.Journey?.TargetOfficerId).OfType<int>().ToHashSet();
        var recruitable = world.Officers.Where(p => !p.IsDormant && p.IsVisible && p.OwnerId != forceId &&
            (p.IsServing && p.Loyalty < 95 && p.Status != OfficerStatus.Ruler || p.Status is OfficerStatus.Free or OfficerStatus.Captive) &&
            !recruiting.Contains(p.Id)).ToArray();
        var enemies = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated && Enemy(f.Id)).Select(f => f.Id).ToHashSet();
        var gossipPairs = world.Relations.Where(r => r.Value >= 40 && enemies.Contains(r.FirstId) && enemies.Contains(r.SecondId))
            .OrderBy(r => r.FirstId).ThenBy(r => r.SecondId).ToArray();
        var pending = attempts.IsDefault ? new HashSet<(string, string)>() : attempts.Where(a => a.Outcome == "pending")
            .Select(a => (a.ControllerId, a.Key)).ToHashSet();
        // These facts belong to the frozen observation, not to an individual envoy candidate.
        // Project them once before comparing every available officer with every foreign force.
        var diplomacyTargets = world.Forces.Where(f => f.Id != forceId && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null).ToArray();
        var placesById = world.Settlements.ToDictionary(s => s.Id);
        var prisonersByForce = world.Officers.Where(p => p.OwnerId == forceId && p.Status == OfficerStatus.Captive &&
            p.SettlementId is int prison && placesById.ContainsKey(prison))
            .ToLookup(p => placesById[p.SettlementId!.Value].OwnerId);
        var ownCampaigns = campaigns.Where(g => g.ForceId == forceId).ToArray();
        var assemblingCampaigns = ownCampaigns.Where(g => g.Phase is CampaignPhase.Preparing or CampaignPhase.Assembling).ToArray();
        var attackingOwners = ownCampaigns.Where(g => g.Phase == CampaignPhase.Attacking).Select(g => g.ExpectedOwnerId).ToHashSet();
        var campaignTargets = ownCampaigns.Select(g => g.TargetId).ToHashSet();
        var borderOwners = world.Settlements.Where(s => !s.IsDestroyed && campaignTargets.Contains(s.Id)).Select(s => s.OwnerId).ToHashSet();
        var settlementCounts = world.Settlements.Where(s => !s.IsDestroyed).ToLookup(s => s.OwnerId);
        var unitsById = world.Units.ToDictionary(u => u.Id);
        var threateningOwners = support.Arrivals.Where(a => a.Hostile && a.UnitId is int id && unitsById.ContainsKey(id))
            .Select(a => unitsById[a.UnitId!.Value].OwnerId).ToHashSet();
        bool diplomaticMissionPending = world.Simulation.Tasks.Any(t => t.ForceId == forceId &&
            (t.Gift is not null || t.PrisonerReturn is not null || t.AttackRequest is not null || t.Surrender is not null || t.Journey?.ReturnLeg?.DiplomaticEnvoy == true));
        var relations = diplomacyTargets.ToDictionary(f => f.Id, f => Relation(f.Id));
        var strategyUnits = world.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && (u.OwnerId == forceId || Enemy(u.OwnerId))).ToArray();
        bool complete = true;
        foreach (var (controller, economy) in economies)
        foreach (var home in economy.Domestic.Settlements)
        {
            if (!work.Spend()) { complete = false; break; }
            var account = economy.Accounts.Single(a => a.Id == home.AccountId);
            int posts = postsByPlace[home.Id].Count();
            bool Wanted(string purpose) => completed?.Contains(controller + ":" + purpose) != true;
            foreach (int actor in home.OfficerIds.OrderByDescending(id => abilities[id].Intelligence + abilities[id].Politics).ThenBy(id => id))
            {
                var reserve = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == home.Id && r.DepartingOfficerId == actor);
                uint currentReserve = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == home.Id && r.DepartingOfficerId is null)?.Soldiers ?? 0;
                bool CanDepart(uint soldiers) => home.Soldiers >= soldiers && (reserve is null ||
                    Math.Max(0, reserve.Soldiers - (long)home.Soldiers + soldiers) <= Math.Max(0, currentReserve - (long)home.Soldiers));
                if (Wanted("personnel"))
                foreach (var person in recruitable)
                {
                    if (posts >= 3 && person.Status is OfficerStatus.Free or OfficerStatus.Captive) continue;
                    if (person.IsServing && (person.Loyalty >= 95 || person.Status == OfficerStatus.Ruler)) continue;
                    if (!work.Spend()) { complete = false; break; }
                    if (!CanDepart(0) || pending.Contains((controller, $"{nameof(RecruitOfficerCommand)}:{person.Id}"))) continue;
                    var journey = engine.GetPersonnelJourney(forceId, actor, person.Id, PersonnelJourneyKind.Recruit);
                    if (journey.Available)
                        Add(new RecruitOfficerCommand(Guid.Empty, forceId, 0, actor, person.Id), () => journey.Advice,
                            person.IsServing ? Math.Clamp(100 - person.Loyalty, 5, 60) : 40,
                            (person.BaseAbilities.Politics + person.BaseAbilities.Leadership) * 50L,
                            "personnel", person.Id, journey.Days ?? 10, 0);
                }
                if (posts < 3 && Wanted("search"))
                {
                    if (!work.Spend()) { complete = false; break; }
                    // Search advice is the public discovery consultation; undiscovered registries are never enumerated.
                    if (CanDepart(0) && !pending.Contains((controller, $"{nameof(SearchCommand)}:{home.Id}")) &&
                        !world.Simulation.Tasks.Any(t => t.ForceId == forceId && t.Kind == "personnel-search" && t.Journey?.DestinationPlaceId == home.Id))
                    {
                        var journey = engine.GetPersonnelJourney(forceId, actor, home.Id, PersonnelJourneyKind.Search);
                        if (journey.Available) Add(new SearchCommand(Guid.Empty, forceId, 0, actor, home.Id), () => journey.Advice, 35, 2500,
                            "search", home.Id, journey.Days ?? 10, 0);
                    }
                }
                if (Wanted("diplomacy") && active.Kind == ControllerKind.Automatic && controller == active.ControllerId &&
                    !diplomaticMissionPending)
                foreach (var target in diplomacyTargets)
                {
                    if (!work.Spend()) { complete = false; break; }
                    long ownSoldiers = soldiersByForce.GetValueOrDefault(forceId);
                    long targetSoldiers = soldiersByForce.GetValueOrDefault(target.Id);
                    var surrender = new DemandSurrenderCommand(Guid.Empty, forceId, 0, actor, target.Id);
                    if (ownSoldiers >= 3 * Math.Max(1000, targetSoldiers) && attackingOwners.Contains(target.Id))
                    Add(surrender, () => engine.GetSurrenderAdvice(forceId, actor, target.Id), 25,
                        settlementCounts[target.Id].Count() * 8000L, "diplomacy", target.Id, 10, 0);
                    foreach (var captive in prisonersByForce[target.Id])
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var command = new ReturnPrisonerCommand(Guid.Empty, forceId, 0, actor, target.Id, captive.Id);
                        Add(command, () => engine.GetPrisonerReturnAdvice(forceId, actor, target.Id, captive.Id), 60,
                            30L * (captive.BaseAbilities.Politics + captive.BaseAbilities.Leadership), "diplomacy", captive.Id, 10, 0);
                    }
                    int relation = relations[target.Id];
                    if (relation is >= 20 and < 60 && !borderOwners.Contains(target.Id) && account.Money >= 10000 &&
                        (threateningOwners.Contains(target.Id) ||
                         relation >= 40 && targetSoldiers >= 10000 && assemblingCampaigns.Any(g => g.ExpectedOwnerId != target.Id)))
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var gift = new GiftCommand(Guid.Empty, forceId, 0, actor, target.Id, new(GiftKind.Money, 2000, null));
                        Add(gift, () => engine.GetGiftAdvice(forceId, actor, target.Id, gift.Offer), 60, 5000, "diplomacy", target.Id, 10, 2000);
                    }
                    if (relation >= 60)
                    foreach (var goal in assemblingCampaigns)
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var request = new RequestAttackCommand(Guid.Empty, forceId, 0, actor, target.Id, goal.TargetId);
                        Add(request, () => engine.GetAttackRequestAdvice(forceId, actor, target.Id, goal.TargetId), 50, 7000, "diplomacy", goal.TargetId, 10, 0);
                    }
                }
                if (rules is null || !Wanted("strategy")) continue;
                foreach (var unit in strategyUnits)
                {
                    if (!work.Spend()) { complete = false; break; }
                    foreach (var kind in unit.OwnerId == forceId ? new[] { UnitStrategyKind.Inspire, UnitStrategyKind.Reinforce }
                        : new[] { UnitStrategyKind.Disorder, UnitStrategyKind.FalseReport })
                    {
                        if (kind == UnitStrategyKind.Inspire && unit.Morale >= 80 || kind == UnitStrategyKind.Reinforce &&
                            (home.Soldiers < 6000 || rules.Capacity(unit) <= unit.Soldiers + 1000)) continue;
                        var command = new UnitStrategyCommand(Guid.Empty, forceId, 0, actor, kind, unit.Id);
                        long food = kind == UnitStrategyKind.Reinforce ? FoodSupplyForecast.DeploymentExpenditure(home.Soldiers, 3000, account.HorizonDays, home.Wounded, home.ConsumesFood) : 0;
                        if (!work.Spend()) { complete = false; break; }
                        Add(command, () => engine.GetUnitStrategyAdvice(forceId, actor, kind, unit.Id), 50,
                            kind == UnitStrategyKind.Inspire ? (100 - unit.Morale) * 80L : Math.Min(unit.Soldiers, 8000), "strategy", unit.Id, 10, 0, food);
                    }
                }
                foreach (var goal in campaigns.Where(g => g.ForceId == forceId && g.Phase is CampaignPhase.Preparing or CampaignPhase.Assembling or CampaignPhase.Attacking))
                {
                    if (!work.Spend()) { complete = false; break; }
                    var target = world.Settlements.FirstOrDefault(s => s.Id == goal.TargetId && s.OwnerId is int owner && Enemy(owner));
                    if (target is null) continue;
                    var burn = new BurnCommand(Guid.Empty, forceId, 0, actor, target.Id);
                    Add(burn, () => engine.GetBurnAdvice(forceId, actor, target.Id), 45, target.Durability * 5L + 1000, "strategy", target.Id, 10, 0);
                    if (!work.Spend()) { complete = false; break; }
                    var seize = new SeizeCommand(Guid.Empty, forceId, 0, actor, target.Id);
                    Add(seize, () => engine.GetSeizeAdvice(forceId, actor, target.Id), 35, 2500, "strategy", target.Id, 10, 0);
                    foreach (var person in world.Officers.Where(p => p.IsVisible && p.IsServing && p.SettlementId == target.Id))
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var alienate = new AlienateCommand(Guid.Empty, forceId, 0, actor, person.Id);
                        Add(alienate, () => engine.GetAlienateAdvice(forceId, actor, person.Id), 40, 3000, "strategy", person.Id, 10, 0);
                    }
                }
                foreach (var pair in gossipPairs)
                {
                    if (!work.Spend()) { complete = false; break; }
                    var gossip = new GossipCommand(Guid.Empty, forceId, 0, actor, pair.FirstId, pair.SecondId);
                    Add(gossip, () => engine.GetGossipAdvice(forceId, actor, pair.FirstId, pair.SecondId), 40, 2000, "strategy", pair.FirstId, 10, 0);
                }
                if (!complete) break;

                void Add(GameCommand command, Func<CommandAdvice> consult, int prior, long benefit, string purpose,
                    int target, int days, long money, long food = 0)
                {
                    string key = $"{command.GetType().Name}:{target}";
                    if (command is UnitStrategyCommand strategy) key += $":{strategy.Strategy}";
                    if (command is GossipCommand gossip) key += $":{gossip.SecondTargetForceId}";
                    if (pending.Contains((controller, key))) return;
                    // Missions remove their actor from garrison command; reinforcement also removes troops.
                    // Use the same defensive requirement as departures, including already committed relief.
                    if (!support.Complete && !support.DefenseComplete) return;
                    uint departingSoldiers = command is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce } ? 3000u : 0;
                    if (!CanDepart(departingSoldiers)) return;
                    if (!work.Spend()) { complete = false; return; }
                    var advice = consult();
                    if (advice.Rejection != CommandRejection.None) return;
                    var query = command with { CommandId = queryId, Sequence = world.Simulation.NextSequence };
                    if (command is RecruitOfficerCommand or SearchCommand)
                    {
                        if (engine.GetPreparationEligibility(new(controller), query) != CommandRejection.None) return;
                    }
                    else
                    {
                        var preparation = engine.GetMissionPreparation(new(controller), query);
                        if (preparation.Rejection != CommandRejection.None) return;
                        days = preparation.Arrival is { } arrival ? (int)(arrival.DayNumber - world.Clock.DayNumber) : days;
                    }
                    int? receivingAccount = null;
                    long receivingFood = 0;
                    if (command is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce } reinforcement)
                    {
                        var unit = world.Units.Single(u => u.Id == reinforcement.TargetUnitId);
                        int destinationAccount = unit.CorpsId ?? world.Corps.Single(c => c.OwnerId == forceId && c.IsRoot).Id;
                        if (destinationAccount != account.Id)
                        {
                            var receiver = economies.SelectMany(e => e.Observation.Accounts).FirstOrDefault(a => a.Id == destinationAccount);
                            if (receiver is null) return;
                            // Keep the source's convoy/failed-delivery reserve; uncertain delivery cannot
                            // release it in advance. Successful delivery must also be affordable to the receiver.
                            uint delivered = Math.Min(3000u, rules!.Capacity(unit) - unit.Soldiers);
                            receivingAccount = destinationAccount;
                            receivingFood = (FoodSupplyForecast.FieldConsumption(unit.Soldiers + delivered, unit.Wounded) -
                                FoodSupplyForecast.FieldConsumption(unit.Soldiers, unit.Wounded)) * Math.Max(0, receiver.HorizonDays - days);
                        }
                    }
                    OpportunitySubject? subject = command switch
                    {
                        RecruitOfficerCommand or AlienateCommand or ReturnPrisonerCommand => world.Officers
                            .Where(p => p.Id == target && p.IsVisible).Select(p => new OpportunityPerson(
                                p.OwnerId, p.Status, p.SettlementId, p.UnitId, p.IsServing ? (int?)p.Loyalty : null)).FirstOrDefault(),
                        UnitStrategyCommand => world.Units.Where(u => u.Id == target).Select(u => new OpportunityUnit(
                            u.OwnerId, u.Position, u.Soldiers, u.Morale, u.Condition)).FirstOrDefault(),
                        BurnCommand or SeizeCommand or RequestAttackCommand => world.Settlements.Where(s => s.Id == target)
                            .Select(s => new OpportunityPlace(s.OwnerId, s.Soldiers, s.Durability)).FirstOrDefault(),
                        SearchCommand => new OpportunitySearch(posts),
                        DemandSurrenderCommand => new OpportunitySurrender(world.Settlements.Where(s => s.OwnerId == forceId).Sum(s => (long)s.Soldiers),
                            world.Settlements.Where(s => s.OwnerId == target).Sum(s => (long)s.Soldiers)),
                        _ => null
                    };
                    var conditions = new OpportunityConditions(accuracy, advice.AdviserId, prior, subject,
                        new(abilities[actor].Intelligence / 10, abilities[actor].Politics / 10),
                        command is GiftCommand or DemandSurrenderCommand ? (int?)Relation(target) : null,
                        command is GossipCommand pair ? world.Relations.FirstOrDefault(r => r.FirstId == pair.FirstTargetForceId && r.SecondId == pair.SecondTargetForceId)?.Value : null);
                    options.Add(new(new(controller, home.AccountId, command, [actor], 5, benefit, money, food, home.Id, target, Purpose: purpose)
                        { OfficerOpportunityCost = abilities[actor].Politics * Math.Max(1, (days + 9) / 10),
                            ReceivingAccountId = receivingAccount, ReceivingFood = receivingFood }, advice, prior, days, key, conditions));
                }
            }
            if (!complete) break;
        }
        return new(world.Clock, accuracy, options.ToImmutable(), complete);
    }
}
