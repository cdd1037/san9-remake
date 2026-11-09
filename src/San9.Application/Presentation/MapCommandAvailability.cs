using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Read-only menu gates, in explanation priority order. Does not submit or simulate commands.</summary>
internal sealed partial class MapCommandAvailability(StrategySnapshot view)
{
    private readonly Dictionary<int, SettlementState> places = view.World.Settlements.ToDictionary(s => s.Id);
    private readonly Dictionary<int, ForceState> forces = view.World.Forces.ToDictionary(f => f.Id);
    private readonly Dictionary<int, OfficerView> people = view.Officers.ToDictionary(p => p.Id);
    private int Owner => view.RequirePlayerForce();
    private ForceState Force => forces[Owner];
    private static string? Key(string? reason) => reason is null ? null : CommandMenuSkin.ReasonKey(reason);
    private bool Controlled(int? corps) => !view.World.Corps.Any(c => c.OwnerId == Owner &&
        (c.IsRoot || c.Id == corps) && c.ControlMode == CorpsControlMode.Automatic);
    private bool Present(SettlementState place) => !place.IsDormant && !place.IsDestroyed &&
        place.CanHostPersons && place.Facility?.BuilderUnitId is null;
    private bool Actor(int home) => view.Officers.Any(p => p.OwnerId == Owner && p.SettlementId == home && p.IsAvailable);
    private CorpsTreasury Treasury(int? corps = null) => view.World.Corps.Single(c =>
        corps is int id ? c.Id == id : c.OwnerId == Owner && c.IsRoot).Treasury;

    public string? SettlementReasonKey(int id, string command) => Key(SettlementReason(id, command));

    private string? SettlementReason(int id, string command)
    {
        if (!places.TryGetValue(id, out var home) || !Present(home) || home.OwnerId != Owner)
            return "unavailable";
        // Management must remain reachable so a player can restore directly controlled corps.
        if (command == "delegate") return null;
        if (!(command is "move" or "summon" ? Controlled(null) : Controlled(home.CorpsId))) return "unavailable";
        if (IsDomestic(command) && home.Kind != SettlementKind.City && command is not ("repair" or "train"))
            return "unavailable";
        if (command is "strategist" or "rank" or "reward" or "bestow" or "confiscate" or "execute" or "exile")
            return Appointment(command);
        if (command == "summon") return Recall(home);
        if (command == "demolish" && !places.Values.Any(s => FacilityDemolitionRules.IsOtherDirectFacility(home, s)))
            return "no-move-destination";
        if (home.ConfusionDaysRemaining > 0) return "confused";
        // Deployment and transport remain available under siege; ordinary missions do not.
        if (command is not ("sortie" or "build" or "transport") && view.IsSettlementEngaged(id)) return "engaged";
        if (command == "demolish") return null;
        if (command == "move" && !OtherHomes(home, directOnly: false).Any()) return "no-move-destination";
        if (command == "transport" && !OtherHomes(home, directOnly: false).Any()) return "no-other-facility";
        if (command == "build" && !BuildableTypes().Any()) return "no-construction-site";
        if (command == "employ" && !view.World.Officers.Any(Recruitable)) return "no-employ-target";
        if (!Actor(id)) return "no-officer";
        if (IsDomestic(command)) return Domestic(home, command);
        if (command is "sortie" or "build" or "transport")
        {
            if (home.Soldiers == 0) return "no-soldiers";
            if (view.MilitaryRules is null) return "unavailable";
            if (command == "build" && Treasury(home.CorpsId).Money < MinimumConstructionCost(home)) return "funds";
            return null;
        }
        return command switch
        {
            "move" or "search" or "employ" => null,
            "alienate" => view.World.Officers.Any(p => p.IsServing && p.Status != OfficerStatus.Ruler &&
                p.OwnerId is int owner && !forces[owner].IsBarbarian && Hostile(owner) && p.Loyalty > 0) ? null : "no-alienate-target",
            "burn" or "seize" => places.Values.Any(p => BuildingTarget(home, p, command == "seize")) ? null : "no-target-facility",
            "false-report" or "disorder" or "inspire" or "rescue" => UnitStrategy(home, command),
            "rumor" => OtherForces().Count() < 2 ? "no-rumor-forces" :
                OtherForces().Any(f => view.World.Officers.Count(p => p.IsServing && !p.IsDormant && p.OwnerId == f.Id) > 1) ? null : "no-rumor-officers",
            "gift" => !OtherForces().Any() ? "no-target-force" : Treasury().Money >= GiftOffer.MinimumMoney || Treasury().Food >= GiftOffer.MinimumFood ||
                view.World.Treasures.Any(t => t.Discovered && t.HolderId == Force.LeaderId) ? null : "no-gift-resources",
            "surrender" => OtherForces().Any(f => !f.IsBarbarian && Hostile(f.Id)) ? null : "no-target-force",
            "return" => view.World.Officers.Any(p => p.Status == OfficerStatus.Captive && p.OwnerId == Owner &&
                p.SettlementId is int prison && places.TryGetValue(prison, out var location) && location.OwnerId is int captor && captor != Owner)
                ? null : "no-captured-officer",
            "request" => RequestReason(),
            _ => "unavailable"
        };
    }

    public string? UnitReasonKey(UnitState unit)
    {
        if (!Controlled(unit.CorpsId)) return Key("unavailable");
        // CommandedThisXun is informational in the remake; repeated orders remain supported.
        return Key(unit.Condition switch
        {
            UnitCondition.Routed => "unit-routed", UnitCondition.Confused => "unit-confused",
            UnitCondition.NoFormation => "unit-no-formation", UnitCondition.Disguised => "unit-false-report", _ => null
        });
    }

    private IEnumerable<ForceState> OtherForces() => forces.Values.Where(f => f.Id != Owner && !f.IsDormant && !f.IsEliminated);
    private bool Protected(int other) => view.World.Simulation.Alliance?.Protects(Owner, other) == true ||
        view.World.Simulation.AttackRequests.Any(r => r.RemainingXun > 0 && r.Connects(Owner, other));
    private bool Hostile(int other) => other != Owner && forces.TryGetValue(other, out var f) && !f.IsDormant && !f.IsEliminated && !Protected(other);
    private IEnumerable<SettlementState> OtherHomes(SettlementState home, bool directOnly = true) => places.Values.Where(s =>
        s.Id != home.Id && s.OwnerId == Owner && Present(s) && (!directOnly || Controlled(s.CorpsId)));
    private bool InRange(int origin, int target)
    {
        if (!view.RegionCityIds.TryGetValue(origin, out int sourceCity) || !view.RegionCityIds.TryGetValue(target, out int targetCity)) return false;
        return sourceCity == targetCity || view.CityConnections.Any(c => c.FromCityId == sourceCity && c.ToCityId == targetCity);
    }
    private bool BuildingTarget(SettlementState home, SettlementState target, bool needsMoney) =>
        Present(target) && target.OwnerId is int owner && !forces[owner].IsBarbarian && Hostile(owner) &&
        InRange(home.Id, target.Id) && (!needsMoney || view.World.Corps.Single(c => target.CorpsId is int id ? c.Id == id : c.OwnerId == owner && c.IsRoot).Treasury.Money > 0);
    private string? UnitStrategy(SettlementState home, string command)
    {
        bool friendly = command is "inspire" or "rescue";
        bool candidate = view.World.Units.Any(u => !u.IsEmbedded && !u.IsPersonnel && u.HasEnteredDailyCycle && u.Soldiers > 0 && u.FormationId is not null &&
            (friendly ? u.OwnerId == Owner : Hostile(u.OwnerId)) && view.MilitaryRules?.RegionAt(u.Position) is int region && InRange(home.Id, region) &&
            (command != "disorder" || !view.MilitaryRules.Formation(u).ImmuneToDisorder) &&
            (command != "inspire" || u.Morale < 100 || u.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits)));
        if (!candidate) return command switch { "false-report" => "no-false-report-target", "disorder" => "no-disorder-target",
            "inspire" => "no-inspire-target", _ => "no-rescue-target" };
        return command == "rescue" && home.Soldiers < 3000 ? "rescue-soldiers" : null;
    }
    private string? RequestReason()
    {
        var alliance = view.World.Simulation.Alliance;
        var targets = OtherForces().Where(f => alliance is null ||
            (Owner != alliance.TargetForceId || !alliance.MemberForceIds.Contains(f.Id)) &&
            (!alliance.MemberForceIds.Contains(Owner) || f.Id != alliance.TargetForceId));
        if (!targets.Any()) return "no-target-force";
        int owners = places.Values.Where(s => Present(s) && s.OwnerId != Owner &&
            (alliance?.MemberForceIds.Contains(Owner) != true || s.OwnerId is not int other || !alliance.MemberForceIds.Contains(other)))
            .Select(s => s.OwnerId).Distinct().Count();
        return owners > 1 ? null : "no-target-facility";
    }
}
