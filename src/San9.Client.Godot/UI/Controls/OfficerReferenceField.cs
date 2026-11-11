using Godot;

namespace San9.Client.Godot;

/// <summary>A named officer reference with a searchable, single-selection dialog.</summary>
internal static class OfficerReferenceField
{
    public static Button Add<T>(Node parent, string title, IReadOnlyList<(string Label, T Value)> candidates,
        T current, Action<T> changed, string name, string tooltip, string unavailableLabel = "当前人物不可用", bool showCaption = true)
    {
        if (showCaption) ContentEditorControls.Caption(parent, title);
        int selected = candidates.ToList().FindIndex(c => EqualityComparer<T>.Default.Equals(c.Value, current));
        var button = new Button
        {
            Name = name, Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(112, 34),
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        void UpdateLabel()
        {
            button.Text = selected >= 0 ? candidates[selected].Label : unavailableLabel;
            button.TooltipText = button.Text + "\n" + tooltip;
        }
        UpdateLabel(); parent.AddChild(button);
        button.Pressed += () =>
        {
            var dialog = new ConfirmationDialog
            { Title = "选择" + title, OkButtonText = "确定", CancelButtonText = "取消", Exclusive = true };
            var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); dialog.AddChild(column);
            var search = new LineEdit { PlaceholderText = "搜索姓名", Name = name + "Search" }; column.AddChild(search);
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
                    if (i != 0 && !candidates[i].Label.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    visible.Add(i); list.AddItem(candidates[i].Label);
                    if (i == selected) { pending = i; list.Select(visible.Count - 1); }
                }
                dialog.GetOkButton().Disabled = pending < 0;
            }
            void Apply()
            {
                if (pending < 0) return;
                selected = pending; UpdateLabel(); dialog.QueueFree();
                changed(candidates[selected].Value);
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
