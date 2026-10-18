using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>The force chronicle is a dated text journal, independent of catalog sorting.</summary>
public partial class InformationJournalView : JournalPage
{
    private const int VisibleRows = ProgressRecordHistory.VisibleRows;
    public Func<ChroniclePresentation> Read { get; init; } = null!;
    protected override string Topic => "chronicle";
    protected override int Offset => _offset;
    protected override int RowCount => _data.Lines.Length;
    private readonly List<(Control Row, Label Year, Label Month, Label Xun, GameRichTextLabel Text, ColorRect Border, ColorRect Color)> _rows = [];
    private ChroniclePresentation _data = null!;
    private Label _title = null!;
    private int _offset;

    public override void _Ready()
    {
        InitializeJournal(845);
        _title = LabelAt("", new(112, 52), new(720, 28), 24);
        AddAction("输入年表", "chronicle-input", new(888, 629), new(120, 40), null, "input");
        for (int slot = 0; slot < VisibleRows; slot++)
        {
            var row = new Control { Position = new(117, 133 + slot * 24), Size = new(720, 16), MouseFilter = MouseFilterEnum.Ignore }; AddChild(row);
            var year = LabelAt("", Vector2.Zero, new(40, 16), parent: row);
            var month = LabelAt("", new(40, 0), new(32, 16), parent: row);
            var xun = LabelAt("", new(72, 0), new(32, 16), parent: row);
            var text = new GameRichTextLabel { Textures = Textures, Position = new(123, 0), Size = new(597, 16), ClipContents = true }; row.AddChild(text);
            var border = new ColorRect { Position = new(107, 2), Size = new(12, 12), Color = new("080808"), MouseFilter = MouseFilterEnum.Ignore };
            var color = new ColorRect { Position = new(108, 3), Size = new(10, 10), MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(border); row.AddChild(color); _rows.Add((row, year, month, xun, text, border, color));
        }
        RefreshData();
    }

    public override void RefreshData()
    {
        _data = Read();
        _title.Text = _data.RulerName + "军年表";
        RefreshScroll();
        Scroll(_offset); QueueRedraw();
    }
    protected override void Scroll(int offset)
    {
        _offset = Math.Clamp(offset, 0, Math.Max(0, _data.Lines.Length - VisibleRows));
        for (int slot = 0; slot < _rows.Count; slot++)
        {
            var (row, year, month, xun, text, border, marker) = _rows[slot];
            int index = _offset + slot; row.Visible = index < _data.Lines.Length;
            if (!row.Visible) continue;
            var entry = _data.Lines[index]; var date = _data.DateAt(index);
            year.Text = date.Year; month.Text = date.Month.PadLeft(date.Month.Length == 0 ? 0 : 3); xun.Text = date.Xun;
            text.Present(entry.BbCode.Replace('\n', ' '), new(16, new Color(Skin.Colors["map-record-normal"]), GameTextEffect.Outline, Colors.Black), San9.Rendering.MessageEmphasisMode.Foreground);
            border.Visible = marker.Visible = entry.ForceColorCode is not null;
            if (entry.ForceColorCode is int color) marker.Color = new(Skin.Colors[$"overview-force-{color}"]);
        }
        SyncScroll();
    }
    public override void _Draw()
    {
        if (_data is not null) new JournalSkinPainter(this, Textures).Draw(802, 116 + 24 * (_data.RulerName.Length + 3));
    }
}
