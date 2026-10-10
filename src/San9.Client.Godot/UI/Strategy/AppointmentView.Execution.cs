using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class AppointmentView
{
    private void BuildExecution()
    {
        string Hint(string role) => Texts.Resolve(AppointmentText.Key("execute",role));
        string idle = Texts.Resolve(CommandMenuSkin.HintKey("execute"));
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title." + Draft.Kind), Position = new(252,320), Size = new(520,128),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = Help,
            AcceptHint = Hint(Draft.CanExecute ? "accept" : "select"), CancelHint = Hint("cancel"),
            HintChanged = hint => message.Text = hint ?? idle };
        body!.AddChild(window);
        var choose = window.ActionButton(CommandText.Resolve(Texts, "label.target-officer"),"edit",new(0,0,104,40),SelectPeople);
        choose.Disabled = Draft.Candidates.Length == 1;
        window.BindHint(choose,choose.Disabled ? idle : Hint("target"));
        var officer = Draft.Members.IsEmpty ? null : Draft.View.Officers.Single(p => p.Id == Draft.Members[0]);
        window.AddChild(new OfficerCard { Textures = Textures, Service = Service, Position = new(104,0),
            Officer = officer is null ? null : new(Texts.Resolve(officer.NameKey),$"catalog-person-{officer.Id}",officer.Abilities) });
        var abilities = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = new(312,0) };
        abilities.SetProperties([new(CommandText.Resolve(Texts, "label.leadership"),officer?.Abilities.Leadership.ToString() ?? "",40),new(CommandText.Resolve(Texts, "label.war"),officer?.Abilities.War.ToString() ?? "",40),
            new(CommandText.Resolve(Texts, "label.intelligence"),officer?.Abilities.Intelligence.ToString() ?? "",40),new(CommandText.Resolve(Texts, "label.politics"),officer?.Abilities.Politics.ToString() ?? "",40)]);
        window.AddChild(abilities);
        string value = officer is null ? "" : OfficerCatalog.Read(Draft.View,Texts,1,null,null,officer.Id).Rows.Single().Cells[7].Text;
        var loyalty = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-mode", Position = new(416,0) };
        loyalty.SetProperties([new(CommandText.Resolve(Texts, "label.loyalty"),value,40)]); window.AddChild(loyalty);
        body.AddChild(message);
    }

    private void SelectExecutionTarget() => SelectSingleAppointmentOfficer(
        [(0,0),(1,7),(1,1),(0,1),(0,2),(0,3),(0,4),(0,5),(0,7),(1,4)]);
}
