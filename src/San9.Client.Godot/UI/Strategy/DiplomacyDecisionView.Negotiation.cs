using Godot;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class DiplomacyDecisionView
{
    private void BuildNegotiation()
    {
        var data = Draft.Diplomacy!;
        bool attack = Draft.Role == "attack-request", accepting = data.Offer is not null;
        string kind = attack ? "request" : "return";
        string stage = accepting ? "decide" : "reply";
        string idle = accepting ? Draft.Prompt.BbCode : Text("hint.reply-" + kind);
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        var bounds = attack ? accepting ? new Rect2(292, 248, 440, 272) : new Rect2(292, 228, 440, 312) :
            accepting ? new Rect2(360, 240, 304, 288) : new Rect2(360, 220, 304, 328);
        var window = new CommandWindow
        {
            Texts = Texts, Textures = Textures, Title = Text("title." + stage + "-" + kind),
            Position = bounds.Position, Size = bounds.Size, CanClose = false, Closed = () => { },
            Help = () => message.SetMessage(idle),
            Accepted = accepting ? () => Choose(Draft.Options.Single(o => o.Role == "accept")) : ConfirmCounteroffer,
            Cancelled = () => Choose(Draft.Options.Single(o => o.Role == "decline")),
            AcceptTitle = Text("action.promise"), CancelTitle = Text("action.refuse"),
            AcceptHint = Text("hint." + stage + "-" + kind + "-accept"),
            CancelHint = Text("hint." + stage + "-" + kind + "-decline"),
            HelpHint = idle, CloseHint = idle,
            HintChanged = hint => message.SetMessage(hint ?? idle)
        };
        AddChild(window); AddChild(message);
        int peer = accepting ? data.TargetForceId : data.RequesterForceId;
        Heading(window, Text(accepting ? "value.deciding-force" : "value.reply-force", Force(Draft.ForceId)));
        LargeField(window, 0, 48, 96, Text(accepting ? "label.recipient-force" : "label.actor-force"), true);
        LargeField(window, attack ? 100 : 104, 48, attack ? 96 : 80, Force(peer));
        if (accepting || !attack)
            SmallField(window, attack ? 272 : 192, 52, attack ? 80 : 48, Text("label.relation"), RelationText(data.Relation), 48);
        if (attack)
        {
            int place = data.TargetSettlementId!.Value;
            var target = Draft.Before.Settlements.Single(s => s.Id == place);
            LargeField(window, 0, 88, 96, Text("label.target-facility"), true);
            LargeField(window, 100, 88, 160, target.ResolveName(Texts));
            SmallField(window, 272, 92, 80, Text("label.target-force"), target.OwnerId is int owner ? Force(owner) : "", 80);
            string relation = target.OwnerId is int enemy ? RelationText(Draft.Before.World.Relations.SingleOrDefault(r =>
                r.FirstId == Math.Min(data.TargetForceId, enemy) && r.SecondId == Math.Max(data.TargetForceId, enemy))?.Value ?? 0) : "";
            SmallField(window, 272, 128, 80, Text("label.relation"), relation, 48);
        }
        else
        {
            LargeField(window, 0, 88, 96, Text("label.prisoner"), true);
            var officer = Draft.Before.Officers.Single(o => o.Id == data.PrisonerId);
            window.AddChild(new OfficerCard { Textures = Textures, Service = Service, Position = new(104, 88),
                Officer = new(Texts.Resolve(officer.NameKey), $"information-person-{officer.Id}", officer.Abilities) });
        }
        var offer = data.Offer ?? Counteroffer!.Offer;
        int termsY = attack ? 156 : 216, valueX = attack ? 100 : 104;
        LargeField(window, 0, termsY, 96, Text("label.condition"), true);
        string description = offer.Kind switch
        {
            CompensationKind.Free => "",
            CompensationKind.Prisoner => Texts.Resolve(Draft.Before.MessageSpeaker(offer.PrisonerId!.Value).NameKey),
            CompensationKind.Money => Text("value.money-offer", Text("label.money"), offer.Money),
            CompensationKind.Treasure => Texts.Resolve(Draft.Before.Information.Treasures.Single(t => t.Id == offer.TreasureId).NameKey),
            _ => throw new InvalidDataException("A refusal has no terms window.")
        };
        LargeField(window, valueX, termsY, 112, description);
        if (accepting)
        {
            int moneyY = attack ? 196 : 256;
            LargeField(window, 0, moneyY, 96, Text("label.money"), true);
            LargeField(window, valueX, moneyY, 144, offer.Kind == CompensationKind.Money
                ? Text("value.balance-change", data.PaymentBalance, "→", data.PaymentBalance - offer.Money) : "");
        }
        else
        {
            string[] roles = ["prisoner", "money", "treasure", "free"];
            string[] labels = ["label.prisoner", "label.money", "label.treasure", "label.none"];
            for (int i = 0; i < roles.Length; i++)
            {
                string role = roles[i]; var option = Draft.Options.SingleOrDefault(o => o.Role == role);
                var button = window.ActionButton(Text(labels[i]), "edit",
                    new((attack ? 100 : 104) + (attack ? 108 : 104) * (i % 2), (attack ? 196 : 256) + 40 * (i / 2), 104, 40),
                    () => { if (option is not null) Choose(option); });
                button.Disabled = option is null || role == "free" && !Counteroffer!.HasCondition;
                window.BindHint(button, Text(option is null ? "hint.compensation-unavailable" : "hint.compensation-" + role));
            }
        }
        if (attack)
        {
            int periodY = accepting ? 236 : 276;
            LargeField(window, 0, periodY, 96, Text("label.request-period"), true);
            LargeField(window, 100, periodY, 56, Text("value.days", 180));
        }
    }
}
