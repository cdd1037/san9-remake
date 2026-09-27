using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationMenuView : Control, IInformationPage
{
    public InformationQuery Query { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action<InformationCategory> Open { get; init; } = null!;
    public Action Back { get; init; } = null!;
    private readonly Rect2 _menu = new(842, 47, 104, 272);
    public InformationMenuButton? HoverButton => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered());
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
            foreach (var category in InformationQuery.MenuCategories)
            {
                var button = new InformationMenuButton
                {
                    Title = InformationQuery.MenuTitle(category), HintText = Texts.ResolveRich(Query.MenuHintKey(category)),
                    Textures = Textures, Position = new(858, 63 + (int)category * 24), Size = new(72, 24),
                    Disabled = !Query.Available(category)
                };
                button.Pressed += () => { Open(category); };
                AddChild(button);
            }
    }
    public void RefreshData()
    {
        var buttons = GetChildren().OfType<InformationMenuButton>().ToArray();
        for (int i = 0; i < buttons.Length; i++) buttons[i].Disabled = !Query.Available(InformationQuery.MenuCategories[i]);
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Back(); }
    }
    public override void _GuiInput(InputEvent input)
    {
        AcceptEvent();
        if (input is InputEventMouseButton { Pressed: true } mouse && !_menu.HasPoint(mouse.Position)) Back();
    }
    private void Tile(string role, Rect2 rect)
    {
        SkinPainter.Tile(this, Textures[role], rect);
    }
    private void Strip(string prefix, Vector2 at, float width)
    {
        var left = Textures[prefix + "-left"]; var right = Textures[prefix + "-right"];
        DrawTexture(left, at); Tile(prefix + "-middle", new(at + new Vector2(left.GetWidth(), 0), new(width - left.GetWidth() - right.GetWidth(), left.GetHeight())));
        DrawTexture(right, at + new Vector2(width - right.GetWidth(), 0));
    }
    public override void _Draw()
    {
            Tile("information-menu-center", _menu);
            Tile("information-menu-top", new(842, 47, 104, 16)); Tile("information-menu-left", new(842, 47, 16, 272));
            Tile("information-menu-right", new(930, 47, 16, 272)); Tile("information-menu-bottom", new(842, 303, 104, 16));
            DrawTexture(Textures["information-menu-top-left"], new(842, 47)); DrawTexture(Textures["information-menu-top-right"], new(930, 47));
            DrawTexture(Textures["information-menu-bottom-left"], new(842, 303)); DrawTexture(Textures["information-menu-bottom-right"], new(914, 287));
    }
}
