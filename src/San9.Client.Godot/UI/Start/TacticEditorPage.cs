using System.Collections.Immutable;
using Godot;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class TacticEditorPage : SharedCatalogEditorPage
{
    private SharedCatalogActions _actions = null!;
    public override bool HasPendingChanges => _actions.Busy;
    public override bool CommitPending() => !_actions.Busy;
    protected override void BuildSidebarActions(VBoxContainer sidebar)
    {
        _actions = new()
        {
            Page = this, Subject = "兵法", Identity = "Tactic", Current = () => Current,
            CanDelete = Workspace.IsCreatedTactic, EntryName = id => Workspace.GetTacticCatalog(id).Name,
            Create = async (id, token) => (await Workspace.CreateTacticAsync(id, token)).Id,
            Delete = Workspace.DeleteTacticAsync, Select = SelectEntry,
            SuccessMessage = "已更新共享兵法草稿，尚未保存；新增兵法不会自动授予武将。"
        };
        sidebar.AddChild(_actions);
    }
    protected override void RefreshActions() => _actions.Refresh();
    protected override string Subject => "兵法";
    protected override IEnumerable<(int Id, string Name)> Entries => Workspace.Tactics.Select(t => (t.Id, Workspace.GetTacticCatalog(t.Id).Name));
    protected override Action<int, string> Rename => (id, value) => Workspace.SetTacticCatalog(Workspace.GetTacticCatalog(id) with { Name = value });
    protected override bool Changed(int id) => Workspace.IsTacticChanged(id);
    protected override void Restore(int id) => Workspace.RestoreTactic(id);
    private void Edit(int id, Func<TacticRule, TacticRule> change) => Workspace.SetTactic(change(Workspace.GetTactic(id)));
    protected override void BuildForm(int id)
    {
        var t = Workspace.GetTactic(id); var catalog = Workspace.GetTacticCatalog(id);
        TextField("描述", catalog.Description, v => Workspace.SetTacticCatalog(Workspace.GetTacticCatalog(id) with { Description = v }), true);
        Form.AddChild(new Label { Text = "效果：" + EffectLabel(t.Effect) });
        var appearances = Workspace.TacticAppearances(t.Effect);
        Choice(Form, "战斗表现", new[] { "无" }.Concat(appearances.Select(a => a.Name)).ToArray(),
            appearances.ToList().FindIndex(a => a.BattleAppearanceId == catalog.BattleAppearanceId) + 1,
            index => Workspace.SetTacticCatalog(Workspace.GetTacticCatalog(id) with { BattleAppearanceId = index == 0 ? null : appearances[index - 1].BattleAppearanceId }));
        var basics = Section("基本参数");
        if (t.Effect != TacticEffect.FacilityAttack)
            Choice(basics, "熟练类别", TacticCategoryLabels.Names, t.Category ?? 0, v => Edit(id, x => x with { Category = v }));
        Number(basics, "威力／效果参数", t.Power, 0, MaximumPower(t.Effect), v => Edit(id, x => x with { Power = (int)v }));
        Number(basics, "最小发动间隔", t.MinimumInterval, 0, int.MaxValue, v => Edit(id, x => x with { MinimumInterval = (int)v }));
        Number(basics, "学习熟练度门槛", t.TrainingRequirement, 0, int.MaxValue, v => Edit(id, x => x with { TrainingRequirement = (int)v }));
        Number(basics, "作用半径", t.EffectRadius, 0, int.MaxValue, v => Edit(id, x => x with { EffectRadius = (int)v }));
        var flags = Options("使用资格");
        if (t.Effect is TacticEffect.Martial or TacticEffect.Confusion or TacticEffect.TrapSelection or TacticEffect.Defection or TacticEffect.Illusion or TacticEffect.Taunt or TacticEffect.Inspire or TacticEffect.Heal or TacticEffect.Sorcery or TacticEffect.CounterConfusion or TacticEffect.CounterTrap)
            Toggle(flags, "允许武将预选", t.Selectable, v => Edit(id, x => x with { Selectable = v }));
        Toggle(flags, "可作用于设施", t.AffectsFacilities, v => Edit(id, x => x with { AffectsFacilities = v }));
        if (t.Effect == TacticEffect.Martial)
        {
            Toggle(flags, "仅限水域部队目标", t.RequiresWaterTarget, v => Edit(id, x => x with { RequiresWaterTarget = v }));
            Toggle(flags, "可解救俘虏", t.CanRescuePrisoners, v => Edit(id, x => x with { CanRescuePrisoners = v }));
            Toggle(flags, "限制近距发动", t.ContactRange is not null, v => { Edit(id, x => x with { ContactRange = v ? 5 : null }); Rebuild(); });
            if (t.ContactRange is int range) Number(basics, "近距范围", range, 1, int.MaxValue, v => Edit(id, x => x with { ContactRange = (int)v }));
            Choice(basics, "狙击熟练类别", new[] { "不进行狙击" }.Concat(TacticCategoryLabels.Names).ToArray(), (t.SnipingProficiencyCategory ?? -1) + 1,
                v => Edit(id, x => x with { SnipingProficiencyCategory = v == 0 ? null : v - 1 }));
        }
        if (t.FacilityAttack is { } attack)
        {
            Number(basics, "设施射程加成", attack.RangeBonus, 0, int.MaxValue, v => Edit(id, x => x with { FacilityAttack = x.FacilityAttack! with { RangeBonus = (int)v } }));
            Toggle(flags, "使用落石威力", attack.UsesRockfallPower, v => Edit(id, x => x with { FacilityAttack = x.FacilityAttack! with { UsesRockfallPower = v } }));
        }
        var compatible = Options("适配阵形与船型");
        foreach (var formation in Workspace.Formations)
        {
            int f = formation.Id;
            Toggle(compatible, Workspace.GetFormationCatalog(f).Name, t.CompatibleFormations.Contains(f), v => Edit(id, x => x with
            { CompatibleFormations = v ? x.CompatibleFormations.Append(f).Distinct().Order().ToImmutableArray() : x.CompatibleFormations.Where(a => a != f).ToImmutableArray() }));
        }
    }
    private static int MaximumPower(TacticEffect effect) => effect switch
    { TacticEffect.Taunt => 9, TacticEffect.Defection => 29, TacticEffect.Illusion or TacticEffect.Sorcery => 32,
        TacticEffect.Trap or TacticEffect.Heal => 19, TacticEffect.Inspire => 14, _ => int.MaxValue };
    private static string EffectLabel(TacticEffect effect) => effect switch
    {
        TacticEffect.Martial => "武力", TacticEffect.FormationTechnology => "阵形技术", TacticEffect.Construction => "造营", TacticEffect.StoneFormation => "石兵",
        TacticEffect.CounterConfusion => "教谕", TacticEffect.CounterTrap => "破陷", TacticEffect.Confusion => "混乱", TacticEffect.TrapSelection => "陷阱选择",
        TacticEffect.Trap => "具体陷阱", TacticEffect.Defection => "攻心", TacticEffect.Illusion => "幻术", TacticEffect.Taunt => "辱骂",
        TacticEffect.Inspire => "鼓舞", TacticEffect.Heal => "治疗", TacticEffect.Sorcery => "妖术", TacticEffect.FacilityAttack => "设施攻击", _ => throw new ArgumentOutOfRangeException(nameof(effect))
    };
}
