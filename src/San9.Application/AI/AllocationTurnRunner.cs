using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class AllocationTurnRunner
{
    internal static AiSessionState Run(SimulationEngine engine, ControlSeat active, MilitaryRulebook? rules,
        AiSessionState state, Func<string, string, Guid> identity, List<DomainEvent> events,
        List<AllocationTrace>? traces = null, List<AllocationBatchTrace>? batches = null)
    {
        var work = new AllocationWork(state.Configuration.WorkBudget);
        var routes = new ArrivalProjectionCache();
        var controllers = (active.Kind == ControllerKind.Automatic ? new[] { active.ControllerId } : [])
            .Concat(engine.Snapshot.Corps.Where(c => c.OwnerId == active.ForceId && c.ControlMode == CorpsControlMode.Automatic &&
                !(active.Kind == ControllerKind.Automatic && c.IsRoot)).OrderBy(c => c.Id).Select(c => CorpsControl.ControllerId(c.Id)))
            .ToImmutableArray();
        HashSet<string> completed = [];
        HashSet<string> developed = [];
        HashSet<string> purchaseLimited = [];
        Dictionary<string, int> purchases = [];
        HashSet<string> rejected = [];
        while (work.Remaining > 0 && controllers.Length > 0)
        {
            var economies = ImmutableArray.CreateBuilder<(string ControllerId, EconomicObservation Observation)>();
            // Bound observation as well as navigation. Keep room to choose and submit an already known action.
            long observationRemaining = Math.Min(work.Remaining, Math.Max(1, work.Remaining / 2));
            for (int index = 0; index < controllers.Length; index++)
            {
                string controller = controllers[index];
                long allowance = (observationRemaining + controllers.Length - index - 1) / (controllers.Length - index);
                var observation = EconomicObservation.Capture(engine, active, new(controller), allowance, quoteAll: true);
                work.Charge(observation.ProjectionWork);
                observationRemaining -= observation.ProjectionWork;
                economies.Add((controller, observation));
            }
            var input = economies.ToImmutable();
            // Retain decision capacity when route search is unfinished; optional navigation cannot starve known food needs.
            long supportBudget = work.Remaining / 2;
            var supportWork = new AllocationWork(supportBudget);
            var support = SupportObservationCapture.Capture(engine, active, input, rules, supportWork, routes);
            work.Charge(supportBudget - supportWork.Remaining);
            input = EconomicSupplyProjection.Apply(input, support.FoodChanges);
            var proposals = support.Candidates.Where(c => !completed.Contains(c.ControllerId + ":" + c.Purpose)).ToImmutableArray();
            bool campaignComplete = true;
            if (rules is not null && support.Complete && work.Remaining > 1 && input.All(e => e.Observation.Complete) &&
                (state.Campaigns.Any(g => g.ForceId == active.ForceId) || input.All(e => e.Observation.Accounts.All(a => a.Food >= a.RequiredFood))))
            {
                long campaignBudget = work.Remaining / 2;
                var campaignWork = new AllocationWork(campaignBudget);
                var observation = CampaignObservationCapture.Capture(engine, active, input, support, rules, campaignWork, routes, state.Campaigns);
                campaignComplete = observation.Complete;
                var campaign = CampaignPlanner.Decide(observation, state.Campaigns, campaignWork);
                state = state with { Campaigns = campaign.Goals };
                proposals = proposals.AddRange(campaign.Candidates.Where(c => !completed.Contains(c.ControllerId + ":" + c.Purpose)));
                work.Charge(campaignBudget - campaignWork.Remaining);
            }
            if (work.Remaining > 2 && input.All(e => e.Observation.Complete))
            {
                long opportunityBudget = work.Remaining / 2;
                var opportunityWork = new AllocationWork(opportunityBudget);
                var captureWork = new AllocationWork(opportunityWork.Remaining / 2);
                long captureBudget = captureWork.Remaining;
                var observation = OpportunityObservationCapture.Capture(engine, active, input, support, state.Campaigns, rules, captureWork);
                opportunityWork.Charge(captureBudget - captureWork.Remaining);
                proposals = proposals.AddRange(OpportunityPlanner.Plan(observation, state.Opportunities, opportunityWork)
                    .Where(c => !completed.Contains(c.ControllerId + ":" + c.Purpose)));
                proposals = proposals.AddRange(PersonnelConfiguration.Capture(engine, active, input, opportunityWork)
                    .Where(c => !completed.Contains(c.ControllerId + ":" + c.Purpose)));
                work.Charge(opportunityBudget - opportunityWork.Remaining);
            }
            var decision = SharedAllocator.Decide(input, proposals, state.FoodSecurity, Math.Max(0, work.Remaining - 1),
                developed, purchaseLimited, rejected);
            work.Charge(decision.Work);
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
                support.Complete && campaignComplete, input.SelectMany(e => e.Observation.Accounts).ToImmutableArray(), support.Arrivals)
                { Candidates = proposals, SelectedIntentHash = intent is null ? null : CanonicalJson.Hash(intent) });
            if (batches?.Count > AllocationDiagnostics.Capacity) batches.RemoveAt(0);
            if (controllerId is null || intent is null) break;
            if (!work.Spend()) break;
            var source = candidate?.SourceId is int sourceId ? engine.Snapshot.Settlements.Single(s => s.Id == sourceId) : null;
            var command = intent with { CommandId = identity(controllerId, candidate?.Purpose ?? (economic is PurchaseFoodIntent ? "supply" : "domestic")),
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
                    { IntentHash = CanonicalJson.Hash(intent), AccountId = candidate?.AccountId,
                        PredictedAccountFoodExpenditure = candidate?.Intent is TransportUnitCommand shipment && source is not null && candidate.Arrival?.Days is int days
                            ? input.SelectMany(e => e.Observation.Accounts).Single(a => a.Id == candidate.AccountId).DailyConsumption * days +
                                FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, shipment.Soldiers, days, source.Wounded, source.ConsumesFood) : null });
                if (traces.Count > AllocationDiagnostics.Capacity) traces.RemoveAt(0);
            }
            if (!result.Submission.Accepted)
            {
                rejected.Add(CanonicalJson.Hash(intent));
                if (economic is not null) rejected.Add(CanonicalJson.Hash(economic));
                if (economic is DevelopIntent failure) rejected.Add(CanonicalJson.Hash(failure.Development));
                if (candidate is not null && candidate.Purpose is not ("support" or "campaign")) completed.Add(controllerId + ":" + candidate.Purpose);
                continue;
            }
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
                var deployed = result.Events.OfType<UnitDeployedEvent>().Select(e => e.UnitId).Concat(command is OrderUnitCommand order ? [order.UnitId] : []);
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
                if (account.Food + purchase.Amount >= account.RequiredFood)
                    state = state with { FoodSecurity = state.FoodSecurity.Where(g => g.ControllerId != controllerId || g.AccountId != account.Id).ToImmutableArray() };
                purchases[controllerId] = purchases.GetValueOrDefault(controllerId) + 1;
                if (purchases[controllerId] >= state.Configuration.MaximumPurchases) purchaseLimited.Add(controllerId);
            }
        }
        return state;

    }
}
