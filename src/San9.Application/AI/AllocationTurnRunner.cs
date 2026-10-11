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
            long personnelBudget = work.Remaining / 4;
            var personnelWork = new AllocationWork(personnelBudget);
            var personnel = PersonnelAllocation.Capture(engine, input, personnelWork);
            work.Charge(personnelBudget - personnelWork.Remaining);
            var proposals = support.Candidates.Concat(personnel).Where(c => !completed.Contains(c.ControllerId + ":" + c.Purpose)).ToImmutableArray();
            // Unmigrated war/diplomacy/strategy policies supply bounded proposals, never execute a separate phase.
            if (work.Remaining > 0 && support.Complete && proposals.All(c => c.Priority > 0) &&
                input.All(e => e.Observation.Complete && e.Observation.Accounts.All(a => a.Food >= a.RequiredFood)))
            {
                long opportunityBudget = work.Remaining / 2;
                var opportunityWork = new AllocationWork(opportunityBudget);
                if (Opportunity(input, support, opportunityWork) is { } opportunity) proposals = proposals.Add(opportunity);
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
                support.Complete, input.SelectMany(e => e.Observation.Accounts).ToImmutableArray(), support.Arrivals)
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
                    engine.Snapshot.Clock, deployment?.UnitId, candidate?.TargetId, candidate?.Arrival?.Days,
                    candidate?.Intent is TransportUnitCommand transport ? transport.Soldiers : null, candidate?.AdditionalFood ?? 0,
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
                if (candidate is not null && candidate.Purpose != "support") completed.Add(controllerId + ":" + candidate.Purpose);
                continue;
            }
            events.AddRange(result.Events);
            if (candidate is not null && candidate.Purpose != "support") completed.Add(controllerId + ":" + candidate.Purpose);
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

        AllocationCandidate? Opportunity(ImmutableArray<(string ControllerId, EconomicObservation Observation)> input, SupportObservation support,
            AllocationWork proposalWork)
        {
            foreach (var (controller, economy) in input)
            {
                var seat = active with { ControllerId = controller, Kind = ControllerKind.Automatic };
                if (!completed.Contains(controller + ":military") && proposalWork.Spend())
                {
                    CommandSubmissionResult Validate(CommandContext c, GameCommand command) => proposalWork.Spend()
                        ? engine.Validate(c, command) : new(false, CommandRejection.InvalidTask);
                    var command = MilitaryPlanner.ChooseCombat(engine.Snapshot, Validate, rules, seat, identity(controller, "military"),
                        controller.StartsWith("corps:", StringComparison.Ordinal) ? int.Parse(controller.AsSpan(6)) : null, proposalWork.Navigation());
                    // Reinforcement is now owned exclusively by support analysis, including arrival and source safety.
                    if (command is not WaitCommand && Safe(controller, command, economy, support) is { } candidate) return candidate;
                    completed.Add(controller + ":military");
                }
                if (!completed.Contains(controller + ":diplomacy") && controller == active.ControllerId && active.Kind == ControllerKind.Automatic && proposalWork.Spend())
                {
                    var command = DiplomacyPlanner.Choose(engine, seat, identity(controller, "diplomacy"), proposalWork.Spend);
                    if (command is not WaitCommand && Safe(controller, command, economy, support) is { } candidate) return candidate with { Purpose = "diplomacy" };
                    completed.Add(controller + ":diplomacy");
                }
                if (!completed.Contains(controller + ":strategy") && proposalWork.Spend())
                {
                    CommandRejection Eligible(CommandContext c, GameCommand command) => proposalWork.Spend()
                        ? engine.GetPreparationEligibility(c, command) : CommandRejection.InvalidTask;
                    var command = StrategyPlanner.Choose(engine.Snapshot, Eligible, rules, seat, identity(controller, "strategy"));
                    if (command is not WaitCommand && Safe(controller, command, economy, support) is { } candidate) return candidate with { Purpose = "strategy" };
                    completed.Add(controller + ":strategy");
                }
            }
            return null;
        }

        AllocationCandidate? Safe(string controller, GameCommand command, EconomicObservation economy, SupportObservation support)
        {
            var world = engine.Snapshot;
            int? source = command switch
            {
                DeployUnitCommand d => d.SettlementId,
                OrderUnitCommand => null,
                _ => Actor(command) is int person ? world.Officers.Single(p => p.Id == person).SettlementId : null
            };
            var home = economy.Domestic.Settlements.FirstOrDefault(s => s.Id == source);
            if (home is null && command is not OrderUnitCommand) return null;
            // Discretionary departures cannot consume a threatened garrison or its remaining officer supply.
            if (source is int id && support.Arrivals.Any(a => a.Hostile && a.TargetId == id && a.Estimate.Status != ArrivalEstimateStatus.Unreachable)) return null;
            long cost = command switch
            {
                DeployUnitCommand d when rules is not null => (long)rules.Formations[d.FormationId].DeploymentCost +
                    (d.NavalFormationId is int naval ? rules.Formations[naval].DeploymentCost : 0),
                GiftCommand { Offer.Kind: GiftKind.Money } g => g.Offer.Amount,
                _ => 0
            };
            int accountId = home?.AccountId ?? economy.Accounts.First().Id;
            var account = economy.Accounts.Single(a => a.Id == accountId);
            long food = command is DeployUnitCommand deploy && home is not null
                ? FoodSupplyForecast.DeploymentExpenditure(home.Soldiers, deploy.Soldiers, account.HorizonDays, home.Wounded, home.ConsumesFood) : 0;
            if (command is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce } && home is not null)
                food = FoodSupplyForecast.DeploymentExpenditure(home.Soldiers, 3000, account.HorizonDays, home.Wounded, home.ConsumesFood);
            if (account.Money < cost || account.Food < account.RequiredFood + food) return null;
            return new(controller, accountId, command with { CommandId = Guid.Empty, Sequence = 0 },
                command is DeployUnitCommand deployment ? deployment.OfficerIds : Actor(command) is int actor ? [actor] : [],
                3, 0, cost, food, source, Purpose: "military");
        }
    }

    private static int? Actor(GameCommand command) => command switch
    {
        GiftCommand c => c.OfficerId, ReturnPrisonerCommand c => c.OfficerId, RequestAttackCommand c => c.OfficerId,
        DemandSurrenderCommand c => c.OfficerId, UnitStrategyCommand c => c.OfficerId, BurnCommand c => c.OfficerId,
        SeizeCommand c => c.OfficerId, AlienateCommand c => c.OfficerId, GossipCommand c => c.OfficerId, _ => null
    };
}
