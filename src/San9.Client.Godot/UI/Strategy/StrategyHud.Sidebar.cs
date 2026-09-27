using Godot;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private const int SidebarTravel = 198;
    private readonly List<(TextureButton Button, Vector2 Origin)> _sidebarButtons = [];
    private (bool Strategy, int? Corps)? _sidebarContext;
    private bool _sidebarExpanded;
    private int _sidebarOffset;
    private double _sidebarElapsed;
    private bool SidebarMoving => _sidebarOffset != (_sidebarExpanded ? 0 : SidebarTravel);
    private bool SidebarVisible => _sidebarOffset < SidebarTravel;

    private void BuildSidebar()
    {
        foreach (var button in new[] { _large!, _small!, _advanceButton, _facilities!, _officers!, _units! })
            _sidebarButtons.Add((button, button.Position));
        UpdateSidebar(0);
    }

    private void UpdateSidebar(double delta)
    {
        var context = (Turn.IsStrategy, Scenario.PlayerCorps?.Id);
        bool expanded = Interaction.ShowSidebar && context.IsStrategy && context.Item2 is not null && !_commandMenu.Visible;
        bool first = _sidebarContext is null;
        bool contextChanged = !first && _sidebarContext != context;
        bool changed = first || _sidebarExpanded != expanded;
        _sidebarContext = context;
        if (changed)
        {
            _sidebarExpanded = expanded;
            _sidebarElapsed = 0;
            // Context transitions animate; command/target selection and initial layout snap.
            if (!contextChanged || _commandMenu.Visible || _targets is not null)
                _sidebarOffset = expanded ? 0 : SidebarTravel;
        }
        else if (SidebarMoving)
        {
            _sidebarElapsed += delta;
            if (_sidebarElapsed >= 0.1)
            {
                int distance = (int)Math.Floor(_sidebarElapsed * SidebarTravel / 0.2);
                _sidebarElapsed = 0;
                _sidebarOffset = expanded ? Math.Max(0, _sidebarOffset - distance)
                    : Math.Min(SidebarTravel, _sidebarOffset + distance);
                changed = true;
            }
        }

        bool ready = expanded && !SidebarMoving;
        _rosterList.Visible = ready;
        foreach (var (button, origin) in _sidebarButtons)
        {
            button.Position = origin + new Vector2(_sidebarOffset, 0);
            button.Visible = SidebarVisible;
            button.Disabled = !ready || (button == _advanceButton ? !Turn.CanAdvance : !Turn.CanBrowseMap);
        }
        if (changed) QueueRedraw();
    }
}
