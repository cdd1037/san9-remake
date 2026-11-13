using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>A revision-bound preparation draft. Editing never issues commands or changes the world.</summary>
public sealed partial class DeploymentDraft
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
    public int? NavalTrainingId { get; private set; }
    public int? SiegeEquipmentId { get; private set; }
    public bool CanUseSiegeEquipment(int? id)
    {
        if (!View.World.Simulation.NewGame.MixedTroopsEnabled) return false;
        if (id is int selected && (!Rules.SiegeEquipment.TryGetValue(selected, out var equipment) ||
            !Force.CompletedResearch.Contains(equipment.ResearchProjectId) ||
            Rules.Formations[equipment.FormationId].RequiredTacticId is int tactic &&
            !TacticAvailability.HasForMembers(tactic, View.World.Officers.Where(o => Members.Contains(o.Id)), Force, View.World.Treasures, _treasures, View.EquippedItems))) return false;
        return true;
    }
    public bool SetSiegeEquipment(int? id)
    {
        if (!CanUseSiegeEquipment(id)) return false;
        SiegeEquipmentId = id;
        return true;
    }
    public bool SetNavalTraining(int id)
    {
        if (!View.World.Simulation.NewGame.MixedTroopsEnabled || !Rules.NavalTraining.TryGetValue(id, out var training) ||
            training.ResearchProjectId is string project && !Force.CompletedResearch.Contains(project)) return false;
        NavalTrainingId = id;
        return true;
    }
    private ImmutableDictionary<int, DetachmentState> _detachments = ImmutableDictionary<int, DetachmentState>.Empty;
    public ImmutableDictionary<int, DetachmentState> Detachments
    {
        get
        {
            if (!View.World.Simulation.NewGame.MixedTroopsEnabled) return ImmutableDictionary<int, DetachmentState>.Empty;
            if (_detachments.Count != Members.Length || Members.Any(id => !_detachments.ContainsKey(id)))
            {
                var defaults = DetachmentAllocation.CreateDefault(Members, Tactics, Soldiers, Force, Rules);
                if (defaults.IsEmpty) return defaults;
                var updated = defaults.ToBuilder();
                foreach (int member in Members)
                    if (_detachments.TryGetValue(member, out var previous)) updated[member] = previous;
                _detachments = ClearChangedArmament(DetachmentAllocation.Resize(updated.ToImmutable(), Soldiers));
            }
            else if (!_detachments.IsEmpty && _detachments.Values.Aggregate(0UL, (sum, d) => sum + d.Soldiers) != Soldiers)
                _detachments = ClearChangedArmament(DetachmentAllocation.Resize(_detachments, Soldiers));
            return _detachments;
        }
    }
    private static ImmutableDictionary<int, DetachmentState> ClearChangedArmament(ImmutableDictionary<int, DetachmentState> detachments) =>
        detachments.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
        {
            Weapon = pair.Value.Weapon?.InitialSoldiers == pair.Value.Soldiers ? pair.Value.Weapon : null,
            Armor = pair.Value.Armor?.InitialSoldiers == pair.Value.Soldiers ? pair.Value.Armor : null
        });
    public bool SetDetachments(ImmutableDictionary<int, DetachmentState> value)
    {
        if (!View.World.Simulation.NewGame.MixedTroopsEnabled || value.Count != Members.Length ||
            value.Any(pair => !Members.Contains(pair.Key) || pair.Value is null || pair.Value.Soldiers == 0 ||
                !Rules.TroopTypes.TryGetValue(pair.Value.TroopTypeId, out var type) || pair.Value.Level <= 0 ||
                pair.Value.Level > type.MaximumLevel || type.LevelResearchProjects.Any(project =>
                    project.Key <= pair.Value.Level && !Force.CompletedResearch.Contains(project.Value))) ||
            value.Values.Aggregate(0UL, (sum, d) => sum + d.Soldiers) != Soldiers) return false;
        _detachments = value;
        ClearIncompatibleDetachmentTactics();
        return true;
    }
    public int? CommanderId => Members.IsEmpty ? null : Members[0];
    public int? FormationId { get; private set; }
    public int? NavalFormationId { get; private set; }
    public uint Soldiers { get; private set; }
    public uint Money { get; }
    public long Cost => (FormationId is int land ? Rules.Formations[land].DeploymentCost : 0L) +
        (NavalFormationId is int water ? Rules.Formations[water].DeploymentCost : 0L) +
        Rules.DetachmentEquipmentCost(Detachments.Values) +
        (SiegeEquipmentId is int siege ? Rules.SiegeEquipment[siege].DeploymentCost : 0L) +
        (NavalFormationId is not null && NavalTrainingId is int training && Rules.NavalTraining.TryGetValue(training, out var definition)
            ? definition.DeploymentCost : 0L);
    public int MemberLimit => CandidateFormations(false)
        .Select(f => f.OfficerCapacity).DefaultIfEmpty(0).Max();
    public uint MaximumSoldiers => CommanderId is int commander && FormationId is int land
        ? Math.Min(Home.Soldiers, Math.Min(CommandCapacity(commander), Math.Min(Rules.Formations[land].SoldierCapacity,
            NavalFormationId is int water ? Rules.Formations[water].SoldierCapacity : uint.MaxValue))) : 0;
    public bool CanProceed => !Members.IsEmpty && FormationId is not null && Soldiers > 0 && Soldiers <= MaximumSoldiers && Cost <= Money &&
        (!View.World.Simulation.NewGame.MixedTroopsEnabled || Detachments.Count == Members.Length && Detachments.Values.All(d => d.Soldiers > 0));

    public static string? UnavailableReasonKey(StrategySnapshot view, int settlementId) =>
        new MapCommandAvailability(view).SettlementReasonKey(settlementId, "sortie");

    public DeploymentDraft(StrategySnapshot view, int settlementId)
    {
        if (UnavailableReasonKey(view, settlementId) is string reason) throw new ArgumentException(reason, nameof(settlementId));
        View = view; Rules = view.MilitaryRules!;
        Home = view.World.Settlements.Single(s => s.Id == settlementId);
        Force = view.World.Forces.Single(f => f.Id == Home.OwnerId);
        if (view.World.Simulation.NewGame.MixedTroopsEnabled)
            NavalTrainingId = Rules.NavalTraining.Values.Where(t => t.ResearchProjectId is null)
                .OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefault();
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
        ClearIncompatibleDetachmentTactics();
        return true;
    }

    public IReadOnlyList<FormationRule> Formations(bool naval) => Members.IsEmpty ? [] : Formations(naval, Members);
    private IEnumerable<FormationRule> CandidateFormations(bool naval) => Rules.Formations.Values.Where(f =>
        f.TravelMode == (naval ? TravelMode.Water : TravelMode.Land) && f.Id != Rules.UnformedFormationId &&
        (!naval && View.World.Simulation.NewGame.FormationKnowledgeEnabled ? !Rules.IsFixedFormation(f.Id) : Rules.CanSelectFormation(Force, f.Id)) &&
        (!f.MixedTroopsOnly || View.World.Simulation.NewGame.FormationKnowledgeEnabled) &&
        (!View.World.Simulation.NewGame.MixedTroopsEnabled || !Rules.SiegeEquipment.Values.Any(item => item.FormationId == f.Id)));
    private IReadOnlyList<FormationRule> Formations(bool naval, ImmutableArray<int> members)
    {
        var people = View.World.Officers.Where(o => members.Contains(o.Id)).ToArray();
        var commander = View.World.Officers.Single(o => o.Id == members[0]);
        return CandidateFormations(naval).Where(f => f.OfficerCapacity >= members.Length &&
            (!View.World.Simulation.NewGame.FormationKnowledgeEnabled ||
                (!naval && commander.FormationProficiencies is not null
                    ? FormationKnowledge.Knows(commander, f.Id)
                    : Rules.CanSelectFormation(Force, f.Id) && (f.ResearchProjectId is null || Force.CompletedResearch.Contains(f.ResearchProjectId)))) &&
            (View.World.Simulation.NewGame.FormationKnowledgeEnabled && !naval && commander.FormationProficiencies is not null ||
                f.RequiredTacticId is not int tactic || TacticAvailability.HasForMembers(tactic, people, Force, View.World.Treasures, _treasures, View.EquippedItems)))
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
            if (View.Companion.ConfigureTacticsOnFormationSelection) ConfigureTactics();
        }
        Soldiers = Math.Min(Soldiers, MaximumSoldiers); return true;
    }
    public bool SetSoldiers(uint value)
    { if (value == 0 || value > MaximumSoldiers) return false; Soldiers = value; return true; }

    public FormationPresentation? Presentation(bool naval) => View.Information.Formations.FirstOrDefault(f => f.Id == (naval ? NavalFormationId : FormationId))?.Presentation;
    public ImmutableArray<FormationSlot> Slots => FormationId is int id ? FormationSlots(View, id) : [];
    internal static ImmutableArray<FormationSlot> FormationSlots(StrategySnapshot view, int id) =>
        view.MilitaryRules!.Formations[id].Positions is { IsEmpty: false } positions
            ? positions.Select(p => new FormationSlot(p.Column, p.Row)).ToImmutableArray()
            : Enumerable.Range(0, view.MilitaryRules!.Formations[id].OfficerCapacity).Select(i => new FormationSlot(i, 1)).ToImmutableArray();
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
        var available = TacticAvailability.ForOfficer(person, Force, View.World.Treasures, _treasures, View.EquippedItems);
        return Rules.Tactics.Values.Where(t => t.Selectable && available.ContainsKey(t.Id)).OrderBy(t => t.Id).ToArray();
    }
    public bool SetTactic(int officerId, int? tacticId)
    {
        if (!Members.Contains(officerId) || tacticId is int tactic && !SelectableTactics(officerId).Any(t => t.Id == tactic)) return false;
        Tactics = Tactics.SetItem(officerId, tacticId); return true;
    }
    public FormationBoardData Board(TextCatalog texts) => new(Presentation(false) is { Slots.IsEmpty: false } presentation
        ? presentation : new(Math.Max(1, Slots.Length), 3, Slots, 0, 0, null, null), Members.Select(id =>
        {
            var person = View.Officers.Single(o => o.Id == id);
            bool compatible = IsTacticCompatible(id);
            return new FormationMemberView(id, texts.Resolve(person.NameKey), Tactics[id] is int tactic
                ? View.Information.Tactics.Single(t => t.Id == tactic).Name : "—",
                Positions[id], id == CommanderId)
            {
                TacticAvailable = Tactics[id] is null || compatible,
                HoverText = $"武力{person.Abilities.War}、智力{person.Abilities.Intelligence}、兵法数{AvailableTactics(id).Count}" +
                    (Tactics[id] is null ? "\n尚未配置兵法" : compatible ? "" :
                        !FormationSupportsTactic(Rules.Tactics[Tactics[id]!.Value]) ? "\n当前兵法不适配陆上阵形" : "\n当前兵种或等级无法发动该兵法") +
                    (View.World.Simulation.NewGame.MixedTroopsEnabled && FormationId is int formation
                        ? View.World.Officers.Single(o => o.Id == id).FormationProficiencies?.TryGetValue(formation, out int proficiency) == true
                            ? $"\n阵形熟练 {proficiency}" : "\n未掌握此阵形"
                        : "")
            };
        }).ToArray());
    public CatalogCell[] Combat(bool naval) => UnitCombatPreview.Read(new(0, Force.Id, new(0, 0), Soldiers)
    { FormationId = FormationId, NavalFormationId = NavalFormationId, Morale = Home.Morale },
        View.Officers.Where(o => Members.Contains(o.Id)), Rules, naval);
}
