using San9.Core.Domain;

namespace San9.Application.Presentation;

internal sealed partial class MapCommandAvailability
{
    private bool Stationed(OfficerState person) => person.SettlementId is int id && places.TryGetValue(id, out var place) &&
        Present(place) && place.OwnerId == Owner && !view.World.Simulation.Tasks.Any(t => t.OfficerId == person.Id && t.Journey is not null);
    private IEnumerable<OfficerState> Subordinates() => view.World.Officers.Where(p => !p.IsDormant && p.IsServing &&
        p.OwnerId == Owner && p.Id != Force.LeaderId && p.Status != OfficerStatus.Ruler);
    private bool Captive(OfficerState person) => !person.IsDormant && person.Status == OfficerStatus.Captive && Stationed(person);
    public IEnumerable<OfficerState> AppointmentPeople(string command) => command switch
    {
        "strategist" => Subordinates().Where(p => p.Status == OfficerStatus.Serving && people[p.Id].Abilities.Intelligence >= 70),
        "reward" => Subordinates().Where(p => Stationed(p) && p.Loyalty < 100 && !p.RewardedThisXun),
        "rank" or "bestow" => Subordinates().Where(Stationed),
        "execute" => view.World.Officers.Where(Captive),
        "exile" => Subordinates().Where(Stationed).Concat(view.World.Officers.Where(Captive)),
        _ => []
    };
    public IEnumerable<TreasureState> AppointmentTreasures(string command) => view.World.Treasures.Where(t => t.Discovered &&
        (command == "bestow" ? t.HolderId == Force.LeaderId : command == "confiscate" &&
            view.World.Officers.Any(p => !p.IsDormant && p.Id == t.HolderId && Stationed(p) && (Captive(p) ||
                p.IsServing && p.OwnerId == Owner && p.Id != Force.LeaderId && p.Status != OfficerStatus.Ruler))));
    public IEnumerable<int> AppointmentOffices()
    {
        var rules = new GovernanceRulebook(view.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { RequiredRankId = o.RequiredRankId }), view.Information.Ranks.ToDictionary(r => r.Id, r => r.Precedence));
        return rules.Offices.Values.Where(o => rules.CanAppoint(Force.RankId, o) &&
            !Subordinates().Any(p => p.OfficeId == o.OfficeId && !Stationed(p))).Select(o => o.OfficeId);
    }
    private string? Appointment(string command)
    {
        var subordinates = Subordinates().ToArray();
        var stationary = subordinates.Where(Stationed).ToArray();
        switch (command)
        {
            case "strategist":
                return AppointmentPeople(command).Any()
                    ? null : "no-adviser-candidate";
            case "reward":
                if (Treasury().Money < 100) return "reward-funds";
                if (stationary.Length == 0) return "no-stationed-officer";
                var loyal = stationary.Where(p => p.Loyalty < 100).ToArray();
                if (loyal.Length == 0) return "loyalty-max";
                return AppointmentPeople(command).Any() ? null : "no-reward-candidate";
            case "bestow":
                if (!AppointmentTreasures(command).Any()) return "no-treasure";
                return stationary.Length > 0 ? null : "no-stationed-officer";
            case "confiscate":
                var holders = subordinates.Concat(view.World.Officers.Where(Captive))
                    .Where(p => view.World.Treasures.Any(t => t.Discovered && t.HolderId == p.Id)).ToArray();
                if (holders.Length == 0) return "no-treasure-holder";
                return AppointmentTreasures(command).Any() ? null : "no-stationed-treasure-holder";
            case "execute": return AppointmentPeople(command).Any() ? null : "no-prisoner";
            case "exile": return AppointmentPeople(command).Any() ? null : "no-exile-candidate";
            case "rank":
                if (subordinates.Length == 0) return "no-subordinate";
                if (stationary.Length == 0) return "no-office-candidate";
                return AppointmentOffices().Any() ? null : "no-office";
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
        return PersonnelActors(home, "summon").Any() ? null : "no-recall-candidate";
    }

    public IEnumerable<OfficerState> PersonnelActors(SettlementState home, string kind)
    {
        if (kind != "summon") return view.World.Officers.Where(p => people.TryGetValue(p.Id, out var shown) && shown.IsAvailable &&
            p.OwnerId == Owner && p.SettlementId == home.Id);
        var origins = OtherHomes(home).Where(s => s.ConfusionDaysRemaining == 0 && !view.IsSettlementEngaged(s.Id)).Select(s => s.Id).ToHashSet();
        return view.World.Officers.Where(p => people.TryGetValue(p.Id, out var shown) && shown.IsAvailable && p.OwnerId == Owner &&
            p.SettlementId is int origin && origins.Contains(origin) && (p.Status is OfficerStatus.Serving or OfficerStatus.Strategist ||
                p.Id == Force.LeaderId && (home.CorpsId is null || view.World.Corps.Any(c => c.Id == home.CorpsId && c.IsRoot))));
    }

    public IEnumerable<int> PersonnelTargets(SettlementState home, string kind) => kind == "move"
        ? OtherHomes(home).Select(s => s.Id) : kind == "employ" ? view.World.Officers.Where(Recruitable).Select(p => p.Id) : [];

    private bool Recruitable(OfficerState person)
    {
        if (person.IsDormant || forces.Values.Any(f => f.LeaderId == person.Id)) return false;
        if (person.Status == OfficerStatus.Captive) return Captive(person);
        if (person.IsServing) return person.Status != OfficerStatus.Ruler && person.OwnerId is int owner &&
            !forces[owner].IsBarbarian && Hostile(owner);
        if (person.Status != OfficerStatus.Free || (person.SettlementId ?? person.RegionId) is not int location) return false;
        if (places.TryGetValue(location, out var place) && !place.IsDestroyed && place.OwnerId is int holder) return holder == Owner;
        return !view.RegionCityIds.TryGetValue(location, out int city) || !places.TryGetValue(city, out var parent) || parent.OwnerId is null || parent.OwnerId == Owner;
    }
}
