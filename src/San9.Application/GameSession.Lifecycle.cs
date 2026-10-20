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
        _snapshotSource = new(definition.Content, definition.MilitaryRules);
        _view = _snapshotSource.Project(simulation.Snapshot);
        _initialView = _view;
        _strategicContext = StrategicContextState.Empty with { Clock = simulation.Snapshot.Clock.IsXunBoundary ? simulation.Snapshot.Clock : null };
        _readOnlyRecords = _records.AsReadOnly();
        _readOnlyChronicle = _chronicle.AsReadOnly();
        InitialDate = $"{definition.Content.Year}年{definition.Content.Month}月{definition.Content.Day}日";
    }

    internal static Func<WorldState, SimulationEngine> EngineFactory(ScenarioInitialState definition)
    {
        var rules = new EconomyRulebook(definition.Information.Offices.Select(o => new OfficeSalary(o.Id, o.Salary)).ToImmutableArray(),
            definition.DomesticPairs, definition.ImperialSealId)
        { FoodMarket = definition.FoodMarket, ImperialCourt = definition.ImperialCourt, RegionCityIds = definition.RegionCityIds.ToImmutableDictionary() };
        var personnel = new PersonnelRulebook(definition.Settlements.Select(s => new PersonPlace(s.Id, s.Position)).Concat(definition.UnbuiltPlaces), definition.RegionCityIds, definition.CityConnections, definition.CityProvinceIds, definition.TacticCourses, definition.SearchEvents);
        var treasures = new TreasureRulebook(definition.Information.Treasures.Select(t => t.ToRule()));
        var governance = new GovernanceRulebook(definition.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { AbilityBonuses = o.AbilityBonuses, Salary = o.Salary, RequiredRankId = o.RequiredRankId }),
            definition.Information.Ranks.ToDictionary(r => r.Id, r => r.Precedence));
        var military = definition.Military is { } militaryDefinition ? new MilitaryRulebook(militaryDefinition) : null;
        var politics = new PoliticalRulebook(definition.PoliticalEvents,
            definition.Information.Ranks.Select(r => new PeerageRule(r.Id, r.Precedence, r.RequiredTrust, r.LoyaltyBonus)),
            definition.Information.CountryTitles.Select(t => t.Rule), definition.ProvinceCountryTitleIds);
        return world => new(world, rules, personnel, treasures, governance, military, definition.Terminal, politics, definition.QiaoMarriage, definition.SunMarriage,
            definition.HanzhongKing, definition.FiveTigers, definition.ExpeditionMemorial, definition.ThreeVisits, definition.ChainedPlot, definition.RedCliffs, definition.ShanyangGong, definition.Guandu);
    }

    public static GameSession Start(ScenarioInitialState definition, ulong seed, int computerDamageBonusPercent = 0,
        bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player, bool enableXunStartCheckpoints = false)
    {
        if (!Enum.IsDefined(playerForceController)) throw new ArgumentOutOfRangeException(nameof(playerForceController));
        return Start(definition, seed, EngineFactory(definition), computerDamageBonusPercent, battleDeathsEnabled, playerForceController, enableXunStartCheckpoints);
    }

    internal static GameSession Start(ScenarioInitialState definition, ulong seed, Func<WorldState, SimulationEngine> factory, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false, ControllerKind playerForceController = ControllerKind.Player, bool enableXunStartCheckpoints = false)
    {
        var owned = new ScenarioSessionDefinition(definition);
        var session = new GameSession(owned, factory(owned.CreateWorld(seed, computerDamageBonusPercent, battleDeathsEnabled, playerForceController)))
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
        Restore(definition, snapshot, EngineFactory(definition));

    // A player load starts a new random run; exact restoration above is used by replay and diagnostics.
    internal static GameSession Load(ScenarioSessionDefinition owned, SessionSnapshot snapshot,
        Func<WorldState, SimulationEngine> factory)
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

    internal static GameSession Restore(ScenarioInitialState definition, SessionSnapshot snapshot, Func<WorldState, SimulationEngine> factory)
    {
        return Restore(new ScenarioSessionDefinition(definition), snapshot, factory);
    }

    internal static GameSession Restore(ScenarioSessionDefinition owned, SessionSnapshot snapshot, Func<WorldState, SimulationEngine> factory)
    {
        var world = WorldState.Restore(snapshot.World);
        SessionValidation.Validate(owned, snapshot);
        return RestoreValidated(owned, snapshot, world, factory);
    }

    private static GameSession RestoreValidated(ScenarioSessionDefinition owned, SessionSnapshot snapshot, WorldState world,
        Func<WorldState, SimulationEngine> factory)
    {
        var simulation = factory(world);
        var session = new GameSession(owned, simulation);
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
