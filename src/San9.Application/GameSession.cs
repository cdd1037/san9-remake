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

    public GameSession(WorldState initialState)
    {
        _simulation = new SimulationEngine(initialState);
        _world = initialState;
    }

    public WorldSnapshot Snapshot => _simulation.Snapshot;
    private StrategySnapshot? _view;
    public StrategySnapshot View => _view is not null && _view.World.Revision == Snapshot.Revision ? _view :
        _view = new(_definition ?? throw new InvalidOperationException("No scenario content attached."), Snapshot);

    public static GameSession Start(ScenarioInitialState definition, ulong seed)
    {
        definition.Validate();
        definition = definition with
        {
            Forces = definition.Forces.ToArray(), Officers = definition.Officers.ToArray(), Units = definition.Units.ToArray(),
            Settlements = definition.Settlements.Select(s => s with { Icons = s.Icons.ToArray() }).ToArray(),
            RegionNameKeys = new(definition.RegionNameKeys), RegionCityIds = new(definition.RegionCityIds)
        };
        if (definition.Day is not (1 or 11 or 21)) throw new ArgumentException("Expected a stable xun boundary.");
        var world = WorldState.Create(new(definition.Year, definition.Month, (XunIndex)((definition.Day - 1) / 10 + 1)), seed,
            definition.Forces.Select(f => new ForceState(f.Id, f.Money, f.Food, f.Trust) { IsBarbarian = f.IsBarbarian, LeaderId = f.LeaderId }),
            definition.Settlements.Select(s => new SettlementState(s.Id, s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability,
                s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce, s.Cultivation, s.MaxCultivation) { Kind = s.Kind, CorpsId = s.CorpsId }), definition.Officers.Select(o => o.State), definition.Units);
        return new GameSession(world) { _definition = definition };
    }

    public DevelopmentOutcome Develop(DevelopmentCommand command)
    {
        var result = _world.Develop(command);
        if (result.Accepted) Changed?.Invoke();
        return result;
    }

    public CommandSubmissionResult Validate(GameCommand command) => _simulation.Validate(command);

    public CommandSubmissionResult Submit(GameCommand command) => _simulation.Submit(command);

    public AdvanceXunResult AdvanceXun() => _simulation.AdvanceXun();
}
