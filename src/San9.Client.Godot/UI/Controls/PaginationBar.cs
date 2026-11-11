using Godot;

namespace San9.Client.Godot;

/// <summary>Page navigation with one-based input and zero-based page indices.</summary>
internal partial class PaginationBar : HBoxContainer
{
    private readonly Button _previous = new() { Text = "上一页", CustomMinimumSize = new(0, 34) };
    private readonly Button _next = new() { Text = "下一页", CustomMinimumSize = new(0, 34) };
    private readonly SpinBox _pageNumber = new()
    {
        Name = "PageNumber", MinValue = 1, MaxValue = 1, Step = 1, Rounded = true,
        CustomMinimumSize = new(88, 34),
        TooltipText = "输入页码后按回车跳转，也可用上下箭头调整。"
    };
    private readonly Label _counter = new();
    private string _summary = "";
    private bool _updating;

    public int PageIndex { get; private set; }
    public int PageCount { get; private set; } = 1;
    public event Action<int>? PageChanged;

    public PaginationBar()
    {
        AddChild(_previous);
        var center = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = AlignmentMode.Center
        };
        center.AddThemeConstantOverride("separation", 8);
        AddChild(center);
        center.AddChild(_pageNumber);
        center.AddChild(_counter);
        AddChild(_next);
        _previous.Pressed += () => Navigate(PageIndex - 1);
        _next.Pressed += () => Navigate(PageIndex + 1);
        _pageNumber.ValueChanged += value =>
        {
            if (!_updating) Navigate((int)value - 1);
        };
        SetPage(0, 1);
    }

    /// <summary>Synchronizes the displayed state without raising PageChanged.</summary>
    public void SetPage(int pageIndex, int pageCount, string summary = "")
    {
        PageCount = Math.Max(1, pageCount);
        PageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        _summary = summary;
        _updating = true;
        _pageNumber.MaxValue = PageCount;
        _pageNumber.Value = PageIndex + 1;
        _pageNumber.Editable = PageCount > 1;
        _updating = false;
        _previous.Disabled = PageIndex == 0;
        _next.Disabled = PageIndex + 1 == PageCount;
        _counter.Text = $"/ {PageCount} 页" + (summary.Length == 0 ? "" : " · " + summary);
    }

    private void Navigate(int pageIndex)
    {
        int target = Math.Clamp(pageIndex, 0, PageCount - 1);
        if (target == PageIndex) return;
        SetPage(target, PageCount, _summary);
        PageChanged?.Invoke(target);
    }
}
