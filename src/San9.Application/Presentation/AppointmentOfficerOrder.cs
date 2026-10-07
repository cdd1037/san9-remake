namespace San9.Application.Presentation;

public static class AppointmentOfficerOrder
{
    public static IEnumerable<int> Selected(StrategySnapshot view, IEnumerable<int> ids)
    {
        var people = view.World.Officers.ToDictionary(p => p.Id);
        return ids.OrderByDescending(id => people[id].IsServing).ThenByDescending(id => people[id].Loyalty)
            .ThenBy(id => people[id].BirthYear).ThenBy(id => id);
    }
}
