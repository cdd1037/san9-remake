using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class InformationMenuView : HudDropdown, IInformationPage
{
    public InformationQuery Query { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action<InformationCategory> Open { get; init; } = null!;
    public Action Back { get; init; } = null!;
    protected override Vector2 MenuSize => new(104, 272);
    public InformationMenuButton? HoverButton => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered());
    public override void _Ready()
    {
        Size = GetViewportRect().Size; MouseFilter = MouseFilterEnum.Stop;
            foreach (var category in InformationQuery.MenuCategories)
            {
                var button = new InformationMenuButton
                {
                    Title = InformationQuery.MenuTitle(category), HintText = Texts.ResolveRich(Query.MenuHintKey(category)),
                    Textures = Textures, Position = new(16, 16 + (int)category * 24), Size = new(72, 24),
                    Disabled = !Query.Available(category)
                };
                button.Pressed += () => { Open(category); };
                AddChild(button);
            }
        _Process(0);
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
        if (input is InputEventMouseButton { Pressed: true } mouse && !MenuBounds.HasPoint(mouse.Position)) Back();
    }
    public override void _Draw() => DrawFrame(Textures);
}
