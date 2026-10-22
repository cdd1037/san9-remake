using Godot;

namespace San9.Client.Godot;

/// <summary>The journal help topic and its original report subtopic.</summary>
public partial class JournalHelpView : Control
{
    public TextureScope Textures { get; init; } = null!;
    public Func<string, string> Hint { get; init; } = null!;
    public string Topic { get; init; } = "";
    public Action Closed { get; init; } = null!;
    private GameRichTextLabel _title = null!, _body = null!;
    private DetailActionButton? _subtopic;
    private bool _detail;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ViewportFill { Color = new(0, 0, 0, .45f) });
        _title = new GameRichTextLabel { Textures = Textures, Position = new(292, 170), Size = new(440, 36) }; AddChild(_title);
        _body = new GameRichTextLabel { Textures = Textures, Position = new(292, 220), Size = new(440, 292), ScrollActive = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Stop }; AddChild(_body);
        if (Topic == "records")
        {
            _subtopic = new DetailActionButton { Textures = Textures, Title = Hint("records-help-detail-title"), Role = "tab", Position = new(440, 522), Size = new(120, 40) };
            _subtopic.Pressed += () => { _detail = !_detail; Refresh(); }; AddChild(_subtopic);
        }
        var close = new DetailActionButton { Textures = Textures, Title = "关闭", Role = "close", Position = new(458, 574), Size = new(108, 44) };
        close.Pressed += Closed; AddChild(close); Refresh();
    }
    private void Refresh()
    {
        string role = _detail ? "records-help-detail" : Topic + "-help";
        _title.Present(Hint(role + "-title"), GameTextStyles.CatalogRow with { FontSize = 24 });
        _body.Present(Hint(role), GameTextStyles.CatalogRow with { FontSize = 16 });
        if (_subtopic is not null) _subtopic.Selected = _detail;
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _Draw()
    {
        new DetailSkinPainter(this, Textures).Panel(new(264, 144, 496, 504));
    }
}
