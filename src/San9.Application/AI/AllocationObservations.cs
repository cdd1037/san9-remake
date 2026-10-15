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
    internal ImmutableArray<AllocationCandidate> Field { get; set; }
    internal bool FieldComplete { get; set; } = true;
    internal ImmutableArray<AllocationCandidate> Campaign { get; set; }
    internal CampaignObservation? CampaignInput { get; set; }
    internal ImmutableArray<AllocationCandidate> Opportunities { get; set; }
    internal bool CampaignComplete { get; set; } = true;
    internal bool OpportunitiesComplete { get; set; } = true;
    private readonly HashSet<GameCommand> executed = new(PlanningIntentEquality.Instance);
    private readonly HashSet<int> appointed = [];
    private readonly HashSet<int> orderedUnits = [];
    private GarrisonAssessment? defense;

    internal long RefreshWork(GameCommand command, WorldSnapshot world) => command is RewardOfficerCommand or AppointAdviserCommand or
        SetUnitPoliciesCommand or SelectTacticCommand
        ? 1 + Economies.Sum(e => (long)e.Observation.Accounts.Length)
        : 1 + world.Settlements.Count + world.Officers.Count + world.Corps.Count + world.Simulation.Tasks.Length +
            Economies.Sum(e => (long)e.Observation.Accounts.Length + e.Observation.Domestic.Settlements.Sum(s => 1L + s.OfficerIds.Length)) +
            (Support?.GarrisonRequirements.Length ?? 0);

    internal long ExecutionWork(AllocationCandidate candidate, WorldSnapshot world) => 2 + RefreshWork(candidate.Intent, world) +
        (candidate.Continuation is { DelayDays: 0 } next ? ExecutionWork(next.Action, world) : 0);

    internal IEnumerable<AllocationCandidate> Remaining(IEnumerable<AllocationCandidate> candidates) => candidates.Where(c =>
        !executed.Contains(c.Intent) &&
        (c.Intent is not OrderUnitCommand order || !orderedUnits.Contains(order.UnitId)) &&
        (c.Intent is not SetUnitPoliciesCommand policies || !orderedUnits.Contains(policies.UnitId)) &&
        (c.Intent is not AssignOfficesCommand offices || !offices.Assignments.Any(a => appointed.Contains(a.OfficerId))));

    internal void Applied(SimulationEngine engine, ControlSeat seat, MilitaryRulebook? rules, GameCommand command,
        GameCommand intent, AllocationCandidate? candidate, IEnumerable<DomainEvent> events)
    {
        executed.Add(intent);
        if (command is OrderUnitCommand committedOrder) orderedUnits.Add(committedOrder.UnitId);
        if (command is AssignOfficesCommand assigned)
            foreach (var person in assigned.Assignments) appointed.Add(person.OfficerId);
        var world = engine.Snapshot;
        // Local effects do not move actors or change garrison consumption. Update their resource deltas
        // without building world indexes or revisiting every settlement and officer.
        if (command is RewardOfficerCommand or AppointAdviserCommand or SetUnitPoliciesCommand or SelectTacticCommand)
        {
            var changes = events.OfType<ResourceChangedEvent>().Where(e => e.Account.Kind == ResourceOwnerKind.Corps).ToArray();
            Economies = Economies.Select(e => (e.ControllerId, e.Observation with
            { Accounts = e.Observation.Accounts.Select(a => a with
                { Money = a.Money + changes.Where(c => c.Account.Id == a.Id && c.Resource == ResourceKind.Money).Sum(c => c.Actual),
                  Food = a.Food + changes.Where(c => c.Account.Id == a.Id && c.Resource == ResourceKind.Food).Sum(c => c.Actual) }).ToImmutableArray() })).ToImmutableArray();
            if (command is AppointAdviserCommand) Opportunities = default;
            if (command is SelectTacticCommand) { Campaign = default; CampaignInput = null; }
            return;
        }
        var places = world.Settlements.ToDictionary(s => s.Id);
        var people = world.Officers.Where(p => p.OwnerId == seat.ForceId).ToDictionary(p => p.Id);
        var accounts = world.Corps.ToDictionary(c => c.Id);
        var busy = world.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        bool abilitiesChanged = command is AssignOfficesCommand or GrantTreasureCommand or ConfiscateTreasureCommand;
        var abilities = abilitiesChanged ? engine.GetAbilities(world) : null;
        if (abilitiesChanged)
        {
            if (!Configuration.IsDefault)
                Configuration = Configuration.Select(c => c.Intent is RewardOfficerCommand reward
                    ? c with { Benefit = PersonnelConfiguration.RewardValue(people[reward.OfficerId], abilities![reward.OfficerId]) }
                    : c).ToImmutableArray();
            Field = default;
            Campaign = default;
            CampaignInput = null;
            Opportunities = default;
        }
        if (command is DevelopCommand { Development.Activity: DomesticActivity.Train })
        { Campaign = default; CampaignInput = null; }
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

    internal IEnumerable<AllocationCandidate> ConfigurationCandidates()
    {
        foreach (var original in Remaining(Configuration))
        {
            if (original.Benefit <= 0) continue;
            var candidate = original;
            if (original.Intent is ChangeCorpsCommand change)
            {
                // The plan owns a transfer amount; the public command takes absolute treasury values.
                // Materialize against the live ledger without repeating organizational observation.
                var receiver = Economies.SelectMany(e => e.Observation.Accounts).First(a => a.Id == change.CorpsId);
                long amount = Math.Min(original.AdditionalFood, Math.Max(0, receiver.RequiredFood - receiver.Food));
                amount = Math.Min(amount, uint.MaxValue - receiver.Food);
                if (amount <= 0) continue;
                candidate = original with { AdditionalFood = amount, Intent = change with
                    { Money = checked((uint)receiver.Money), Food = checked((uint)(receiver.Food + amount)) } };
                yield return candidate;
                continue;
            }
            // Public eligibility is checked once for the selected step, immediately before commitment.
            yield return candidate;
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

    internal IEnumerable<AllocationCandidate> CampaignCandidates(WorldSnapshot world,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies)
    {
        foreach (var candidate in Remaining(Campaign))
        {
            if (candidate.Intent is TransportUnitCommand transport && candidate.ReceivingSoldierTarget is uint desired)
            {
                var destination = world.Settlements.Single(s => s.Id == candidate.TargetId);
                long incoming = world.Units.Where(u => u.OwnerId == destination.OwnerId &&
                    u.Order is { Kind: MilitaryOrderKind.Transport or MilitaryOrderKind.Return } && u.Order.SettlementId == destination.Id)
                    .Sum(u => (long)u.Soldiers);
                var donor = world.Settlements.Single(s => s.Id == transport.SettlementId);
                long donorReserve = defense?.NeedAfterDeparture(donor, transport.OfficerIds).Required ??
                    Support!.GarrisonRequirements.Where(r => r.SettlementId == donor.Id && transport.OfficerIds.Contains(r.DepartingOfficerId ?? -1))
                        .Select(r => r.Soldiers).DefaultIfEmpty(donor.Soldiers).Max();
                uint soldiers = (uint)Math.Max(0, Math.Min(transport.Soldiers, Math.Min((long)desired - destination.Soldiers - incoming, donor.Soldiers - donorReserve)));
                if (soldiers < 1000) continue;
                // Original full-shipment costs conservatively cover a reduced delivery.
                yield return candidate with { Intent = transport with { Soldiers = soldiers } };
                continue;
            }
            if (candidate.Intent is not DeployUnitCommand deploy) { yield return candidate; continue; }
            var home = world.Settlements.Single(s => s.Id == deploy.SettlementId);
            var source = CampaignInput!.Sources.Single(s => s.Id == home.Id);
            int leadership = source.Guards.Where(g => !deploy.OfficerIds.Contains(g.OfficerId))
                .Select(g => g.Leadership).DefaultIfEmpty(50).Max();
            long reserve = Math.Max(defense?.NeedAfterDeparture(home, deploy.OfficerIds).Required ?? source.Reserves[leadership],
                CampaignInput.Waves.Where(w => w.TargetId == home.Id && w.Days <= candidate.Arrival!.Days + 12).Sum(w => (long)w.Soldiers));
            if (deploy.Soldiers + reserve > home.Soldiers) continue;
            var account = economies.SelectMany(e => e.Observation.Accounts).First(a => a.Id == candidate.AccountId);
            yield return candidate with { AdditionalFood = FoodSupplyForecast.DeploymentExpenditure(home.Soldiers,
                deploy.Soldiers, account.HorizonDays, home.Wounded, home.ConsumesFood) };
        }
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
