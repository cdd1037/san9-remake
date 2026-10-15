using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class AllocationTurnRunner
{
    internal static AiSessionState Run(SimulationEngine engine, ControlSeat active, MilitaryRulebook? rules,
        AiSessionState state, List<DomainEvent> events,
        List<AllocationTrace>? traces = null, List<AllocationBatchTrace>? batches = null)
    {
        var work = new AllocationWork(state.Configuration.WorkBudget);
        var routes = new ArrivalProjectionCache();
        var observations = new AllocationObservations();
        var controllers = (active.Kind == ControllerKind.Automatic ? new[] { active.ControllerId } : [])
            .Concat(engine.Snapshot.Corps.Where(c => c.OwnerId == active.ForceId && c.ControlMode == CorpsControlMode.Automatic &&
                !(active.Kind == ControllerKind.Automatic && c.IsRoot)).OrderBy(c => c.Id).Select(c => CorpsControl.ControllerId(c.Id)))
            .ToImmutableArray();
        HashSet<string> completed = [];
        HashSet<string> developed = [];
        HashSet<string> purchaseLimited = [];
        Dictionary<string, int> purchases = [];
        var rejected = new RejectedPlanningIntents();
        var workByKind = new Dictionary<string, long>();
        var allocatedByKind = new Dictionary<string, long>();
        var elapsedByKind = new Dictionary<string, long>();
        var pathsByKind = new Dictionary<string, long>();
        T Measure<T>(string kind, Func<T> operation)
        {
            if (batches is null) return operation();
            using var paths = new PathSearchDiagnostics();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            T result = operation();
            elapsedByKind[kind] = elapsedByKind.GetValueOrDefault(kind) + System.Diagnostics.Stopwatch.GetTimestamp() - start;
            allocatedByKind[kind] = allocatedByKind.GetValueOrDefault(kind) + GC.GetAllocatedBytesForCurrentThread() - bytes;
            pathsByKind[kind] = pathsByKind.GetValueOrDefault(kind) + paths.Queries;
            return result;
        }
        void Charge(string kind, long amount)
        {
            work.Charge(amount);
            workByKind[kind] = workByKind.GetValueOrDefault(kind) + amount;
        }
        bool searchComplete = true;
        ImmutableArray<EconomicAccountObservation> lastAccounts = [];
        while (work.Remaining > 0 && controllers.Length > 0)
        {
            if (observations.Economies.IsDefault)
            {
                var economies = ImmutableArray.CreateBuilder<(string ControllerId, EconomicObservation Observation)>();
                // Bound observation as well as navigation. Keep room to choose and submit an already known action.
                long observationRemaining = Math.Min(work.Remaining, Math.Max(1, work.Remaining / 2));
                for (int index = 0; index < controllers.Length; index++)
                {
                    string controller = controllers[index];
                    long allowance = (observationRemaining + controllers.Length - index - 1) / (controllers.Length - index);
                    var observation = EconomicObservation.Capture(engine, active, new(controller), allowance);
                    Charge("economy", observation.ProjectionWork);
                    observationRemaining -= observation.ProjectionWork;
                    economies.Add((controller, observation));
                }
                observations.Economies = economies.ToImmutable();
            }
            var input = observations.Economies;
            // Reserve account comparison and one submission before any optional domain search.
            long decisionReserve = 2 + observations.RefreshWork(new WaitCommand(Guid.Empty, active.ForceId, 0), engine.Snapshot) +
                controllers.Length + input.Sum(e => 2L * e.Observation.Accounts.Length);
            long OptionalWork() => Math.Max(0, work.Remaining - decisionReserve);
            // Retain decision capacity when route search is unfinished; optional navigation cannot starve known food needs.
            if (observations.Support is null)
            {
                long supportBudget = OptionalWork() / 2;
                var supportWork = new AllocationWork(supportBudget);
                observations.Support = Measure("support", () => SupportObservationCapture.Capture(engine, active, input, rules, supportWork, routes));
                Charge("support", supportBudget - supportWork.Remaining);
            }
            var support = observations.Support;
            input = EconomicSupplyProjection.Apply(input, support.FoodChanges);
            for (int index = 0; index < input.Length; index++)
            {
                var (owner, economy) = input[index];
                var missing = economy.Accounts.Where(a => a.Food < a.RequiredFood && !economy.QuotedAccounts.Contains(a.Id))
                    .Select(a => a.Id).ToHashSet();
                if (missing.Count == 0) continue;
                var quoted = EconomicObservation.CapturePurchases(engine, active.ForceId, economy, missing, OptionalWork() / 4);
                Charge("economy", quoted.ProjectionWork);
                input = input.SetItem(index, (owner, quoted));
            }
            observations.Economies = input;
            if (observations.Configuration.IsDefault)
            {
                long configurationBudget = OptionalWork() / 4;
                var configurationWork = new AllocationWork(configurationBudget);
                observations.Configuration = PersonnelConfiguration.Capture(engine, active, input, configurationWork);
                Charge("configuration", configurationBudget - configurationWork.Remaining);
            }
            bool Available(AllocationCandidate c) => !completed.Contains(c.ControllerId + ":" + c.Purpose) &&
                AllocationObservations.CanDepart(c, support, input);
            var configuration = observations.ConfigurationCandidates().Where(Available).ToImmutableArray();
            // Resolve free capability changes before evaluating facts they invalidate. Rewards still
            // compete with funded missions; this is a dependency order, not a blanket configuration tier.
            bool changingCapabilities = configuration.Any(c => c.Intent is AssignOfficesCommand or GrantTreasureCommand or
                ConfiscateTreasureCommand or AppointAdviserCommand or SelectTacticCommand && SharedAllocator.CanFund(c, input, rejected));
            if (observations.Field.IsDefault && !changingCapabilities)
            {
                var fieldWork = new AllocationWork(OptionalWork() / 8);
                var field = rules is null ? new FieldObservation([], true) : FieldOperations.Capture(engine, active, input, rules, fieldWork, routes);
                observations.Field = field.Candidates;
                observations.FieldComplete = field.Complete;
                Charge("field", fieldWork.Maximum - fieldWork.Remaining);
            }
            var proposals = observations.SupportCandidates(engine.Snapshot, input)
                .AddRange(configuration)
                .AddRange(observations.Field.IsDefault ? [] : observations.Remaining(observations.Field))
                .Where(Available).ToImmutableArray();
            decisionReserve += proposals.Length;
            if (!proposals.IsEmpty) decisionReserve += proposals.Max(c => observations.ExecutionWork(c, engine.Snapshot));
            void CaptureOpportunities(long budget)
            {
                if (!observations.Opportunities.IsDefault || budget < 2) return;
                var opportunityWork = new AllocationWork(budget);
                var captureWork = new AllocationWork(budget / 2);
                var observation = OpportunityObservationCapture.Capture(engine, active, input, support, state.Campaigns, rules, captureWork,
                    completed, state.Opportunities);
                opportunityWork.Charge(captureWork.Maximum - captureWork.Remaining);
                observations.Opportunities = OpportunityPlanner.Plan(observation, state.Opportunities, opportunityWork);
                observations.OpportunitiesComplete = observation.Complete && opportunityWork.Remaining > 0;
                Charge("opportunity", budget - opportunityWork.Remaining);
            }
            bool campaignComplete = observations.CampaignComplete;
            if (!changingCapabilities && observations.Campaign.IsDefault && !proposals.Any(c => !c.Configuration && c.Priority < 4 && SharedAllocator.CanFund(c, input, rejected)) && rules is not null && (support.Complete || support.DefenseComplete) && OptionalWork() > 1 && input.All(e => e.Observation.Complete) &&
                (state.Campaigns.Any(g => g.ForceId == active.ForceId) || input.All(e => e.Observation.Accounts.All(a => a.Food >= a.RequiredFood))))
            {
                long campaignBudget = OptionalWork() / 2;
                var observation = observations.CampaignInput;
                using var capture = observation is null ? new CampaignObservationCapture(engine, active, input, support, rules, routes, state.Campaigns) : null;
                if (capture is not null)
                {
                    var captureWork = new AllocationWork(campaignBudget * 3 / 4);
                    Measure("campaign-observation", () => { capture.Advance(captureWork, stopAtDecision: true); return 0; });
                    long used = captureWork.Maximum - captureWork.Remaining;
                    Charge("campaign-observation", used);
                    campaignBudget -= used;
                    observation = capture.Observation;
                }
                // Reserve a comparison for every possible output before consuming the reclaimed pool.
                // Funded campaign actions are bounded by observed forces, returns, consolidation and accounts.
                campaignBudget = Math.Min(campaignBudget, OptionalWork());
                long forceOutputs = observation!.Forces.Select(f => (f.Action.TargetId, f.Action.SourceId, f.UnitId)).Distinct().LongCount();
                long allocationReserve = Math.Min(campaignBudget, forceOutputs + observation.Returns.Length +
                    observation.Consolidation.Length + observation.Accounts.Length +
                    observation.Forces.Select(f => observations.ExecutionWork(f.Action, engine.Snapshot)).DefaultIfEmpty(0).Max());
                campaignBudget -= allocationReserve;
                decisionReserve += allocationReserve;
                var campaignWork = new AllocationWork(campaignBudget);
                // Ask for optional campaign funding only after there is a concrete force to fund.
                // Quotes already requested for subsistence are reused, never regenerated here.
                var accountsToQuote = observation.Forces.GroupBy(f => f.Action.AccountId).Where(group =>
                {
                    var account = observation.Accounts.Single(a => a.Id == group.Key);
                    long potential = group.GroupBy(f => (f.Action.SourceId, f.UnitId)).Sum(source => source.Max(f =>
                        f.Action.AdditionalFood + FoodSupplyForecast.FieldConsumption(f.Soldiers, 0) *
                        Math.Max(0, (f.Action.Arrival?.Days ?? 0) + 102 - account.HorizonDays)));
                    return account.Food >= account.RequiredFood && account.Food < account.RequiredFood + potential;
                }).Select(g => g.Key).ToHashSet();
                if (accountsToQuote.Count > 0)
                {
                    var purchasesForCampaign = observation.Purchases.ToBuilder();
                    foreach (var (owner, economy) in input)
                    {
                        var quotes = EconomicObservation.CapturePurchases(engine, active.ForceId, economy,
                            accountsToQuote.Except(economy.QuotedAccounts).ToHashSet(), campaignWork.Remaining / 2);
                        campaignWork.Charge(quotes.ProjectionWork);
                        purchasesForCampaign.AddRange(quotes.Purchases.Except(economy.Purchases).Select(p => new CampaignPurchase(owner, p)));
                    }
                    observation = observation with { Purchases = purchasesForCampaign.ToImmutable() };
                }
                observations.CampaignInput = observation;
                var campaign = Measure("campaign", () => CampaignPlanner.Decide(observation, state.Campaigns, campaignWork));
                Charge("campaign", campaignBudget - campaignWork.Remaining);
                // Additional observation must answer a missing plan, rather than consume every spare unit before comparing anything.
                while (campaign.Candidates.IsEmpty && capture is { Finished: false } && OptionalWork() > 4)
                {
                    var further = new AllocationWork(OptionalWork() / 2);
                    Measure("campaign-observation", () => { capture.Advance(further, stopAtDecision: true); return 0; });
                    Charge("campaign-continuation", further.Maximum - further.Remaining);
                    observation = capture.Observation;
                    var comparison = new AllocationWork(OptionalWork() / 2);
                    campaign = Measure("campaign", () => CampaignPlanner.Decide(observation, state.Campaigns, comparison));
                    Charge("campaign", comparison.Maximum - comparison.Remaining);
                    if (further.Remaining == further.Maximum) break;
                }
                campaignComplete = observation.Complete && campaignWork.Remaining > 0;
                state = state with { Campaigns = campaign.Goals };
                observations.CampaignInput = observation;
                observations.Campaign = campaign.Candidates;
                observations.CampaignComplete = campaignComplete;
                if (campaign.Candidates.Any(c => SharedAllocator.CanFund(c, input, rejected))) observations.Opportunities = default;
            }
            if (!observations.Campaign.IsDefault)
                proposals = proposals.AddRange(observations.CampaignCandidates(engine.Snapshot, input).Where(Available));
            bool opportunityComplete = !observations.Opportunities.IsDefault ? observations.OpportunitiesComplete :
                OptionalWork() > 2 && input.All(e => e.Observation.Complete);
            if (opportunityComplete && !changingCapabilities)
            {
                long opportunityBudget = OptionalWork() / 2;
                // Opportunities all have priority 5. A funded earlier tier wins regardless of their scores;
                // query them once the preceding tiers have committed their actors and resources.
                if (observations.Opportunities.IsDefault && !proposals.Any(c => !c.Configuration && c.Priority < 5 && SharedAllocator.CanFund(c, input, rejected)))
                {
                    CaptureOpportunities(opportunityBudget);
                }
                opportunityComplete &= observations.OpportunitiesComplete;
            }
            if (!observations.Opportunities.IsDefault)
                proposals = proposals.AddRange(observations.Remaining(observations.Opportunities).Where(c =>
                    Available(c) &&
                    SharedAllocator.CanFund(c, input, rejected)));
            var decision = SharedAllocator.Decide(input, proposals, state.FoodSecurity, Math.Max(0, work.Remaining - 1),
                developed, purchaseLimited, rejected);
            Charge("allocation", decision.Work);
            searchComplete &= input.All(e => e.Observation.Complete && e.Observation.QuotesComplete) && support.Complete &&
                observations.FieldComplete && campaignComplete && opportunityComplete && !decision.Exhausted;
            lastAccounts = input.SelectMany(e => e.Observation.Accounts).ToImmutableArray();
            state = state with { FoodSecurity = decision.Goals };
            var candidate = decision.Candidate;
            var economic = decision.Economic;
            string? controllerId = decision.ControllerId;
            var intent = candidate?.Intent ?? economic switch
            {
                PurchaseFoodIntent food => new FoodTradeCommand(Guid.Empty, active.ForceId, 0,
                    food.SettlementId, food.OfficerIds, FoodTradeDirection.Buy, food.Amount),
                DevelopIntent develop => new DevelopCommand(Guid.Empty, active.ForceId, 0, develop.Development),
                null => null,
                _ => throw new InvalidOperationException("Unknown allocation intent.")
            };
            batches?.Add(new(engine.Snapshot.Clock, active.ForceId, state.Configuration.WorkBudget - work.Remaining,
                searchComplete, lastAccounts, support.Arrivals)
                { Candidates = proposals });
            if (controllerId is null || intent is null) break;
            // Commit a selected sequence without reopening allocation between its zero-delay steps.
            // Reserve submission and ledger work for the whole immediate chain before starting it.
            if (candidate is { BeforeDeparture.IsEmpty: false, Continuation.DelayDays: 0 } optional &&
                work.Remaining < observations.ExecutionWork(optional, engine.Snapshot))
            {
                candidate = optional.Continuation.Action;
                controllerId = candidate.ControllerId;
                intent = candidate.Intent;
            }
            if (candidate is not null && work.Remaining < observations.ExecutionWork(candidate, engine.Snapshot)) break;
            if (work.Remaining < observations.RefreshWork(intent, engine.Snapshot) + 2) break;
            do
            {
                long refreshWork = observations.RefreshWork(intent, engine.Snapshot);
                if (work.Remaining < refreshWork + 2) break;
                Charge("eligibility", 1);
                var eligibilityQuery = intent with { CommandId = SimulationIdentity.Command(engine.Snapshot.Simulation.NextSequence, active.ForceId),
                    Sequence = engine.Snapshot.Simulation.NextSequence };
                if (intent is RewardOfficerCommand reward)
                {
                    var person = engine.Snapshot.Officers.Single(p => p.Id == reward.OfficerId);
                    if (PersonnelRewardMath.Gain(person.Loyalty, person.Righteousness) <= 0) { rejected.Add(intent); break; }
                }
                // Configuration was captured once and may have lost its local prerequisites.
                // Other actions retain their domain preparation and the authoritative Execute validation.
                if (candidate?.Configuration == true && engine.GetPreparationEligibility(new(controllerId), eligibilityQuery) != CommandRejection.None)
                {
                    rejected.Add(intent);
                    if (economic is not null) rejected.Add(economic);
                    if (economic is DevelopIntent invalid) rejected.Add(invalid.Development);
                    break;
                }
                if (!work.Spend()) break;
                workByKind["execution"] = workByKind.GetValueOrDefault("execution") + 1;
                var source = traces is not null && candidate?.Intent is TransportUnitCommand && candidate.SourceId is int sourceId
                    ? engine.Snapshot.Settlements.Single(s => s.Id == sourceId) : null;
                var command = intent with { CommandId = SimulationIdentity.Command(engine.Snapshot.Simulation.NextSequence, active.ForceId),
                    Sequence = engine.Snapshot.Simulation.NextSequence };
                var result = engine.Execute(new(controllerId), command);
                if (traces is not null)
                {
                    var deployment = result.Events.OfType<UnitDeployedEvent>().FirstOrDefault();
                    traces.Add(new(command.CommandId, controllerId, candidate?.Purpose ?? (economic is PurchaseFoodIntent ? "supply" : "domestic"),
                        engine.Snapshot.Clock, deployment?.UnitId, candidate?.TargetId, candidate?.Campaign?.ExpectedDays ?? candidate?.Arrival?.Days,
                        candidate?.Intent is TransportUnitCommand transport ? transport.Soldiers : candidate?.Intent is DeployUnitCommand expedition ? expedition.Soldiers : null, candidate?.AdditionalFood ?? 0,
                        result.Submission.Accepted ? deployment is null || candidate?.Arrival is null ? AllocationOutcome.Completed : AllocationOutcome.Pending : AllocationOutcome.Rejected,
                        work.Remaining, input.SelectMany(e => e.Observation.Accounts).ToImmutableArray())
                        { AccountId = candidate?.AccountId,
                            ForceId = active.ForceId, CommandType = command.GetType().Name,
                            Rejection = result.Submission.Rejection,
                            PredictedAccountFoodExpenditure = candidate?.Intent is TransportUnitCommand shipment && source is not null && candidate.Arrival?.Days is int days
                                ? input.SelectMany(e => e.Observation.Accounts).Single(a => a.Id == candidate.AccountId).DailyConsumption * days +
                                    FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, shipment.Soldiers, days, source.Wounded, source.ConsumesFood) : null });
                }
                if (!result.Submission.Accepted)
                {
                    rejected.Add(intent);
                    if (economic is not null) rejected.Add(economic);
                    if (economic is DevelopIntent failure) rejected.Add(failure.Development);
                    if (candidate is not null && candidate.Purpose is not ("support" or "campaign")) completed.Add(controllerId + ":" + candidate.Purpose);
                    break;
                }
                Charge("ledger", refreshWork);
                observations.Applied(engine, active, rules, command, intent, candidate, result.Events);
                input = EconomicSupplyProjection.Apply(observations.Economies, observations.Support?.FoodChanges ?? []);
                observations.Economies = input;
                lastAccounts = input.SelectMany(e => e.Observation.Accounts).ToImmutableArray();
                events.AddRange(result.Events);
                if (candidate?.Opportunity is { } opportunity)
                {
                    var started = result.Events.OfType<TaskStartedEvent>().FirstOrDefault();
                    if (started is not null)
                        state = state with { Opportunities = state.Opportunities
                            .Where(a => a.ControllerId != controllerId || a.Key != opportunity.Key)
                            .Append(new(active.ForceId, controllerId, opportunity.Key, opportunity.Conditions,
                                started.Task.Id, started.Task.OfficerId, engine.Snapshot.Clock, engine.Snapshot.Clock, "pending")).ToImmutableArray() };
                }
                state = state with { Opportunities = OpportunityPlanner.Feedback(state.Opportunities, engine.Snapshot, result.Events) };
                if (candidate?.Campaign is { } commitment)
                {
                    var previous = state.Campaigns.FirstOrDefault(g => g.ForceId == commitment.ForceId && g.TargetId == commitment.TargetId);
                    var deployed = (command is DeployUnitCommand ? result.Events.OfType<UnitDeployedEvent>().Select(e => e.UnitId) : [])
                        .Concat(command is OrderUnitCommand order ? [order.UnitId] : []);
                    var updated = (previous ?? commitment) with { UnitIds = (previous?.UnitIds ?? []).Concat(deployed).Distinct().Order().ToImmutableArray(),
                        Phase = command is DeployUnitCommand or OrderUnitCommand ? CampaignPhase.Assembling : CampaignPhase.Preparing,
                        SourceIds = commitment.SourceIds };
                    state = state with { Campaigns = state.Campaigns.Where(g => g.ForceId != updated.ForceId || g.TargetId != updated.TargetId)
                        .Append(updated).OrderBy(g => g.ForceId).ThenBy(g => g.TargetId).ToImmutableArray() };
                }
                if (candidate is not null && candidate.Purpose is not ("support" or "campaign")) completed.Add(controllerId + ":" + candidate.Purpose);
                if (economic is DevelopIntent) developed.Add(controllerId);
                if (economic is PurchaseFoodIntent purchase)
                {
                    var account = input.Single(e => e.ControllerId == controllerId).Observation.Accounts.Single(a => a.Id == purchase.AccountId);
                    if (account.Food >= account.RequiredFood)
                        state = state with { FoodSecurity = state.FoodSecurity.Where(g => g.ControllerId != controllerId || g.AccountId != account.Id).ToImmutableArray() };
                    purchases[controllerId] = purchases.GetValueOrDefault(controllerId) + 1;
                    if (purchases[controllerId] >= state.Configuration.MaximumPurchases) purchaseLimited.Add(controllerId);
                }
                var continuation = candidate?.Continuation;
                if (continuation is not { DelayDays: 0 }) break;
                candidate = continuation.Action with { Campaign = continuation.Action.Campaign ?? candidate!.Campaign };
                controllerId = candidate.ControllerId;
                intent = candidate.Intent;
                economic = null;
                if (!SharedAllocator.CanFund(candidate, observations.Economies, rejected)) break;
            } while (work.Remaining > 0);
        }
        batches?.Add(new(engine.Snapshot.Clock, active.ForceId, state.Configuration.WorkBudget - work.Remaining,
            searchComplete && work.Remaining > 0, lastAccounts, [])
            { IsTurnSummary = true, BudgetExhausted = work.Remaining == 0, WorkByKind = workByKind.ToImmutableDictionary(),
                AllocatedBytesByKind = allocatedByKind.ToImmutableDictionary(), ElapsedTicksByKind = elapsedByKind.ToImmutableDictionary(),
                PathQueriesByKind = pathsByKind.ToImmutableDictionary() });
        return state;

    }
}
