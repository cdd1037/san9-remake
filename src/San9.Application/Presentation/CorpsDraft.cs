using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public enum CorpsOperation { Create, Change, Dissolve }

public sealed class CorpsDraft
{
    private readonly GameSession session;
    private readonly Guid commandId = Guid.NewGuid();
    private GameCommand? previewCommand;
    private CommandPreparationPreview? preview;
    public StrategySnapshot View { get; }
    public CorpsOperation Operation { get; private set; }
    public int? CorpsId { get; private set; }
    public ImmutableArray<int> Cities { get; private set; } = [];
    public uint Money { get; private set; }
    public uint Food { get; private set; }
    public CorpsDelegation Policy { get; private set; } = new GeneralDelegation();
    public CorpsControlMode ControlMode { get; private set; }
    public bool IsCurrent => session.Snapshot.Revision == View.World.Revision;
    public ImmutableArray<CorpsState> Corps => View.World.Corps.Where(c => c.OwnerId == View.RequirePlayerForce()).OrderBy(c => c.Id).ToImmutableArray();
    private CorpsState Root => Corps.Single(c => c.IsRoot);
    public uint MaximumMoney => (uint)Math.Min(uint.MaxValue, (ulong)Root.Treasury.Money + (Selected?.IsRoot == false ? Selected.Treasury.Money : 0));
    public uint MaximumFood => (uint)Math.Min(uint.MaxValue, (ulong)Root.Treasury.Food + (Selected?.IsRoot == false ? Selected.Treasury.Food : 0));
    public bool CanCreate => Corps.Length < GovernanceRulebook.MaximumCorpsPerForce &&
        CityCandidates.Any(id => View.World.Settlements.Single(s => s.Id == id).CorpsId == Root.Id);
    public CorpsState? Selected => Corps.SingleOrDefault(c => c.Id == CorpsId);
    public ImmutableArray<int> CityCandidates
    {
        get
        {
            var ruler = View.World.Officers.Single(p => p.Id == View.World.Forces.Single(f => f.Id == View.RequirePlayerForce()).LeaderId);
            int? home = ruler.SettlementId ?? View.World.Units.SingleOrDefault(u => u.Id == ruler.UnitId)?.ServicePlaceId;
            return View.World.Settlements.Where(s => s.OwnerId == View.RequirePlayerForce() && s.Kind == SettlementKind.City &&
                !s.IsDormant && !s.IsDestroyed && s.Id != home &&
                (s.CorpsId == Root.Id || Operation == CorpsOperation.Change && s.CorpsId == CorpsId)).Select(s => s.Id).Order().ToImmutableArray();
        }
    }
    public CorpsDraft(GameSession session) { this.session = session; View = session.View; SetOperation(CorpsOperation.Create); }
    public void SetOperation(CorpsOperation operation)
    {
        Operation = operation; Cities = []; Money = Food = 0;
        CorpsId = null; Policy = new GeneralDelegation(); ControlMode = CorpsControlMode.Cooperative;
        var candidate = Corps.FirstOrDefault(c => !c.IsRoot);
        if (operation != CorpsOperation.Create && candidate is not null) SelectCorps(candidate.Id);
    }
    public bool SelectCorps(int id)
    {
        var corps = Corps.SingleOrDefault(c => c.Id == id);
        if (!IsCurrent || corps is null || corps.IsRoot) return false;
        CorpsId = id; Money = corps.Treasury.Money; Food = corps.Treasury.Food;
        Cities = View.World.Settlements.Where(s => s.CorpsId == id && s.Kind == SettlementKind.City).Select(s => s.Id).Order().ToImmutableArray();
        Policy = corps.Delegation; ControlMode = corps.ControlMode; return true;
    }
    public bool SetCities(IEnumerable<int> ids)
    {
        var cities = ids.Order().ToImmutableArray();
        if (!IsCurrent || cities.Distinct().Count() != cities.Length || cities.Except(CityCandidates).Any()) return false;
        if (Cities.SequenceEqual(cities)) return true;
        Cities = cities;
        if (Operation == CorpsOperation.Create)
        {
            var counts = Counts;
            Money = counts.TotalPeople == 0 ? 0 : (uint)((UInt128)MaximumMoney * counts.People / counts.TotalPeople);
            Food = counts.TotalSoldiers == 0 ? 0 : (uint)((UInt128)MaximumFood * counts.Soldiers / counts.TotalSoldiers);
        }
        return true;
    }
    public void SetMoney(uint amount) => Money = amount;
    public void SetFood(uint amount) => Food = amount;
    public void SetPolicy(CorpsDelegation policy) => Policy = policy;
    public void SetControl(CorpsControlMode mode) => ControlMode = mode;
    public bool HasInput => Operation == CorpsOperation.Create || CorpsId is not null;
    public CommandPreparationPreview Preview
    {
        get
        {
            if (!IsCurrent || session.IsExecuting) return new(CommandRejection.NotStable, null, null);
            if (!HasInput) return new(CommandRejection.InvalidCorps, null, null);
            var command = CreateCommand();
            if (previewCommand != command)
            {
                preview = session.Preview(new("player"), command);
                previewCommand = command;
            }
            return preview!;
        }
    }
    public CommandRejection Rejection => Preview.Rejection;
    public int? GovernorId => Operation is CorpsOperation.Create or CorpsOperation.Change ? Preview.GovernorId : Selected?.LeaderId;
    public ImmutableArray<int> PolicyTargets(bool force)
    {
        if (!IsCurrent) return [];
        var candidates = force ? View.Forces.Select(f => f.Id) : View.Settlements.Select(s => s.Id);
        return candidates.Where(id => session.CanSelectCorpsDelegation(new("player"), View.RequirePlayerForce(),
            force ? new DefeatForceDelegation(id) : new CaptureSettlementDelegation(id))).Order().ToImmutableArray();
    }
    public bool HasChanges => Operation switch
    {
        CorpsOperation.Change => Selected is {} corps &&
            (Money != corps.Treasury.Money || Food != corps.Treasury.Food || Policy != corps.Delegation || ControlMode != corps.ControlMode ||
             !Cities.SequenceEqual(View.World.Settlements.Where(s => s.CorpsId == corps.Id && s.Kind == SettlementKind.City).Select(s => s.Id).Order())),
        _ => true
    };
    public bool CanExecute => HasChanges && Rejection == CommandRejection.None;

    public (ulong People, ulong TotalPeople, ulong Soldiers, ulong TotalSoldiers) Counts
    {
        get
        {
            var ruler = View.Officers.Single(p => p.Id == View.Forces.Single(f => f.Id == View.RequirePlayerForce()).LeaderId);
            bool creating = Operation == CorpsOperation.Create;
            var territory = CorpsTerritoryPlan.ForCities(View.World.Settlements, View.RegionCityIds,
                View.RequirePlayerForce(), creating ? null : CorpsId, Cities, ruler.ServicePlaceId);
            var pool = View.World.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.OwnerId == View.RequirePlayerForce() &&
                (s.CorpsId == Root.Id || s.CorpsId == CorpsId || !creating && territory.Transfers(s.Id)))
                .Select(s => s.Id).ToHashSet();
            var places = territory.Assigned.Intersect(pool);
            var units = View.World.Units.Where(u => u.OwnerId == View.RequirePlayerForce() &&
                (u.CorpsId == Root.Id || u.CorpsId == CorpsId || !creating &&
                    u.ServicePlaceId is int home && pool.Contains(home) && territory.Transfers(home) && u.FollowsServicePlace(false))).ToArray();
            var unitIds = units.Select(u => u.Id).ToHashSet();
            var selectedUnits = units.Where(u => u.ServicePlaceId is int home && places.Contains(home) &&
                u.FollowsServicePlace(creating)).Select(u => u.Id).ToHashSet();
            var people = View.Officers.Where(p => p.IsServing && p.OwnerId == View.RequirePlayerForce() &&
                (p.UnitId is int unit ? unitIds.Contains(unit) : p.ServicePlaceId is int home && pool.Contains(home))).ToArray();
            ulong assigned = (ulong)people.Count(p => p.UnitId is int unit ? selectedUnits.Contains(unit) : p.ServicePlaceId is int home && places.Contains(home));
            ulong soldiers = (ulong)View.World.Settlements.Where(s => places.Contains(s.Id)).Sum(s => (long)s.Soldiers + s.Wounded) +
                (ulong)units.Where(u => selectedUnits.Contains(u.Id)).Sum(u => (long)u.Soldiers + u.Wounded);
            ulong total = (ulong)View.World.Settlements.Where(s => pool.Contains(s.Id)).Sum(s => (long)s.Soldiers + s.Wounded) +
                (ulong)units.Sum(u => (long)u.Soldiers + u.Wounded);
            return (assigned, (ulong)people.Length, soldiers, total);
        }
    }

    public GameCommand CreateCommand()
    {
        int force = View.RequirePlayerForce(); long sequence = View.World.Simulation.NextSequence;
        return Operation switch
        {
            CorpsOperation.Create => new CreateCorpsCommand(commandId, force, sequence, Cities, Money, Food, Policy, ControlMode),
            CorpsOperation.Change => new ChangeCorpsCommand(commandId, force, sequence, CorpsId!.Value, Cities, Money, Food, Policy, ControlMode),
            CorpsOperation.Dissolve => new DissolveCorpsCommand(commandId, force, sequence, CorpsId!.Value),
            _ => throw new InvalidOperationException("Unknown corps operation.")
        };
    }
}
