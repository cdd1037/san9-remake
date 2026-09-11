using San9.Core.Simulation;
using San9.Core.Domain;
using San9.Content.Scenarios;
using San9.Application.Presentation;

namespace San9.Application;

public sealed class GameSession
{
    private readonly SimulationEngine _simulation;
    private readonly StrategySnapshotSource _snapshotSource;
    private readonly StrategySnapshot _initialView;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<SessionRecord> _readOnlyRecords;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<ChronicleEntry> _readOnlyChronicle;
    private StrategySnapshot _view;
    public event Action? Changed;
    private readonly List<SessionRecord> _records = [];
    private readonly List<ChronicleEntry> _chronicle = [];
    public IReadOnlyList<SessionRecord> Records => _readOnlyRecords;
    public IReadOnlyList<ChronicleEntry> Chronicle => _readOnlyChronicle;
    public void AddChronicleNote(string text)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 240) throw new ArgumentException("Chronicle notes must contain 1 to 240 characters.");
        var clock = Snapshot.Clock;
        _chronicle.Add(new(clock.Year, clock.Month, clock.Day, View.PlayerForceId, text));
        Changed?.Invoke();
    }

    private GameSession(ScenarioInitialState definition, WorldState initialState)
    {
        _simulation = new SimulationEngine(initialState);
        _snapshotSource = new(definition);
        _view = _snapshotSource.Project(_simulation.Snapshot);
        _initialView = _view;
        _readOnlyRecords = _records.AsReadOnly();
        _readOnlyChronicle = _chronicle.AsReadOnly();
        InitialDate = $"{definition.Year}年{definition.Month}月{definition.Day}日";
    }

    public string InitialDate { get; }
    public IReadOnlyList<DomainEvent> InitialEvents => _simulation.InitialEvents;
    public WorldSnapshot CaptureStableState() => _simulation.CaptureStableState();
    public WorldSnapshot Snapshot => _simulation.Snapshot;
    public StrategySnapshot InitialView => _initialView;
    public StrategySnapshot View
    {
        get
        {
            WorldSnapshot snapshot = Snapshot;
            return _view.World.Revision == snapshot.Revision ? _view : _view = _snapshotSource.Project(snapshot);
        }
    }

    public static GameSession Start(ScenarioInitialState definition, ulong seed)
    {
        definition.Validate();
        definition = definition with
        {
            Information = definition.Information.Copy(), Forces = definition.Forces.ToArray(), Officers = definition.Officers.ToArray(), Units = definition.Units.ToArray(),
            Settlements = definition.Settlements.Select(s => s with { Icons = s.Icons.ToArray() }).ToArray(),
            RegionNameKeys = new(definition.RegionNameKeys), RegionCityIds = new(definition.RegionCityIds)
        };
        if (definition.Day is not (1 or 11 or 21)) throw new ArgumentException("Expected a stable xun boundary.");
        var world = WorldState.Create(new(definition.Year, definition.Month, definition.Day), seed,
            definition.Forces.Select(f => new ForceState(f.Id, f.Money, f.Food, f.Trust) { ProvidedTactics = f.ProvidedTactics, IsBarbarian = f.IsBarbarian, LeaderId = f.LeaderId, RankId = f.RankId }),
            definition.Settlements.Select(s => new SettlementState(s.Id, s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability,
                s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce, s.Cultivation, s.MaxCultivation) { Kind = s.Kind, CorpsId = s.CorpsId, Population = s.Population, DraftPopulation = s.DraftPopulation }), definition.Officers.Select(o => o.State), definition.Units, definition.Information.Corps, definition.Information.TreasureStates, definition.Information.Relations,
            definition.Forces.OrderBy(f => f.Id == definition.PlayerForceId ? 0 : 1).ThenBy(f => f.Id)
                .Select(f => new ControlSeat(f.Id == definition.PlayerForceId ? "player" : $"ai:{f.Id}", f.Id,
                    f.Id == definition.PlayerForceId ? ControllerKind.Player : ControllerKind.Automatic)));
        return new GameSession(definition, world);
    }

    public DevelopmentOutcome Develop(DevelopmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        long sequence = Snapshot.Simulation.NextSequence;
        Span<byte> id = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(id, checked(sequence + 1));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(id[8..], Snapshot.Simulation.InitialSeed);
        var result = Execute(new("player"), new DevelopCommand(new Guid(id), command.ForceId, sequence, command));
        return result.Development ?? new(DevelopmentRejection.NotAuthorized);
    }

    public CommandExecutionResult Execute(CommandContext context, GameCommand command)
    {
        var result = _simulation.Execute(context, command);
        foreach (var completed in result.Events.OfType<DevelopmentCompletedEvent>())
        {
            _records.Add(new(completed.Clock, completed.Command.ForceId, completed.Command.SettlementId,
                completed.Command.OfficerId, completed.Command.Activity, completed.Increment, completed.Cost));
        }
        if (result.Submission.Accepted) Changed?.Invoke();
        return result;
    }

    public CommandSubmissionResult Validate(CommandContext context, GameCommand command) => _simulation.Validate(context, command);

    public AdvanceXunResult EndControl(CommandContext context)
    {
        var result = _simulation.EndControl(context);
        Changed?.Invoke();
        return result;
    }

    // The current client has one player and no automatic planner yet. Ending its strategy
    // phase explicitly closes remaining automatic seats; G11 supplies actual planning.
    public AdvanceXunResult AdvanceXun()
    {
        List<DomainEvent> events = [];
        while (Snapshot.Simulation.Boundary == SimulationBoundary.Stable &&
            Snapshot.Simulation.ActiveControl < Snapshot.Simulation.Controls.Length)
        {
            var seat = Snapshot.Simulation.Controls[Snapshot.Simulation.ActiveControl];
            events.AddRange(_simulation.EndControl(new(seat.ControllerId)).Events);
        }
        var result = _simulation.AdvanceXun();
        events.AddRange(result.Events);
        Changed?.Invoke();
        return new(events.AsReadOnly(), result.Snapshot);
    }

}

public sealed record SessionRecord(GameClock Clock, int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity, int Increment, int Cost);
