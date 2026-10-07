using Godot;

namespace San9.Client.Godot;

/// <summary>Three shaped lines per page, retaining one rich-text document and its styles.</summary>
public partial class GamePagedText : Control
{
    public TextureScope Textures { get; init; } = null!;
    public float UnwrappedWidth { get; private set; }
    public float MaximumPageHeight { get; private set; }
    public float MaximumPageLayoutHeight { get; private set; }
    public int PageCount => _pages.Count;
    public int PageIndex { get; private set; }
    private GameRichTextLabel _label = null!;
    private readonly List<(float Offset, float Height, int EndCharacter)> _pages = [];

    public GamePagedText()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Present(string markup, GameTextStyle style)
    {
        if (_label is null)
        {
            _label = new() { Textures = Textures, VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping };
            AddChild(_label);
        }
        _label.VisibleCharacters = -1;
        _label.Position = Vector2.Zero;
        _label.AutowrapMode = TextServer.AutowrapMode.Off;
        _label.Present(markup, style);
        UnwrappedWidth = _label.GetContentWidth();
    }

    public void Paginate(float width)
    {
        _label.Size = new(width, 24);
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        int lines = _label.GetLineCount();
        _label.Size = new(width, _label.GetContentHeight());
        // GetLineHeight includes line separation. Negative separation controls baseline
        // spacing, but must not remove the last line's descent from the clipping bounds.
        int finalLineOverflow = Math.Max(0, -_label.GetThemeConstant("line_separation"));
        _pages.Clear();
        for (int first = 0; first < lines; first += 3)
        {
            int last = Math.Min(first + 2, lines - 1);
            float offset = _label.GetLineOffset(first);
            _pages.Add((offset, _label.GetLineOffset(last) - offset + _label.GetLineHeight(last),
                Math.Min(_label.GetLineRange(last).Y, _label.GetTotalCharacterCount())));
        }
        // Empty messages still have one acknowledgement page.
        if (_pages.Count == 0) _pages.Add((0, 24, 0));
        MaximumPageLayoutHeight = _pages.Max(page => page.Height);
        MaximumPageHeight = MaximumPageLayoutHeight + finalLineOverflow;
        Size = new(width, MaximumPageHeight);
        PageIndex = 0;
        ShowPage();
    }

    public bool Advance()
    {
        if (PageIndex + 1 >= _pages.Count) return false;
        PageIndex++;
        ShowPage();
        return true;
    }

    private void ShowPage()
    {
        var page = _pages[PageIndex];
        _label.Position = new(0, -page.Offset);
        _label.VisibleCharacters = page.EndCharacter;
    }
}
