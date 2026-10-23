using San9.Content.Scenarios;

namespace San9.Application.Presentation;

public sealed class StrategySelection(GameSession session)
{
    private StrategySnapshot scenario => session.View;
    public event Action? Changed;
    public (int X, int Y)? Square { get; private set; }
    public int? BuildingId { get; private set; }

    public void SelectBuilding(int id)
    {
        var building = scenario.Settlements.SingleOrDefault(s => s.Id == id)
            ?? throw new ArgumentOutOfRangeException(nameof(id));
        Square = (building.X, building.Y); BuildingId = id; Changed?.Invoke();
    }

    public void SelectSquare(int x, int y)
    {
        if (x < 0 || y < 0 || scenario.MilitaryRules is { } rules && (x >= rules.Map.Width || y >= rules.Map.Height))
            throw new ArgumentOutOfRangeException(nameof(x));
        Square = (x, y);
        BuildingId = scenario.Settlements.FirstOrDefault(s => s.X == x && s.Y == y)?.Id;
        Changed?.Invoke();
    }

    public void Clear() { Square = null; BuildingId = null; Changed?.Invoke(); }
}
