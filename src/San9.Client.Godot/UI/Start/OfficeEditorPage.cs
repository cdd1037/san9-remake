using Godot;
using San9.Content.Scenarios;

namespace San9.Client.Godot;

public partial class OfficeEditorPage : SharedCatalogEditorPage
{
    private SharedCatalogActions _actions = null!;
    public override bool HasPendingChanges => _actions.Busy;
    public override bool CommitPending() => !_actions.Busy;
    protected override void BuildSidebarActions(VBoxContainer sidebar)
    {
        _actions = new()
        {
            Page = this, Subject = "官职", Identity = "Office", Current = () => Current,
            CanDelete = Workspace.IsCreatedOffice, EntryName = id => Workspace.GetOffice(id).Name,
            Create = async (id, token) => (await Workspace.CreateOfficeAsync(id, token)).Id,
            Delete = Workspace.DeleteOfficeAsync, Select = SelectEntry,
            SuccessMessage = "已更新共享官职草稿，尚未保存；符合势力官爵条件后可供玩家及电脑任命。"
        };
        sidebar.AddChild(_actions);
    }
    protected override void RefreshActions() => _actions.Refresh();
    protected override ItemList CreateList() => new CatalogOrderList
    {
        CanReorder = () => !_actions.Busy && !IsFiltering && CanProcess(),
        Reorder = (id, index) =>
        {
            if (Workspace.MoveOfficeTo(id, index)) { SelectEntry(id); Status("已调整官职顺序，尚未保存。"); }
        },
        TooltipText = "拖动官职调整顺序；搜索期间不可排序。"
    };
    protected override string Subject => "官职";
    protected override string RestoreText => "恢复官职及顺序";
    protected override string RestoreTooltip => "恢复所选官职的资料及整个列表的顺序，保留其他官职的资料修改。";
    protected override IEnumerable<(int Id, string Name)> Entries => Workspace.OrderedOffices.Select(o => (o.Id, o.Name));
    protected override Action<int, string> Rename => (id, value) => Edit(id, p => p with { Name = value });
    protected override bool Changed(int id) => Workspace.IsOfficeChanged(id);
    protected override void Restore(int id) => Workspace.RestoreOffice(id);
    private void Edit(int id, Func<OfficeDefinition, OfficeDefinition> change) => Workspace.SetOffice(change(Workspace.GetOffice(id)));
    protected override void BuildForm(int id)
    {
        var office = Workspace.GetOffice(id);
        TextField("描述", office.Description, v => Edit(id, p => p with { Description = v }), true);
        var basics = Section("基本参数");
        Number(basics, "带兵量", office.CommandCapacity, 0, uint.MaxValue, v => Edit(id, p => p with { CommandCapacity = (uint)v }), "OfficeCapacity");
        Number(basics, "每旬俸禄", office.Salary, 0, int.MaxValue, v => Edit(id, p => p with { Salary = (int)v }), "OfficeSalary");
        Choice(basics, "官职类别", ["武官", "文官"], (int)office.AppointmentGroup, v => Edit(id, p => p with { AppointmentGroup = (OfficeAppointmentGroup)v }));
        var bonuses = Section("任职能力加成");
        Number(bonuses, "统率", office.AbilityBonuses.Leadership, 0, int.MaxValue, v => Edit(id, p => p with { AbilityBonuses = p.AbilityBonuses with { Leadership = (int)v } }), "OfficeLeadership");
        Number(bonuses, "武力", office.AbilityBonuses.War, 0, int.MaxValue, v => Edit(id, p => p with { AbilityBonuses = p.AbilityBonuses with { War = (int)v } }));
        Number(bonuses, "智力", office.AbilityBonuses.Intelligence, 0, int.MaxValue, v => Edit(id, p => p with { AbilityBonuses = p.AbilityBonuses with { Intelligence = (int)v } }));
        Number(bonuses, "政治", office.AbilityBonuses.Politics, 0, int.MaxValue, v => Edit(id, p => p with { AbilityBonuses = p.AbilityBonuses with { Politics = (int)v } }));
        var requirements = Section("任命条件");
        var ranks = Workspace.Ranks.OrderBy(r => r.Precedence).ToArray();
        List<int?> rankIds = [null, .. ranks.Select(r => (int?)r.Id)];
        List<string> names = ["无势力官爵要求", .. ranks.Select(r => r.Name)];
        int selected = rankIds.IndexOf(office.RequiredRankId);
        if (selected < 0) { selected = rankIds.Count; rankIds.Add(office.RequiredRankId); names.Add($"缺失势力官爵 {office.RequiredRankId}"); }
        Choice(requirements, "所需势力官爵", names, selected, v => Edit(id, p => p with { RequiredRankId = rankIds[v] }));
        Toggle(Form, "优先于普通官职分配", office.FillBeforeOrdinaryOffices, v => Edit(id, p => p with { FillBeforeOrdinaryOffices = v }));
    }
}
