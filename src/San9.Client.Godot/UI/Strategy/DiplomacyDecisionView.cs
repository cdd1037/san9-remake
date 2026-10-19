using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Diplomatic replies show the incoming proposal and submit the corresponding public response.</summary>
public partial class DiplomacyDecisionView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public DecisionDraft Draft { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Action<DecisionOption> Choose { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        var data = Draft.Diplomacy!;
        bool binary = Draft.Role is "gift" or "surrender";
        bool attack = data.TargetSettlementId is not null, accepting = binary || data.Offer is not null;
        var accept = Draft.Options.Single(o => o.Role == (accepting ? "accept" : "free"));
        var decline = Draft.Options.Single(o => o.Role == "decline");
        var bounds = attack ? accepting ? new Rect2(292, 248, 440, 272) : new Rect2(292, 228, 440, 312) :
            accepting ? new Rect2(360, 240, 304, 288) : new Rect2(360, 220, 304, 328);
        var window = new CommandWindow { Texts = Texts,
            Textures = Textures,
            Title = Draft.Role switch { "gift" => "接受赠礼", "surrender" => "回应劝降", _ => attack ? "请求攻打" : "俘虏返还" },
            Position = bounds.Position,
            Size = bounds.Size,
            Help = () => { },
            Closed = () => { },
            CanClose = false,
            Cancelled = () => Choose(decline),
            Accepted = () => Choose(accept),
            AcceptTitle = accept.Title,
            CancelTitle = decline.Title
        };
        AddChild(window);
        string Force(int id) => Texts.Resolve(Draft.Before.Forces.Single(f => f.Id == id).NameKey) + "军";
        Field(window, 0, binary ? "来访势力" : "执行势力", Force(binary || !accepting ? data.RequesterForceId : data.TargetForceId));
        Field(window, 32, "友好", data.Relation.ToString());
        if (data.TargetSettlementId is int target)
        {
            Field(window, 64, "目标设施", Draft.Before.Settlements.Single(s => s.Id == target).ResolveName(Texts));
            Field(window, 96, "目标势力", Draft.Before.Settlements.Single(s => s.Id == target).OwnerId is int owner ? Force(owner) : "");
            Field(window, 128, "期间", "180日");
        }
        else if (data.PrisonerId is int prisoner)
        {
            var officer = Draft.Before.Officers.Single(o => o.Id == prisoner);
            window.AddChild(new OfficerCard { Textures = Textures, Service = Service, Position = new(100, 64), Officer = new(Texts.Resolve(officer.NameKey), $"information-person-{prisoner}", officer.Abilities) });
        }
        if (data.Offer is { } offer)
        {
            string condition = offer.Kind switch
            {
                CompensationKind.Free => "无",
                CompensationKind.Money => $"资金 {offer.Money}",
                CompensationKind.Prisoner => Texts.Resolve(Draft.Before.MessageSpeaker(offer.PrisonerId!.Value).NameKey),
                CompensationKind.Treasure => Texts.Resolve(Draft.Before.Information.Treasures.Single(t => t.Id == offer.TreasureId).NameKey),
                _ => "拒绝"
            };
            Field(window, attack ? 160 : 192, "条件", condition);
            if (offer.Kind == CompensationKind.Money) Field(window, attack ? 192 : 224, "资金", $"{data.PaymentBalance} → {data.PaymentBalance - offer.Money}");
        }
        else if (!binary)
        {
            string[] roles = ["prisoner", "money", "treasure", "free"];
            for (int i = 0; i < roles.Length; i++)
            {
                string role = roles[i]; var option = Draft.Options.SingleOrDefault(o => o.Role == role);
                var button = window.ActionButton(Draft.Definition.Options[role], "edit", new((attack ? 100 : 104) + (attack ? 108 : 104) * (i % 2), (attack ? 196 : 256) + 40 * (i / 2), 104, 40), () => { if (option is not null) Choose(option); });
                button.Disabled = option is null;
            }
        }
        if (data.Gift is { } gift)
        {
            Field(window, 64, "赠礼", gift.Kind switch
            {
                GiftKind.Money => $"资金 {gift.Amount}",
                GiftKind.Food => $"兵粮 {gift.Amount}",
                GiftKind.Treasure => Texts.Resolve(Draft.Before.Information.Treasures.Single(t => t.Id == gift.TreasureId).NameKey),
                _ => throw new InvalidDataException("Unknown gift kind.")
            });
        }
        else if (Draft.Role == "surrender") Field(window, 64, "投降于", Force(data.RequesterForceId));
    }
    private void Field(Control parent, int y, string label, string value)
    {
        parent.AddChild(new CommandLabelField { Textures = Textures, Position = new(0, y), Size = new(96, 32) });
        parent.AddChild(new CommandValueField { Textures = Textures, Position = new(104, y), Size = new(parent.Size.X - 104, 32) });
        CommandPanelArt.Label(parent, _text, label, new(0, y, 96, 32));
        CommandPanelArt.Label(parent, _text, value, new(104, y, parent.Size.X - 104, 32), alignment: HorizontalAlignment.Left);
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() => _text.Dispose();
}
