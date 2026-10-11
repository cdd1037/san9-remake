using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>A permitted, prepared alternative. No world, query callbacks or resolution dice cross this boundary.</summary>
public sealed record OpportunityOption(AllocationCandidate Candidate, CommandAdvice Advice, int Prior,
    int Days, string Key, string Conditions);
public sealed record OpportunityObservation(GameClock Clock, int AdviserAccuracy,
    ImmutableArray<OpportunityOption> Options, bool Complete);
public sealed record OpportunityAttempt(int ForceId, string ControllerId, string Key, string Conditions,
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
                (a.Outcome == "pending" || a.Conditions == option.Conditions && a.RetryAfter.DayNumber > observation.Clock.DayNumber))) continue;
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
        ImmutableArray<CampaignGoal> campaigns, MilitaryRulebook? rules, AllocationWork work)
    {
        var world = engine.Snapshot;
        var abilities = engine.GetAbilities(world);
        var options = ImmutableArray.CreateBuilder<OpportunityOption>();
        int forceId = active.ForceId;
        int accuracy = world.Forces.Single(f => f.Id == forceId).AdviserId is int adviser
            ? AdviserAccuracy.Percent(abilities[adviser].Intelligence) : 50;
        int Relation(int target) => world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(forceId, target) && r.SecondId == Math.Max(forceId, target))?.Value ?? 0;
        bool Enemy(int target) => target != forceId && MilitaryDiplomacy.AutomaticEnemy(world, forceId, target);
        bool complete = true;
        foreach (var (controller, economy) in economies)
        foreach (var home in economy.Domestic.Settlements)
        {
            if (!work.Spend()) { complete = false; break; }
            var account = economy.Accounts.Single(a => a.Id == home.AccountId);
            int posts = world.Officers.Count(p => p.IsServing && p.OwnerId == forceId && p.SettlementId == home.Id);
            foreach (int actor in home.OfficerIds.OrderByDescending(id => abilities[id].Intelligence + abilities[id].Politics).ThenBy(id => id))
            {
                foreach (var person in world.Officers.Where(p => !p.IsDormant && p.IsVisible && p.OwnerId != forceId && (p.IsServing || p.Status is OfficerStatus.Free or OfficerStatus.Captive)))
                {
                    if (posts >= 3 && person.Status is OfficerStatus.Free or OfficerStatus.Captive) continue;
                    if (person.IsServing && (person.Loyalty >= 95 || person.Status == OfficerStatus.Ruler)) continue;
                    if (!work.Spend()) { complete = false; break; }
                    var journey = engine.GetPersonnelJourney(forceId, actor, person.Id, PersonnelJourneyKind.Recruit);
                    if (journey.Available && !world.Simulation.Tasks.Any(t => t.ForceId == forceId && t.Journey?.TargetOfficerId == person.Id))
                        Add(new RecruitOfficerCommand(Guid.Empty, forceId, 0, actor, person.Id), journey.Advice,
                            person.IsServing ? Math.Clamp(100 - person.Loyalty, 5, 60) : 40,
                            (person.BaseAbilities.Politics + person.BaseAbilities.Leadership) * 50L,
                            "personnel", person.Id, journey.Days ?? 10, 0);
                }
                if (posts < 3)
                {
                    if (!work.Spend()) { complete = false; break; }
                    // Search advice is the public discovery consultation; undiscovered registries are never enumerated.
                    var journey = engine.GetPersonnelJourney(forceId, actor, home.Id, PersonnelJourneyKind.Search);
                    if (journey.Available && !world.Simulation.Tasks.Any(t => t.ForceId == forceId && t.Kind == "personnel-search" && t.Journey?.DestinationPlaceId == home.Id)) Add(new SearchCommand(Guid.Empty, forceId, 0, actor, home.Id), journey.Advice, 35, 2500,
                        "search", home.Id, journey.Days ?? 10, 0);
                }
                if (active.Kind == ControllerKind.Automatic && controller == active.ControllerId &&
                    !world.Simulation.Tasks.Any(t => t.ForceId == forceId &&
                        (t.Gift is not null || t.PrisonerReturn is not null || t.AttackRequest is not null || t.Surrender is not null || t.Journey?.ReturnLeg?.DiplomaticEnvoy == true)))
                foreach (var target in world.Forces.Where(f => f.Id != forceId && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null))
                {
                    if (!work.Spend()) { complete = false; break; }
                    long ownSoldiers = world.Settlements.Where(s => !s.IsDormant && s.OwnerId == forceId).Sum(s => (long)s.Soldiers) + world.Units.Where(u => u.OwnerId == forceId).Sum(u => (long)u.Soldiers);
                    long targetSoldiers = world.Settlements.Where(s => !s.IsDormant && s.OwnerId == target.Id).Sum(s => (long)s.Soldiers) + world.Units.Where(u => u.OwnerId == target.Id).Sum(u => (long)u.Soldiers);
                    var surrender = new DemandSurrenderCommand(Guid.Empty, forceId, 0, actor, target.Id);
                    if (ownSoldiers >= 3 * Math.Max(1000, targetSoldiers) && campaigns.Any(g => g.ForceId == forceId && g.ExpectedOwnerId == target.Id && g.Phase == CampaignPhase.Attacking))
                    Add(surrender, engine.GetSurrenderAdvice(forceId, actor, target.Id), 25,
                        world.Settlements.Count(s => s.OwnerId == target.Id && !s.IsDestroyed) * 8000L, "diplomacy", target.Id, 10, 0);
                    foreach (var captive in world.Officers.Where(p => p.OwnerId == forceId && p.Status == OfficerStatus.Captive &&
                        p.SettlementId is int prison && world.Settlements.Any(s => s.Id == prison && s.OwnerId == target.Id)))
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var command = new ReturnPrisonerCommand(Guid.Empty, forceId, 0, actor, target.Id, captive.Id);
                        Add(command, engine.GetPrisonerReturnAdvice(forceId, actor, target.Id, captive.Id), 60,
                            30L * (captive.BaseAbilities.Politics + captive.BaseAbilities.Leadership), "diplomacy", captive.Id, 10, 0);
                    }
                    bool border = world.Settlements.Any(s => s.OwnerId == target.Id && !s.IsDestroyed &&
                        campaigns.Any(g => g.ForceId == forceId && g.TargetId == s.Id));
                    if (Relation(target.Id) is >= 20 and < 60 && !border && account.Money >= 10000 &&
                        (support.Arrivals.Any(a => a.Hostile && a.UnitId is int unit && world.Units.Any(u => u.Id == unit && u.OwnerId == target.Id)) ||
                         Relation(target.Id) >= 40 && targetSoldiers >= 10000 && campaigns.Any(g => g.ForceId == forceId && g.ExpectedOwnerId != target.Id &&
                             g.Phase is CampaignPhase.Preparing or CampaignPhase.Assembling)))
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var gift = new GiftCommand(Guid.Empty, forceId, 0, actor, target.Id, new(GiftKind.Money, 2000, null));
                        Add(gift, engine.GetGiftAdvice(forceId, actor, target.Id, gift.Offer), 60, 5000, "diplomacy", target.Id, 10, 2000);
                    }
                    if (Relation(target.Id) >= 60)
                    foreach (var goal in campaigns.Where(g => g.ForceId == forceId && g.Phase is CampaignPhase.Preparing or CampaignPhase.Assembling))
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var request = new RequestAttackCommand(Guid.Empty, forceId, 0, actor, target.Id, goal.TargetId);
                        Add(request, engine.GetAttackRequestAdvice(forceId, actor, target.Id, goal.TargetId), 50, 7000, "diplomacy", goal.TargetId, 10, 0);
                    }
                }
                if (rules is null) continue;
                foreach (var unit in world.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && (u.OwnerId == forceId || Enemy(u.OwnerId))))
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
                        Add(command, engine.GetUnitStrategyAdvice(forceId, actor, kind, unit.Id), 50,
                            kind == UnitStrategyKind.Inspire ? (100 - unit.Morale) * 80L : Math.Min(unit.Soldiers, 8000), "strategy", unit.Id, 10, 0, food);
                    }
                }
                foreach (var goal in campaigns.Where(g => g.ForceId == forceId && g.Phase is CampaignPhase.Preparing or CampaignPhase.Assembling or CampaignPhase.Attacking))
                {
                    if (!work.Spend()) { complete = false; break; }
                    var target = world.Settlements.FirstOrDefault(s => s.Id == goal.TargetId && s.OwnerId is int owner && Enemy(owner));
                    if (target is null) continue;
                    var burn = new BurnCommand(Guid.Empty, forceId, 0, actor, target.Id);
                    Add(burn, engine.GetBurnAdvice(forceId, actor, target.Id), 45, target.Durability * 5L + 1000, "strategy", target.Id, 10, 0);
                    if (!work.Spend()) { complete = false; break; }
                    var seize = new SeizeCommand(Guid.Empty, forceId, 0, actor, target.Id);
                    Add(seize, engine.GetSeizeAdvice(forceId, actor, target.Id), 35, 2500, "strategy", target.Id, 10, 0);
                    foreach (var person in world.Officers.Where(p => p.IsVisible && p.IsServing && p.SettlementId == target.Id))
                    {
                        if (!work.Spend()) { complete = false; break; }
                        var alienate = new AlienateCommand(Guid.Empty, forceId, 0, actor, person.Id);
                        Add(alienate, engine.GetAlienateAdvice(forceId, actor, person.Id), 40, 3000, "strategy", person.Id, 10, 0);
                    }
                }
                var enemies = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated && Enemy(f.Id)).ToArray();
                foreach (var first in enemies)
                foreach (var second in enemies.Where(f => f.Id > first.Id &&
                    world.Relations.Any(r => r.FirstId == first.Id && r.SecondId == f.Id && r.Value >= 40)))
                {
                    if (!work.Spend()) { complete = false; break; }
                    var gossip = new GossipCommand(Guid.Empty, forceId, 0, actor, first.Id, second.Id);
                    Add(gossip, engine.GetGossipAdvice(forceId, actor, first.Id, second.Id), 40, 2000, "strategy", first.Id, 10, 0);
                }
                if (!complete) break;

                void Add(GameCommand command, CommandAdvice advice, int prior, long benefit, string purpose,
                    int target, int days, long money, long food = 0)
                {
                    if (advice.Rejection != CommandRejection.None) return;
                    // Missions remove their actor from garrison command; reinforcement also removes troops.
                    // Use the same defensive requirement as departures, including already committed relief.
                    if (!support.Complete) return;
                    uint departingSoldiers = command is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce } ? 3000u : 0;
                    var reserve = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == home.Id && r.DepartingOfficerId == actor);
                    uint currentReserve = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == home.Id && r.DepartingOfficerId is null)?.Soldiers ?? 0;
                    if (home.Soldiers < departingSoldiers || reserve is not null &&
                        Math.Max(0, reserve.Soldiers - (long)home.Soldiers + departingSoldiers) >
                        Math.Max(0, currentReserve - (long)home.Soldiers)) return;
                    if (!work.Spend()) { complete = false; return; }
                    var query = command with { CommandId = new Guid(Convert.FromHexString(CanonicalJson.Hash(new { command, controller, world.Clock })).AsSpan(0, 16)), Sequence = world.Simulation.NextSequence };
                    if (command is RecruitOfficerCommand or SearchCommand)
                    {
                        if (!engine.Validate(new(controller), query).Accepted) return;
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
                    string key = $"{command.GetType().Name}:{target}";
                    if (command is UnitStrategyCommand strategy) key += $":{strategy.Strategy}";
                    if (command is GossipCommand gossip) key += $":{gossip.SecondTargetForceId}";
                    object? subject = command switch
                    {
                        RecruitOfficerCommand or AlienateCommand or ReturnPrisonerCommand => world.Officers
                            .Where(p => p.Id == target && p.IsVisible).Select(p => new
                            { p.OwnerId, p.Status, p.SettlementId, p.UnitId, Loyalty = p.IsServing ? (int?)p.Loyalty : null }).FirstOrDefault(),
                        UnitStrategyCommand => world.Units.Where(u => u.Id == target).Select(u => new
                            { u.OwnerId, u.Position, u.Soldiers, u.Morale, u.Condition }).FirstOrDefault(),
                        BurnCommand or SeizeCommand or RequestAttackCommand => world.Settlements.Where(s => s.Id == target)
                            .Select(s => new { s.OwnerId, s.Soldiers, s.Durability }).FirstOrDefault(),
                        SearchCommand => posts,
                        DemandSurrenderCommand => new { ownSoldiers = world.Settlements.Where(s => s.OwnerId == forceId).Sum(s => (long)s.Soldiers),
                            targetSoldiers = world.Settlements.Where(s => s.OwnerId == target).Sum(s => (long)s.Soldiers) },
                        _ => null
                    };
                    string conditions = CanonicalJson.Hash(new { accuracy, adviserId = advice.AdviserId, prior, subject,
                        actorBand = new { Intelligence = abilities[actor].Intelligence / 10, Politics = abilities[actor].Politics / 10 },
                        relation = command is GiftCommand or DemandSurrenderCommand ? (int?)Relation(target) : null,
                        gossipRelation = command is GossipCommand pair ? world.Relations.FirstOrDefault(r => r.FirstId == pair.FirstTargetForceId && r.SecondId == pair.SecondTargetForceId)?.Value : null });
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
