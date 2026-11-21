using System.Text.Json;
using Godot;
using San9.Content.Packages;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Formation subpage. The parent owns loading, save, close and the shared draft.</summary>
public partial class FormationEditorPage : SharedDataEditorPage
{
    private readonly List<int> _visible = [];
    private ItemList _list = null!;
    private LineEdit _search = null!;
    private TabContainer _tabs = null!;
    private LineEdit _title = null!;
    private Label _changed = null!;
    private HSplitContainer _split = null!;
    private FormationEditorPreview _preview = null!;
    private FormationEditorPreview? _positionEditor;
    private TextEdit? _json;
    private int? _current;
    private int _generation;
    private bool _building, _rawDirty;
    private SharedCatalogActions _actions = null!;
    public override void Activate() { if (IsNodeReady() && !HasPendingChanges) RefreshAfterSave(); }
    public override bool HasPendingChanges => _rawDirty || _actions.Busy;
    public override bool CommitPending() => !_actions.Busy && CommitJson();
    public override void RefreshAfterSave() { RefreshList(); ShowCurrent(); }
    public override void Saved() { if (IsVisibleInTree()) { RefreshList(); UpdateHeading(); } }
    private static readonly JsonSerializerOptions EditorJson = new(ScenarioContentFiles.DataJsonOptions)
    { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public override void _Ready()
    {
        _split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [230] }; AddChild(_split); _split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var sidebar = ContentEditorControls.Sidebar(_split);
        _actions = new()
        {
            Page = this, Subject = "阵形", Identity = "Formation", Current = () => _current,
            CanDelete = Workspace.IsCreatedFormation, EntryName = id => Workspace.GetFormationCatalog(id).Name,
            Create = async (id, token) => (await Workspace.CreateFormationAsync(id, token)).Id,
            Delete = Workspace.DeleteFormationAsync, Commit = CommitJson,
            Select = id =>
            {
                _current = id ?? Workspace.Formations.FirstOrDefault()?.Id;
                _search.Text = ""; RefreshList(); if (_current is not null) _list.EnsureCurrentIsVisible(); ShowCurrent();
            }
        };
        sidebar.AddChild(_actions);
        _search = new LineEdit { PlaceholderText = "搜索阵形", Name = "FormationSearch" }; sidebar.AddChild(_search);
        _search.TextChanged += _ => RefreshList();
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, Name = "FormationList" }; sidebar.AddChild(_list);
        _list.ItemSelected += index => Select(_visible[(int)index]);
        Button(sidebar, "恢复打开时的值", () =>
        {
            if (_current is not int id || !CanProcess()) return;
            _rawDirty = false; Workspace.RestoreFormation(id); ShowCurrent(); RefreshList(); Status("已恢复，尚未保存。");
        }, "RestoreFormation").SizeFlagsHorizontal = SizeFlags.Fill;
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(600, 0) }; body.AddThemeConstantOverride("separation", 12); _split.AddChild(body);
        var hero = new HBoxContainer(); hero.AddThemeConstantOverride("separation", 12); body.AddChild(hero);
        _title = new LineEdit { Name = "FormationName", PlaceholderText = "阵形名称", TooltipText = "名称",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _title.AddThemeFontSizeOverride("font_size", 20); hero.AddChild(_title);
        _title.TextChanged += value =>
        {
            if (_building || !CanProcess() || _current is not int id) return;
            Workspace.SetFormationCatalog(Workspace.GetFormationCatalog(id) with { Name = value });
            RefreshList(); UpdateHeading();
        };
        _changed = new Label { Text = "*", Visible = false }; hero.AddChild(_changed);
        _preview = new FormationEditorPreview { CustomMinimumSize = new(190, 100), MouseFilter = MouseFilterEnum.Ignore }; hero.AddChild(_preview);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Name = "FormationPages" }; body.AddChild(_tabs);
        _tabs.TabChanged += index =>
        {
            if (_building) return;
            if (!_rawDirty || index == 3) { ShowTab((int)index); return; }
            if (!CommitJson()) { _building = true; _tabs.CurrentTab = 3; _building = false; }
            else ShowCurrent();
        };
        _current = Workspace.Formations.FirstOrDefault()?.Id;
        _title.Editable = _current is not null;
        RefreshList(); ShowCurrent();
    }

    private void RefreshList()
    {
        if (_building) return;
        _building = true; _list.Clear(); _visible.Clear();
        foreach (var formation in Workspace.Formations)
        {
            string name = Workspace.FormationNames.GetValueOrDefault(formation.Id, $"阵形 {formation.Id}");
            if (_search.Text.Length > 0 && !name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) && formation.Id.ToString() != _search.Text) continue;
            _visible.Add(formation.Id); _list.AddItem(name + (Workspace.IsFormationChanged(formation.Id) ? "  *" : ""));
            _list.SetItemTooltip(_visible.Count - 1, $"{name} · ID {formation.Id}");
            if (_current == formation.Id) _list.Select(_visible.Count - 1);
        }
        _building = false;
    }

    private void Select(int id)
    {
        if (_building || !CanProcess() || id == _current) return;
        if (!CommitJson()) { RefreshList(); return; }
        _current = id; ShowCurrent();
    }

    private void ShowCurrent()
    {
        int tab = Math.Max(0, _tabs.CurrentTab);
        _building = true; _generation++; _json = null; _positionEditor = null;
        foreach (Node child in _tabs.GetChildren()) { _tabs.RemoveChild(child); child.QueueFree(); }
        _title.Editable = _current is not null; _preview.Visible = _current is not null;
        if (_current is not int id)
        {
            _title.Text = ""; _changed.Visible = false; _building = false; _actions.Refresh(); return;
        }
        foreach (string title in new[] { "基础", "战斗", "兵法与特性", "阵位与完整数据" })
            _tabs.AddChild(new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        _tabs.CurrentTab = tab;
        ShowTab(tab);
        _building = false; UpdateHeading();
    }

    private void ShowTab(int index)
    {
        if (_current is not int id) return;
        var scroll = (ScrollContainer)_tabs.GetTabControl(index);
        if (scroll.GetChildCount() != 0) return;
        var page = ContentEditorControls.PageBody(scroll);
        var rule = Workspace.GetFormation(id);
        switch (index)
        {
            case 0: BuildBasics(rule, page); break;
            case 1: BuildCombat(rule, page); break;
            case 2: BuildTactics(rule, page); break;
            case 3: BuildAdvanced(rule, page); break;
        }
    }

    private void UpdateHeading()
    {
        _actions.Refresh();
        if (_current is not int id) return;
        var rule = Workspace.GetFormation(id);
        string name = Workspace.FormationNames.GetValueOrDefault(id, $"阵形 {id}");
        if (_title.Text != name)
        {
            bool building = _building; _building = true; _title.Text = name; _building = building;
        }
        _changed.Visible = Workspace.IsFormationChanged(id) || _rawDirty;
        _preview.Rule = rule;
        if (_positionEditor is not null) _positionEditor.Rule = rule;
    }

    private void Edit(int id, Func<FormationRule, FormationRule> change, bool rebuild = false)
    {
        if (_building || !CanProcess() || _current != id) return;
        Workspace.SetFormation(change(Workspace.GetFormation(id))); RefreshList(); UpdateHeading();
        if (rebuild) ShowCurrent();
        else if (_json is not null)
        {
            _building = true; _json.Text = JsonSerializer.Serialize(Workspace.GetFormation(id), EditorJson); _building = false;
        }
    }

    private bool CommitJson()
    {
        if (!_rawDirty || _json is null || _current is not int id) return true;
        try
        {
            var rule = JsonSerializer.Deserialize<FormationRule>(_json.Text, EditorJson) ?? throw new InvalidDataException("阵形数据为空。");
            if (rule.Id != id) throw new InvalidDataException("请保留当前阵形的身份 ID。");
            var current = Workspace.GetFormation(id);
            if (rule.Positions.Length != current.Positions.Length || rule.OfficerCapacity != current.OfficerCapacity)
                throw new InvalidDataException("暂不支持修改阵位数量或武将上限。");
            if (!rule.Positions.SequenceEqual(current.Positions) && rule.Positions.Any(p => p.Column is < 0 or > 2))
                throw new InvalidDataException("阵位只能放在 3×3 范围内。");
            Workspace.SetFormation(rule); _rawDirty = false; RefreshList(); UpdateHeading(); return true;
        }
        catch (Exception error) { Status("完整数据尚未应用：" + error.Message, true); return false; }
    }

    private Action<T> Guard<T>(Action<T> action)
    {
        int generation = _generation;
        return value => { if (!_building && CanProcess() && generation == _generation) action(value); };
    }

    private static Button Button(Node parent, string title, Action action, string name = "") =>
        ContentEditorControls.ActionButton(parent, title, action, name);

}
