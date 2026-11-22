using Godot;
using San9.Content.Scenarios;

namespace San9.Client.Godot;

public partial class ScenarioEditor
{
    private void EditOperation(string title, Func<VBoxContainer, Action> build, int? selection = null)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = "应用", CancelButtonText = "取消", DialogHideOnOk = false };
        var body = new VBoxContainer { CustomMinimumSize = new(440, 0) };
        dialog.AddChild(body);
        var apply = build(body);
        var error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("ffb4a8") };
        body.AddChild(error);
        dialog.Confirmed += () =>
        {
            try { apply(); }
            catch (Exception ex) { error.Text = ex.Message; return; }
            dialog.QueueFree();
            BuildPages(_pages.CurrentTab, selection);
            Status("已更新剧本草稿。");
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered();
    }

    private static Func<int> Pick(Node body, string title, IEnumerable<(int Id, string Name)> choices, int? current = null)
    {
        var entries = choices.Select(c => ($"{c.Name} [{c.Id}]", c.Id)).ToArray();
        int? selected = current;
        SearchableReferenceField.Add(body, title, entries, current ?? -1, id => selected = id,
            "Pick" + title, "按名称或 ID 搜索。", "请选择", keepFirstVisible: false);
        return () => selected ?? throw new InvalidDataException("请选择" + title + "。");
    }

    private static HashSet<int> PickMany(Node body, string title, IEnumerable<(int Id, string Name)> choices, IEnumerable<int>? initial = null,
        string searchHint = "搜索名称或 ID", int columnWidth = 225, int listHeight = 165)
    {
        var selected = (initial ?? []).ToHashSet();
        var entries = choices.ToArray();
        ContentEditorControls.Caption(body, title);
        var search = new LineEdit { PlaceholderText = searchHint };
        var columns = new HBoxContainer();
        var available = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var chosen = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(columns); columns.AddChild(available); columns.AddChild(chosen);
        available.AddChild(search);
        var list = new ItemList { SelectMode = ItemList.SelectModeEnum.Toggle, CustomMinimumSize = new(columnWidth, listHeight) };
        available.AddChild(list);
        var count = new Label();
        chosen.AddChild(count);
        var picked = new ItemList { Name = "SelectedEntries", CustomMinimumSize = new(columnWidth, 110), SizeFlagsVertical = SizeFlags.ExpandFill };
        chosen.AddChild(picked);
        void RefreshChosen()
        {
            picked.Clear();
            foreach (var (id, name) in entries.Where(e => selected.Contains(e.Id)))
                picked.SetItemMetadata(picked.AddItem($"{name} [{id}]"), id);
            count.Text = $"已选 {selected.Count} 项";
        }
        void Filter(string query)
        {
            list.Clear();
            foreach (var (id, name) in entries)
            {
                var label = $"{name} [{id}]";
                if (!label.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                int row = list.AddItem(label);
                list.SetItemMetadata(row, id);
                if (selected.Contains(id)) list.Select(row, false);
            }
        }
        var remove = ContentEditorControls.ActionButton(chosen, "移除选中", () =>
        {
            if (picked.GetSelectedItems() is not [var row]) return;
            selected.Remove(picked.GetItemMetadata(row).AsInt32());
            RefreshChosen(); Filter(search.Text);
        });
        remove.Disabled = true;
        picked.ItemSelected += _ => remove.Disabled = false;
        remove.Pressed += () => remove.Disabled = true;
        list.MultiSelected += (row, on) =>
        {
            int id = list.GetItemMetadata((int)row).AsInt32();
            if (on) selected.Add(id); else selected.Remove(id);
            RefreshChosen(); remove.Disabled = true;
        };
        search.TextChanged += Filter; Filter(""); RefreshChosen();
        return selected;
    }

    private void NewForceDialog() => EditOperation("建立势力", body =>
    {
        var workspace = _workspace!;
        var ruler = Pick(body, "君主", workspace.Founders.Select(p => (p.Id, p.Name)));
        var city = Pick(body, "空白都市", workspace.DeploymentCities.Where(s => s.OwnerId is null).Select(s => (s.Id, workspace.Name(s))));
        return () => workspace.FoundForce(ruler(), city());
    });

    private void TransferDialog(SettlementInitialState place) => EditOperation("移交据点", body =>
    {
        var workspace = _workspace!;
        int? target = place.OwnerId, retreat = null;
        Reference(body, "接收势力（无为无主）", target, workspace.Document.Forces.Where(f => !f.IsDormant)
            .Select(f => (f.Id, workspace.Name(f))), id => target = id);
        Reference(body, "驻将撤往（无为就地改属，无主时转在野）", null, workspace.Document.Settlements
            .Where(s => s.OwnerId == place.OwnerId && s.Id != place.Id && !s.IsDormant && s.CanHostPersons)
            .Select(s => (s.Id, workspace.Name(s))), id => retreat = id);
        return () => workspace.TransferSettlement(place.Id, target, retreat);
    }, place.Id);

    private void DeploymentDialog(bool withdraw, int? cityId = null) => EditOperation(withdraw ? "撤回武将安置" : "批量安置武将", body =>
    {
        var workspace = _workspace!;
        if (withdraw)
        {
            var places = workspace.Document.Settlements.ToDictionary(s => s.Id, s => workspace.Name(s));
            var forces = ForceChoices().ToDictionary(f => f.Id, f => f.Name);
            string Describe(ScenarioOfficerState p)
            {
                string place = p.SettlementId is int site ? places.GetValueOrDefault(site) ?? $"据点 {site}" :
                    p.UnitId is int unit ? $"部队 {unit}" : p.RegionId is int region ? workspace.RegionName(region) : "无地点";
                string force = p.OwnerId is int owner ? forces.GetValueOrDefault(owner) ?? $"势力 {owner}" : "无势力";
                return $"{workspace.OfficerName(p.Id)}　{force} · {place} · {OfficerStatusNames[(int)p.Status]}";
            }
            var ids = PickMany(body, "选择撤回的武将", workspace.WithdrawableOfficers.Select(p => (p.Id, Describe(p))),
                searchHint: "搜索姓名、ID、势力、地点或身份", columnWidth: 330, listHeight: 375);
            return () => workspace.WithdrawOfficers(ids);
        }
        var selected = PickMany(body, "选择尚未安置的共享武将", workspace.UnplacedOfficers.Select(p => (p.Id, p.Name)), listHeight: 375);
        var serve = new CheckBox { Text = "在该都市仕官（未勾选为在野）" };
        Func<int> city;
        if (cityId is int fixedCity)
        {
            var place = workspace.Document.Settlements.Single(s => s.Id == fixedCity);
            ContentEditorControls.Caption(body, "都市：" + workspace.Name(place));
            serve.Disabled = place.OwnerId is null;
            city = () => fixedCity;
        }
        else city = Pick(body, "都市", workspace.DeploymentCities.Select(s => (s.Id, workspace.Name(s))));
        body.AddChild(serve);
        return () => workspace.DeployOfficers(selected, city(), serve.ButtonPressed);
    });

    private void RunChange(Action apply, int id)
    {
        try { apply(); }
        catch (Exception error) { Status(error.Message, true); }
        BuildPages(_pages.CurrentTab, id);
    }

    private void CheckScenario()
    {
        if (_workspace is null || _busy || !CommitJson()) return;
        try { _workspace.CheckDraft(); Status("剧本校验通过，可以开局。"); }
        catch (Exception error) { ShowIssue(error); }
    }

    private void ShowIssue(Exception error)
    {
        if (_workspace?.FindIssue() is { } issue && _pages.GetNodeOrNull<Control>(issue.Page) is { } page)
        {
            if (issue.Page == "势力") _forceTab = 0;
            if (issue.Page == "外交") _diplomacyTab = 1;
            BuildPages(page.GetIndex(), issue.ObjectId);
        }
        Status(error.Message, true);
    }
}
