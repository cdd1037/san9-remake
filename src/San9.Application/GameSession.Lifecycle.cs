using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    private GameSession(ScenarioSessionDefinition definition, SimulationEngine simulation, int? perspectiveForceId = null)
    {
        _definition = definition;
        _simulation = simulation;
        _lastXunStartDay = simulation.Snapshot.Clock.DayNumber;
        _originalAi = OriginalAiState.Create(definition.Content);
        _initialEvents = simulation.InitialEvents;
        _snapshotSource = new(definition.Content, definition.Rules);
        _view = _snapshotSource.Project(simulation.Snapshot, perspectiveForceId);
        _initialView = _view;
        _strategicContext = StrategicContextState.Empty with { Clock = simulation.Snapshot.Clock.IsXunBoundary ? simulation.Snapshot.Clock : null };
        _readOnlyRecords = _records.AsReadOnly();
        _readOnlyChronicle = _chronicle.AsReadOnly();
        InitialDate = $"{definition.Content.Year}年{definition.Content.Month}月{definition.Content.Day}日";
    }

    internal static Func<WorldState, SimulationEngine> EngineFactory(ScenarioInitialState definition) =>
        new ScenarioRules(definition).CreateEngine;

    public static GameSession Start(ScenarioInitialState definition, ulong seed, int computerDamageBonusPercent = 0,
        bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player, bool enableXunStartCheckpoints = false)
    {
        if (!Enum.IsDefined(playerForceController)) throw new ArgumentOutOfRangeException(nameof(playerForceController));
        return Start(definition, seed, null, computerDamageBonusPercent, battleDeathsEnabled, playerForceController, enableXunStartCheckpoints);
    }

    internal static GameSession Start(ScenarioInitialState definition, ulong seed, Func<WorldState, SimulationEngine>? factory, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player, bool enableXunStartCheckpoints = false)
    {
        var owned = new ScenarioSessionDefinition(definition);
        var session = new GameSession(owned, (factory ?? owned.Rules.CreateEngine)(owned.CreateWorld(seed, computerDamageBonusPercent, battleDeathsEnabled, playerForceController)))
            { PlayerForceController = playerForceController };
        session.History = ChronicleHistory.Initialize(session.View);
        if (enableXunStartCheckpoints)
        {
            session.EnableXunStartCheckpoints();
            session._lastXunStartDay = session.Snapshot.Clock.DayNumber - 1;
            session.PrepareXunStartCheckpoint();
        }
        return session;
    }

    public static GameSession Restore(ScenarioInitialState definition, SessionSnapshot snapshot) =>
        Restore(definition, snapshot, null);

    // A player load starts a new random run; exact restoration above is used by replay and diagnostics.
    internal static GameSession Load(ScenarioSessionDefinition owned, SessionSnapshot snapshot,
        Func<WorldState, SimulationEngine>? factory)
    {
        _ = WorldState.Restore(snapshot.World);
        SessionValidation.Validate(owned, snapshot);
        ulong seed;
        do
        {
            seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
        } while (seed == snapshot.World.Simulation.RunSeed);
        var next = snapshot with { World = snapshot.World with
        {
            Simulation = snapshot.World.Simulation with { RunSeed = seed, RandomStreams = [] }
        } };
        return RestoreValidated(owned, next, WorldState.Restore(next.World), factory);
    }

    internal static GameSession Restore(ScenarioInitialState definition, SessionSnapshot snapshot, Func<WorldState, SimulationEngine>? factory)
    {
        return Restore(new ScenarioSessionDefinition(definition), snapshot, factory);
    }

    internal static GameSession Restore(ScenarioSessionDefinition owned, SessionSnapshot snapshot, Func<WorldState, SimulationEngine>? factory)
    {
        var world = WorldState.Restore(snapshot.World);
        SessionValidation.Validate(owned, snapshot);
        return RestoreValidated(owned, snapshot, world, factory);
    }

    private static GameSession RestoreValidated(ScenarioSessionDefinition owned, SessionSnapshot snapshot, WorldState world,
        Func<WorldState, SimulationEngine>? factory)
    {
        var simulation = (factory ?? owned.Rules.CreateEngine)(world);
        var session = new GameSession(owned, simulation, snapshot.PerspectiveForceId);
        session._records.AddRange(snapshot.Records);
        session._chronicle.AddRange(snapshot.Chronicle);
        session.History = snapshot.History;
        session.NextOperationSequence = snapshot.NextOperationSequence;
        session._ending = snapshot.Ending;
        session._strategicContext = snapshot.StrategicContext;
        session._originalAi = snapshot.OriginalAi;
        session.PlayerForceController = snapshot.PlayerForceController;
        session.HasUnsavedChanges = false;
        return session;
    }
}
