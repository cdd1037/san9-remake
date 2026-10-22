using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class FacilityCommandView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public FacilityCommandDraft Draft { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Submit { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private Control? _body, _child;
    private StyleBoxTexture _field = null!;
    private int Height => Draft.Kind == "train" ? 232 : Draft.Kind == "repair" ? 272 : Draft.Kind is "recruit" or "buy" or "sell" ? 352 : 312;
    private int Top => (768-Height)/2;
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        _field = CommandPanelArt.Field(Textures, this); Rebuild();
    }
    private void Label(string value, Rect2 rect)
    {
        var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _body!.AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow);
    }
    private void Rebuild()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title." + Draft.Kind), Position = new(280,Top), Size = new(464,Height),
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed, Help = () => Notice(Texts.Resolve("ui.command." + Draft.Kind)) };
        _body.AddChild(window);
        Label(CommandText.Resolve(Texts, "value.source", Draft.View.Settlements.Single(s => s.Id == Draft.Home.Id).ResolveName(Texts)), new(280,Top,160,32));
        window.ActionButton(CommandText.Resolve(Texts, "label.actor"), "edit", new(0,48,104,40), SelectOfficers).Disabled = Draft.Candidates.Length < 2;
        var roster = new OfficerRosterPanel { Service = Service, Textures = Textures, Position = new(384,Top+48), VisibleRows = 5,
            Columns = [new(CommandText.Resolve(Texts, "label.officer"),80), new(CommandText.Resolve(Texts, Draft.UsesLeadership ? "label.leadership" : "label.politics"),64)] };
        roster.SetEntries(Draft.Members.Select(id => {
            var person = Draft.View.Officers.Single(o => o.Id == id); string name = Texts.Resolve(person.NameKey);
            return new OfficerRosterEntry(new(name,$"catalog-person-{id}",person.Abilities),
                [name,(Draft.UsesLeadership ? person.Abilities.Leadership : person.Abilities.Politics).ToString()]); }));
        _body.AddChild(roster);
        var home = Draft.Home;
        string Quantity(uint amount) => amount == 0 ? "" : amount.ToString();
        string Change(long current, long next, uint amount) => $"{current}→" +
            (amount > 0 ? next.ToString() : Draft.IsTrade ? "   " : "    ");
        (string Name,string Value)[] fields = Draft.Kind switch {
            "commerce" => [(CommandText.Resolve(Texts, "label.money"),$"{Draft.Cost}/{Draft.Money}"),(CommandText.Resolve(Texts, "title.commerce"),$"{home.Commerce}/{home.MaxCommerce}"),(CommandText.Resolve(Texts, "label.revenue"),EconomyMath.Revenue(home).ToString())],
            "cultivate" => [(CommandText.Resolve(Texts, "label.money"),$"{Draft.Cost}/{Draft.Money}"),(CommandText.Resolve(Texts, "title.cultivate"),$"{home.Cultivation}/{home.MaxCultivation}"),(CommandText.Resolve(Texts, "label.harvest"),EconomyMath.Harvest(home).ToString())],
            "repair" => [(CommandText.Resolve(Texts, "label.money"),$"{Draft.Cost}/{Draft.Money}"),(CommandText.Resolve(Texts, "label.durability"),$"{home.Durability}/{home.MaxDurability}")],
            "train" => [(CommandText.Resolve(Texts, "label.morale"),$"{home.Morale}/100")],
            "recruit" => [("",Quantity(Draft.RecruitAmount)),(CommandText.Resolve(Texts, "label.soldiers"),Change(home.Soldiers,home.Soldiers + Draft.RecruitAmount,Draft.RecruitAmount)),
                (CommandText.Resolve(Texts, "label.draft-population"),Change(home.DraftPopulation,home.DraftPopulation - Draft.RecruitAmount,Draft.RecruitAmount)),
                (CommandText.Resolve(Texts, "label.money"),Change(Draft.Money,Draft.Money-Draft.Cost,Draft.RecruitAmount))],
            _ => [("",Quantity(Draft.FoodAmount)),(CommandText.Resolve(Texts, "label.food"),Change(Draft.Food,Draft.Kind == "buy" ? (long)Draft.Food + Draft.FoodAmount : (long)Draft.Food - Draft.FoodAmount,Draft.FoodAmount)),
                (CommandText.Resolve(Texts, "label.money"),Change(Draft.Money,Draft.Kind == "buy" ? (long)Draft.Money - Draft.Cost : (long)Draft.Money + Draft.Cost,Draft.FoodAmount)),
                (CommandText.Resolve(Texts, "label.price"),CommandText.Resolve(Texts, "value.exchange-rate", Draft.View.World.Simulation.FoodExchangeRate))] };
        int[] widths = Draft.Kind switch { "commerce" or "cultivate" => [136,96,72], "repair" => [136,96], "train" => [80], _ => [64,136,136,136] };
        var art = new FacilityCommandFields { Textures = Textures, Field = _field, Top = Top, Widths = widths,
            HasQuantity = Draft.IsTrade || Draft.Kind == "recruit", MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(art); _body.MoveChild(art,1);
        for (int i=0;i<fields.Length;i++)
        { Label(fields[i].Name,new(280,Top+200+i*40,96,32)); Label(fields[i].Value,new(384,Top+200+i*40,widths[i],32)); }
        if (Draft.IsTrade || Draft.Kind == "recruit")
            window.ActionButton(CommandText.Resolve(Texts, "title." + Draft.Kind), "edit", new(0,200,104,40), SelectAmount).Disabled = Draft.Members.IsEmpty;
        _body.AddChild(new GameMessageBar { Textures = Textures, Text = Texts.Resolve(CommandMenuSkin.HintKey(Draft.Kind)) });
    }
    private void CloseChild()
    { if (_child is not null) { RemoveChild(_child); _child.QueueFree(); _child = null; } Rebuild(); }
    private void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; dialog.Closed += CloseChild;
        _child = dialog; AddChild(dialog); dialog.SetNotice(message);
    }
    private void SelectAmount()
    {
        uint maximum = Draft.IsTrade ? Draft.Quote?.MaximumFoodAmount ?? 0 : (uint)Draft.RecruitCount;
        if (maximum == 0) return;
        _child = new NumberInputDialog { Textures = Textures, Subject = CommandText.Resolve(Texts, "value.quantity-title", CommandText.Resolve(Texts, "title." + Draft.Kind), CommandText.Resolve(Texts, Draft.IsTrade ? "label.food" : "label.soldiers")),
            State = new(1,maximum,Draft.IsTrade ? Draft.FoodAmount : Draft.RecruitAmount),
            Confirmed = value => { if (Draft.IsTrade ? Draft.SetFoodAmount(value) : Draft.SetRecruitAmount(value)) CloseChild(); }, Closed = CloseChild };
        AddChild(_child);
    }
    private void SelectOfficers()
    {
        int primary = Draft.UsesLeadership ? 1 : 4;
        int[] columns = [0,primary,8,.. new[] { 1,2,3,4,5 }.Where(c => c != primary)];
        CatalogRow[] Rows(int page) => OfficerCatalog.Read(Draft.View,Texts,page == 0 ? 0 : page-1,null,Draft.Home.Id).Rows
            .Where(r => Draft.Candidates.Contains(r.Key.Id)).OrderBy(r => Draft.Candidates.IndexOf(r.Key.Id))
            .Select(r => new CatalogRow(r.Key.Id,page == 0 ? columns.Select(c => r.Cells[c]).ToArray() : r.Cells.ToArray())).ToArray();
        _child = new CatalogView { Title = CommandText.Resolve(Texts, "select.officer"), HintText = Texts.Resolve(CommandMenuSkin.OfficerSelectionHintKey), Service = Service, Textures = Textures,
            Pages = [new(CommandText.Resolve(Texts, "title." + Draft.Kind),columns.Select(c => OfficerCatalog.Pages[0].Columns[c]).ToArray()),.. OfficerCatalog.Pages],
            ReadRows = Rows, Portrait = id => $"catalog-person-{id}", Selection = new(Draft.Candidates,Draft.Members,Draft.SelectionLimit),
            CanConfirmSelection = ids => Draft.CanSelect(ids), ConfirmSelection = ids => { if (Draft.SetMembers(ids)) CloseChild(); }, Closed = CloseChild };
        AddChild(_child);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (_child is null && input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right }) Closed();
        AcceptEvent();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_child is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}

internal partial class FacilityCommandFields : Control
{
    public TextureScope Textures { get; init; } = null!;
    public StyleBoxTexture Field { get; init; } = null!;
    public int Top { get; init; }
    public int[] Widths { get; init; } = [];
    public bool HasQuantity { get; init; }
    public override void _Draw()
    {
        void Strip(string role, Rect2 rect) => SkinPainter.Strip(this,Textures.Get($"deployment-{role}-left", this),Textures.Get($"deployment-{role}-middle", this),Textures.Get($"deployment-{role}-right", this),rect);
        Strip("facility",new(280,Top,160,32));
        SkinPainter.Tile(this,Textures.Get("deployment-divider", this),new(280,Top+40,464,5));
        for (int i=0;i<Widths.Length;i++)
        {
            int y = Top+200+i*40, width = Widths[i];
            if (!HasQuantity || i > 0) Strip("label",new(280,y,96,32));
            DrawStyleBox(Field,new(384,y,width,32));
            SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(384,y,width,4));
            SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(384,y+28,width,4));
        }
    }
}
