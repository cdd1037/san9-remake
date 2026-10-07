using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class AppointmentView
{
    private void BuildRosterAppointment()
    {
        bool reward = Draft.Kind == "reward";
        string Hint(string role) => Texts.Resolve(AppointmentText.Key(Draft.Kind, role));
        string idle = Texts.Resolve(CommandMenuSkin.HintKey(Draft.Kind));
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        var window = new CommandWindow { Textures = Textures, Title = Draft.Title, Position = reward ? new(280,216) : new(208,238), Size = reward ? new(464,336) : new(608,292),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = Help,
            AcceptHint = Hint(Draft.CanExecute ? "accept" : "select"), CancelHint = Hint("cancel"),
            HintChanged = hint => message.Text = hint ?? idle };
        body!.AddChild(window);
        var choose = window.ActionButton("对象武将", "edit", new(0,0,104,40), SelectPeople);
        choose.Disabled = Draft.Candidates.Length == 1;
        window.BindHint(choose, choose.Disabled ? idle : Hint("target"));
        var roster = new OfficerRosterPanel { Textures = Textures, Service = Service, Position = new(104,0),
            Columns = reward ? [new("武将",80), new("忠诚",64, Alignment: HorizontalAlignment.Right)]
                : [new("武将",80), new("身份",64), new("势力",80), new("忠诚",64, Alignment: HorizontalAlignment.Right)], VisibleRows = 10, ShowPagination = true, PortraitFollowsPage = false };
        var affiliations = OfficerCatalog.Read(Draft.View,Texts,1,null,null).Rows.ToDictionary(r => r.Key.Id);
        roster.SetEntries(Draft.Members.Select(id => {
            var officer = Draft.View.Officers.Single(p => p.Id == id);
            string name = Texts.Resolve(officer.NameKey);
            var cells = affiliations[id].Cells;
            return new OfficerRosterEntry(new(name,$"catalog-person-{id}",officer.Abilities),
                reward ? [name,cells[7].Text] : [name,cells[5].Text,cells[1].Text,cells[7].Text]); }));
        window.AddChild(roster);
        if (reward)
        {
            var funds = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-label", LabelWidth = 96,
                RowHeight = 32, RowSpacing = 40, Position = new(0,304) };
            funds.SetProperties([new("资金", $"{(Draft.Members.IsEmpty ? "   " : Draft.Cost.ToString())}/{Draft.Money}",112)]);
            window.AddChild(funds);
        }
        body.AddChild(message);
    }

    private void SelectRosterAppointmentOfficers()
    {
        (int Page, int Column)[] columns = Draft.Kind == "reward"
            ? [(0,0),(1,7),(3,1),(3,2),(0,1),(0,2),(0,3),(0,4),(0,5),(1,4)]
            : [(0,0),(1,5),(1,1),(1,7),(0,1),(0,2),(0,3),(0,4),(0,7),(1,4)];
        var tables = new[] { 0,1,3 }.ToDictionary(page => page,
            page => OfficerCatalog.Read(Draft.View,Texts,page,null,null).Rows.ToDictionary(r => r.Key.Id));
        CatalogRow[] Rows(int page) => page == 0
            ? Draft.Candidates.Select(id => new CatalogRow(id,columns.Select(c => tables[c.Page][id].Cells[c.Column]).ToArray())).ToArray()
            : OfficerCatalog.Read(Draft.View,Texts,page-1,null,null).Rows.Where(r => Draft.Candidates.Contains(r.Key.Id))
                .OrderBy(r => Draft.Candidates.IndexOf(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id,r.Cells.ToArray())).ToArray();
        ReplaceChild(new CatalogView { Title = "选择武将", Textures = Textures, Service = Service,
            HintText = Texts.Resolve(AppointmentText.Key(Draft.Kind, "select")),
            Pages = [new(Draft.Title,columns.Select(c => OfficerCatalog.Pages[c.Page].Columns[c.Column]).ToArray()), .. OfficerCatalog.Pages],
            ReadRows = Rows, Portrait = id => $"catalog-person-{id}", Selection = new(Draft.Candidates,Draft.Members,Draft.SelectionLimit),
            ConfirmSelection = ids => { if (Draft.SetMembers(ids)) CloseChild(); }, Closed = CloseChild });
    }
}
