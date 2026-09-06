using Godot;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public async Task RunSmokeAsync()
    {
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task View()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!World.IsVisibleReady)
            {
                Check(World.HasCompleteView, "UI navigation exposed missing terrain.");
                Check(World.LoadError is null && watch.Elapsed.TotalSeconds < 30, "UI view preparation failed.");
                await Frame();
            }
            await Frame();
        }
        async Task Click(Vector2 point)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
            await View();
        }
        await Frame();
        var territoryButton = (StrategyButton)_territory!;
        bool originalSelection = territoryButton.ButtonPressed;
        foreach (bool disabled in new[] { false, true })
        foreach (bool selected in new[] { false, true })
        foreach (bool hovered in new[] { false, true })
        {
            territoryButton.Disabled = disabled;
            territoryButton.SetPressedNoSignal(selected);
            var point = hovered ? new Vector2(726, 28) : new Vector2(512, 400);
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            territoryButton.RefreshVisual();
            var settings = territoryButton.TextSettings;
            for (int repeat = 0; repeat < 3; repeat++) territoryButton.RefreshVisual();
            Check(ReferenceEquals(settings, territoryButton.TextSettings), "Unchanged state recreated text settings.");
            var expectedStyle = GameTextStyles.Button(GameButtonTextRole.Territory, hovered, selected, disabled);
            Check(settings.FontColor == expectedStyle.Foreground && settings.OutlineColor == expectedStyle.Outline &&
                settings.OutlineSize == 1 && settings.ShadowColor.A == 0, "Button text effect mismatch.");
            Check(territoryButton.TextPosition == (selected ? new Vector2(2, 2) : Vector2.Zero),
                "Button text offset drifted or disagreed with selected state.");
            Check(territoryButton.VisualFrame == San9.Rendering.ButtonVisualState.Frame(true, hovered, selected, disabled),
                "Territory background state mismatch.");
        }
        territoryButton.Disabled = false;
        territoryButton.SetPressedNoSignal(originalSelection);
        territoryButton.RefreshVisual();
        Check(GetChildren().OfType<StrategyButton>().Where(b => b.Disabled).All(b => b.VisualFrame == 3),
            "Disabled buttons must use the original fourth frame.");
        Check(_rosterList.Rows.Count == 14, "Facility list must have a fixed ruler slot and thirteen ordinary slots.");
        Check(_message.LabelSettings.OutlineSize == 1 && _message.LabelSettings.ShadowColor.A > 0 &&
            _footer.LabelSettings.OutlineColor == GameTextStyles.For(GameTextRole.Region).Outline,
            "Footer text effects mismatch.");
        Check(_message.LabelSettings.Font == _message.GetThemeFont("font") &&
            territoryButton.TextSettings.Font == GetThemeFont("font"), "Shared text settings changed the project font.");
        var headers = GetChildren().OfType<Label>().Where(l => l.LabelSettings?.FontSize == 24).ToArray();
        Check(headers.Length == 3 && headers.All(l => l.LabelSettings.OutlineSize == 0 && l.LabelSettings.ShadowColor.A == 0 &&
            l.LabelSettings.Font == GetThemeFont("font")),
            "Header must not inherit an extra outline or shadow.");
        int first = _rosterList.Roster.Current.Pinned!.Key.Id;
        var ruler = _rosterList.Rows[0];
        var ordinary = _rosterList.Rows.First(b => b.Visible && b.EntryKey?.Id != first);
        var brown = _rosterList.Rows.First(b => b.Visible && b.TextColor == new Color(Skin.Colors["roster-unavailable"]));
        var oldSelection = Selection.BuildingId;
        void Motion(Vector2 point, MouseButtonMask mask = (MouseButtonMask)0) => GetViewport().PushInput(
            new InputEventMouseMotion { Position = point, GlobalPosition = point, ButtonMask = mask }, true);
        void Left(Vector2 point, bool pressed, bool doubleClick = false) => GetViewport().PushInput(
            new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left,
                Pressed = pressed, DoubleClick = doubleClick }, true);
        var rowPoint = brown.GetGlobalRect().GetCenter();
        Motion(rowPoint); await Frame(); brown.RefreshVisual();
        Check(brown.VisualFrame == 1 && brown.HasFocus(), "Hover failed to acquire retained row focus.");
        var beforeCamera = World.CameraPosition;
        Motion(new(512, 400)); await Frame(); brown.RefreshVisual();
        Check(brown.VisualFrame == 1 && World.CameraPosition == beforeCamera, "Pointer exit cleared focus or hover navigated.");
        Check(string.IsNullOrEmpty(brown.TooltipText), "Facility row must not show a custom detail tooltip.");
        Motion(rowPoint); Left(rowPoint, true); await Frame(); brown.RefreshVisual();
        Check(brown.VisualFrame == 2 && brown.TextOffset == new Vector2(2, 2), "Pressed row frame or offset mismatch.");
        Motion(new(850, 400), MouseButtonMask.Left); await Frame(); brown.RefreshVisual();
        Check(brown.VisualFrame != 2, "Dragging out retained the pressed appearance.");
        Motion(rowPoint, MouseButtonMask.Left); await Frame(); brown.RefreshVisual();
        Check(brown.VisualFrame == 2, "Dragging back failed to restore pressed appearance.");
        Motion(new(850, 400), MouseButtonMask.Left); Left(new(850, 400), false); await View();
        Check(World.CameraPosition == beforeCamera, "Drag-out release activated the facility.");
        await Click(rowPoint); brown.RefreshVisual();
        int last = brown.EntryKey!.Value.Id;
        Check(Selection.BuildingId == oldSelection && brown.VisualFrame == 1 && brown.TextOffset == Vector2.Zero,
            "Facility activation changed map selection or retained a toggle.");
        Check(brown.TextColor == new Color(Skin.Colors["roster-unavailable"]), "Focus changed availability color.");
        var target = World.Package!.Buildings.Single(b => b.Id == last);
        var center = San9.Rendering.MapGeometry.Center(target.X, target.Y);
        Check(World.CameraPosition.DistanceTo(new(center.X, center.Y)) < .01f, "Brown facility camera focus failed.");
        FocusFacility(first); await View();
        Motion(new(512, 400));
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = false }, true);
        await View();
        Check(World.CameraPosition.DistanceTo(new(center.X, center.Y)) < .01f, "Enter did not activate retained list focus.");
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Down, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Down, Pressed = false }, true);
        Check(brown.HasFocus(), "Down incorrectly traversed facility rows.");
        Motion(ordinary.GetGlobalRect().GetCenter()); await Frame(); ruler.RefreshVisual(); brown.RefreshVisual();
        Check(ordinary.HasFocus() && brown.VisualFrame == 0, "Focus transfer left an old row highlighted.");
        await Click(ruler.GetGlobalRect().GetCenter());
        Check(ruler.EntryKey?.Id == first && _rosterList.Roster.Current.Ordinary.All(s => s.Key.Id != first), "Ruler row was not pinned and deduplicated.");
        Left(ruler.GetGlobalRect().GetCenter(), true, true); Left(ruler.GetGlobalRect().GetCenter(), false); await View();
        Check(!_contextMenu.Visible && Selection.BuildingId == oldSelection, "Double click introduced an extra action.");
        GetViewport().PushInput(new InputEventMouseButton { Position = rowPoint, GlobalPosition = rowPoint,
            ButtonIndex = MouseButton.Right, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = rowPoint, GlobalPosition = rowPoint,
            ButtonIndex = MouseButton.Right, Pressed = false }, true);
        await Frame();
        Check(_contextMenu.Visible && _contextMenu.ItemCount == 4 && Enumerable.Range(0, 4).All(_contextMenu.IsItemDisabled),
            "Shared context menu did not expose the current capability boundary.");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
        await Frame(); Check(!_contextMenu.Visible, "Escape did not close the shortcut menu.");
        await Click(new(955, 254));
        Check(_rosterList.Roster.Page == San9.Application.Presentation.RosterPage.Officers &&
            _officers!.ButtonPressed && !_facilities!.ButtonPressed, "Officer tab did not select exclusively.");
        int leaderId = Scenario.Forces.Single(f => f.Id == Scenario.PlayerForceId).LeaderId!.Value;
        Check(_rosterList.Rows[0].EntryKey == new San9.Application.Presentation.RosterKey(
            San9.Application.Presentation.RosterPage.Officers, leaderId), "Officer ruler shortcut mismatch.");
        var officerEntry = _rosterList.Roster.Current.Visible.First();
        await Click(_rosterList.Rows[1].GetGlobalRect().GetCenter());
        var officerLocation = Scenario.Settlements.Single(s => s.Id == officerEntry.SettlementId);
        var officerCenter = San9.Rendering.MapGeometry.Center(officerLocation.X, officerLocation.Y);
        Check(World.CameraPosition.DistanceTo(new(officerCenter.X, officerCenter.Y)) < .01f && Selection.BuildingId == oldSelection,
            "Officer activation did not navigate independently of map selection.");
        var bar = _rosterList.ScrollBar;
        Check(bar.Visible, "Overflow did not show the scrollbar.");
        void WheelAt(Vector2 point, MouseButton direction)
        {
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(
                new InputEventMouseButton { Position = point, GlobalPosition = point, Pressed = pressed,
                    ButtonIndex = direction, Factor = 10 }, true);
        }
        WheelAt(new(940, 350), MouseButton.WheelDown); await Frame();
        Check(_rosterList.Roster.Current.Offset == 1, "Row wheel magnitude moved more than one entry.");
        WheelAt(bar.GetGlobalRect().GetCenter(), MouseButton.WheelDown); await Frame();
        Check(_rosterList.Roster.Current.Offset == 2, "Scrollbar wheel did not use discrete row steps.");
        await Click(new(1000, 578));
        Check(_rosterList.Roster.Current.Offset == 3, "Scrollbar down arrow did not advance one row.");
        await Click(new(1000, 550));
        Check(_rosterList.Roster.Current.Offset == Math.Min(16, _rosterList.Roster.Current.MaximumOffset), "Scrollbar track did not advance thirteen rows.");
        var thumbPoint = new Vector2(1000, bar.GlobalPosition.Y + bar.Size.Y * ((float)_rosterList.Roster.Current.Offset / _rosterList.Roster.Current.Ordinary.Count) + 5);
        Motion(thumbPoint); Left(thumbPoint, true);
        Motion(new(1000, 270), MouseButtonMask.Left); Left(new(1000, 270), false); await Frame();
        Check(_rosterList.Roster.Current.Offset == 0, "Dragging thumb to top did not clamp to first row.");
        Check(_rosterList.Rows[0].EntryKey?.Id == leaderId, "Scrolling moved the ruler shortcut.");
        _rosterList.Wheel(1);
        await Click(new(927, 284));
        Check(_rosterList.Roster.Page == San9.Application.Presentation.RosterPage.Facilities && _facilities!.ButtonPressed && !_officers!.ButtonPressed,
            "Facility tab did not restore exclusively.");
        await Click(new(955, 254));
        Check(_rosterList.Roster.Current.Offset == 1, "Officer tab failed to retain its scroll offset.");
        await Click(new(955, 254));
        Check(_officers!.ButtonPressed, "Clicking current tab deselected it.");
        await Click(new(927, 284));
        Check(!bar.Visible && _rosterList.Roster.Current.Offset == 0, "Refresh did not hide and clamp the scrollbar.");
        ruler.GrabFocus();
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Tab, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Tab, Pressed = false }, true);
        await Frame(); Check(ordinary.HasFocus(), "Tab did not traverse the next visible control.");
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Tab, Pressed = true, ShiftPressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Tab, Pressed = false, ShiftPressed = true }, true);
        await Frame(); Check(ruler.HasFocus(), "Shift+Tab did not restore previous focus.");
        FocusFacility(first); await View();
        var old = Selection.BuildingId;
        await Click(new(970, 590));
        Check(Selection.BuildingId == old, "Sidebar input leaked to map.");
        float zoom = World.ZoomFactor;
        await Click(new(979, 628));
        Check(World.ZoomFactor == .5f && _small!.ButtonPressed, "Small map button failed.");
        await Click(new(922, 649));
        Check(World.ZoomFactor == 1 && _large!.ButtonPressed, "Large map button failed.");
        ((StrategyButton)_large!).RefreshVisual();
        ((StrategyButton)_small!).RefreshVisual();
        Check(((StrategyButton)_large).TextPosition == new Vector2(2, 2) &&
            ((StrategyButton)_small).TextPosition == Vector2.Zero, "Zoom button text did not follow selection.");
        bool boundaries = World.ShowBoundaries;
        await Click(new(726, 28));
        Check(World.ShowBoundaries != boundaries && _territory!.ButtonPressed == World.ShowBoundaries, "Boundary toggle failed.");
        await Click(new(726, 28));
        Check(World.ShowBoundaries == boundaries, "Boundary toggle did not restore.");
        var mini = San9.Rendering.MinimapGeometry.FromGrid(120, 95);
        await Click(_minimap!.Position + new Vector2((float)mini.X * _minimap.Size.X, (float)mini.Y * _minimap.Size.Y));
        var expected = San9.Rendering.MapGeometry.Center(120, 95);
        Check(World.CameraPosition.DistanceTo(new(expected.X, expected.Y)) < .1f, "GUI minimap navigation failed.");
        var camera = World.CameraPosition;
        Check(!_minimap.Navigate(Vector2.Zero) && World.CameraPosition == camera, "Minimap decoration navigated.");
        await Click(new(974, 698));
        Check(Scenario.Day == 1 && Selection.BuildingId == old, "Disabled advance changed initial state.");
        FocusFacility(first); await View();
        foreach (int buildingId in new[] { 15, 99, 100 })
        {
            var facility = World.Package!.Buildings.Single(b => b.Id == buildingId);
            var ground = San9.Rendering.MapGeometry.Center(facility.X, facility.Y);
            World.FocusWorld(new(ground.X, ground.Y));
            await View();
            var point = World.WorldToViewport(new(ground.X, ground.Y));
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            UpdateHover(point);
            var expectedRegion = San9.Application.Presentation.RegionHoverInfo.Resolve(
                World.Package.Squares[facility.Y * 200 + facility.X], Scenario, Texts);
            Check(_regionVisible && _footer.Text == expectedRegion.Region && _footerCity.Text == expectedRegion.City,
                "Mouse region and parent city mismatch.");
            Selection.SelectBuilding(first);
            Check(_footer.Text == expectedRegion.Region, "Selection overwrote hovered region.");
        }
        foreach (var point in new[] { new Vector2(-10, 400), new Vector2(970, 400), new Vector2(800, 100) })
        {
            UpdateHover(point);
            Check(!_regionVisible && !_footer.Visible, "UI or black bar retained the region panel.");
        }
        World.SetZoom(1); FocusFacility(first); await View();
        await Click(new(512, 384));
        Check(Selection.BuildingId == first, "Map click did not select facility.");
        Check(IsBlockedPoint(new(500, 25)) && !IsBlockedPoint(new(500, 400)), "UI hit regions are incorrect.");
        World.SetZoom(1); FocusFacility(first); Selection.Clear();
        GD.Print("UI_SMOKE_OK viewport-dispatched facility focus, press, drag cancel/reentry, ruler, Enter, context menu, discrete scrolling and thumb drag, Tab traversal, map selection isolation, minimap, zoom");
    }
    public async Task CaptureFacilityStatesAsync(Func<string, Task> capture)
    {
        var row = _rosterList.Rows.First(b => b.Visible && b.TextColor == new Color(Skin.Colors["roster-unavailable"]));
        var point = row.GetGlobalRect().GetCenter();
        void Motion(Vector2 at, MouseButtonMask mask = (MouseButtonMask)0) => GetViewport().PushInput(
            new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = mask }, true);
        void Left(Vector2 at, bool pressed) => GetViewport().PushInput(new InputEventMouseButton
            { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _territory!.GrabFocus(); Motion(new(512, 400));
        await capture("-facility-normal");
        Motion(point); await capture("-facility-focus");
        Motion(new(512, 400)); await capture("-facility-retained-focus");
        Motion(point); Left(point, true); await capture("-facility-pressed");
        Motion(new(850, 400), MouseButtonMask.Left); Left(new(850, 400), false);
        await capture("-facility-cancelled");
        _territory.GrabFocus();
        SelectRosterPage(San9.Application.Presentation.RosterPage.Officers);
        _rosterList.Roster.Current.ScrollTo(0); _rosterList.Wheel(0);
        Motion(new(512, 400)); await capture("-scroll-top");
        _rosterList.Wheel(1); await capture("-scroll-middle");
        _rosterList.Roster.Current.ScrollTo(_rosterList.Roster.Current.MaximumOffset);
        _rosterList.Wheel(0); await capture("-scroll-bottom");
        SelectRosterPage(San9.Application.Presentation.RosterPage.Facilities);
    }

}
