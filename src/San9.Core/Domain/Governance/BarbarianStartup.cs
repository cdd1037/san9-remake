namespace San9.Core.Domain;

public static class BarbarianStartup
{
    public static IReadOnlyList<SettlementState> CreateSettlements(IEnumerable<ForceState> forces,
        IEnumerable<SettlementState> settlements, IEnumerable<OfficerState> officers)
    {
        var result = settlements.ToDictionary(s => s.Id);
        var people = officers.ToDictionary(o => o.Id);
        foreach (var force in forces.Where(f => !f.IsDormant && f.IsBarbarian).OrderBy(f => f.Id))
        {
            var cities = result.Values.Where(s => s.OwnerId == force.Id && s.Kind == SettlementKind.City &&
                !s.IsDormant && !s.IsDestroyed).OrderBy(s => s.Id).ToArray();
            int? home = force.LeaderId is int leader && people.TryGetValue(leader, out var ruler) ? ruler.SettlementId : null;
            var city = cities.FirstOrDefault(s => s.Id == home) ?? cities.FirstOrDefault();
            if (city is null) continue;
            result[city.Id] = city with { Soldiers = Math.Min(city.Soldiers + 200000u, city.SoldierCapacity) };
        }
        return result.Values.OrderBy(s => s.Id).ToArray();
    }
}
