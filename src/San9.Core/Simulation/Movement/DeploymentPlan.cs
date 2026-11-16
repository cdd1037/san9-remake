using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>One issuer's read-only batch. Reserves resources without creating units or world events.</summary>
public sealed class DeploymentPlan
{
    private readonly SimulationEngine engine;
    private readonly CommandContext context;
    private readonly WorldMutation world;
    private readonly SimulationRules rules;
    private readonly long revision;
    private readonly HashSet<int> officers = [];
    private readonly Dictionary<int, long> soldiers = [];
    private readonly Dictionary<ResourceAccount, long> money = [];
    private readonly List<MapOccupancy> positions = [];
    private int committed;
    private bool invalidated;
    private int? issuer;
    internal int Count { get; private set; }
    internal IReadOnlyList<MapOccupancy> Positions => positions;

    internal DeploymentPlan(SimulationEngine engine, CommandContext context, WorldMutation world, SimulationRules rules)
    {
        this.engine = engine;
        this.context = context;
        this.world = world;
        this.rules = rules;
        revision = engine.Snapshot.Revision;
    }

    public bool IsOfficerReserved(int officerId) => officers.Contains(officerId);
    public long AvailableSoldiers(int settlementId) => world.Settlements.TryGetValue(settlementId, out var place)
        ? place.Soldiers - soldiers.GetValueOrDefault(settlementId) : 0;
    public long AvailableMoney(int settlementId)
    {
        var place = world.Settlements[settlementId];
        var account = ResourceAccounts.Resolve(world, place.OwnerId!.Value, place.CorpsId);
        return ResourceAccounts.Balance(world, account, ResourceKind.Money) - ReservedMoney(account);
    }
    internal long ReservedMoney(ResourceAccount account) => money.GetValueOrDefault(account);

    public CommandRejection TryPrepare(DeployUnitCommand command, out PreparedDeployment? prepared)
    {
        prepared = null;
        if (engine.Snapshot.Revision != revision || invalidated) return CommandRejection.NotStable;
        var rejection = engine.CheckDeploymentEnvelope(context, command);
        if (rejection != CommandRejection.None) return rejection;
        if (issuer is int owner && owner != command.IssuerId) return CommandRejection.NotActiveController;
        rejection = MilitaryMovement.PrepareDeployment(world, command, rules.Military, rules.Governance, rules.Treasures,
            UnitPurpose.Combat, 0, 0, out var data, this);
        if (rejection != CommandRejection.None) return rejection;
        issuer = command.IssuerId;
        prepared = new(this, Count, command, data!);
        foreach (int officer in command.OfficerIds) officers.Add(officer);
        soldiers[command.SettlementId] = soldiers.GetValueOrDefault(command.SettlementId) + command.Soldiers;
        money[data!.Account] = ReservedMoney(data.Account) + data.Cost;
        var travel = rules.Military!.Travel(command.FormationId, data.NavalId, command.Soldiers);
        positions.Add(new(data.Start, travel.Map.ShapeAt(data.Start, travel.Shape))
            { Hard = false, Avoidance = NavigationAvoidance.Friendly });
        Count++;
        return CommandRejection.None;
    }

    internal bool Matches(SimulationEngine owner, CommandContext caller, DeployUnitCommand command, PreparedDeployment prepared) =>
        ReferenceEquals(owner, engine) && caller.ControllerId == context.ControllerId &&
        command with { CommandId = prepared.Command.CommandId, Sequence = prepared.Command.Sequence } == prepared.Command;
    internal bool CanReuse(PreparedDeployment prepared, long currentRevision) =>
        !invalidated && prepared.Index == committed && currentRevision == revision + committed;
    internal void Complete(bool accepted, bool reused)
    {
        if (!accepted || !reused) { invalidated = true; return; }
        committed++;
    }
}

/// <summary>Opaque, operation-local result; only the creating engine can reuse its preparation.</summary>
public sealed class PreparedDeployment
{
    public DeployUnitCommand Command { get; }
    internal DeploymentPlan Plan { get; }
    internal int Index { get; }
    internal MilitaryMovement.DeploymentPreparation Data { get; }
    internal PreparedDeployment(DeploymentPlan plan, int index, DeployUnitCommand command, MilitaryMovement.DeploymentPreparation data)
        => (Plan, Index, Command, Data) = (plan, index, command, data);
}

public sealed partial class SimulationEngine
{
    public DeploymentPlan PrepareDeployments(CommandContext context) => new(this, context, PreparationWorld(), _rules);
    internal CommandRejection CheckDeploymentEnvelope(CommandContext context, DeployUnitCommand command) => ValidateEnvelope(context, command);

    public CommandExecutionResult Execute(CommandContext context, DeployUnitCommand command, PreparedDeployment prepared)
    {
        if (!prepared.Plan.Matches(this, context, command, prepared)) return Reject(CommandRejection.InvalidTask);
        bool reuse = prepared.Plan.CanReuse(prepared, _world.Revision);
        var result = Evaluate(context, command, commit: true, deployment: reuse ? prepared.Data : null);
        prepared.Plan.Complete(result.Submission.Accepted, reuse);
        return result;
    }
}
