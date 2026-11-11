using Godot;

namespace San9.Client.Godot;

/// <summary>Search, selection and guarded fields for shared definition pages.</summary>
public abstract partial class SharedCatalogEditorPage : SharedDataEditorPage
{
    protected VBoxContainer Form = null!;
    protected int? Current;
    private ItemList _list = null!;
    private LineEdit _search = null!;
    private Label? _title;
    private LineEdit? _name;
    private Label _changed = null!;
    private readonly List<int> _visible = [];
    private bool _building;
    private int _generation;
    protected abstract string Subject { get; }
    protected abstract IEnumerable<(int Id, string Name)> Entries { get; }
    protected virtual Action<int, string>? Rename => null;
    protected virtual bool ScrollForm => true;
    protected virtual string RestoreText => "恢复打开时的值";
    protected virtual string RestoreTooltip => "";
    protected bool IsFiltering => _search is { Text.Length: > 0 };
    protected virtual void BuildSidebarActions(VBoxContainer sidebar) { }
    protected virtual void RefreshActions() { }
    protected virtual ItemList CreateList() => new();
    protected abstract bool Changed(int id);
    protected abstract void Restore(int id);
    protected abstract void BuildForm(int id);
    public override bool HasPendingChanges => false;
    public override bool CommitPending() => true;
    public override void RefreshAfterSave() { RefreshList(); ShowCurrent(); }
    public override void Saved() { if (IsVisibleInTree()) RefreshList(); }
    public override void Activate() { if (IsNodeReady()) { RefreshList(); ShowCurrent(); } }

    public override void _Ready()
    {
        var split = new HSplitContainer { SplitOffsets = [230] }; AddChild(split);
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var side = ContentEditorControls.Sidebar(split);
        BuildSidebarActions(side);
        _search = new LineEdit { PlaceholderText = "搜索" + Subject, Name = GetType().Name.Replace("EditorPage", "", StringComparison.Ordinal) + "Search" }; side.AddChild(_search);
        _search.TextChanged += _ => RefreshList();
        _list = CreateList(); _list.SizeFlagsVertical = SizeFlags.ExpandFill;
        _list.Name = GetType().Name.Replace("EditorPage", "", StringComparison.Ordinal) + "List"; side.AddChild(_list);
        _list.ItemSelected += index => { if (!_building && CanProcess()) { Current = _visible[(int)index]; ShowCurrent(); } };
        var restore = new Button { Text = RestoreText, TooltipText = RestoreTooltip, CustomMinimumSize = new(0, 34) }; side.AddChild(restore);
        restore.Pressed += () => { if (Current is int id && CanProcess()) { Restore(id); RefreshAfterSave(); Status("已恢复，尚未保存。"); } };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(600, 0) }; body.AddThemeConstantOverride("separation", 12); split.AddChild(body);
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", 12); body.AddChild(heading);
        if (Rename is { } rename)
        {
            _name = new LineEdit { Name = GetType().Name.Replace("EditorPage", "", StringComparison.Ordinal) + "Name",
                PlaceholderText = "名称", TooltipText = "名称", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _name.AddThemeFontSizeOverride("font_size", 20); heading.AddChild(_name);
            _name.TextChanged += value =>
            {
                if (!_building && CanProcess() && Current is int id) { rename(id, value); RefreshCurrent(); }
            };
        }
        else
        {
            _title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _title.AddThemeFontSizeOverride("font_size", 20); heading.AddChild(_title);
        }
        _changed = new Label { Text = "*", Visible = false }; heading.AddChild(_changed);
        if (ScrollForm)
        {
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            body.AddChild(scroll); Form = ContentEditorControls.PageBody(scroll);
        }
        else
        {
            Form = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddChild(Form);
        }
        Current = Entries.Select(e => (int?)e.Id).FirstOrDefault(); RefreshList(); ShowCurrent();
    }
    private void RefreshList()
    {
        _building = true; _list.Clear(); _visible.Clear();
        foreach (var (id, name) in Entries)
        {
            if (_search.Text.Length > 0 && !name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) && id.ToString() != _search.Text) continue;
            _visible.Add(id); _list.AddItem(name + (Changed(id) ? "  *" : ""));
            _list.SetItemMetadata(_visible.Count - 1, id);
            if (Current == id) _list.Select(_visible.Count - 1);
        }
        _building = false;
        UpdateHeading();
    }
    private void UpdateHeading()
    {
        string name = Current is int id ? Entries.First(e => e.Id == id).Name : "";
        _changed.Visible = Current is int selected && Changed(selected);
        if (_name is not null)
        {
            _name.Editable = Current is not null;
            if (_name.Text != name)
            {
                bool building = _building; _building = true; _name.Text = name; _building = building;
            }
        }
        else _title!.Text = Current is not null ? name : "没有可编辑的" + Subject;
        RefreshActions();
    }
    private void ShowCurrent()
    {
        _building = true; _generation++;
        foreach (Node child in Form.GetChildren()) { Form.RemoveChild(child); child.QueueFree(); }
        if (Current is int id) BuildForm(id);
        _building = false; UpdateHeading();
    }
    protected Action<T> Guard<T>(Action<T> action)
    {
        int generation = _generation;
        return value => { if (!_building && CanProcess() && generation == _generation) { action(value); RefreshCurrent(); } };
    }
    private void RefreshCurrent()
    {
        if (_search.Text.Length > 0) { RefreshList(); return; }
        if (Current is int id)
        {
            int row = _visible.IndexOf(id);
            if (row >= 0) _list.SetItemText(row, Entries.First(e => e.Id == id).Name + (Changed(id) ? "  *" : ""));
        }
        UpdateHeading();
    }
    protected GridContainer Section(string title, string tooltip = "") => ContentEditorControls.ProfileGrid(Form, title, tooltip: tooltip);
    protected HFlowContainer Options(string title, string tooltip = "") => ContentEditorControls.ButtonGroup(Form, title, tooltip);
    protected void TextField(string title, string value, Action<string> changed, bool multiline = false, string name = "") =>
        ContentEditorControls.AddText(Form, title, value, Guard(changed), multiline, name);

    protected void Number(Node parent, string title, double value, double min, double max, Action<double> changed, string name = "", string tooltip = "")
    {
        ContentEditorControls.Caption(parent, title);
        ContentEditorControls.NumericInput(parent, value, min, max, Guard(changed), name, tooltip);
    }
    protected void Toggle(Node parent, string title, bool value, Action<bool> changed, string name = "", string tooltip = "")
    {
        ContentEditorControls.Toggle(parent, title, value, Guard(changed), name, tooltip);
    }
    protected void Choice(Node parent, string title, IReadOnlyList<string> labels, int selected, Action<int> changed, string tooltip = "")
    {
        ContentEditorControls.Choices(parent, title, labels, selected, Guard(changed), tooltip: tooltip);
    }
    protected void SelectEntry(int? id)
    {
        Current = id ?? Entries.Select(e => (int?)e.Id).FirstOrDefault();
        _search.Text = ""; RefreshList(); if (Current is not null) _list.EnsureCurrentIsVisible(); ShowCurrent();
    }

    protected void Rebuild() => ShowCurrent();
}
