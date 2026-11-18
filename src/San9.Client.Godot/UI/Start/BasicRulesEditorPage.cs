using Godot;

namespace San9.Client.Godot;

public partial class BasicRulesEditorPage : SharedDataEditorPage
{
    private readonly List<Action> _refresh = [];
    private TacticLinkEditor? _links;
    public override bool HasPendingChanges => _links?.HasPendingChanges == true;
    public override bool CommitPending() => !HasPendingChanges;
    public override void RefreshAfterSave() { foreach (var refresh in _refresh) refresh(); }
    public override void Activate() { _links?.Refresh(); }

    public override void _Ready()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll); scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var body = ContentEditorControls.PageBody(scroll);
        var growth = ContentEditorControls.ProfileGrid(body, "武将成长");
        Number(growth, "兵法熟练度上限", () => Workspace.Definitions.OfficerGrowth.TacticProficiencyCap,
            1, int.MaxValue, Workspace.SetTacticProficiencyCap, "TacticProficiencyCap",
            "九类兵法共用，默认1000。成长、武将编辑和熟练度全满按钮使用此值；降低上限不扣减已有熟练度。");
        if (Workspace.Definitions.Military is { } military)
        {
            var tactics = ContentEditorControls.ProfileGrid(body, "兵法与联携");
            Number(tactics, "防御方兵法机会基础概率 %", () => Workspace.Definitions.Military!.DefensiveTacticChance,
                0, 100, Workspace.SetDefensiveTacticChance, "DefensiveTacticChance",
                "部队和设施在防御方获得兵法机会前的额外概率门，原版内容为50%。通过后仍须满足发动条件；伴侣的士气修正会在此值上增减。");
            Number(tactics, "血缘联携加成（百分点）", () => Workspace.Definitions.Military!.LinkRules.KinshipBonus,
                0, 100, Workspace.SetKinshipLinkBonus, "KinshipLinkBonus",
                "父子及符合条件的兄弟关系使用此加成，作用于普通联携和相关跨类别带动。须启用关系规则并采用史实关系；不改变人物配对表或相性公式。");
            if (!military.LinkRules.RelationshipsEnabled)
                Note(body, "当前内容未启用关系加成，血缘及配对联携加成暂不生效。");
            if (military.CityInfluence is not null)
            {
                var city = ContentEditorControls.ProfileGrid(body, "都市地利");
                Number(city, "都市地利范围（地图格）", () => Workspace.Definitions.Military!.CityInfluence!.RadiusCells,
                    0, int.MaxValue, value => Workspace.SetCityInfluence(Workspace.Definitions.Military!.CityInfluence! with { RadiusCells = value }),
                    "CityInfluenceRadius", "从己方都市覆盖格向外扩散，受建筑和障碍阻隔。原版内容为8格；0仅保留都市自身覆盖格。");
                Number(city, "陆军兵法机会加成（百分点）", () => Workspace.Definitions.Military!.CityInfluence!.LandTacticBonus,
                    0, 100, value => Workspace.SetCityInfluence(Workspace.Definitions.Military!.CityInfluence! with { LandTacticBonus = value }),
                    "CityInfluenceTacticBonus", "己方都市地利范围内，陆军部队的兵法机会增加此值，原版内容为10个百分点。仍须通过其他资格，不增加水军或设施的机会。");
            }
            else Note(body, "当前内容未声明都市地利规则。");
            if (!military.Facilities.IsEmpty)
            {
                var harbor = ContentEditorControls.Section(body, "设施类型");
                var facilities = military.Facilities.OrderBy(f => f.Id).ToArray();
                int id = facilities[0].Id;
                var choice = new OptionButton();
                foreach (var facility in facilities)
                    choice.AddItem(Workspace.FacilityNames.TryGetValue(facility.Id, out var name)
                        ? $"{name} [{facility.Id}]" : $"设施 {facility.Id}");
                harbor.AddChild(choice);
                var flag = new CheckBox { Text = "港口", ButtonPressed = facilities[0].IsHarbor,
                    TooltipText = "按地图统一半径开放周围水畔，同时参与港口的地域连接及 AI 判断。" };
                harbor.AddChild(flag);
                void RefreshHarbor() => flag.SetPressedNoSignal(Workspace.Definitions.Military!.Facilities.Single(f => f.Id == id).IsHarbor);
                choice.ItemSelected += index => { id = facilities[(int)index].Id; RefreshHarbor(); };
                flag.Toggled += value => Workspace.SetFacilityHarbor(id, value);
                _refresh.Add(RefreshHarbor);
            }
            var links = ContentEditorControls.Section(body, "武将联携关系");
            _links = new TacticLinkEditor { Workspace = Workspace, ReportStatus = ReportStatus };
            links.AddChild(_links);
        }
    }

    private void Number(Node parent, string label, Func<int> read, int minimum, int maximum,
        Action<int> write, string name, string tooltip)
    {
        ContentEditorControls.Caption(parent, label);
        var field = ContentEditorControls.NumericInput(parent, read(), minimum, maximum, value => write((int)value), name, tooltip);
        _refresh.Add(() => field.SetValueNoSignal(read()));
    }

    private static void Note(Node parent, string text) => parent.AddChild(new Label
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart
    });
}
