using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Revision-bound preparation; selection and preview never consume simulation random numbers.</summary>
public sealed class PatrolDraft
{
    public StrategySnapshot View { get; }
    public SettlementState Home { get; }
    public ImmutableArray<int> Candidates { get; }
    public ImmutableArray<int> Members { get; private set; } = [];
    public uint Money { get; }
    public int Cost => Members.Length * 50;
    public int SelectionLimit => 5;
    public bool CanExecute => Members.Length > 0 && Cost <= Money;
    public long DraftGrowth => ReadDraftGrowth(View.World, Home);

    public static string? UnavailableReasonKey(StrategySnapshot view, int id) =>
        new MapCommandAvailability(view).SettlementReasonKey(id, "patrol");
    private static uint Funds(StrategySnapshot view, SettlementState home) => view.World.Corps
        .Single(c => home.CorpsId is int corps ? c.Id == corps : c.OwnerId == home.OwnerId && c.IsRoot).Treasury.Money;

    public PatrolDraft(StrategySnapshot view, int id)
    {
        if (UnavailableReasonKey(view, id) is string reason) throw new ArgumentException(reason, nameof(id));
        View = view; Home = view.World.Settlements.Single(s => s.Id == id); Money = Funds(view, Home);
        Candidates = view.Officers.Where(o => o.IsAvailable && o.OwnerId == Home.OwnerId && o.SettlementId == id)
            .OrderByDescending(o => o.Abilities.Intelligence).ThenBy(o => o.Id).Select(o => o.Id).ToImmutableArray();
        if (Candidates.Length == 1) SetMembers(Candidates);
    }
    public bool SetMembers(IEnumerable<int> ids)
    {
        int[] selected = ids.ToArray();
        if (selected.Length > SelectionLimit || selected.Length * 50 > Money || selected.Distinct().Count() != selected.Length || selected.Except(Candidates).Any()) return false;
        var abilities = View.Officers.ToDictionary(o => o.Id, o => o.Abilities);
        Members = DomesticDevelopment.OrderParticipants(View.World.Officers.Where(p => selected.Contains(p.Id)), abilities, DomesticActivity.Patrol)
            .Select(p => p.Id).ToImmutableArray();
        return true;
    }
    public DevelopCommand CreateCommand(Guid id, long sequence) => CanExecute
        ? new(id, Home.OwnerId!.Value, sequence, new(Home.OwnerId.Value, Home.Id, Members, DomesticActivity.Patrol))
        : throw new InvalidOperationException("No valid patrol selection.");

    public static long ReadDraftGrowth(WorldSnapshot world, SettlementState city)
    {
        var cities = world.Settlements.Where(s => s.Kind == SettlementKind.City).ToArray();
        long military = cities.Sum(s => (long)s.DraftPopulation) + world.Settlements.Sum(s => (long)s.Soldiers + s.Wounded) +
            world.Units.Sum(u => (long)u.Soldiers + u.Wounded) + world.Simulation.Tasks.Where(t => t.Reinforcements is not null)
                .Sum(t => (long)t.Reinforcements!.Soldiers + t.Reinforcements.Wounded);
        // The original preview substitutes the target city's order while retaining the current world's total.
        long growth = EconomyMath.DraftIncrease(cities.Sum(s => (long)s.Population), military, cities.Sum(s => (long)s.PublicOrder), city.PublicOrder);
        return Math.Min(Math.Max(500000L, city.DraftPopulation), city.DraftPopulation + growth) - city.DraftPopulation;
    }
}
