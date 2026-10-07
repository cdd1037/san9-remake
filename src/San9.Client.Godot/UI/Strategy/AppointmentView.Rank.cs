using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class AppointmentView
{
    private string RankHint(string role) => Texts.Resolve(AppointmentText.Key("rank", role));
    private void BuildRank()
    {
        var plan = Draft.OfficePlan!;
        string idle = Texts.Resolve(CommandMenuSkin.HintKey("rank"));
        var message = new GameMessageBar { Textures = Textures, Text = idle };
        var window = new CommandWindow { Textures = Textures, Title = Draft.Title, Position = new(208,204), Size = new(608,360),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = Help,
            AcceptHint = RankHint(Draft.CanExecute ? "accept" : "no-changes"), CancelHint = RankHint("cancel"),
            HintChanged = hint => message.Text = hint ?? idle };
        body!.AddChild(window);
        void Button(string title, string hint, int row, Action action, string? unavailable = null)
        {
            var button = window.ActionButton(title, "edit", new(0,row*40,104,40), action);
            button.Disabled = unavailable is not null;
            window.BindHint(button, RankHint(unavailable ?? hint));
        }
        Button("个别任命", "individual", 0, SelectPeople);
        Button("空缺任命", "fill", 1, () => { plan.FillVacancies(); Rebuild(); }, plan.FillUnavailable);
        Button("右迁任命", "promote", 2, () => { plan.Promote(); Rebuild(); }, plan.PromoteUnavailable);
        Button("全卸任", "remove", 3, () => { plan.RemoveAll(); Rebuild(); }, plan.RemoveUnavailable);
        var roster = new OfficerRosterPanel { Service = Service, Textures = Textures, Position = new(104,0), ShowPortrait = false,
            VisibleRows = 10, BodyHeight = 248, ShowPagination = true, PageNumberWidth = 72, PaginationSpacing = 4,
            PaginationTop = 0, PaginationLabelTop = 8, CountGap = 60,
            Columns = [new("武将",80),new("爵位",104),new("指挥",64),new("统率",64),new("武力",64),new("智力",64),new("政治",64)] };
        roster.SetEntries(plan.Changes.Select(change => {
            int id = change.OfficerId;
            var person = Draft.View.Officers.Single(p => p.Id == id);
            string name = Texts.Resolve(person.NameKey);
            var a = plan.Abilities(id); var bonus = plan.Definition(change.OfficeId)?.AbilityBonuses ?? default;
            string Stat(int value, int addition) => addition == 0 ? value.ToString() : $"{value-addition}+{addition}";
            int direction = plan.Direction(id);
            return new OfficerRosterEntry(new(name,$"catalog-person-{id}",a),
                [name,AppointmentPresentation.Office(Draft,Texts,change.OfficeId),plan.Capacity(id).ToString(),
                 Stat(a.Leadership,bonus.Leadership),Stat(a.War,bonus.War),Stat(a.Intelligence,bonus.Intelligence),Stat(a.Politics,bonus.Politics)])
            { Icons = direction == 0 ? new Dictionary<int,string>() : new Dictionary<int,string> { [1] = $"text-icon-arrow-{(direction > 0 ? "up" : "down")}-right-16" } };
        }));
        window.AddChild(roster);
        var counts = new OfficerPropertyList { Textures = Textures, LabelSkinPrefix = "deployment-label", LabelWidth = 96,
            RowHeight = 32, RowSpacing = 40, Position = new(0,288) };
        counts.SetProperties([new("右迁",plan.Promotions.ToString(),40),new("左迁",plan.Demotions.ToString(),40)]);
        window.AddChild(counts); body.AddChild(message);
    }

    private void SelectRankPeople(OfficeAppointmentPlan edited, CatalogTableState state)
    {
        (int Page,int Column)[] first = [(0,0),(3,1),(1,5),(1,7),(0,1),(0,2),(0,3),(0,4),(1,4)];
        var tables = first.Select(c => c.Page).Distinct().ToDictionary(page => page,
            page => OfficerCatalog.Read(Draft.View,Texts,page,null,null).Rows.ToDictionary(r => r.Key.Id));
        CatalogRow[] Rows(int page)
        {
            var order = edited.OrderedPeople.ToArray();
            if (page == 0) return order.Select(id => new CatalogRow(id,first.Select(c =>
                edited.Cell(id,OfficerCatalog.Pages[c.Page].Columns[c.Column].Id,tables[c.Page][id].Cells[c.Column],Texts)).ToArray())).ToArray();
            var rows = OfficerCatalog.Read(Draft.View,Texts,page-1,null,null).Rows.ToDictionary(r => r.Key.Id);
            return order.Select(id => new CatalogRow(id,rows[id].Cells.Select((cell,index) =>
                edited.Cell(id,OfficerCatalog.Pages[page-1].Columns[index].Id,cell,Texts)).ToArray())).ToArray();
        }
        ReplaceChild(new CatalogView { Title = "选择武将", CloseTitle = "中止", Textures = Textures, Service = Service,
            HintText = RankHint("select"), TableState = state, Portrait = id => $"catalog-person-{id}",
            Pages = [new("爵位",first.Select(c => OfficerCatalog.Pages[c.Page].Columns[c.Column]).ToArray()), .. OfficerCatalog.Pages],
            ReadRows = Rows, Activated = id => SelectRankOffice(edited,id,state), Closed = CloseChild,
            Confirmed = () => { if (Draft.IsCurrent) Draft.OfficePlan!.Accept(edited); CloseChild(); } });
    }

    private void SelectRankOffice(OfficeAppointmentPlan edited, int person, CatalogTableState peopleState)
    {
        // The chooser index includes an explicit null option, without introducing a sentinel domain office.
        int?[] choices = [.. edited.AvailableOffices.Select(id => (int?)id), null];
        CatalogColumn C(string id,string title,int width) => new(id,title,width,CatalogAlignment.Center);
        string Holder(int? office) => office is null ? "" : string.Join("、",Draft.Candidates.Where(id => edited.OfficeFor(id) == office)
            .Select(id => AppointmentPresentation.Person(Draft,Texts,id)));
        CatalogRow[] Rows(int page) => choices.Select((id,index) => {
            var office = edited.Definition(id); var a = office?.AbilityBonuses ?? default;
            string name = AppointmentPresentation.Office(Draft,Texts,id);
            return new CatalogRow(index,page == 0
                ? [name,Holder(id),(long)(office?.CommandCapacity ?? 10000),office?.Salary ?? 0,a.Leadership,a.War,a.Intelligence,a.Politics,(office?.Salary ?? 0)/2]
                : [name,office?.DescriptionKey is string key ? Texts.Resolve(key) : ""]);
        }).ToArray();
        ReplaceChild(new CatalogView { Title = "选择爵位", CloseTitle = "中止", Textures = Textures, Service = Service,
            HintText = RankHint("select-office"), InitialCurrentId = Array.IndexOf(choices,edited.OfficeFor(person)),
            Pages = [new("特征",[C("office","爵位",8),C("holder","武将",8),C("capacity","指挥",6),C("salary","俸禄",5),
                C("leadership","统率",5),C("war","武力",5),C("intelligence","智力",5),C("politics","政治",5),C("loyalty","忠诚上升",9)]),
                new("说明",[C("office","爵位",8),new("description","说明",61,CatalogAlignment.Left)])],
            ReadRows = Rows, Closed = () => SelectRankPeople(edited,peopleState),
            Activated = index => { if (edited.Assign(person,choices[index])) SelectRankPeople(edited,peopleState); } });
    }
}
