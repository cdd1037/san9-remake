using System.Collections.Immutable;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum SpecialDeploymentKind { Construction, Transport }

/// <summary>Preparation for fixed-formation units. No world state is changed before submission.</summary>
public sealed class SpecialDeploymentDraft
{
    public StrategySnapshot View { get; }
    public MilitaryRulebook Rules { get; }
    public SpecialDeploymentKind Kind { get; }
    public SettlementState Home { get; }
    public ForceState Force { get; }
    public int FormationId { get; }
    public int? NavalFormationId { get; }
    public ImmutableArray<int> Candidates { get; }
    public ImmutableArray<int> Members { get; private set; } = [];
    public int? CommanderId => Members.IsEmpty ? null : Members[0];
    public int? FacilityId { get; private set; }
    public uint Soldiers { get; private set; }
    public uint Money { get; }
    private readonly TreasureRulebook _treasures;
    private readonly Dictionary<int, ImmutableArray<ConstructionSite>> _sites = [];
    public int MemberLimit => Kind == SpecialDeploymentKind.Transport ? 1 : Math.Min(Rules.Formations[FormationId].OfficerCapacity, NavalFormationId is int naval ? Rules.Formations[naval].OfficerCapacity : int.MaxValue);
    public uint MaximumSoldiers => CommanderId is int commander ? Math.Min(Home.Soldiers,
        Math.Min(CommandCapacity(commander), Math.Min(Rules.Formations[FormationId].SoldierCapacity, NavalFormationId is int naval ? Rules.Formations[naval].SoldierCapacity : uint.MaxValue))) : 0;
    public int Cost => FacilityId is int id ? FacilityCost(id) : 0;
    public bool CanProceed => !Members.IsEmpty && Soldiers > 0 && Soldiers <= MaximumSoldiers &&
        (Kind == SpecialDeploymentKind.Transport || FacilityId is int id && AvailableFacilities().Any(f => f.Id == id));
    public string Title => Kind == SpecialDeploymentKind.Construction ? "建设" : "输送";

    public static string? UnavailableReasonKey(StrategySnapshot view, int home, SpecialDeploymentKind kind) =>
        new MapCommandAvailability(view).SettlementReasonKey(home, kind == SpecialDeploymentKind.Construction ? "build" : "transport");

    public SpecialDeploymentDraft(StrategySnapshot view, int home, SpecialDeploymentKind kind)
    {
        View = view; Rules = view.MilitaryRules!; Kind = kind;
        Home = view.World.Settlements.Single(s => s.Id == home);
        Force = view.World.Forces.Single(f => f.Id == Home.OwnerId);
        var pair = (kind == SpecialDeploymentKind.Construction ? Rules.ConstructionFormations : Rules.TransportFormations)
            ?? throw new InvalidOperationException("Missing fixed deployment formations.");
        FormationId = pair.LandFormationId; NavalFormationId = pair.NavalFormationId;
        _treasures = new(view.Information.Treasures.Select(t => t.ToRule()));
        var governance = new GovernanceRulebook(view.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)));
        var eligible = view.Officers.Where(o => o.IsAvailable && o.OwnerId == Force.Id && o.SettlementId == home).Select(o => o.Id).ToHashSet();
        Candidates = governance.OrderCommanders(view.World.Officers.Where(o => eligible.Contains(o.Id)),
            view.Officers.ToDictionary(o => o.Id, o => o.Abilities)).Select(o => o.Id).ToImmutableArray();
        Money = view.World.Corps.Single(c => Home.CorpsId is int corps ? c.Id == corps : c.OwnerId == Force.Id && c.IsRoot).Treasury.Money;
        if (Candidates.Length == 1) SetMembers(Candidates);
    }
    public uint CommandCapacity(int id) => CommanderCapacity.Read(Force.LeaderId == id,
        View.Information.Ranks.FirstOrDefault(r => r.Id == Force.RankId)?.CommandCapacity,
        View.Information.Offices.FirstOrDefault(o => o.Id == View.World.Officers.Single(p => p.Id == id).OfficeId)?.CommandCapacity);
    public bool SetMembers(IEnumerable<int> ids)
    {
        var chosen = ids.ToArray();
        if (chosen.Length == 0 || chosen.Length > MemberLimit || chosen.Distinct().Count() != chosen.Length || chosen.Any(id => !Candidates.Contains(id))) return false;
        Members = Candidates.Where(chosen.Contains).ToImmutableArray(); Soldiers = MaximumSoldiers;
        if (FacilityId is int type && !AvailableFacilities().Any(f => f.Id == type)) FacilityId = null;
        return true;
    }
    public bool SetSoldiers(uint value)
    { if (value == 0 || value > MaximumSoldiers) return false; Soldiers = value; return true; }
    public ImmutableArray<ConstructionSite> Sites(int facility)
    {
        if (!_sites.TryGetValue(facility, out var sites)) _sites.Add(facility, sites = ConstructionLocations.Read(View, Force.Id, facility));
        return sites;
    }
    private bool HasTactic(int tactic, IEnumerable<int> members) => TacticAvailability.HasForMembers(tactic,
        View.World.Officers.Where(o => members.Contains(o.Id)), Force, View.World.Treasures, _treasures);
    public int FacilityCost(int id) => Rules.Facilities[id].Cost(Rules.Facilities[id].CostReductionTacticId is int tactic
        ? Members.Count(member => HasTactic(tactic, [member])) : 0);
    public IEnumerable<FacilityRule> AvailableFacilities() => Members.IsEmpty ? [] : Rules.Facilities.Values
        .Where(f => f.Buildable && (f.RequiredTacticId is not int tactic || HasTactic(tactic, Members)) &&
            FacilityCost(f.Id) <= Money && !Sites(f.Id).IsEmpty).OrderBy(f => f.Id);
    public bool SetFacility(int id)
    { if (!AvailableFacilities().Any(f => f.Id == id)) return false; FacilityId = id; return true; }
    public string FacilityName(int id, TextCatalog texts) => View.FacilityNameSuffixKeys.TryGetValue(id, out string? key) ? texts.Resolve(key) : id.ToString();
}
