using San9.Core.Domain;

namespace San9.Application.Presentation;

internal sealed partial class MapCommandAvailability
{
    private bool Stationed(OfficerState person) => person.SettlementId is int id && places.TryGetValue(id, out var place) &&
        Present(place) && place.OwnerId == Owner && !view.World.Simulation.Tasks.Any(t => t.OfficerId == person.Id && t.Journey is not null);
    private IEnumerable<OfficerState> Subordinates() => view.World.Officers.Where(p => !p.IsDormant && p.IsServing &&
        p.OwnerId == Owner && p.Id != Force.LeaderId && p.Status != OfficerStatus.Ruler);
    private bool Captive(OfficerState person) => !person.IsDormant && person.Status == OfficerStatus.Captive && Stationed(person);
    private string? Appointment(string command)
    {
        var subordinates = Subordinates().ToArray();
        var stationary = subordinates.Where(Stationed).ToArray();
        switch (command)
        {
            case "strategist":
                return subordinates.Any(p => p.Status == OfficerStatus.Serving && people[p.Id].Abilities.Intelligence >= 70)
                    ? null : "no-adviser-candidate";
            case "reward":
                if (Treasury().Money < 100) return "reward-funds";
                if (stationary.Length == 0) return "no-stationed-officer";
                var loyal = stationary.Where(p => p.Loyalty < 100).ToArray();
                if (loyal.Length == 0) return "loyalty-max";
                return loyal.Any(p => !p.RewardedThisXun) ? null : "no-reward-candidate";
            case "bestow":
                if (!view.World.Treasures.Any(t => t.Discovered && t.HolderId == Force.LeaderId)) return "no-treasure";
                return stationary.Length > 0 ? null : "no-stationed-officer";
            case "confiscate":
                var holders = subordinates.Concat(view.World.Officers.Where(Captive))
                    .Where(p => view.World.Treasures.Any(t => t.Discovered && t.HolderId == p.Id)).ToArray();
                if (holders.Length == 0) return "no-treasure-holder";
                return holders.Any(Stationed) ? null : "no-stationed-treasure-holder";
            case "execute": return view.World.Officers.Any(Captive) ? null : "no-prisoner";
            case "exile": return subordinates.Any(Stationed) || view.World.Officers.Any(Captive) ? null : "no-exile-candidate";
            case "rank":
                if (subordinates.Length == 0) return "no-subordinate";
                if (stationary.Length == 0) return "no-office-candidate";
                var rank = view.Information.Ranks.FirstOrDefault(r => r.Id == Force.RankId);
                bool available = view.Information.Offices.Any(o =>
                    (o.RequiredRankId is null || rank is not null && view.Information.Ranks.Any(r => r.Id == o.RequiredRankId && r.Precedence <= rank.Precedence)) &&
                    !subordinates.Any(p => p.OfficeId == o.Id && !Stationed(p)));
                return available ? null : "no-office";
            case "delegate":
                int? rulerHome = view.World.Officers.SingleOrDefault(p => p.Id == Force.LeaderId)?.SettlementId;
                if (!places.Values.Any(s => s.Kind == SettlementKind.City && s.OwnerId == Owner && Present(s) && s.Id != rulerHome)) return "no-delegation-city";
                return subordinates.Any(p => p.SettlementId != rulerHome) ? null : "no-corps-leader";
            default: return "unavailable";
        }
    }

    private string? Recall(SettlementState home)
    {
        var sources = OtherHomes(home).ToArray();
        if (sources.Length == 0) return "no-other-facility";
        var normal = sources.Where(s => s.ConfusionDaysRemaining == 0 && !view.IsSettlementEngaged(s.Id)).Select(s => s.Id).ToHashSet();
        if (normal.Count == 0) return "no-normal-facility";
        return view.Officers.Any(p => p.OwnerId == Owner && p.SettlementId is int location && normal.Contains(location) && p.IsAvailable &&
            (p.Status is OfficerStatus.Serving or OfficerStatus.Strategist ||
                p.Id == Force.LeaderId && (home.CorpsId is null || view.World.Corps.Any(c => c.Id == home.CorpsId && c.IsRoot))))
            ? null : "no-recall-candidate";
    }

    private bool Recruitable(OfficerState person)
    {
        if (person.IsDormant) return false;
        if (person.Status == OfficerStatus.Captive) return Captive(person);
        if (person.IsServing) return person.Status != OfficerStatus.Ruler && person.OwnerId is int owner &&
            !forces[owner].IsBarbarian && Hostile(owner);
        if (person.Status != OfficerStatus.Free || (person.SettlementId ?? person.RegionId) is not int location) return false;
        if (places.TryGetValue(location, out var place) && !place.IsDestroyed && place.OwnerId is int holder) return holder == Owner;
        return !view.RegionCityIds.TryGetValue(location, out int city) || !places.TryGetValue(city, out var parent) || parent.OwnerId is null || parent.OwnerId == Owner;
    }
}
