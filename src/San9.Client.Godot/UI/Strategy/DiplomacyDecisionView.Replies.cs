using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class DiplomacyDecisionView
{
    private void BuildReply()
    {
        var data = Draft.Diplomacy!;
        bool gift = Draft.Role == "gift";
        string Number(long number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string idle = Draft.Prompt.BbCode;
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        var window = new CommandWindow
        {
            Texts = Texts, Textures = Textures, Title = Text("title.reply-" + Draft.Role),
            Position = gift ? new(364, 270) : new(296, 324), Size = gift ? new(296, 228) : new(432, 120),
            Help = () => message.SetMessage(idle), Closed = () => { }, CanClose = false,
            Accepted = () => Choose(Draft.Options.Single(o => o.Role == "accept")),
            Cancelled = () => Choose(Draft.Options.Single(o => o.Role == "decline")),
            AcceptTitle = Draft.Options.Single(o => o.Role == "accept").Title,
            CancelTitle = Draft.Options.Single(o => o.Role == "decline").Title,
            HintChanged = hint => message.SetMessage(hint ?? idle),
            HelpHint = idle, CloseHint = idle,
            AcceptHint = Text("hint.reply-" + Draft.Role + "-accept"),
            CancelHint = Text("hint.reply-" + Draft.Role + "-decline")
        };
        AddChild(window); AddChild(message);
        Heading(window, Text("value.reply-force", Force(data.TargetForceId)));
        Large(0, 48, 96, Text("label.actor-force"), true);
        Large(104, 48, 80, Force(data.RequesterForceId));
        if (gift)
        {
            Small(192, 52, Text("label.relation"), RelationText(data.Relation), 48);
            Small(192, 84, Text("label.trust"), Number(Draft.Before.Forces.Single(f => f.Id == data.RequesterForceId).Trust), 40);
            var offer = data.Gift!;
            string description = offer.Kind switch
            {
                GiftKind.Money => Text("label.money") + Number(offer.Amount),
                GiftKind.Food => Text("label.food") + Number(offer.Amount),
                GiftKind.Treasure => Texts.Resolve(Draft.Before.Information.Treasures.Single(t => t.Id == offer.TreasureId).NameKey),
                _ => throw new InvalidDataException("Unknown gift kind.")
            };
            Large(0, 114, 96, Text("label.gift"), true); Large(104, 114, 128, description);
            var receipt = data.GiftReceipt ?? throw new InvalidDataException("Missing gift receipt preview.");
            Large(0, 156, 96, Text("label.money"), true);
            Large(104, 156, 144, offer.Kind == GiftKind.Money ? $"{receipt.MoneyBefore}→{receipt.MoneyAfter}" : "");
            Large(0, 196, 96, Text("label.food"), true);
            Large(104, 196, 144, offer.Kind == GiftKind.Food ? $"{receipt.FoodBefore}→{receipt.FoodAfter}" : "");
        }
        else
        {
            Large(0, 88, 96, Text("label.reply-force"), true); Large(104, 88, 80, Force(data.TargetForceId));
            var catalog = new AdministrativeCatalog(Draft.Before, Texts);
            foreach (var (force, y) in new[] { (data.RequesterForceId, 52), (data.TargetForceId, 92) })
            {
                Small(192, y, Text("label.trust"), Number(Draft.Before.Forces.Single(f => f.Id == force).Trust), 40);
                Small(296, y, Text("label.soldiers"), catalog.Cell(InformationCategory.Forces, force, "soldiers").Text, 80);
            }
        }
        void Large(int x, int y, int width, string value, bool label = false)
        {
            LargeField(window, x, y, width, value, label);
        }
        void Small(int x, int y, string label, string value, int width)
        {
            SmallField(window, x, y, 48, label, value, width);
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Keycode: Key.Escape })
            GetViewport().SetInputAsHandled();
    }
}

internal partial class DiplomacyReplyDivider : Control
{
    public TextureScope Textures { get; init; } = null!;
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => SkinPainter.Tile(this, Textures.Get("deployment-divider", this), new(Vector2.Zero, Size));
}

internal partial class DiplomacyReplyHeader : Control
{
    public TextureScope Textures { get; init; } = null!;
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => CommandPanelArt.Strip(this, Textures, "deployment-facility", new(Vector2.Zero, Size));
}
