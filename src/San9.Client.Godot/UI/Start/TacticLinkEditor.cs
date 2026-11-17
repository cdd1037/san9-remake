using Godot;
using San9.Application.Authoring;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Edits the shared pair table; new pairs stay in the dialog until explicitly added.</summary>
public partial class TacticLinkEditor : VBoxContainer
{
    public SharedDataEditorWorkspace Workspace { get; init; } = null!;
    public Action<string, bool> ReportStatus { get; init; } = null!;
    public bool HasPendingChanges => _dialog is not null;
    private ConfirmationDialog? _dialog;
    private Tree _table = null!;
    private LineEdit _search = null!;
    private Label _count = null!, _selection = null!;
    private SpinBox _bonus = null!;
    private Button _sibling = null!, _remove = null!;
    private (int First, int Second)? _selected;
    private bool _building;

    public override void _Ready()
    {
        var actions = ContentEditorControls.ButtonGroup(this);
        _search = new LineEdit { Name = "TacticLinkSearch", PlaceholderText = "按任一武将姓名或ID筛选", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddChild(_search); _search.TextChanged += _ => Refresh();
        ContentEditorControls.ActionButton(actions, "添加关系", AddPair, "AddTacticLinkPair");
        _remove = ContentEditorControls.ActionButton(actions, "删除所选", () =>
        {
            if (_selected is not { } pair) return;
            Workspace.RemoveTacticLinkPair(pair.First, pair.Second); _selected = null; Refresh();
        }, "RemoveTacticLinkPair");
        _count = new Label(); _count.AddThemeColorOverride("font_color", ContentEditorStyle.Muted); actions.AddChild(_count);
        _table = new Tree { Name = "TacticLinkPairs", Columns = 4, HideRoot = true, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, CustomMinimumSize = new(0, 340), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _table.AddThemeFontSizeOverride("font_size", 14);
        _table.AddThemeColorOverride("title_button_color", new Color("e8edf3"));
        _table.AddThemeConstantOverride("v_separation", 4);
        foreach (var (column, title) in new[] { (0, "武将甲"), (1, "武将乙"), (2, "加成（百分点）"), (3, "兄弟加成") })
        {
            _table.SetColumnTitle(column, title);
            _table.SetColumnTitleAlignment(column, column < 2 ? HorizontalAlignment.Left : HorizontalAlignment.Center);
            _table.SetColumnExpand(column, column < 2);
            _table.SetColumnCustomMinimumWidth(column, column < 2 ? 140 : 150);
        }
        AddChild(_table);
        _table.ItemSelected += SelectPair;
        var fields = ContentEditorControls.ButtonGroup(this);
        _selection = new Label { Text = "未选择关系", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(180, 34), VerticalAlignment = VerticalAlignment.Center };
        fields.AddChild(_selection);
        ContentEditorControls.Caption(fields, "加成（百分点）");
        _bonus = ContentEditorControls.NumericInput(fields, 0, 0, 100, value => Edit(p => p with { Bonus = (int)value }), "TacticLinkBonus");
        _bonus.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _bonus.CustomMinimumSize = new(100, 34);
        _sibling = ContentEditorControls.Toggle(fields, "兄弟按本表", false,
            value => Edit(p => p with { SuppressesSiblingBonus = value }), "TacticLinkSuppressSibling",
            "默认兄弟关系优先使用血缘加成。开启后，该配对的兄弟关系使用表中加成；父子仍优先使用血缘加成。关系加成须在史实关系模式下启用。");
        Refresh();
    }

    public void Refresh()
    {
        if (!IsNodeReady()) return;
        _building = true;
        var names = Workspace.Officers.ToDictionary(o => o.Id, o => o.Name);
        _table.Clear(); var root = _table.CreateItem();
        string query = _search.Text.Trim();
        int count = 0;
        TreeItem? selected = null;
        foreach (var pair in Workspace.TacticLinkPairs)
        {
            string firstName = names.GetValueOrDefault(pair.FirstId, $"人物 {pair.FirstId}（不在共享目录）");
            string secondName = names.GetValueOrDefault(pair.SecondId, $"人物 {pair.SecondId}（不在共享目录）");
            string first = $"{firstName}（{pair.FirstId}）", second = $"{secondName}（{pair.SecondId}）";
            if (!first.Contains(query, StringComparison.OrdinalIgnoreCase) && !second.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var row = _table.CreateItem(root);
            row.SetText(0, firstName); row.SetText(1, secondName);
            row.SetTooltipText(0, first); row.SetTooltipText(1, second);
            row.SetTextAlignment(2, HorizontalAlignment.Center); row.SetTextAlignment(3, HorizontalAlignment.Center);
            row.SetMetadata(0, pair.FirstId); row.SetMetadata(1, pair.SecondId);
            UpdateRow(row, pair); count++;
            if (_selected == (pair.FirstId, pair.SecondId)) selected = row;
        }
        _count.Text = query.Length == 0 ? $"共 {count} 条" : $"{count} / {Workspace.TacticLinkPairs.Count} 条";
        if (selected is not null) selected.Select(0);
        else _selected = null;
        _building = false;
        SelectPair();
    }

    private void SelectPair()
    {
        if (_building) return;
        var row = _table.GetSelected();
        _selected = row is null ? null : (row.GetMetadata(0).AsInt32(), row.GetMetadata(1).AsInt32());
        var pair = SelectedPair();
        _selection.Text = row is null ? "未选择关系" : $"{row.GetText(0)} ↔ {row.GetText(1)}";
        _bonus.Editable = pair is not null;
        _sibling.Disabled = _remove.Disabled = pair is null;
        _bonus.SetValueNoSignal(pair?.Bonus ?? 0);
        _sibling.SetPressedNoSignal(pair?.SuppressesSiblingBonus ?? false);
    }

    private TacticLinkPair? SelectedPair() => _selected is { } selected
        ? Workspace.TacticLinkPairs.SingleOrDefault(p => p.FirstId == selected.First && p.SecondId == selected.Second) : null;

    private void Edit(Func<TacticLinkPair, TacticLinkPair> change)
    {
        if (SelectedPair() is not { } pair) return;
        var updated = change(pair);
        Try(() => { Workspace.SetTacticLinkPair(updated); UpdateRow(_table.GetSelected()!, updated); });
    }

    private static void UpdateRow(TreeItem row, TacticLinkPair pair)
    {
        row.SetText(2, pair.Bonus.ToString()); row.SetText(3, pair.SuppressesSiblingBonus ? "按本表" : "按血缘");
    }

    private void AddPair()
    {
        if (_dialog is not null) return;
        var dialog = new ConfirmationDialog { Name = "AddTacticLinkDialog", Title = "添加联携关系",
            OkButtonText = "添加", CancelButtonText = "取消", Exclusive = true, DialogHideOnOk = false };
        _dialog = dialog;
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 10); dialog.AddChild(body);
        var grid = ContentEditorControls.ProfileGrid(body, "配对武将");
        var candidates = Workspace.Officers.Select(o => (Label: $"{o.Name}（{o.Id}）", Value: (int?)o.Id))
            .Prepend(("请选择武将", (int?)null)).ToArray();
        int? first = null, second = null;
        SearchableReferenceField.Add(grid, "武将甲", candidates, first, value => first = value, "TacticLinkFirst", "按姓名搜索武将。");
        SearchableReferenceField.Add(grid, "武将乙", candidates, second, value => second = value, "TacticLinkSecond", "按姓名搜索武将。");
        ContentEditorControls.Caption(grid, "联携加成（百分点）");
        int bonus = 0;
        ContentEditorControls.NumericInput(grid, bonus, 0, 100, value => bonus = (int)value, "NewTacticLinkBonus");
        bool sibling = false;
        ContentEditorControls.Toggle(body, "兄弟按本表", sibling, value => sibling = value,
            tooltip: _sibling.TooltipText);
        var error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; body.AddChild(error);
        dialog.Confirmed += () =>
        {
            try
            {
                if (first is not int a || second is not int b) throw new InvalidDataException("请选择两名武将。");
                Workspace.AddTacticLinkPair(a, b, bonus, sibling);
                _selected = (Math.Min(a, b), Math.Max(a, b));
                _search.Text = ""; Refresh();
                _dialog = null; dialog.QueueFree();
            }
            catch (Exception failure) when (failure is ArgumentException or InvalidDataException or InvalidOperationException)
            { error.Text = failure.Message; }
        };
        dialog.Canceled += () => { _dialog = null; dialog.QueueFree(); };
        AddChild(dialog); dialog.PopupCentered(new(650, 340));
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        { ReportStatus(error.Message, true); SelectPair(); }
    }
}
