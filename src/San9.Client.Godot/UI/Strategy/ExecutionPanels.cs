using Godot;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Execution record and pending-command surfaces; row content is not connected yet.</summary>
public partial class ExecutionPanels : Control
{
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    private const int Travel = 214;
    private static readonly Rect2 Records = new(13, 564, 424, 208);
    private static readonly Rect2 Commands = new(738, 610, 272, 140);
    private readonly GameTextPresenter _text = new();
    private Control _headers = null!;
    private bool _expanded;
    private int _offset = Travel;
    private double _elapsed;
    public bool Expanded => _expanded;
    public bool IsExpanded => _expanded && _offset == 0;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new(1024, 768);
        _headers = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_headers);
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

    public void Advance(bool expanded, double delta)
    {
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
        Visible = _offset < Travel;
        _headers.Position = new(0, _offset);
        QueueRedraw();
    }

    public bool Covers(Vector2 point) => Visible &&
        (new Rect2(Records.Position + new Vector2(0, _offset), Records.Size).HasPoint(point) ||
         new Rect2(Commands.Position + new Vector2(0, _offset), Commands.Size).HasPoint(point));

    public override void _Draw()
    {
        DrawTexture(Textures["turn-record-panel"], Records.Position + new Vector2(0, _offset));
        DrawTexture(Textures["turn-command-panel"], Commands.Position + new Vector2(0, _offset));
    }

    public override void _ExitTree() => _text.Dispose();
}
