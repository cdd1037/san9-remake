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
        Check(_facilityButtons.Count == Selection.PlayerFacilities.Length, "Facility filter mismatch.");
        Check(_message.LabelSettings.OutlineSize == 1 && _message.LabelSettings.ShadowColor.A > 0 &&
            _footer.LabelSettings.OutlineColor == GameTextStyles.For(GameTextRole.Region).Outline,
            "Footer text effects mismatch.");
        Check(_message.LabelSettings.Font == _message.GetThemeFont("font") &&
            territoryButton.TextSettings.Font == GetThemeFont("font"), "Shared text settings changed the project font.");
        var headers = GetChildren().OfType<Label>().Where(l => l.LabelSettings?.FontSize == 24).ToArray();
        Check(headers.Length == 3 && headers.All(l => l.LabelSettings.OutlineSize == 0 && l.LabelSettings.ShadowColor.A == 0 &&
            l.LabelSettings.Font == GetThemeFont("font")),
            "Header must not inherit an extra outline or shadow.");
        int first = _facilityButtons.Keys.Min(), last = _facilityButtons.Keys.Max();
        _scroll.EnsureControlVisible(_facilityButtons[first]); await Frame();
        await Click(_facilityButtons[first].GetGlobalRect().GetCenter());
        Check(Selection.BuildingId == first, "GUI facility selection failed.");
        var selectedButton = _facilityButtons[first];
        Check(selectedButton.GetThemeColor("font_color") == selectedButton.GetThemeColor("font_hover_color") &&
            selectedButton.GetThemeColor("font_color") == selectedButton.GetThemeColor("font_pressed_color") &&
            selectedButton.GetThemeConstant("outline_size") == 0, "Facility state colors inherited default theme effects.");
        var listSize = _scroll.Size; _scroll.Size = new(listSize.X, 120); await Frame();
        _scroll.EnsureControlVisible(_facilityButtons[last]); await Frame();
        Check(_scroll.GetVScrollBar().Value > 0, "Overflowing facility list did not scroll.");
        await Click(_facilityButtons[last].GetGlobalRect().GetCenter());
        Check(Selection.BuildingId == last, "Scrolled facility selection failed.");
        var target = World.Package!.Buildings.Single(b => b.Id == last);
        var center = San9.Rendering.MapGeometry.Center(target.X, target.Y);
        Check(World.CameraPosition.DistanceTo(new(center.X, center.Y)) < .01f, "Facility camera focus failed.");
        _scroll.Size = listSize; FocusFacility(first); await View();
        Check(_scroll.GetVScrollBar().Value == 0, "Map selection did not reveal first facility.");
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
        GD.Print("UI_SMOKE_OK viewport-dispatched facility selection, scrolling, map selection, minimap, zoom, disabled actions, input isolation");
    }
}
