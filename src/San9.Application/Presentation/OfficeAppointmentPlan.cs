using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>A local allocation plan. Nested editors fork it; only the appointment command changes the world.</summary>
public sealed class OfficeAppointmentPlan
{
    private readonly StrategySnapshot view;
    private readonly Dictionary<int, OfficerState> people;
    private readonly Dictionary<int, OfficeDefinition> definitions;
    private readonly ImmutableArray<int> available;
    private readonly OfficerAbilityRules abilities;
    private readonly Dictionary<int, int?> assignments;

    internal OfficeAppointmentPlan(StrategySnapshot view, IEnumerable<int> candidates, IEnumerable<int> offices)
    {
        this.view = view;
        people = view.World.Officers.Where(p => candidates.Contains(p.Id)).ToDictionary(p => p.Id);
        definitions = view.Information.Offices.ToDictionary(o => o.Id);
        available = offices.OrderByDescending(id => definitions[id].GovernorPriority).ThenBy(id => id).ToImmutableArray();
        assignments = people.ToDictionary(p => p.Key, p => p.Value.OfficeId);
        abilities = view.Rules.Abilities;
    }
    private OfficeAppointmentPlan(OfficeAppointmentPlan source)
    {
        view = source.view; people = source.people; definitions = source.definitions; available = source.available;
        abilities = source.abilities; assignments = new(source.assignments);
    }
    public OfficeAppointmentPlan Fork() => new(this);
    public void Accept(OfficeAppointmentPlan edited)
    {
        if (!ReferenceEquals(people, edited.people)) throw new ArgumentException("An editor must belong to this plan.", nameof(edited));
        foreach (var (id, office) in edited.assignments) assignments[id] = office;
    }
    public ImmutableArray<int> AvailableOffices => available;
    public int? OfficeFor(int id) => assignments[id];
    public OfficeDefinition? Definition(int? id) => id is int value ? definitions[value] : null;
    public OfficerAbilities Abilities(int id) => abilities.Evaluate(people[id] with { OfficeId = assignments[id] }, view.World.Treasures,
        view.World.EquipmentInitialized ? view.World.Equipment : null);
    public uint Capacity(int id) => Definition(assignments[id])?.CommandCapacity ?? 10000;
    public int Direction(int id)
    {
        int? before = people[id].OfficeId, after = assignments[id];
        if (before == after) return 0;
        if (before is null) return 1;
        if (after is null) return -1;
        return definitions[after.Value].Salary.CompareTo(definitions[before.Value].Salary);
    }
    public int Promotions => assignments.Keys.Count(id => Direction(id) > 0);
    public int Demotions => assignments.Keys.Count(id => Direction(id) < 0);
    private int Priority(int? id) => Definition(id)?.GovernorPriority ?? -1;
    private long Total(int id) { var a = Abilities(id); return (long)a.Leadership + a.War + a.Intelligence + a.Politics; }
    public IEnumerable<int> OrderedPeople => assignments.Keys.OrderByDescending(id => Priority(assignments[id])).ThenByDescending(Total).ThenBy(id => id);
    public ImmutableArray<OfficeAssignment> Changes => assignments.Where(p => people[p.Key].OfficeId != p.Value)
        .OrderByDescending(p => Priority(p.Value)).ThenBy(p => p.Key).Select(p => new OfficeAssignment(p.Key, p.Value)).ToImmutableArray();
    public bool Assign(int person, int? office)
    {
        if (!assignments.ContainsKey(person) || office is int id && !available.Contains(id)) return false;
        if (office is not null)
            foreach (int holder in assignments.Where(p => p.Value == office).Select(p => p.Key).ToArray()) assignments[holder] = null;
        assignments[person] = office;
        return true;
    }
    private int[] Vacancies => available.Where(id => !assignments.ContainsValue(id)).ToArray();
    public string? FillUnavailable => Vacancies.Length == 0 ? "no-vacancy" : assignments.Values.All(id => id is not null) ? "no-target" : null;
    public string? RemoveUnavailable => assignments.Values.Any(id => id is not null) ? null : "no-target";
    public string? PromoteUnavailable => assignments.Values.All(id => id is null) ? "no-target" : PromotionAssignments().Count == 0 ? "no-vacancy" : null;
    public void RemoveAll()
    {
        foreach (int id in assignments.Keys) assignments[id] = null;
    }
    public void FillVacancies()
    {
        var vacancies = Vacancies.Select(id => definitions[id]).ToArray();
        bool balanced = view.Information.Ranks.SingleOrDefault(r => r.Id == view.World.Forces.Single(f => f.Id == view.PlayerForceId).RankId)?.PreferBalancedOfficeCandidates == true;
        var pool = assignments.Where(p => p.Value is null).Select(p => p.Key).ToHashSet();
        var military = pool.OrderByDescending(id => balanced ? Total(id) : 0).ThenByDescending(id => Abilities(id).Leadership)
            .ThenByDescending(id => (long)Abilities(id).Intelligence + Abilities(id).Politics).ThenBy(id => id).ToArray();
        void Fill(IEnumerable<OfficeDefinition> offices, IEnumerable<int> candidates)
        {
            foreach (var pair in offices.Zip(candidates.Where(pool.Contains).ToArray()))
            { assignments[pair.Second] = pair.First.Id; pool.Remove(pair.Second); }
        }
        Fill(vacancies.Where(o => o.FillBeforeOrdinaryOffices), military);
        Fill(vacancies.Where(o => !o.FillBeforeOrdinaryOffices && o.AppointmentGroup == OfficeAppointmentGroup.Military), military);
        Fill(vacancies.Where(o => !o.FillBeforeOrdinaryOffices && o.AppointmentGroup == OfficeAppointmentGroup.Civil),
            pool.OrderByDescending(id => (long)Abilities(id).Intelligence + Abilities(id).Politics).ThenBy(id => id));
    }
    private Dictionary<int, int> PromotionAssignments()
    {
        Dictionary<int, int> changes = [];
        foreach (var group in Enum.GetValues<OfficeAppointmentGroup>())
        {
            var holders = assignments.Where(p => p.Value is int id && definitions[id].AppointmentGroup == group)
                .OrderByDescending(p => Priority(p.Value)).ThenBy(p => p.Key).ToArray();
            var offices = available.Where(id => definitions[id].AppointmentGroup == group).ToArray();
            foreach (var pair in holders.Zip(offices))
                if (Priority(pair.Second) > Priority(pair.First.Value)) changes.Add(pair.First.Key, pair.Second);
        }
        return changes;
    }
    public void Promote()
    {
        // Apply together: the office vacated by a senior holder may be the next holder's destination.
        foreach (var (person, office) in PromotionAssignments()) assignments[person] = office;
    }
    public CatalogCell Cell(int person, string column, CatalogCell original, TextCatalog texts)
    {
        var office = Definition(assignments[person]); var a = Abilities(person);
        return column switch {
            "office" => new(office is null ? "无" : office.Name, Priority(office?.Id)),
            "capacity" => (long)Capacity(person), "salary" => office?.Salary ?? 0,
            "leadership" => a.Leadership, "war" => a.War, "intelligence" => a.Intelligence, "politics" => a.Politics,
            _ => original };
    }
}
