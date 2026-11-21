using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class FormationEditorPage
{
    private static GridContainer Section(Node parent, string title, string tooltip = "") =>
        ContentEditorControls.ProfileGrid(parent, title, tooltip: tooltip);

    private void Number(Node parent, string title, double value, double min, double max, Action<double> changed, string name = "")
    {
        ContentEditorControls.Caption(parent, title);
        ContentEditorControls.NumericInput(parent, value, min, max, Guard(changed), name);
    }

    private void Toggle(Node parent, string title, bool value, Action<bool> changed)
    {
        ContentEditorControls.Toggle(parent, title, value, Guard(changed));
    }

    private void BuildBasics(FormationRule f, VBoxContainer page)
    {
        int id = f.Id;
        var catalog = Workspace.GetFormationCatalog(id);
        ContentEditorControls.AddText(page, "描述", catalog.Description, Guard<string>(v => { Workspace.SetFormationCatalog(Workspace.GetFormationCatalog(id) with { Description = v }); RefreshList(); UpdateHeading(); }), true);
        var appearances = Workspace.FormationAppearances;
        if (appearances.Count > 0)
            ContentEditorControls.Choices(page, "部队外观", appearances.Select(a => a.Name).ToArray(),
                appearances.ToList().FindIndex(a => a.MapAppearanceId == catalog.MapAppearanceId),
                Guard<int>(index => Workspace.SetFormationCatalog(Workspace.GetFormationCatalog(id) with { MapAppearanceId = appearances[index].MapAppearanceId })));
        var grid = Section(page, "行军与编制", "距离与占地以细单位计，5单位为1地图格；普通陆地每格基础消耗50移动力。");
        Number(grid, "每日移动力", f.MovementBudget, 1, int.MaxValue, v => Edit(id, p => p with { MovementBudget = (int)v }), "FormationMovement");
        Number(grid, "出征费用", f.DeploymentCost, 0, int.MaxValue, v => Edit(id, p => p with { DeploymentCost = (int)v }), "FormationCost");
        Number(grid, "兵力上限", f.SoldierCapacity, 1, uint.MaxValue, v => Edit(id, p => p with { SoldierCapacity = (uint)v }), "FormationSoldiers");
        grid.AddChild(new Label { Text = "武将上限" });
        grid.AddChild(new Label { Text = f.OfficerCapacity.ToString() });
        Number(grid, "基础占地宽", f.Footprint.Width, 1, int.MaxValue, v => Edit(id, p => p with { Footprint = p.Footprint with { Width = (int)v } }));
        Number(grid, "基础占地高", f.Footprint.Height, 1, int.MaxValue, v => Edit(id, p => p with { Footprint = p.Footprint with { Height = (int)v } }));
        grid.AddChild(new Label { Text = "行军方式" });
        var travel = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill }; travel.AddItem("陆上"); travel.AddItem("水上"); travel.Select((int)f.TravelMode);
        var choose = Guard<long>(v => Edit(id, p => p with { TravelMode = (TravelMode)v })); travel.ItemSelected += v => choose(v); grid.AddChild(travel);
        var flags = new HBoxContainer(); flags.AddThemeConstantOverride("separation", 16); page.AddChild(flags);
        Toggle(flags, "通用阵形", f.IsCommon, v => Edit(id, p => p with { IsCommon = v }));
        Toggle(flags, "提供视野", f.Vision is not null, v => Edit(id, p => p with { Vision = v ? p.Vision ?? new(15, 180) : null }, true));
        if (f.Vision is { } vision)
        {
            var view = Section(page, "视野");
            Number(view, "视野半径", vision.Radius, 0, int.MaxValue, v => Edit(id, p => p with { Vision = p.Vision! with { Radius = (int)v } }));
            Number(view, "视野角度", vision.ArcDegrees, 0, 360, v => Edit(id, p => p with { Vision = p.Vision! with { ArcDegrees = (int)v } }));
        }
    }

    private void BuildCombat(FormationRule f, VBoxContainer page)
    {
        int id = f.Id;
        Toggle(page, "启用战斗系数", f.Combat is not null, v => Edit(id, p => p with { Combat = v ? p.Combat ?? new(10, 10, 10, 10, 50) : null }, true));
        if (f.Combat is { } combat)
        {
            var grid = Section(page, "攻防系数");
            Number(grid, "对部队攻击", combat.AgainstUnits, 0, int.MaxValue, v => Edit(id, p => p with { Combat = p.Combat! with { AgainstUnits = (int)v } }), "FormationAttack");
            Number(grid, "对守兵攻击", combat.AgainstGarrison, 0, int.MaxValue, v => Edit(id, p => p with { Combat = p.Combat! with { AgainstGarrison = (int)v } }));
            Number(grid, "对城墙攻击", combat.AgainstWalls, 0, int.MaxValue, v => Edit(id, p => p with { Combat = p.Combat! with { AgainstWalls = (int)v } }));
            Number(grid, "防御系数", combat.Defense, 0, int.MaxValue, v => Edit(id, p => p with { Combat = p.Combat! with { Defense = (int)v } }), "FormationDefense");
            Number(grid, "伤兵比例 %", combat.WoundedPercent, 0, 100, v => Edit(id, p => p with { Combat = p.Combat! with { WoundedPercent = (int)v } }));
        }
        Toggle(page, "启用普通攻击", f.Attack is not null, v => Edit(id, p => p with { Attack = v ? p.Attack ?? new(10, false) : null }, true));
        if (f.Attack is { } attack)
        {
            var grid = Section(page, "普通攻击");
            Number(grid, "攻击距离", attack.Range, 1, int.MaxValue, v => Edit(id, p => p with { Attack = p.Attack! with { Range = (int)v } }));
            Number(grid, "每级高差加成", attack.ElevationBonusPerLevel, 0, int.MaxValue, v => Edit(id, p => p with { Attack = p.Attack! with { ElevationBonusPerLevel = (int)v } }));
            Number(grid, "高差加成上限", attack.MaximumElevationBonus, 0, int.MaxValue, v => Edit(id, p => p with { Attack = p.Attack! with { MaximumElevationBonus = (int)v } }));
            var checks = ContentEditorControls.ButtonGroup(page);
            Toggle(checks, "可以削减城墙耐久", attack.DamagesWalls, v => Edit(id, p => p with { Attack = p.Attack! with { DamagesWalls = v } }));
            Toggle(checks, "攻击要求水域接触", attack.RequiresWaterContact, v => Edit(id, p => p with { Attack = p.Attack! with { RequiresWaterContact = v } }));
            Toggle(checks, "远距攻城", attack.RangedSiege, v => Edit(id, p => p with { Attack = p.Attack! with { RangedSiege = v } }));
        }
    }

    private void BuildTactics(FormationRule f, VBoxContainer page)
    {
        int id = f.Id;
        Toggle(page, "启用兵法发动配置", f.Tactics is not null, v => Edit(id, p => p with
        { Tactics = v ? p.Tactics ?? new(15, 2, [], []) : null }, true));
        if (f.Tactics is { } tactics)
        {
            var grid = Section(page, "兵法机会与联动");
            Number(grid, "个人发动机会 %", tactics.OpportunityChance, 0, 100, v => Edit(id, p => p with { Tactics = p.Tactics! with { OpportunityChance = (int)v } }));
            Number(grid, "联动倍率", tactics.LinkMultiplier, 1, int.MaxValue, v => Edit(id, p => p with { Tactics = p.Tactics! with { LinkMultiplier = (int)v } }));
            if (tactics.AutomaticOpportunityChance is int chance)
                Number(grid, "自动兵法机会 %", chance, 0, 100, v => Edit(id, p => p with { Tactics = p.Tactics! with { AutomaticOpportunityChance = (int)v } }));
            var favored = ContentEditorControls.ButtonGroup(page, "优势兵法类别");
            var categories = TacticCategoryLabels.Names;
            for (int category = 0; category < categories.Length; category++)
            {
                int selected = category;
                if (!tactics.FavoredCategories.Contains(selected) &&
                    !Workspace.Tactics.Any(t => t.Effect == TacticEffect.Martial && t.Category == selected)) continue;
                Toggle(favored, categories[selected], tactics.FavoredCategories.Contains(selected), enabled => Edit(id, p => p with
                { Tactics = p.Tactics! with { FavoredCategories = enabled
                    ? p.Tactics.FavoredCategories.Add(selected).Distinct().Order().ToImmutableArray()
                    : p.Tactics.FavoredCategories.Remove(selected) } }));
            }
        }
        var resistance = Section(page, "抗性与狙击");
        Number(resistance, "混乱抵抗 %", f.ConfusionResistanceChance, 0, 100, v => Edit(id, p => p with { ConfusionResistanceChance = (int)v }));
        Number(resistance, "狙击机会加成", f.SnipingChanceBonus, 0, 100, v => Edit(id, p => p with { SnipingChanceBonus = (int)v }));
        var flags = ContentEditorControls.ButtonGroup(page, "阵形特性");
        Toggle(flags, "忽略被动反制", f.BypassesPassiveCounters, v => Edit(id, p => p with { BypassesPassiveCounters = v }));
        Toggle(flags, "俘获效果翻倍", f.DoublesCapture, v => Edit(id, p => p with { DoublesCapture = v }));
        Toggle(flags, "阻止兵法救援", f.PreventsTacticRescue, v => Edit(id, p => p with { PreventsTacticRescue = v }));
        Toggle(flags, "近战阵亡按行位计算", f.MeleeDeathUsesRanks, v => Edit(id, p => p with { MeleeDeathUsesRanks = v }));
        Toggle(flags, "单挑机会翻倍", f.DoublesDuelChance, v => Edit(id, p => p with { DoublesDuelChance = v }));
        Toggle(flags, "每日获得战斗熟练度", f.GainsDailyCombatProficiency, v => Edit(id, p => p with { GainsDailyCombatProficiency = v }));
        Toggle(flags, "受到的敌方计略机会减半", f.HalvesHostileStrategyChance, v => Edit(id, p => p with { HalvesHostileStrategyChance = v }));
        Toggle(flags, "免疫扰乱", f.ImmuneToDisorder, v => Edit(id, p => p with { ImmuneToDisorder = v }));
    }

    private void BuildAdvanced(FormationRule f, VBoxContainer page)
    {
        int id = f.Id;
        page.AddChild(new Label { Text = "拖动阵位调整位置" });
        _positionEditor = new FormationEditorPreview
        {
            Name = "FormationPositionEditor", Editable = true, Rule = f,
            CustomMinimumSize = new(0, 270), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PositionMoved = (index, destination) =>
            {
                if (_rawDirty && !CommitJson()) return;
                Edit(id, p =>
                {
                    var positions = p.Positions;
                    int occupied = positions.IndexOf(destination);
                    if (occupied >= 0) positions = positions.SetItem(occupied, positions[index]);
                    return p with { Positions = positions.SetItem(index, destination) };
                });
            }
        };
        page.AddChild(_positionEditor);
        page.AddChild(new Label { Text = "完整数据（JSON）" });
        _json = new TextEdit { Text = JsonSerializer.Serialize(f, EditorJson), CustomMinimumSize = new(0, 340), SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = "FormationJson" };
        var field = _json; field.TooltipText = "阵形 ID、阵位数量和武将上限不可修改。";
        field.TextChanged += () => { if (!_building) { _rawDirty = true; UpdateHeading(); } }; page.AddChild(field);
        var apply = Guard<bool>(_ => { if (CommitJson()) { ShowCurrent(); Status("已应用，尚未保存。"); } });
        Button(page, "应用数据", () => apply(true));
    }
}
