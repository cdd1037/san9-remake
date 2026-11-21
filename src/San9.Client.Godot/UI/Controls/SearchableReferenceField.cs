using Godot;

namespace San9.Client.Godot;

/// <summary>A named reference with a searchable, single-selection dialog.</summary>
internal static class SearchableReferenceField
{
    public static Button Add<T>(Node parent, string title, IReadOnlyList<(string Label, T Value)> candidates,
        T current, Action<T> changed, string name, string tooltip, string unavailableLabel = "当前引用不可用", bool showCaption = true,
        bool keepFirstVisible = true)
    {
        var label = candidates.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, current)).Label;
        return Add(parent, title, () => candidates, current, label ?? unavailableLabel, changed, name, tooltip, showCaption, keepFirstVisible);
    }

    public static Button Add<T>(Node parent, string title, Func<IReadOnlyList<(string Label, T Value)>> choices,
        T current, string currentLabel, Action<T> changed, string name, string tooltip, bool showCaption = true,
        bool keepFirstVisible = true)
    {
        if (showCaption) ContentEditorControls.Caption(parent, title);
        var button = new Button
        {
            Name = name, Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(112, 34),
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        void UpdateLabel(string label)
        {
            button.Text = label;
            button.TooltipText = button.Text + "\n" + tooltip;
        }
        UpdateLabel(currentLabel); parent.AddChild(button);
        button.Pressed += () =>
        {
            var candidates = choices();
            var dialog = new ConfirmationDialog
            { Title = "选择" + title, OkButtonText = "确定", CancelButtonText = "取消", Exclusive = true };
            var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); dialog.AddChild(column);
            var search = new LineEdit { PlaceholderText = "搜索名称或 ID", Name = name + "Search" }; column.AddChild(search);
            var list = new ItemList
            { CustomMinimumSize = new(540, 350), SizeFlagsVertical = Control.SizeFlags.ExpandFill, Name = name + "Candidates" };
            column.AddChild(list);
            var visible = new List<int>();
            int pending = -1;
            void Refresh()
            {
                visible.Clear(); list.Clear(); pending = -1;
                string query = search.Text.Trim();
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!(keepFirstVisible && i == 0) && !candidates[i].Label.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    visible.Add(i); list.AddItem(candidates[i].Label);
                    if (EqualityComparer<T>.Default.Equals(candidates[i].Value, current)) { pending = i; list.Select(visible.Count - 1); }
                }
                dialog.GetOkButton().Disabled = pending < 0;
            }
            void Apply()
            {
                if (pending < 0) return;
                var choice = candidates[pending];
                current = choice.Value; UpdateLabel(choice.Label); dialog.QueueFree();
                changed(choice.Value);
            }
            search.TextChanged += _ => Refresh();
            list.ItemSelected += index => { pending = visible[(int)index]; dialog.GetOkButton().Disabled = false; };
            list.ItemActivated += index => { pending = visible[(int)index]; Apply(); };
            dialog.Confirmed += Apply; dialog.Canceled += () => dialog.QueueFree();
            button.AddChild(dialog); Refresh(); dialog.PopupCentered(new(600, 470)); search.GrabFocus();
        };
        return button;
    }
}
