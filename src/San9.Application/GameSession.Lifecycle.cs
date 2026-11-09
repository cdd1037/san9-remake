using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    private GameSession(ScenarioSessionDefinition definition, SimulationEngine simulation)
    {
        _definition = definition;
        _simulation = simulation;
        _lastXunStartDay = simulation.Snapshot.Clock.DayNumber;
        _originalAi = OriginalAiState.Create(definition.Content);
        _initialEvents = simulation.InitialEvents;
        _snapshotSource = new(definition.Content, definition.Rules, definition.Definitions);
        _view = _snapshotSource.Project(simulation.Snapshot, simulation.Companion);
        _initialView = simulation.InitialSnapshot is { } initial
            ? _snapshotSource.Project(initial, simulation.Companion) : _view;
        _strategicContexts = simulation.Snapshot.Simulation.Controls.Where(c => c.Kind == ControllerKind.Player)
            .ToImmutableDictionary(c => c.ForceId, _ => StrategicContextState.Empty with { Clock = simulation.Snapshot.Clock.IsXunBoundary ? simulation.Snapshot.Clock : null });
        _readOnlyRecords = _records.AsReadOnly();
        _readOnlyChronicle = _chronicle.AsReadOnly();
        InitialDate = $"{definition.Content.Year}年{definition.Content.Month}月{definition.Content.Day}日";
    }

    internal static Func<WorldState, SimulationEngine> EngineFactory(ScenarioInitialState definition) =>
        new ScenarioRules(definition).CreateEngine;

    public static GameSession Start(ScenarioInitialState definition, ulong seed, int computerDamageBonusPercent = 0,
        bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player, bool enableXunStartCheckpoints = false, NewGameSettings? settings = null, IReadOnlyCollection<int>? playerForceIds = null, CompanionSettings? companionSettings = null, San9.Content.Authoring.NewGameSetup? setup = null)
    {
        if (!Enum.IsDefined(playerForceController)) throw new ArgumentOutOfRangeException(nameof(playerForceController));
        return Start(definition, seed, null, computerDamageBonusPercent, battleDeathsEnabled, playerForceController, enableXunStartCheckpoints, settings, playerForceIds, companionSettings, setup);
    }

    internal static GameSession Start(ScenarioInitialState definition, ulong seed, Func<WorldState, SimulationEngine>? factory, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player, bool enableXunStartCheckpoints = false, NewGameSettings? settings = null, IReadOnlyCollection<int>? playerForceIds = null, CompanionSettings? companionSettings = null, San9.Content.Authoring.NewGameSetup? setup = null)
    {
        var input = setup ?? San9.Content.Authoring.NewGameSetup.Empty;
        var composition = Authoring.NewGameComposer.Compose(definition, input, playerForceIds);
        var owned = new ScenarioSessionDefinition(composition.Scenario, playerForceIds, composition.Definitions);
        var simulation = (factory ?? owned.Rules.CreateEngine)(owned.CreateWorld(seed, computerDamageBonusPercent, battleDeathsEnabled, playerForceController, settings));
        if (companionSettings is not null) simulation.ConfigureCompanion(companionSettings);
        var session = new GameSession(owned, simulation)
            { PlayerForceController = playerForceController, InitialPlayerForceIds = owned.InitialPlayerForceIds };
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
        _ = WorldState.Restore(snapshot.World, owned.Content.DefaultLoyalty);
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
        return RestoreValidated(owned, next, WorldState.Restore(next.World, owned.Content.DefaultLoyalty), factory);
    }

    internal static GameSession Restore(ScenarioInitialState definition, SessionSnapshot snapshot, Func<WorldState, SimulationEngine>? factory)
    {
        snapshot.Definitions.Validate(definition);
        return Restore(new ScenarioSessionDefinition(definition, snapshot.InitialPlayerForceIds, snapshot.Definitions), snapshot, factory);
    }

    internal static GameSession Restore(ScenarioSessionDefinition owned, SessionSnapshot snapshot, Func<WorldState, SimulationEngine>? factory)
    {
        var world = WorldState.Restore(snapshot.World, owned.Content.DefaultLoyalty);
        SessionValidation.Validate(owned, snapshot);
        return RestoreValidated(owned, snapshot, world, factory);
    }

    private static GameSession RestoreValidated(ScenarioSessionDefinition owned, SessionSnapshot snapshot, WorldState world,
        Func<WorldState, SimulationEngine>? factory)
    {
        var simulation = (factory ?? owned.Rules.CreateEngine)(world);
        var session = new GameSession(owned, simulation);
        session._records.AddRange(snapshot.Records);
        session._chronicle.AddRange(snapshot.Chronicle);
        session.History = snapshot.History;
        session.DeploymentPresets = snapshot.DeploymentPresets;
        session.NextOperationSequence = snapshot.NextOperationSequence;
        session._ending = snapshot.Ending;
        session._strategicContexts = snapshot.StrategicContexts;
        session._originalAi = snapshot.OriginalAi;
        session.PlayerForceController = snapshot.PlayerForceController;
        session.InitialPlayerForceIds = snapshot.InitialPlayerForceIds;
        session.HasUnsavedChanges = false;
        return session;
    }
}
