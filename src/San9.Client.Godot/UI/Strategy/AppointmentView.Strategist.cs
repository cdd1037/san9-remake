using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class AppointmentView
{
    private void BuildStrategist()
    {
        string Hint(string role) => Texts.Resolve(AppointmentText.Key("strategist", role));
        string idle = Texts.Resolve(CommandMenuSkin.HintKey("strategist"));
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title." + Draft.Kind), Position = new(304, 256), Size = new(416, 256),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = Help,
            AcceptHint = Hint(Draft.CanExecute ? "accept" : "select"), CancelHint = Hint("cancel"),
            HintChanged = hint => message.Text = hint ?? idle };
        body!.AddChild(window);
        window.BindHint(window.ActionButton(CommandText.Resolve(Texts, "label.target-officer"), "edit", new(0, 0, 104, 40), SelectPeople), Hint("target"));
        int? incumbent = Draft.View.World.Forces.Single(f => f.Id == Draft.View.PlayerForceId).AdviserId;
        window.AddChild(new StrategistLabels { Textures = Textures, MouseFilter = MouseFilterEnum.Ignore });
        void Officer(int? id, int y)
        {
            var officer = Draft.View.Officers.SingleOrDefault(p => p.Id == id);
            window.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = new(104, y),
                Officer = officer is null ? null : new(Texts.Resolve(officer.NameKey), $"catalog-person-{id}", officer.Abilities) });
            var properties = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = new(312, y) };
            properties.SetProperties([new(CommandText.Resolve(Texts, "label.intelligence"), officer?.Abilities.Intelligence.ToString() ?? "", 40)]);
            window.AddChild(properties);
        }
        Officer(Draft.Members.IsEmpty ? null : Draft.Members[0], 0);
        Officer(incumbent, 128);
        CommandPanelArt.Label(window, text, CommandText.Resolve(Texts, "label.current-adviser"), new(0, 128, 96, 32));
        body.AddChild(message);
    }

    private void SelectStrategist() => SelectSingleAppointmentOfficer(
        [(0,0),(0,3),(1,7),(0,1),(0,2),(0,4),(0,5),(0,7),(1,4)]);

}

internal partial class StrategistLabels : Control
{
    public TextureScope Textures { get; init; } = null!;
    public override void _Draw() => CommandPanelArt.Strip(this, Textures, "deployment-label", new(0, 128, 96, 32));
}
