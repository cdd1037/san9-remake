using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>A revision-bound preparation draft. Editing never issues commands or changes the world.</summary>
public sealed class DeploymentDraft
{
    public StrategySnapshot View { get; }
    public MilitaryRulebook Rules { get; }
    public SettlementState Home { get; }
    public ForceState Force { get; }
    private readonly TreasureRulebook _treasures;
    public ImmutableArray<int> Candidates { get; }
    public ImmutableArray<int> Members { get; private set; } = [];
    public ImmutableDictionary<int, FormationSlot> Positions { get; private set; } = ImmutableDictionary<int, FormationSlot>.Empty;
    public ImmutableDictionary<int, int?> Tactics { get; private set; } = ImmutableDictionary<int, int?>.Empty;
    public int? CommanderId => Members.IsEmpty ? null : Members[0];
    public int? FormationId { get; private set; }
    public int? NavalFormationId { get; private set; }
    public uint Soldiers { get; private set; }
    public uint Money { get; }
    public long Cost => (FormationId is int land ? Rules.Formations[land].DeploymentCost : 0L) +
        (NavalFormationId is int water ? Rules.Formations[water].DeploymentCost : 0L);
    public int MemberLimit => CandidateFormations(false)
        .Select(f => f.OfficerCapacity).DefaultIfEmpty(0).Max();
    public uint MaximumSoldiers => CommanderId is int commander && FormationId is int land
        ? Math.Min(Home.Soldiers, Math.Min(CommandCapacity(commander), Math.Min(Rules.Formations[land].SoldierCapacity,
            NavalFormationId is int water ? Rules.Formations[water].SoldierCapacity : uint.MaxValue))) : 0;
    public bool CanProceed => !Members.IsEmpty && FormationId is not null && Soldiers > 0 && Soldiers <= MaximumSoldiers && Cost <= Money;

    public static string? UnavailableReasonKey(StrategySnapshot view, int settlementId) =>
        new MapCommandAvailability(view).SettlementReasonKey(settlementId, "sortie");

    public DeploymentDraft(StrategySnapshot view, int settlementId)
    {
        if (UnavailableReasonKey(view, settlementId) is string reason) throw new ArgumentException(reason, nameof(settlementId));
        View = view; Rules = view.MilitaryRules!;
        Home = view.World.Settlements.Single(s => s.Id == settlementId);
        Force = view.World.Forces.Single(f => f.Id == Home.OwnerId);
        var governance = view.Rules.Governance;
        _treasures = view.Rules.Treasures;
        var abilities = view.Officers.ToDictionary(o => o.Id, o => o.Abilities);
        var candidates = view.Officers.Where(o => o.IsAvailable && o.OwnerId == Force.Id && o.SettlementId == Home.Id).Select(o => o.Id).ToHashSet();
        Candidates = governance.OrderCommanders(view.World.Officers.Where(o => candidates.Contains(o.Id)), abilities).Select(o => o.Id).ToImmutableArray();
        Money = view.World.Corps.Single(c => Home.CorpsId is int corps ? c.Id == corps : c.OwnerId == Force.Id && c.IsRoot).Treasury.Money;
    }

    public uint CommandCapacity(int officerId)
    {
        var person = View.World.Officers.Single(o => o.Id == officerId);
        return CommanderCapacity.Read(Force.LeaderId == officerId,
            View.Information.Ranks.FirstOrDefault(r => r.Id == Force.RankId)?.CommandCapacity,
            View.Information.Offices.FirstOrDefault(o => o.Id == person.OfficeId)?.CommandCapacity);
    }

    public bool SetMembers(IEnumerable<int> ids)
    {
        var selected = ids.ToArray();
        if (selected.Length == 0 || selected.Length > MemberLimit || selected.Distinct().Count() != selected.Length || selected.Any(id => !Candidates.Contains(id))) return false;
        var members = Candidates.Where(selected.Contains).ToImmutableArray();
        var land = Formations(false, members);
        if (land.Count == 0) return false;
        int formation = land.Any(f => f.Id == FormationId) ? FormationId!.Value
            : land.FirstOrDefault(f => f.Id == Rules.DefaultDeploymentFormationId)?.Id ?? land[0].Id;
        var water = Formations(true, members).Where(f => Rules.ValidFormationPair(formation, f.Id)).ToArray();
        bool sameFormation = FormationId == formation;
        Members = members; FormationId = formation;
        NavalFormationId = water.FirstOrDefault(f => f.Id == Rules.BasicNavalFormationId)?.Id ?? water.FirstOrDefault()?.Id;
        Tactics = members.ToImmutableDictionary(id => id, id => Tactics.TryGetValue(id, out var selectedTactic) ? selectedTactic :
            AvailableTactics(id).FirstOrDefault(t => t.Id == View.World.Officers.Single(o => o.Id == id).EquippedTacticId)?.Id);
        Arrange(sameFormation); Soldiers = MaximumSoldiers;
        return true;
    }

    public IReadOnlyList<FormationRule> Formations(bool naval) => Members.IsEmpty ? [] : Formations(naval, Members);
    private IEnumerable<FormationRule> CandidateFormations(bool naval) => Rules.Formations.Values.Where(f =>
        f.TravelMode == (naval ? TravelMode.Water : TravelMode.Land) && f.Id != Rules.UnformedFormationId && Rules.CanSelectFormation(Force, f.Id));
    private IReadOnlyList<FormationRule> Formations(bool naval, ImmutableArray<int> members)
    {
        var people = View.World.Officers.Where(o => members.Contains(o.Id)).ToArray();
        return CandidateFormations(naval).Where(f => f.OfficerCapacity >= members.Length &&
            (f.RequiredTacticId is not int tactic || TacticAvailability.HasForMembers(tactic, people, Force, View.World.Treasures, _treasures)))
            .OrderBy(f => f.Id).ToArray();
    }
    public bool SetFormation(int id, bool naval)
    {
        if (!Formations(naval).Any(f => f.Id == id)) return false;
        if (naval)
        {
            if (FormationId is not int land || !Rules.ValidFormationPair(land, id)) return false;
            NavalFormationId = id;
        }
        else
        {
            if (!Rules.ValidFormationPair(id, NavalFormationId)) return false;
            FormationId = id; Arrange(false);
        }
        Soldiers = Math.Min(Soldiers, MaximumSoldiers); return true;
    }
    public bool SetSoldiers(uint value)
    { if (value == 0 || value > MaximumSoldiers) return false; Soldiers = value; return true; }

    public FormationPresentation? Presentation(bool naval) => View.Information.Formations.FirstOrDefault(f => f.Id == (naval ? NavalFormationId : FormationId))?.Presentation;
    public ImmutableArray<FormationSlot> Slots => Presentation(false) is { Slots.IsEmpty: false } presentation ? presentation.Slots : (FormationId is int id
        ? Enumerable.Range(0, Rules.Formations[id].OfficerCapacity).Select(i => new FormationSlot(i, 1)).ToImmutableArray() : []);
    private void Arrange(bool retain)
    {
        var slots = Slots;
        var positions = retain ? Positions.Where(p => Members.Contains(p.Key) && slots.Contains(p.Value)).ToDictionary() : [];
        foreach (int id in Members.Where(id => !positions.ContainsKey(id)))
            positions.Add(id, slots.MinBy(slot => positions.Values.Count(p => p == slot)));
        Positions = positions.ToImmutableDictionary();
    }
    public bool Move(int officerId, FormationSlot destination)
    {
        if (!Positions.TryGetValue(officerId, out var origin) || !Slots.Contains(destination)) return false;
        int? occupant = Positions.Where(p => p.Value == destination).Select(p => (int?)p.Key).FirstOrDefault();
        if (occupant is int other) Positions = Positions.SetItem(other, origin);
        Positions = Positions.SetItem(officerId, destination); return true;
    }
    public IReadOnlyList<TacticRule> AvailableTactics(int officerId)
    {
        var person = View.World.Officers.Single(o => o.Id == officerId);
        var available = TacticAvailability.ForOfficer(person, Force, View.World.Treasures, _treasures);
        return Rules.Tactics.Values.Where(t => t.Selectable && available.ContainsKey(t.Id)).OrderBy(t => t.Id).ToArray();
    }
    public bool SetTactic(int officerId, int? tacticId)
    {
        if (!Members.Contains(officerId) || tacticId is int tactic && !AvailableTactics(officerId).Any(t => t.Id == tactic)) return false;
        Tactics = Tactics.SetItem(officerId, tacticId); return true;
    }
    public FormationBoardData Board(TextCatalog texts) => new(Presentation(false) is { Slots.IsEmpty: false } presentation
        ? presentation : new(Math.Max(1, Slots.Length), 3, Slots, 0, 0, null, null), Members.Select(id =>
        {
            var person = View.Officers.Single(o => o.Id == id);
            return new FormationMemberView(id, texts.Resolve(person.NameKey), Tactics[id] is int tactic
                ? texts.Resolve(View.Information.Tactics.Single(t => t.Id == tactic).NameKey) : "—",
                (BattleRank)Math.Min(2, Positions[id].Row), id == CommanderId)
            {
                Position = Positions[id],
                HoverText = $"武力{person.Abilities.War}、智力{person.Abilities.Intelligence}、兵法数{AvailableTactics(id).Count}"
            };
        }).ToArray());
    public CatalogCell[] Combat(bool naval) => UnitCombatPreview.Read(new(0, Force.Id, new(0, 0), Soldiers)
    { FormationId = FormationId, NavalFormationId = NavalFormationId, Morale = Home.Morale },
        View.Officers.Where(o => Members.Contains(o.Id)), Rules, naval);
}
