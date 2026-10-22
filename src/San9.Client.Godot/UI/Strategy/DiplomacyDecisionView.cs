using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Shows the current diplomatic proposal; the owning workflow retains all editable terms.</summary>
public partial class DiplomacyDecisionView : FixedPage
{
    public DecisionDraft Draft { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Action<DecisionOption> Choose { get; init; } = null!;
    public DiplomacyCounterofferDraft? Counteroffer { get; init; }
    public Action ConfirmCounteroffer { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        if (Draft.Role is "gift" or "surrender") BuildReply();
        else BuildNegotiation();
    }
    private string Text(string role, params object[] values) => CommandText.Resolve(Texts, role, values);
    private string Force(int id) => Texts.Resolve(Draft.Before.Forces.Single(f => f.Id == id).NameKey);
    private string RelationText(int relation) => Text("relation." + (DiplomacyMath.RelationBand(relation) switch
    {
        ForceRelationBand.Hostile => "hostile", ForceRelationBand.Unfriendly => "unfriendly",
        ForceRelationBand.Neutral => "neutral", ForceRelationBand.Friendly => "friendly", _ => "intimate"
    }));
    private void Heading(CommandWindow window, string caption)
    {
        var heading = new Label { Text = caption, MouseFilter = MouseFilterEnum.Ignore };
        window.AddChild(heading); _text.Apply(heading, GameTextStyles.CatalogRow);
        var size = new Vector2(heading.GetMinimumSize().X + 32, 32);
        heading.Size = size; heading.HorizontalAlignment = HorizontalAlignment.Center; heading.VerticalAlignment = VerticalAlignment.Center;
        var plate = new DiplomacyReplyHeader { Textures = Textures, Size = size };
        window.AddChild(plate); window.MoveChild(plate, heading.GetIndex());
        window.AddChild(new DiplomacyReplyDivider { Textures = Textures, Position = new(0, 40), Size = new(window.Size.X, 5) });
    }
    private void LargeField(CommandWindow window, int x, int y, int width, string value, bool label = false)
    {
        window.AddChild(label ? new CommandLabelField { Textures = Textures, Position = new(x, y), Size = new(width, 32) } :
            new CommandValueField { Textures = Textures, Position = new(x, y), Size = new(width, 32) });
        CommandPanelArt.Label(window, _text, value, new(x, y, width, 32));
    }
    private void SmallField(CommandWindow window, int x, int y, int labelWidth, string label, string value, int width)
    {
        var row = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", LabelWidth = labelWidth, Position = new(x, y) };
        row.SetProperties([new(label, value, width)]); window.AddChild(row);
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() => _text.Dispose();
}
