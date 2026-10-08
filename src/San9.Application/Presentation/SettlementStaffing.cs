using San9.Core.Domain;

namespace San9.Application.Presentation;

internal sealed record SettlementStaffing(OfficerState[] Present, OfficerView[] AssignedPeople, int Available, int Assigned)
{
    public static SettlementStaffing Read(StrategySnapshot view, int id)
    {
        var place = view.World.Settlements.Single(s => s.Id == id);
        var tasks = view.World.Simulation.Tasks;
        var present = view.World.Officers.Where(o => !o.IsDormant && o.UnitId is null &&
            (o.SettlementId ?? o.RegionId) == id && !tasks.Any(t => t.OfficerId == o.Id && t.Journey is not null)).ToArray();
        int available = present.Count(o => o.IsServing && o.OwnerId == place.OwnerId && !o.Acted &&
            !tasks.Any(t => t.OfficerId == o.Id));
        var ownedUnits = view.World.Units.Where(u => u.OwnerId == place.OwnerId).Select(u => u.Id).ToHashSet();
        var assignedPeople = view.Officers.Where(o => o.ServicePlaceId == id &&
            (o.UnitId is not int unitId || ownedUnits.Contains(unitId))).ToArray();
        int assigned = assignedPeople.Count(o => o.IsServing && o.OwnerId == place.OwnerId);
        return new(present, assignedPeople, available, assigned);
    }
}
