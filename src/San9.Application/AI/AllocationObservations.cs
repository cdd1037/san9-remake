using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>A control turn's plans and live allocation ledger. Own commitments do not restart strategic observation.</summary>
internal sealed class AllocationObservations
{
    internal ImmutableArray<(string ControllerId, EconomicObservation Observation)> Economies { get; set; }
    internal SupportObservation? Support { get; set; }
    internal ImmutableArray<AllocationCandidate> Configuration { get; set; }
    internal ImmutableArray<AllocationCandidate> Campaign { get; set; }
    internal CampaignObservation? CampaignInput { get; set; }
    internal ImmutableArray<AllocationCandidate> Opportunities { get; set; }
    internal bool CampaignComplete { get; set; } = true;
    internal bool OpportunitiesComplete { get; set; } = true;
    private readonly HashSet<GameCommand> executed = new(PlanningIntentEquality.Instance);
    private readonly HashSet<int> appointed = [];
    private int? campaignTarget;
    private GarrisonAssessment? defense;

    internal IEnumerable<AllocationCandidate> Remaining(IEnumerable<AllocationCandidate> candidates) => candidates.Where(c =>
        !executed.Contains(c.Intent) && (c.Campaign is null || campaignTarget is null || c.Campaign.TargetId == campaignTarget) &&
        (c.Intent is not AssignOfficesCommand offices || !offices.Assignments.Any(a => appointed.Contains(a.OfficerId))));

    internal void Applied(SimulationEngine engine, ControlSeat seat, MilitaryRulebook? rules, GameCommand command,
        GameCommand intent, AllocationCandidate? candidate, IEnumerable<DomainEvent> events)
    {
        executed.Add(intent);
        if (candidate?.Campaign is { } campaign) campaignTarget = campaign.TargetId;
        if (command is AssignOfficesCommand assigned)
            foreach (var person in assigned.Assignments) appointed.Add(person.OfficerId);
        var world = engine.Snapshot;
        var places = world.Settlements.ToDictionary(s => s.Id);
        var people = world.Officers.Where(p => p.OwnerId == seat.ForceId).ToDictionary(p => p.Id);
        var accounts = world.Corps.ToDictionary(c => c.Id);
        var busy = world.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        bool abilitiesChanged = command is AssignOfficesCommand or GrantTreasureCommand or ConfiscateTreasureCommand;
        var abilities = abilitiesChanged ? engine.GetAbilities(world) : null;
        int? changedAccount = command switch
        {
            DevelopCommand { Development.Activity: DomesticActivity.Recruit } recruit =>
                Economies.SelectMany(e => e.Observation.Domestic.Settlements)
                    .Single(s => s.Id == recruit.Development.SettlementId).AccountId,
            DeployUnitCommand or TransportUnitCommand or UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce } => candidate!.AccountId,
            _ => null
        };
        // Only troop changes alter daily consumption. Other actions merely spend the actual account balance.
        long? consumption = changedAccount is int accountId ? FoodSupplyForecast.Capture(world, seat.ForceId, accountId).DailyConsumption : null;
        Economies = Economies.Select(e =>
        {
            var economy = e.Observation;
            var homes = economy.Domestic.Settlements.Select(s =>
            {
                var current = places[s.Id];
                var treasury = accounts[s.AccountId].Treasury;
                return DomesticObservation.Settlement(current, s.AccountId, treasury.Money, treasury.Food,
                    s.OfficerIds.Where(id => !people[id].Acted && people[id].SettlementId == s.Id && !busy.Contains(id)).ToImmutableArray());
            }).ToImmutableArray();
            var available = homes.SelectMany(s => s.OfficerIds).ToHashSet();
            return (e.ControllerId, economy with
            {
                Domestic = economy.Domestic with { Settlements = homes, Abilities = abilities is null ? economy.Domestic.Abilities :
                    available.ToImmutableDictionary(id => id, id => abilities[id]) },
                Accounts = economy.Accounts.Select(a => a with { Money = accounts[a.Id].Treasury.Money,
                    Food = accounts[a.Id].Treasury.Food, DailyConsumption = a.Id == changedAccount ? consumption!.Value : a.DailyConsumption }).ToImmutableArray(),
                Purchases = abilitiesChanged ? [] : economy.Purchases.Where(p => !places[p.SettlementId].FoodTradeUsed && p.OfficerIds.All(available.Contains)).ToImmutableArray(),
                QuotedAccounts = abilitiesChanged ? [] : economy.QuotedAccounts
            });
        }).ToImmutableArray();
        bool garrisonChanged = abilitiesChanged || changedAccount is not null || command is OrderUnitCommand ||
            command is DevelopCommand { Development.Activity: DomesticActivity.Train or DomesticActivity.Repair } ||
            events.OfType<TaskStartedEvent>().Any(e => e.Task.Journey is not null);
        if (garrisonChanged && Support is not null && rules is not null)
        {
            var arrivals = Support.Arrivals;
            if (candidate?.Arrival is { Status: ArrivalEstimateStatus.Known } arrival)
            {
                var deployed = events.OfType<UnitDeployedEvent>().FirstOrDefault();
                int? unitId = deployed?.UnitId ?? (command is OrderUnitCommand order ? order.UnitId : null);
                if (unitId is int id)
                {
                    arrivals = arrivals.Where(a => a.UnitId != id || a.Hostile).ToImmutableArray();
                    var unit = world.Units.Single(u => u.Id == id);
                    if (unit.Order is { Kind: MilitaryOrderKind.Transport or MilitaryOrderKind.Return, SettlementId: int target })
                        arrivals = arrivals.Add(new(id, unit.ServicePlaceId, target, arrival, unit.Soldiers, 0, false, "committed batch delivery"));
                }
            }
            // Revalue garrison command and delivery commitments, without querying another route or foreign mission.
            defense = new(world, seat.ForceId, rules, engine.GetAbilities(world), arrivals);
            Support = Support with { Arrivals = arrivals, FoodChanges = SupportObservationCapture.ProjectFood(world, seat.ForceId, arrivals),
                GarrisonRequirements = Support.GarrisonRequirements.Select(r => r with
                    { Soldiers = defense.Need(places[r.SettlementId], r.DepartingOfficerId).Required }).ToImmutableArray() };
        }
        // A purchase is a prerequisite, not a new campaign. Reconsider forces with the same routes and target forecast.
        if (candidate?.Campaign is not null && command is FoodTradeCommand && CampaignInput is not null)
        {
            var available = Economies.SelectMany(e => e.Observation.Domestic.Settlements).SelectMany(s => s.OfficerIds).ToHashSet();
            CampaignInput = CampaignInput with { Accounts = EconomicSupplyProjection.Apply(Economies, Support?.FoodChanges ?? [])
                .SelectMany(e => e.Observation.Accounts).DistinctBy(a => a.Id).ToImmutableArray(),
                Forces = CampaignInput.Forces.Where(f => f.Action.Officers.All(available.Contains)).ToImmutableArray(),
                Purchases = Economies.SelectMany(e => e.Observation.Purchases.Select(p => new CampaignPurchase(e.ControllerId, p))).ToImmutableArray() };
            Campaign = default;
        }
    }

    internal ImmutableArray<AllocationCandidate> SupportCandidates(WorldSnapshot world,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies)
    {
        var result = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var accounts = economies.SelectMany(e => e.Observation.Accounts).DistinctBy(a => a.Id).ToDictionary(a => a.Id);
        var places = world.Settlements.ToDictionary(s => s.Id);
        var root = world.Corps.Single(c => c.OwnerId == economies[0].Observation.Domestic.Perspective.ObserverId && c.IsRoot).Id;
        var available = economies.SelectMany(e => e.Observation.Domestic.Settlements).SelectMany(s => s.OfficerIds).ToHashSet();
        foreach (var original in Remaining(Support!.Candidates))
        {
            if (!original.Officers.All(available.Contains)) continue;
            if (original.Intent is not TransportUnitCommand transport)
            {
                result.Add(original);
                continue;
            }
            var source = places[transport.SettlementId];
            var target = places[original.TargetId!.Value];
            var account = accounts[original.AccountId];
            var receiver = accounts.GetValueOrDefault(target.CorpsId ?? root);
            long spare = source.Soldiers - (long)(defense?.Need(source, original.Officers[0]).Required ??
                Support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == source.Id && r.DepartingOfficerId == original.Officers[0])?.Soldiers ?? 0);
            long required = defense?.Need(target).Required ?? Support.GarrisonRequirements.First(r => r.SettlementId == target.Id && r.DepartingOfficerId is null).Soldiers;
            long amount = transport.CargoFood > 0 ? transport.Soldiers : Math.Min(transport.Soldiers, required - target.Soldiers);
            uint soldiers = (uint)Math.Max(0, Math.Min(spare, amount));
            if (soldiers < 1000) continue;
            uint cargo = transport.CargoFood;
            if (cargo > 0)
            {
                if (receiver is null || receiver.Food >= receiver.RequiredFood) continue;
                cargo = (uint)Math.Min(cargo, receiver.RequiredFood - receiver.Food + FoodSupplyForecast.ReinforcementExpenditure(
                    target.Soldiers, soldiers, Math.Max(0, receiver.HorizonDays - original.Arrival!.Days!.Value), target.Wounded, target.ConsumesFood));
            }
            int days = original.Arrival!.Days!.Value;
            bool sameAccount = receiver?.Id == account.Id;
            long food = sameAccount ? FoodSupplyForecast.TransferExpenditure(source.Soldiers, target.Soldiers, soldiers, days, account.HorizonDays,
                source.Wounded, target.Wounded, source.ConsumesFood, target.ConsumesFood) :
                FoodSupplyForecast.DeploymentExpenditure(source.Soldiers, soldiers, Math.Min(days, account.HorizonDays), source.Wounded, source.ConsumesFood);
            long receiving = sameAccount || receiver is null ? 0 : FoodSupplyForecast.ReinforcementExpenditure(target.Soldiers, soldiers,
                Math.Max(0, receiver.HorizonDays - days), target.Wounded, target.ConsumesFood);
            result.Add(original with { Intent = transport with { Soldiers = soldiers, CargoFood = cargo }, AdditionalFood = cargo + food,
                ReceivingAccountId = receiving > 0 && cargo == 0 ? receiver!.Id : null, ReceivingFood = cargo == 0 ? receiving : 0 });
        }
        return result.ToImmutable();
    }

    internal static bool CanDepart(AllocationCandidate candidate, SupportObservation support,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies)
    {
        if (candidate.Opportunity is null && candidate.Intent is not MoveOfficersCommand) return true;
        var home = economies.First(e => e.ControllerId == candidate.ControllerId).Observation.Domestic.Settlements
            .FirstOrDefault(s => s.OfficerIds.Contains(candidate.Officers[0]));
        if (home is null) return false;
        uint soldiers = candidate.Intent is UnitStrategyCommand { Strategy: UnitStrategyKind.Reinforce } ? 3000u : 0;
        uint baseline = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == home.Id && r.DepartingOfficerId is null)?.Soldiers ?? 0;
        var departure = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == home.Id && r.DepartingOfficerId == candidate.Officers[0]);
        return (support.Complete || support.DefenseComplete) && home.Soldiers >= soldiers &&
            (departure is null || Math.Max(0, departure.Soldiers - (long)home.Soldiers + soldiers) <=
                Math.Max(0, baseline - (long)home.Soldiers));
    }
}
