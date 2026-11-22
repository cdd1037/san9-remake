using Godot;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class ScenarioEditor
{
    private int _diplomacyTab;

    private void BuildDiplomacyPage(int? selectedId)
    {
        var tabs = new TabContainer { Name = "外交" };
        _pages.AddChild(tabs);
        var body = new VBoxContainer { Name = "关系矩阵" };
        tabs.AddChild(body);
        BuildRelationMatrix(body);
        BuildListPage("联盟与委托", ForceChoices, BuildDiplomacyAgreements, selectedId, tabs);
        tabs.CurrentTab = _diplomacyTab;
        tabs.TabChanged += tab => _diplomacyTab = (int)tab;
    }

    private void BuildRelationMatrix(VBoxContainer body)
    {
        var workspace = _workspace!;
        var forces = ForceChoices().ToArray();
        var relations = workspace.Document.Information.Relations.ToLookup(r => (r.FirstId, r.SecondId));
        var bar = new HBoxContainer();
        body.AddChild(bar);
        var pair = new Label { Text = "选择两个势力的交叉格以编辑关系值", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var value = new SpinBox { MinValue = 0, MaxValue = 100, Step = 1, Editable = false };
        bar.AddChild(pair);
        bar.AddChild(value);
        var tree = new Tree { Name = "RelationMatrix", Columns = forces.Length + 1, HideRoot = true,
            ColumnTitlesVisible = true, SelectMode = Tree.SelectModeEnum.Single, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(tree);
        tree.SetColumnTitle(0, "势力");
        var root = tree.CreateItem();
        var rows = new TreeItem[forces.Length];
        tree.SetColumnCustomMinimumWidth(0, 92);
        void Display(int row, int col, int score)
        {
            var band = DiplomacyMath.RelationBand(Math.Clamp(score, 0, 100));
            var color = band switch
            {
                ForceRelationBand.Hostile => "f39489", ForceRelationBand.Unfriendly => "e7b181",
                ForceRelationBand.Neutral => "d2dae3", ForceRelationBand.Friendly => "a5d8b0", _ => "72dbbe"
            };
            rows[row].SetMetadata(col + 1, score);
            rows[row].SetText(col + 1, score.ToString());
            rows[row].SetCustomColor(col + 1, new Color(color));
            rows[row].SetTooltipText(col + 1, $"{forces[row].Name} [{forces[row].Id}] ↔ {forces[col].Name} [{forces[col].Id}]");
        }
        for (int i = 0; i < forces.Length; i++)
        {
            tree.SetColumnTitle(i + 1, forces[i].Name);
            tree.SetColumnCustomMinimumWidth(i + 1, Math.Max(66, (int)tree.GetThemeFont("font").GetStringSize(forces[i].Name, fontSize: tree.GetThemeFontSize("font_size")).X + 16));
            rows[i] = tree.CreateItem(root);
            rows[i].SetMetadata(0, i);
            rows[i].SetText(0, forces[i].Name);
            rows[i].SetSelectable(0, false);

            rows[i].SetText(i + 1, "—");
            rows[i].SetSelectable(i + 1, false);
            for (int j = 0; j < i; j++)
            {
                int a = forces[i].Id, b = forces[j].Id;
                int score = relations[(Math.Min(a, b), Math.Max(a, b))].FirstOrDefault()?.Value ?? 0;
                Display(i, j, score);
                Display(j, i, score);
            }
        }
        int row = -1, col = -1;
        tree.CellSelected += () =>
        {
            row = tree.GetSelected().GetMetadata(0).AsInt32();
            col = tree.GetSelectedColumn() - 1;
            pair.Text = rows[row].GetTooltipText(col + 1);
            value.SetValueNoSignal(rows[row].GetMetadata(col + 1).AsInt32());
            value.Editable = true;
        };
        value.ValueChanged += score =>
        {
            if (row < 0 || col < 0 || row == col) return;
            workspace.SetRelation(forces[row].Id, forces[col].Id, (int)score);
            Display(row, col, (int)score);
            Display(col, row, (int)score);
        };
    }

    private void BuildDiplomacyAgreements(VBoxContainer body, int id)
    {
        var workspace = _workspace!;
        var document = workspace.Document;
        var alliance = document.Alliance;
        ContentEditorControls.Caption(body, alliance is null ? "当前无联盟" :
            $"联盟目标：{ForceName(alliance.TargetForceId)}　盟主：{ForceName(alliance.LeaderForceId)}　剩余 {alliance.RemainingXun} 旬\n成员：{string.Join("、", alliance.MemberForceIds.Select(ForceName))}");
        var actions = ContentEditorControls.ButtonGroup(body, "开局联盟");
        ContentEditorControls.ActionButton(actions, "配置联盟", () => EditOperation("配置开局联盟", form =>
        {
            var candidates = document.Forces.Where(f => !f.IsDormant && !f.IsBarbarian).Select(f => (f.Id, workspace.Name(f))).ToArray();
            var target = Pick(form, "讨伐目标", candidates, alliance?.TargetForceId);
            var leader = Pick(form, "盟主", candidates, alliance?.LeaderForceId);
            var members = PickMany(form, "参加势力（盟主自动加入）", candidates, alliance?.MemberForceIds);
            int duration = alliance?.RemainingXun ?? 12;
            Number(form, "剩余旬数", duration, 1, int.MaxValue, v => duration = (int)v);
            return () =>
            {
                var chief = leader();
                var group = members.Append(chief).Distinct().Order().ToArray();
                if (group.Length < 2 || group.Contains(target())) throw new InvalidDataException("联盟至少有两个成员，讨伐目标不能加入联盟。");
                workspace.SetAlliance(new(target(), chief, [.. group], duration));
            };
        }, id));
        ContentEditorControls.ActionButton(actions, "取消联盟", () => EditOperation("取消开局联盟", form =>
        {
            ContentEditorControls.Caption(form, "取消本剧本的开局联盟？");
            return () => workspace.SetAlliance(null);
        }, id)).Disabled = alliance is null;
        var request = document.AttackRequests.SingleOrDefault(r => r.RequestedForceId == id);
        ContentEditorControls.Caption(body, request is null ? "本势力未受托攻打据点" :
            $"{ForceName(request.RequesterForceId)}委托本势力攻打{workspace.Name(document.Settlements.Single(s => s.Id == request.TargetSettlementId))}，剩余 {request.RemainingXun} 旬");
        var requests = ContentEditorControls.ButtonGroup(body, "攻打委托");
        ContentEditorControls.ActionButton(requests, "配置委托", () => EditOperation("配置攻打委托", form =>
        {
            var requester = Pick(form, "委托势力", ForceChoices().Where(f => f.Id != id), request?.RequesterForceId);
            var target = Pick(form, "目标据点", document.Settlements.Where(s => s.OwnerId is not null && s.OwnerId != id)
                .Select(s => (s.Id, workspace.Name(s))), request?.TargetSettlementId);
            int duration = request?.RemainingXun ?? 3;
            Number(form, "剩余旬数", duration, 1, int.MaxValue, v => duration = (int)v);
            return () => workspace.SetAttackRequest(id, new(id, requester(), target(), duration));
        }, id));
        ContentEditorControls.ActionButton(requests, "清除委托", () => RunChange(() => workspace.SetAttackRequest(id, null), id)).Disabled = request is null;
    }
}
