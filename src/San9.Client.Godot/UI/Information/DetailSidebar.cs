using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Binds a detail page's candidate viewport without owning its selection state.</summary>
public partial class DetailSidebar : Control
{
    public DetailSelectionState State { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Func<int, string> ReadName { get; init; } = null!;
    public Action<int> Activated { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public int Rows { get; init; }
    public Rect2 NameBounds { get; init; }
    private readonly List<DetailNameButton> _names = [];
    private SkinnedScrollBar _scroll = null!;
    private bool _refreshing;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        State.SetVisibleRows(Rows);
        for (int row = 0; row < Rows; row++)
        {
            int slot = row;
            var button = new DetailNameButton { Textures = Textures,
                Position = NameBounds.Position + new Vector2(0, row * 20), Size = NameBounds.Size };
            button.MouseEntered += () => { State.PointAt(State.Candidates[State.Offset + slot]); Refresh(); };
            button.Pressed += () => Activated(State.Candidates[State.Offset + slot]);
            AddChild(button); _names.Add(button);
        }
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures),
            Position = new(990,124), Size = new(16, Rows * 20 - 2), ContextRequested = Closed };
        AddChild(_scroll);
        _scroll.Range.Page = Rows;
        _scroll.Range.ValueChanged += value =>
        {
            if (_refreshing) return;
            State.Scroll((int)value);
            Refresh();
        };
        Refresh();
    }

    public void Refresh()
    {
        _refreshing = true;
        for (int row = 0; row < _names.Count; row++)
        {
            int index = State.Offset + row;
            var button = _names[row];
            button.Visible = index < State.Candidates.Count;
            if (button.Visible)
            {
                int id = State.Candidates[index];
                button.Bind(ReadName(id), State.FocusedId == id);
            }
        }
        _scroll.Range.MaxValue = Math.Max(Rows, State.Candidates.Count);
        _scroll.Range.Value = State.Offset;
        _scroll.Visible = State.Candidates.Count > Rows;
        _scroll.UpdateArrows();
        _refreshing = false;
    }

    // The page handles close/help first and forwards navigation explicitly.
    public void HandleInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            GetViewport().SetInputAsHandled();
            State.Scroll(State.Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1));
            Refresh();
        }
        else if (input is InputEventKey { Pressed: true } key)
        {
            if (key.Keycode is Key.Up or Key.Down)
            {
                GetViewport().SetInputAsHandled();
                State.MoveFocus(key.Keycode == Key.Down ? 1 : -1);
                Refresh();
            }
            else if (key.Keycode == Key.Enter && State.FocusedId is int id)
            {
                GetViewport().SetInputAsHandled();
                Activated(id);
            }
        }
    }
}
