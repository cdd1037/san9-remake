using San9.Application.Presentation;

namespace San9.Client.Godot.Settings;

public enum SelectionViewGroup { Forces, ExplorationRegions, Cities, Facilities, StrategyUnits, MilitaryTargets }
public enum SelectionViewMode { Map, List }

public sealed record SelectionViewPreferences(SelectionViewMode Forces, SelectionViewMode ExplorationRegions,
    SelectionViewMode Cities, SelectionViewMode Facilities, SelectionViewMode StrategyUnits, SelectionViewMode MilitaryTargets)
{
    public static SelectionViewPreferences Default { get; } = new(SelectionViewMode.Map, SelectionViewMode.Map,
        SelectionViewMode.Map, SelectionViewMode.Map, SelectionViewMode.Map, SelectionViewMode.Map);

    public SelectionViewMode Get(SelectionViewGroup group) => group switch
    {
        SelectionViewGroup.Forces => Forces, SelectionViewGroup.ExplorationRegions => ExplorationRegions,
        SelectionViewGroup.Cities => Cities, SelectionViewGroup.Facilities => Facilities,
        SelectionViewGroup.StrategyUnits => StrategyUnits, SelectionViewGroup.MilitaryTargets => MilitaryTargets,
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };

    public SelectionViewPreferences With(SelectionViewGroup group, SelectionViewMode mode) => group switch
    {
        SelectionViewGroup.Forces => this with { Forces = mode },
        SelectionViewGroup.ExplorationRegions => this with { ExplorationRegions = mode },
        SelectionViewGroup.Cities => this with { Cities = mode },
        SelectionViewGroup.Facilities => this with { Facilities = mode },
        SelectionViewGroup.StrategyUnits => this with { StrategyUnits = mode },
        SelectionViewGroup.MilitaryTargets => this with { MilitaryTargets = mode },
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };
}

/// <summary>Client navigation habits; no world identity, selection, draft, or camera position is persisted.</summary>
public sealed record UiPreferences(int SaveLoadPage, RosterPage RosterPage, EdgeScrollMode EdgeScrollMode,
    bool ShowForces, bool ShowFacilities, bool ShowUnits, bool ShowBoundaries, SelectionViewPreferences SelectionViews)
{
    public WindowPlacement? Window { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool AutoAssignOfficerFlag { get; init; } = true;
    public static UiPreferences Default { get; } = new(0, RosterPage.Facilities, EdgeScrollMode.Hover,
        true, true, true, true, SelectionViewPreferences.Default);

    public void Validate()
    {
        Window?.Validate();
        if (SaveLoadPage < 0 || !Enum.IsDefined(RosterPage) || !Enum.IsDefined(EdgeScrollMode) || SelectionViews is null ||
            Enum.GetValues<SelectionViewGroup>().Any(group => !Enum.IsDefined(SelectionViews.Get(group))))
            throw new InvalidDataException("Invalid UI preferences.");
    }
}

/// <summary>Normal desktop client bounds, retained while maximized, minimized, or fullscreen.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0) throw new InvalidDataException("Invalid window dimensions.");
    }
}
