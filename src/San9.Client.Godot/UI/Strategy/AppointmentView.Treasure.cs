using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class AppointmentView
{
    private void BuildTreasureAppointment()
    {
        bool grant = Draft.Kind == "bestow";
        var preview = TreasureTransferPreview.Read(Draft);
        var treasure = Draft.TreasureId is int item ? TreasureDetails.Read(Draft.View,Texts,item) : null;
        string Hint(string role) => Texts.Resolve(AppointmentText.Key(Draft.Kind,role));
        string idle = Texts.Resolve(CommandMenuSkin.HintKey(Draft.Kind));
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        int rulerY = Math.Max(312,184 + 32 * (preview.TargetAbilities.Count + 1) + 8);
        var window = new CommandWindow { Textures = Textures, Title = Draft.Title, Position = new(292,168), Size = new(440,rulerY+120),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = Help,
            AcceptHint = Hint(Draft.CanExecute ? "accept" : Draft.TreasureId is null ? "select-treasure" : "select"),
            CancelHint = Hint("cancel"), HintChanged = hint => message.Text = hint ?? idle };
        body!.AddChild(window);
        var itemButton = window.ActionButton("宝物","edit",new(0,0,104,40),SelectTreasure);
        itemButton.Disabled = Draft.TreasureIds.Length == 1;
        window.BindHint(itemButton,itemButton.Disabled ? idle : Hint("treasure"));
        window.AddChild(new TreasureCard { Textures = Textures, Service = Service, Treasure = treasure, Position = new(104,0) });
        if (grant)
        {
            var targetButton = window.ActionButton("对象武将","edit",new(0,184,104,40),SelectPeople);
            targetButton.Disabled = Draft.Candidates.Length == 1;
            window.BindHint(targetButton,targetButton.Disabled ? idle : Hint("target"));
        }
        else AddLabel("对象武将",184);
        AddLabel("君主",rulerY);
        void AddLabel(string value,int y)
        {
            // Static caption shares the command label skin without adding a value field.
            window.AddChild(new TreasureAppointmentCaption { Textures = Textures, Position = new(0,y) });
            CommandPanelArt.Label(window,text,value,new(0,y,96,32));
        }
        void Officer(int? id,int y,IReadOnlyList<AttributeChange> abilities,bool showLoyalty)
        {
            var person = Draft.View.Officers.SingleOrDefault(p => p.Id == id);
            window.AddChild(new OfficerCard { Textures = Textures, Service = Service, Position = new(104,y),
                Officer = person is null ? null : new(Texts.Resolve(person.NameKey),$"catalog-person-{id}",person.Abilities) });
            if (abilities.Count > 0)
            {
                var properties = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = new(312,y) };
                properties.SetProperties(abilities.Select(a => new OfficerProperty(a.Title,a.Text))); window.AddChild(properties);
            }
            else if (treasure is not null && preview.RulerAbilities.Count > 0)
            {
                var properties = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = new(312,y) };
                properties.SetProperties(preview.RulerAbilities.Select(a => new OfficerProperty(a.Title,""))); window.AddChild(properties);
            }
            if (showLoyalty)
            {
                var loyalty = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode",
                    Position = new(312,y+32*Math.Max(1,abilities.Count)) };
                loyalty.SetProperties([new("忠诚",preview.Loyalty?.Text ?? "")]); window.AddChild(loyalty);
            }
        }
        Officer(preview.TargetId,184,preview.TargetAbilities,true);
        Officer(preview.RulerId,rulerY,preview.RulerAbilities,false);
        body.AddChild(message);
    }

    private void SelectTreasureRecipient() => SelectSingleAppointmentOfficer(
        [(0,0),(1,7),(0,1),(0,2),(0,3),(0,4),(0,5),(3,2),(0,7),(1,4)]);

}

internal partial class TreasureAppointmentCaption : Control
{
    public TextureScope Textures { get; init; } = null!;
    public override void _Draw() => CommandPanelArt.Strip(this,Textures,"deployment-label",new(0,0,96,32));
}
