using System.Collections.Immutable;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Revision-bound personnel preparation. Selections remain local until one atomic submission.</summary>
public sealed class PersonnelDraft
{
    private readonly GameSession _session;
    // A draft belongs to one world revision; repeated UI reads share its immutable previews.
    private readonly Dictionary<(int Actor, int Target), PersonnelJourneyPreview> _previews = [];
    public StrategySnapshot View { get; }
    public SettlementState Home { get; }
    public string Kind { get; }
    public string Title => Kind switch { "summon" => "召来", "move" => "移动", "search" => "探索", "employ" => "登庸", _ => throw new InvalidOperationException() };
    public PersonnelJourneyKind JourneyKind => Kind switch { "search" => PersonnelJourneyKind.Search, "employ" => PersonnelJourneyKind.Recruit, _ => PersonnelJourneyKind.Move };
    public ImmutableArray<int> Candidates { get; }
    public ImmutableArray<int> Targets { get; }
    public ImmutableArray<int> Members { get; private set; } = [];
    public int? TargetId { get; private set; }
    public int SelectionLimit => Kind is "search" or "employ" ? 1 : Candidates.Length;
    public bool IsCurrent => _session.Snapshot.Revision == View.World.Revision;
    public bool CanExecute => IsCurrent && TargetId is int target && !Members.IsEmpty && Members.All(id => Preview(id, target).Available);
    public CommandAdvice? Advice => IsCurrent && Members.Length == 1 && TargetId is int target && Kind is "search" or "employ" ? Preview(Members[0], target).Advice : null;
    public ThreeVisitsStep? SearchAdvice => IsCurrent && Kind == "search" && Members.Length == 1 && TargetId is int target ? Preview(Members[0],target).SearchAdvice : null;
    public static bool Supports(string kind) => kind is "summon" or "move" or "search" or "employ";
    public static string? UnavailableReasonKey(StrategySnapshot view, int homeId, string kind) => Supports(kind)
        ? new MapCommandAvailability(view).SettlementReasonKey(homeId, kind) : San9.Content.Interface.CommandMenuSkin.ReasonKey("unavailable");

    public PersonnelDraft(GameSession session, int homeId, string kind)
    {
        _session = session; View = session.View; Kind = kind;
        if (UnavailableReasonKey(View, homeId, kind) is string reason) throw new ArgumentException(reason, nameof(homeId));
        Home = View.World.Settlements.Single(s => s.Id == homeId);
        var availability = new MapCommandAvailability(View);
        Candidates = availability.PersonnelActors(Home, kind)
            .Where(p => kind != "summon" || Preview(p.Id, homeId).Available)
            .OrderByDescending(p => View.Officers.Single(o => o.Id == p.Id).Abilities.Politics)
            .ThenBy(p => p.Id).Select(p => p.Id).ToImmutableArray();
        Targets = kind == "summon" ? [homeId] : kind == "search" ? session.GetSearchPlaces(Home.OwnerId!.Value) :
            availability.PersonnelTargets(Home, kind).Order().ToImmutableArray();
        if (kind == "employ") Targets = Targets.Intersect(session.GetRecruitTargets(Home.OwnerId!.Value, Candidates[0])).ToImmutableArray();
        if (Targets.Length == 1) TargetId = Targets[0];
        if (Candidates.Length == 1 || kind is "search" or "employ") Members = Candidates.Take(1).ToImmutableArray();
    }
    private PersonnelJourneyPreview Preview(int actor, int target)
    {
        if (!IsCurrent) throw new InvalidOperationException("Stale personnel preparation.");
        if (!_previews.TryGetValue((actor, target), out var preview))
        {
            preview = _session.GetPersonnelJourney(Home.OwnerId!.Value, actor, target, JourneyKind);
            _previews.Add((actor, target), preview);
        }
        return preview;
    }
    public int? ArrivalDays(int actor) => IsCurrent && TargetId is int target ? Preview(actor, target).Days : null;
    public int? ArrivalDaysTo(int target) => IsCurrent && Targets.Contains(target)
        ? Preview(Members.IsEmpty ? Candidates[0] : Members[0], target).Days : null;
    public int? DistanceDaysTo(int target) => IsCurrent && Targets.Contains(target)
        ? Preview(Members.IsEmpty ? Candidates[0] : Members[0], target).DistanceDays : null;
    public bool CanSelect(IReadOnlyCollection<int> ids) => IsCurrent && ids.Count <= SelectionLimit &&
        ids.Distinct().Count() == ids.Count && !ids.Except(Candidates).Any() &&
        (TargetId is not int target || ids.All(id => Preview(id, target).Available));
    public bool SetMembers(IEnumerable<int> ids)
    {
        int[] chosen = ids.ToArray();
        if (!CanSelect(chosen)) return false;
        Members = Candidates.Where(chosen.Contains).ToImmutableArray(); return true;
    }
    public bool SetTarget(int id)
    {
        if (!IsCurrent || !Targets.Contains(id)) return false;
        TargetId = id; return true;
    }
    public ImmutableHashSet<MapCell> TargetCells()
    {
        if (View.MilitaryRules is not {} rules) return [];
        if (Kind == "move") return View.World.Settlements.Where(s => Targets.Contains(s.Id)).SelectMany(rules.GeographicFootprint).ToImmutableHashSet();
        if (Kind != "search") return [];
        var cells = ImmutableHashSet.CreateBuilder<MapCell>();
        var regions = Targets.ToHashSet();
        for (int y = 0; y < rules.Map.Height; y++)
        for (int x = 0; x < rules.Map.Width; x++)
            if (rules.RegionAt(MapCoordinates.Center(new(x, y))) is int id && regions.Contains(id)) cells.Add(new(x, y));
        foreach (var settlement in View.World.Settlements.Where(s => Targets.Contains(s.Id))) cells.UnionWith(rules.GeographicFootprint(settlement));
        return cells.ToImmutable();
    }
    public int? PlaceAt(MapCell cell) => View.RegionAt(MapCoordinates.Center(cell));
    public string PlaceName(TextCatalog texts, int id) => View.Settlements.SingleOrDefault(s => s.Id == id)?.ResolveName(texts)
        ?? (View.RegionNameKeys.TryGetValue(id, out string? key) ? texts.Resolve(key) : throw new InvalidDataException("Unknown personnel place."));
    public string DestinationName(TextCatalog texts, int id)
    {
        var place = View.Settlements.Single(s => s.Id == id);
        string name = place.ResolveName(texts);
        return place.Kind == SettlementKind.City ? name : name + "〈" + PlaceName(texts, View.RegionCityIds[id]) + "〉";
    }
    public GameCommand CreateCommand(Guid id, long sequence)
    {
        if (!CanExecute) throw new InvalidOperationException("Invalid or stale personnel preparation.");
        int owner = Home.OwnerId!.Value, target = TargetId!.Value;
        return Kind switch
        {
            "search" => new SearchCommand(id, owner, sequence, Members[0], target),
            "employ" => new RecruitOfficerCommand(id, owner, sequence, Members[0], target),
            _ => new MoveOfficersCommand(id, owner, sequence, Members, target)
        };
    }
}
