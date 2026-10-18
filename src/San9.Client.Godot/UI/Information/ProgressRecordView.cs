using Godot;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

public partial class ProgressRecordView : JournalPage
{
    protected override string Topic => "records";
    protected override int Offset => _history.Offset;
    protected override int RowCount => _history.Rows.Length;
    public MapMessageLog RecordsLog { get; init; } = null!;
    public Action<ProgressReportMode> ChangeReportMode { get; init; } = null!;
    private readonly ProgressRecordHistory _history = new();
    private readonly List<(Control Row, GameRichTextLabel Text, ColorRect Border, ColorRect Color)> _rows = [];
    private DetailActionButton _summary = null!, _detail = null!;

    public override void _Ready()
    {
        InitializeJournal(669);
        LabelAt("进行记录", new(112, 52), new(100, 24), 24);
        _summary = AddAction("要约", "tab", new(888, 585), new(120, 40), () => SelectMode(ProgressReportMode.Summary), "summary");
        _detail = AddAction("详细", "tab", new(888, 629), new(120, 40), () => SelectMode(ProgressReportMode.Detailed), "detailed");
        for (int slot = 0; slot < ProgressRecordHistory.VisibleRows; slot++)
        {
            var row = new Control { Position = new(117, 133 + slot * 24), Size = new(496, 16), MouseFilter = MouseFilterEnum.Ignore };
            AddChild(row);
            var label = new GameRichTextLabel { Textures = Textures, Position = new(16, 0), Size = new(480, 16), ClipContents = true };
            row.AddChild(label);
            var border = new ColorRect { Position = new(0, 2), Size = new(12, 12), Color = new("080808"), MouseFilter = MouseFilterEnum.Ignore };
            var color = new ColorRect { Position = new(1, 3), Size = new(10, 10), MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(border); row.AddChild(color);
            _rows.Add((row, label, border, color));
        }
        RefreshData();
    }

    private void SelectMode(ProgressReportMode mode)
    {
        ChangeReportMode(mode);
        _history.SelectMode(RecordsLog, mode);
        DrawHistory();
    }

    public override void RefreshData()
    {
        _history.Refresh(RecordsLog, RecordsLog.ReportMode);
        DrawHistory();
    }

    private void DrawHistory()
    {
        _summary.Selected = _history.Mode == ProgressReportMode.Summary;
        _detail.Selected = _history.Mode == ProgressReportMode.Detailed;
        RefreshScroll();
        DrawRows();
    }

    private void DrawRows()
    {
        for (int slot = 0; slot < _rows.Count; slot++)
        {
            var (row, label, border, marker) = _rows[slot];
            int index = _history.Offset + slot;
            row.Visible = index < _history.Rows.Length;
            if (!row.Visible) continue;
            var entry = _history.Rows[index];
            label.Present(entry.Message.BbCode.Replace('\n', ' '), new(16, new Color(Skin.Colors["map-record-normal"]), GameTextEffect.Outline, Colors.Black), San9.Rendering.MessageEmphasisMode.Foreground);
            border.Visible = marker.Visible = entry.ForceColorCode is not null;
            if (entry.ForceColorCode is int color) marker.Color = new(Skin.Colors[$"overview-force-{color}"]);
        }
        SyncScroll();
    }

    protected override void Scroll(int offset)
    {
        _history.Scroll(offset);
        DrawRows();
    }
    public override void _Draw() => new JournalSkinPainter(this, Textures).Draw(626, 212);
}
