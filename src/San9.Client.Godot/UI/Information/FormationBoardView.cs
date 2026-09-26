using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Formation diagram and member cards. The caller owns selection and any deployment draft.</summary>
public partial class FormationBoardView : Control
{
    public FormationBoardData Data { get; init; } = null!;
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    public Action<int>? OfficerActivated { get; init; }
    private readonly List<(Rect2 Rect, bool Commander)> _cards = [];
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        int columns = Data.Presentation?.Columns ?? 3, rows = Data.Presentation?.Rows ?? 3;
        var slots = Data.Presentation?.Slots.ToArray() ?? [];
        var assigned = new List<(FormationMemberView Member, int Column, int Row)>();
        foreach (var member in Data.Members)
        {
            int row = Math.Min(rows - 1, (int)member.Rank);
            var candidates = slots.Where(s => s.Row == row).Select(s => s.Column).ToArray();
            if (candidates.Length == 0) candidates = Enumerable.Range(0, columns).ToArray();
            int count = assigned.Count(p => p.Row == row);
            assigned.Add((member, candidates[count % candidates.Length], row));
        }
        foreach (var group in assigned.GroupBy(p => (p.Column, p.Row)))
        {
            int index = 0;
            foreach (var item in group)
            {
                float cellWidth = Size.X / columns, cellHeight = Size.Y / rows;
                float height = Math.Min(80, cellHeight / group.Count());
                var rect = new Rect2(item.Column * cellWidth + (cellWidth - Math.Min(120, cellWidth)) / 2,
                    item.Row * cellHeight + index++ * height + 4, Math.Min(120, cellWidth), height);
                _cards.Add((rect, item.Member.IsCommander));
                var button = new FormationMemberButton { Position = rect.Position, Size = rect.Size,
                    Member = item.Member };
                button.Pressed += () => OfficerActivated?.Invoke(item.Member.OfficerId);
                AddChild(button);
            }
        }
    }
    public override void _Draw()
    {
        var presentation = Data.Presentation;
        if (presentation?.BackgroundRole is { } role) DrawTextureRect(Textures[role], new(Vector2.Zero, Size), false);
        else DrawRect(new(Vector2.Zero, Size), new Color("171b1b"));
        int columns = presentation?.Columns ?? 3, rows = presentation?.Rows ?? 3;
        if (presentation?.AvailableRole is { } available)
            foreach (var slot in presentation.Slots)
            {
                var texture = Textures[available];
                DrawTextureRectRegion(texture, new(slot.Column * Size.X / columns, slot.Row * Size.Y / rows, Size.X / columns, Size.Y / rows),
                    new(slot.Column * texture.GetWidth() / columns, slot.Row * texture.GetHeight() / rows, texture.GetWidth() / columns, texture.GetHeight() / rows));
            }
        for (int x = 1; x < columns; x++) DrawLine(new(x * Size.X / columns, 0), new(x * Size.X / columns, Size.Y), new Color(0,0,0,.5f));
        for (int y = 1; y < rows; y++) DrawLine(new(0, y * Size.Y / rows), new(Size.X, y * Size.Y / rows), new Color(0,0,0,.5f));
        foreach (var card in _cards)
            DrawTextureRectRegion(Textures[card.Commander ? "formation-commander" : "formation-member"], card.Rect, new(0,0,120,80));
    }
}

/// <summary>Text follows the two interiors of the native 120×80 member plaque.</summary>
public partial class FormationMemberButton : BaseButton
{
    public FormationMemberView Member { get; init; } = null!;
    private readonly GameTextPresenter _text = new();

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        AddText(Member.Tactic, new(24, 8, 72, 28));
        AddText(Member.Name, new(8, 42, 104, 28));
    }

    private void AddText(string text, Rect2 interior)
    {
        var scale = Size / new Vector2(120, 80);
        var label = new Label {
            Text = text, Position = interior.Position * scale, Size = interior.Size * scale,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ClipText = true, MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(label);
        _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = Math.Max(1, (int)(24 * Math.Min(scale.X, scale.Y))) });
    }

    public override void _ExitTree() => _text.Dispose();
}
