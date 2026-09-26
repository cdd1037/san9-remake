using San9.Core.Domain;

namespace San9.Application.Presentation;

internal sealed record SettlementStaffing(OfficerState[] Present, int Available, int Assigned)
{
    public static SettlementStaffing Read(StrategySnapshot view, int id)
    {
        var place = view.World.Settlements.Single(s => s.Id == id);
        var tasks = view.World.Simulation.Tasks;
        var present = view.World.Officers.Where(o => !o.IsDormant && o.UnitId is null &&
            (o.SettlementId ?? o.RegionId) == id && !tasks.Any(t => t.OfficerId == o.Id && t.Journey is not null)).ToArray();
        int available = present.Count(o => o.IsServing && o.OwnerId == place.OwnerId && !o.Acted &&
            !tasks.Any(t => t.OfficerId == o.Id));
        var serviceUnits = view.World.Units.Where(u => u.ServicePlaceId == id && u.OwnerId == place.OwnerId)
            .Select(u => u.Id).ToHashSet();
        int assigned = view.World.Officers.Count(o => !o.IsDormant && o.IsServing && o.OwnerId == place.OwnerId &&
            (o.UnitId is int unitId ? serviceUnits.Contains(unitId) : (o.SettlementId ?? o.RegionId) == id));
        return new(present, available, assigned);
    }
}
