using San9.Core.Simulation;
using San9.Core.Domain;
using San9.Content.Scenarios;
using San9.Application.Presentation;

namespace San9.Application;

public sealed class GameSession
{
    private readonly SimulationEngine _simulation;
    private readonly WorldState _world;
    private ScenarioInitialState? _definition;
    public event Action? Changed;
    private readonly List<SessionRecord> _records = [];
    private readonly List<ChronicleEntry> _chronicle = [];
    public IReadOnlyList<SessionRecord> Records => _records.AsReadOnly();
    public IReadOnlyList<ChronicleEntry> Chronicle => _chronicle.AsReadOnly();
    public void AddChronicleNote(string text)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 240) throw new ArgumentException("Chronicle notes must contain 1 to 240 characters.");
        var clock = Snapshot.Clock;
        _chronicle.Add(new(clock.Year, clock.Month, 1 + 10 * ((int)clock.Xun - 1), View.PlayerForceId, text));
        Changed?.Invoke();
    }

    public GameSession(WorldState initialState)
    {
        _simulation = new SimulationEngine(initialState);
        _world = initialState;
    }

    public string InitialDate => _definition is { } d ? $"{d.Year}年{d.Month}月{d.Day}日" : "";
    public WorldSnapshot Snapshot => _simulation.Snapshot;
    private StrategySnapshot? _initialView;
    public StrategySnapshot InitialView => _initialView ?? throw new InvalidOperationException("No scenario attached.");
    private StrategySnapshot? _view;
    public StrategySnapshot View => _view is not null && _view.World.Revision == Snapshot.Revision ? _view :
        _view = new(_definition ?? throw new InvalidOperationException("No scenario content attached."), Snapshot);

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
            definition.Forces.Select(f => new ForceState(f.Id, f.Money, f.Food, f.Trust) { IsBarbarian = f.IsBarbarian, LeaderId = f.LeaderId, RankId = f.RankId }),
            definition.Settlements.Select(s => new SettlementState(s.Id, s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability,
                s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce, s.Cultivation, s.MaxCultivation) { Kind = s.Kind, CorpsId = s.CorpsId, Population = s.Population, DraftPopulation = s.DraftPopulation }), definition.Officers.Select(o => o.State), definition.Units, definition.Information.Corps, definition.Information.TreasureStates, definition.Information.Relations);
        var session = new GameSession(world) { _definition = definition };
        session._initialView = session.View;
        return session;
    }

    public DevelopmentOutcome Develop(DevelopmentCommand command)
    {
        var result = _world.Develop(command);
        if (result.Accepted)
        {
            _records.Add(new(Snapshot.Clock, command.ForceId, command.SettlementId, command.OfficerId, command.Activity, result.Increment, result.Cost));
            Changed?.Invoke();
        }
        return result;
    }

    public CommandSubmissionResult Validate(GameCommand command) => _simulation.Validate(command);

    public CommandSubmissionResult Submit(GameCommand command) => _simulation.Submit(command);

    public AdvanceXunResult AdvanceXun() { var result = _simulation.AdvanceXun(); Changed?.Invoke(); return result; }
}

public sealed record SessionRecord(GameClock Clock, int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity, int Increment, int Cost);
