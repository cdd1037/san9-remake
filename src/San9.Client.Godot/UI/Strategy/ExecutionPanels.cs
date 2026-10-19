using Godot;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Retained event messages and current pending commands, projected without evaluating messages on repaint.</summary>
public partial class ExecutionPanels : Control
{
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    private const int Travel = 214;
    private static readonly Rect2 Records = new(13, 564, 424, 208);
    private static readonly Rect2 Commands = new(738, 610, 272, 140);
    private readonly GameTextPresenter _text = new();
    private Control _recordsMotion = null!, _commandsMotion = null!;
    private Control _headers = null!;
    private Control _rows = null!;
    public MapMessageLog RecordsLog { get; init; } = null!;
    public event Action<MapCell>? LocationRequested;
    private long _revision = -1;
    private bool _expanded;
    private bool _covered;
    private int _offset = Travel;
    private double _elapsed;
    public bool Expanded => _expanded;
    public bool IsExpanded => _expanded && _offset == 0;
    private bool ReportsVisible => RecordsLog.ReportMode != Settings.ProgressReportMode.Off;
    public void SetReportMode(Settings.ProgressReportMode mode)
    {
        RecordsLog.ReportMode = mode;
        RefreshRecords(); QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var layout = GD.Load<PackedScene>("res://UI/Strategy/ExecutionPanelsLayout.tscn").Instantiate<Control>();
        AddChild(layout);
        _recordsMotion = layout.GetNode<Control>("%RecordsMotion");
        _commandsMotion = layout.GetNode<Control>("%CommandsMotion");
        layout.Resized += QueueRedraw;
        _headers = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _commandsMotion.AddChild(_headers);
        _rows=new Control { MouseFilter=MouseFilterEnum.Ignore }; _commandsMotion.AddChild(_rows);
        int[] x = [28, 88, 124, 184];
        for (int i = 0; i < ExecutionPanelSkin.Columns.Count; i++)
        {
            var label = new Label { Text = Texts.Resolve(ExecutionPanelSkin.HeaderKey(ExecutionPanelSkin.Columns[i])),
                Position = Commands.Position + new Vector2(x[i], 18), Size = new(i == 3 ? 48 : 24, 12),
                MouseFilter = MouseFilterEnum.Ignore };
            _headers.AddChild(label);
            _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = 12 });
        }
        ApplyOffset();
    }

    public bool Append(MapMessageRecord entry)
    {
        if (!RecordsLog.Append(entry)) return false;
        if (!RecordsLog.Allows(entry)) return false;
        RefreshRecords();
        return true;
    }

    public void ExpandImmediately()
    {
        _expanded = true; _offset = 0; _elapsed = 0;
        ApplyOffset();
    }

    private Control? _recordRows;
    private void RefreshRecords()
    {
        if (!IsNodeReady()) return;
        if (_recordRows is not null) { _recordsMotion.RemoveChild(_recordRows); _recordRows.QueueFree(); }
        _recordRows=new Control { MouseFilter=MouseFilterEnum.Ignore, Visible = ReportsVisible }; _recordsMotion.AddChild(_recordRows);
        var visible=RecordsLog.Visible.ToArray();
        for (int i=0;i<visible.Length;i++)
        {
            var entry = visible[i];
            var row = new Control { Position = Records.Position + new Vector2(21,21+16*(10-visible.Length+i)), Size = new(376,16),
                MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = entry.Location is null ? CursorShape.Arrow : CursorShape.PointingHand };
            _recordRows.AddChild(row);
            var label=new GameRichTextLabel { Textures=Textures,Position=new(16,0),Size=new(360,16),
                MouseFilter=MouseFilterEnum.Ignore,ClipContents=true };
            row.AddChild(label);
            void Style(string role) => label.Present(entry.Message.BbCode.Replace('\n',' '),new(12,new Color(Skin.Colors[role]),GameTextEffect.Outline,Colors.Black),
                San9.Rendering.MessageEmphasisMode.Foreground);
            Style("map-record-normal");
            row.MouseEntered += () => Style("map-record-hover");
            row.MouseExited += () => Style("map-record-normal");
            row.GuiInput += input =>
            {
                if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse) return;
                Style(mouse.Pressed ? "map-record-pressed" : "map-record-hover");
                if (!mouse.Pressed && entry.Location is { } location) LocationRequested?.Invoke(location);
                row.AcceptEvent();
            };
            if (entry.ForceColorCode is int color)
            {
                row.AddChild(new ColorRect { Position = new(2,2), Size = new(12,12), Color = new("080808"), MouseFilter = MouseFilterEnum.Ignore });
                row.AddChild(new ColorRect { Position = new(3,3), Size = new(10,10), Color = new(Skin.Colors[$"overview-force-{color}"]), MouseFilter = MouseFilterEnum.Ignore });
            }
        }
    }

    public void Bind(StrategySnapshot view)
    {
        if (_revision==view.World.Revision || !IsNodeReady()) return;
        _revision=view.World.Revision;
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        var rows=ExecutionCommands.Read(view,Texts).TakeLast(5).ToArray();
        int[] x=[14,74,110,166], widths=[60,36,56,74];
        var force = view.World.Forces.Single(f => f.Id == view.PlayerForceId);
        for(int row=0;row<rows.Length;row++)
        {
            var value=rows[row];
            // Columns and decorations are local to the row surface, inset from the panel.
            var origin = Commands.Position + new Vector2(14,35+16*(5-rows.Length+row));
            var container = new Control { Position=origin,Size=new(244,16),MouseFilter=MouseFilterEnum.Ignore };
            _rows.AddChild(container);
            container.AddChild(new ColorRect { Position=new(2,2),Size=new(12,12),Color=new Color("080808"),MouseFilter=MouseFilterEnum.Ignore });
            container.AddChild(new ColorRect { Position=new(3,3),Size=new(10,10),Color=new Color(Skin.Colors[$"overview-force-{force.ColorCode}"]),MouseFilter=MouseFilterEnum.Ignore });
            bool bar=value.FinishesThisXun;
            string[] cells=[value.Officer,value.Command,value.Target,bar ? "" : "-------"];
            for(int column=0;column<cells.Length;column++)
            {
                var label=new Label { Text=cells[column],Position=new(x[column],0),Size=new(widths[column],16),
                    HorizontalAlignment=column==3 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                    ClipText=true,MouseFilter=MouseFilterEnum.Ignore };
                container.AddChild(label); _text.Apply(label,GameTextStyles.CatalogRow with { FontSize=12 });
            }
            if(bar)
            {
                container.AddChild(new TextureRect { Texture=Textures.Get("turn-command-remaining-frame", this),Position=new(166,3),MouseFilter=MouseFilterEnum.Ignore });
                var fill=Textures.Get("turn-command-remaining-fill", this);
                int width=(int)(fill.GetWidth()*Math.Min(10,value.RemainingDays)/10);
                if(width>0) container.AddChild(new TextureRect { Texture=new AtlasTexture { Atlas=fill,Region=new(0,0,width,3) },Position=new(169,6),MouseFilter=MouseFilterEnum.Ignore });
            }
        }
    }

    public void Advance(bool expanded, double delta, bool covered = false)
    {
        _covered = covered;
        Visible = _offset < Travel && !_covered;
        if (_expanded != expanded)
        {
            _expanded = expanded;
            _elapsed = 0;
            ApplyOffset();
            return;
        }
        int target = expanded ? 0 : Travel;
        if (_offset == target) return;
        _elapsed += delta;
        if (_elapsed < 0.1) return;
        int distance = (int)Math.Floor(_elapsed * Travel / 0.5);
        _elapsed = 0;
        _offset = expanded ? Math.Max(target, _offset - distance) : Math.Min(target, _offset + distance);
        ApplyOffset();
    }

    private void ApplyOffset()
    {
        Visible = _offset < Travel && !_covered;
        _recordsMotion.Position = _commandsMotion.Position = new(0, _offset);
        QueueRedraw();
    }

    private Vector2 RecordsOffset => GetGlobalTransform().AffineInverse() * _recordsMotion.GlobalPosition;
    private Vector2 CommandsOffset => GetGlobalTransform().AffineInverse() * _commandsMotion.GlobalPosition;

    public IEnumerable<Rect2> OccupiedAreas()
    {
        if (!IsVisibleInTree()) yield break;
        if (ReportsVisible) yield return new(Records.Position + RecordsOffset, Records.Size);
        yield return new(Commands.Position + CommandsOffset, Commands.Size);
    }

    public bool Covers(Vector2 point) => OccupiedAreas().Any(rect => rect.HasPoint(point));

    public override void _Draw()
    {
        if (ReportsVisible) DrawTexture(Textures.Get("turn-record-panel", this), Records.Position + RecordsOffset);
        DrawTexture(Textures.Get("turn-command-panel", this), Commands.Position + CommandsOffset);
    }

    public override void _ExitTree() => _text.Dispose();
}
