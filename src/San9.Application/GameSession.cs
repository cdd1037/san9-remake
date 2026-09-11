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
        _chronicle.Add(new(clock.Year, clock.Month, 1 + 10 * ((int)clock.Xun - 1), View.PlayerForceId, text));
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
        var world = WorldState.Create(new(definition.Year, definition.Month, (XunIndex)((definition.Day - 1) / 10 + 1)), seed,
            definition.Forces.Select(f => new ForceState(f.Id, f.Money, f.Food, f.Trust) { ProvidedTactics = f.ProvidedTactics, IsBarbarian = f.IsBarbarian, LeaderId = f.LeaderId, RankId = f.RankId }),
            definition.Settlements.Select(s => new SettlementState(s.Id, s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability,
                s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce, s.Cultivation, s.MaxCultivation) { Kind = s.Kind, CorpsId = s.CorpsId, Population = s.Population, DraftPopulation = s.DraftPopulation }), definition.Officers.Select(o => o.State), definition.Units, definition.Information.Corps, definition.Information.TreasureStates, definition.Information.Relations);
        return new GameSession(definition, world);
    }

    public DevelopmentOutcome Develop(DevelopmentCommand command)
    {
        var result = _simulation.Develop(command);
        foreach (var completed in result.Events.OfType<DevelopmentCompletedEvent>())
        {
            _records.Add(new(completed.Clock, completed.Command.ForceId, completed.Command.SettlementId,
                completed.Command.OfficerId, completed.Command.Activity, completed.Increment, completed.Cost));
        }
        if (result.Outcome.Accepted) Changed?.Invoke();
        return result.Outcome;
    }

    public CommandSubmissionResult Validate(GameCommand command) => _simulation.Validate(command);

    public CommandSubmissionResult Submit(GameCommand command) => _simulation.Submit(command);

    public AdvanceXunResult AdvanceXun() { var result = _simulation.AdvanceXun(); Changed?.Invoke(); return result; }
}

public sealed record SessionRecord(GameClock Clock, int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity, int Increment, int Cost);
