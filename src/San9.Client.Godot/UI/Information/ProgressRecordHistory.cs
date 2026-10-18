using San9.Application.Presentation;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

/// <summary>A chronological text viewport over captured publications.</summary>
public sealed class ProgressRecordHistory
{
    public const int VisibleRows = 22;
    public MapMessageRecord[] Rows { get; private set; } = [];
    public int Offset { get; private set; }
    public ProgressReportMode Mode { get; private set; }

    public void Refresh(MapMessageLog log, ProgressReportMode mode)
    {
        Mode = mode == ProgressReportMode.Off ? ProgressReportMode.Detailed : mode;
        Rows = log.Entries.Where(r => Mode == ProgressReportMode.Detailed || r.Importance == MapReportImportance.Summary).ToArray();
        Scroll(Offset);
    }

    public void SelectMode(MapMessageLog log, ProgressReportMode mode)
    {
        Offset = 0;
        Refresh(log, mode);
    }

    public void Scroll(int offset) => Offset = Math.Clamp(offset, 0, Math.Max(0, Rows.Length - VisibleRows));
}
