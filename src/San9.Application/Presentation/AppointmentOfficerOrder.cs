namespace San9.Application.Presentation;

public static class AppointmentOfficerOrder
{
    public static IEnumerable<int> Selected(StrategySnapshot view, IEnumerable<int> ids)
    {
        var people = view.World.Officers.ToDictionary(p => p.Id);
        var offices = view.Information.Offices.ToDictionary(o => o.Id);
        return ids.OrderByDescending(id => people[id].IsServing).ThenByDescending(id => people[id].Loyalty)
            .ThenByDescending(id => people[id].OfficeId is null)
            .ThenByDescending(id => people[id].OfficeId is int office ? offices[office].GovernorPriority : 0).ThenBy(id => id);
    }
}
