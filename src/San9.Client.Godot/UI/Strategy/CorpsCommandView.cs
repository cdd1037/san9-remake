using San9.Content.Text;
using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class CorpsCommandView : CommandPreparationView
{
    public CorpsDraft Draft { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    protected override bool IsCurrent => Draft.IsCurrent;
    private string OperationName(CorpsOperation operation) => operation switch
    { CorpsOperation.Create => CommandText.Resolve(Texts, "corps.create"), CorpsOperation.Change => CommandText.Resolve(Texts, "corps.change"), CorpsOperation.Dissolve => CommandText.Resolve(Texts, "corps.dissolve"), _ => throw new InvalidOperationException("Unknown corps operation.") };
    protected override void Build()
    {
        if (Draft.Operation is CorpsOperation.Control or CorpsOperation.Policy) { BuildAdministration(); return; }
        // Original corps-management content rectangle and selector/card anchors (RE-0182).
        Vector2 at = new(360,148);
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title.delegate"), Position = at, Size = new(304,472), CanAccept = Draft.CanExecute,
            Accepted = Submit, Closed = Closed,
            Help = () => Notice(CommandText.Resolve(Texts, "hint.corps")) };
        Body.AddChild(window);
        bool editing = Draft.Operation is CorpsOperation.Create or CorpsOperation.Change;
        bool populated = Draft.Cities.Length > 0;
        bool ready = editing && populated && Draft.GovernorId is not null;
        window.ActionButton(CommandText.Resolve(Texts, "label.corps-operation"),"edit",new(0,0,104,40),SelectOperation).Disabled = !Draft.Corps.Any(c => !c.IsRoot);
        Value(OperationName(Draft.Operation),at+new Vector2(104,0),new(48,32));
        window.ActionButton(CommandText.Resolve(Texts, "label.city"),"edit",new(0,40,104,40),SelectCities).Disabled = !editing || Draft.CityCandidates.IsEmpty;
        string cities = Draft.Cities.Length switch { 0 => "", 1 => Draft.View.Settlements.Single(s => s.Id == Draft.Cities[0]).ResolveName(Texts), _ => CommandText.Resolve(Texts, "value.cities", Draft.Cities.Length) };
        Value(cities,at+new Vector2(104,40),new(64,32));
        Caption(CommandText.Resolve(Texts, "label.governor"),at+new Vector2(0,80),new(96,32));
        Person(Draft.View,Draft.GovernorId,at+new Vector2(104,80));
        window.ActionButton(CommandText.Resolve(Texts, "label.corps-policy"),"edit",new(0,208,104,40),SelectPolicy).Disabled = !ready;
        string policy = Draft.Policy switch { null => "", GeneralDelegation => CommandText.Resolve(Texts, "policy.general"), DomesticDelegation => CommandText.Resolve(Texts, "policy.domestic"), DefeatForceDelegation => CommandText.Resolve(Texts, "policy.defeat-force"), _ => CommandText.Resolve(Texts, "policy.capture-facility") };
        Value(policy,at+new Vector2(104,208),new(80,32));
        Caption(CommandText.Resolve(Texts, "label.target"),at+new Vector2(0,248),new(96,32));
        string target = Draft.Policy switch
        {
            DefeatForceDelegation force => Texts.Resolve(Draft.View.Forces.Single(f => f.Id == force.ForceId).NameKey),
            CaptureSettlementDelegation place => Draft.View.Settlements.Single(s => s.Id == place.SettlementId).ResolveName(Texts),
            _ => ""
        };
        Value(target,at+new Vector2(104,248),new(160,32));
        Body.AddChild(new CorpsBudgetHeadings { Textures = Textures, Position = at+new Vector2(104,288) });
        Label(CommandText.Resolve(Texts, "title.delegate"),at+new Vector2(104,288),new(48,24));
        Label(CommandText.Resolve(Texts, "label.ruler"),at+new Vector2(200,288),new(48,24));
        window.ActionButton(CommandText.Resolve(Texts, "label.money"),"edit",new(0,320,104,40),() => Number(true)).Disabled = !ready || Draft.MaximumMoney == 0;
        window.ActionButton(CommandText.Resolve(Texts, "label.food"),"edit",new(0,360,104,40),() => Number(false)).Disabled = !ready || Draft.MaximumFood == 0;
        Value(populated ? Draft.Money.ToString() : "",at+new Vector2(104,320),new(72,32));
        Value(populated ? Draft.Food.ToString() : "",at+new Vector2(104,360),new(72,32));
        Value(populated ? ((long)Draft.MaximumMoney-Draft.Money).ToString() : "",at+new Vector2(200,320),new(72,32));
        Value(populated ? ((long)Draft.MaximumFood-Draft.Food).ToString() : "",at+new Vector2(200,360),new(72,32));
        var counts = Draft.Counts;
        Caption(CommandText.Resolve(Texts, "label.officer"),at+new Vector2(0,400),new(96,32));
        Value(populated ? counts.People.ToString() : "",at+new Vector2(104,400),new(40,32));
        Value(populated ? (counts.TotalPeople-counts.People).ToString() : "",at+new Vector2(200,400),new(40,32));
        Caption(CommandText.Resolve(Texts, "label.total-soldiers"),at+new Vector2(0,440),new(96,32));
        Value(populated ? counts.Soldiers.ToString() : "",at+new Vector2(104,440),new(88,32));
        Value(populated ? (counts.TotalSoldiers-counts.Soldiers).ToString() : "",at+new Vector2(200,440),new(88,32));
        Body.AddChild(new GameMessageBar { Textures = Textures, Text = CommandText.Resolve(Texts, "hint.corps") });
    }

    private string ControlName(CorpsControlMode mode) => CommandText.Resolve(Texts,
        mode == CorpsControlMode.Direct ? "corps.direct" : "corps.automatic");

    private void BuildAdministration()
    {
        bool control = Draft.Operation == CorpsOperation.Control;
        Vector2 at = new(304,224);
        var window = new CommandWindow { Texts = Texts, Textures = Textures,
            Title = CommandText.Resolve(Texts, control ? "corps.control" : "corps.policy"),
            Position = at, Size = new(416,312), CanAccept = Draft.CanExecute,
            Accepted = Submit, Closed = Closed, Help = () => Notice(CommandText.Resolve(Texts, "corps.scope")) };
        Body.AddChild(window);
        window.ActionButton(CommandText.Resolve(Texts, "select.corps"), "edit", new(0,0,104,40), () => SelectCorps(Draft.Operation));
        var corps = Draft.Selected;
        string PolicyName(CorpsDelegation? policy) => CommandText.Resolve(Texts, policy switch {
            DomesticDelegation => "policy.domestic", DefeatForceDelegation => "policy.defeat-force",
            CaptureSettlementDelegation => "policy.capture-facility", _ => "policy.general" });
        string name = corps?.LeaderId is int leader ? Texts.Resolve(Draft.View.Officers.Single(p => p.Id == leader).NameKey) : "";
        Value(name, at + new Vector2(112,0), new(288,32));
        Caption(CommandText.Resolve(Texts, "corps.current-control"), at + new Vector2(0,40), new(104,32));
        Value(corps is null ? "" : ControlName(corps.ControlMode), at + new Vector2(112,40), new(288,32));
        if (control)
        {
            window.ActionButton(CommandText.Resolve(Texts, "corps.control"), "edit", new(0,80,104,40), () =>
                Choices(CommandText.Resolve(Texts, "corps.control"), [ControlName(CorpsControlMode.Direct), ControlName(CorpsControlMode.Automatic)],
                    id => { Draft.SetControl(id == 0 ? CorpsControlMode.Direct : CorpsControlMode.Automatic); CloseChild(); }));
            Value(ControlName(Draft.ControlMode), at + new Vector2(112,80), new(288,32));
            Caption(CommandText.Resolve(Texts, "label.corps-policy"), at + new Vector2(0,120), new(104,32));
            Value(PolicyName(corps?.Delegation), at + new Vector2(112,120), new(288,32));
        }
        else
        {
            window.ActionButton(CommandText.Resolve(Texts, "label.corps-policy"), "edit", new(0,80,104,40), SelectPolicy);
            Value(PolicyName(Draft.Policy), at + new Vector2(112,80), new(288,32));
            Caption(CommandText.Resolve(Texts, "label.target"), at + new Vector2(0,120), new(104,32));
            string target = Draft.Policy switch {
                DefeatForceDelegation force => Texts.Resolve(Draft.View.Forces.Single(f => f.Id == force.ForceId).NameKey),
                CaptureSettlementDelegation place => Draft.View.Settlements.Single(s => s.Id == place.SettlementId).ResolveName(Texts), _ => "" };
            Value(target, at + new Vector2(112,120), new(288,32));
        }
        Label(CommandText.Resolve(Texts, "corps.scope"), at + new Vector2(0,168), new(416,120));
    }

    private void Choices(string title,string[] names,Action<int> selected, IReadOnlySet<int>? disabled = null)
    {
        ReplaceChild(new VerticalChoiceDialog { Message = title, Textures = Textures,
            DisabledOptions = disabled ?? new HashSet<int>(), Options = names.Select((name,id) => (name,(Action)(() => selected(id)))).Append((CommandText.Resolve(Texts, "action.cancel"),(Action)CloseChild)).ToArray() });
    }
    private void SelectOperation() => ReplaceChild(new CorpsChoiceDialog
    {
        Textures = Textures, Title = CommandText.Resolve(Texts, "label.corps-operation"),
        Hint = CommandText.Resolve(Texts, "hint.corps"),
        Options = [CommandText.Resolve(Texts, "corps.create"), CommandText.Resolve(Texts, "corps.change"), CommandText.Resolve(Texts, "corps.dissolve")],
        DisabledOptions = Draft.CanCreate ? new HashSet<int>() : new HashSet<int> { 0 },
        Closed = CloseChild,
        Selected = id => { if (id == 0) { Draft.SetOperation(CorpsOperation.Create); CloseChild(); } else SelectCorps((CorpsOperation)id); }
    }, keepBodyVisible: true);
    private void SelectCorps(CorpsOperation operation)
    {
        var query = new InformationQuery(Session,Texts);
        var ids = Draft.Corps.Where(c => operation == CorpsOperation.Control || !c.IsRoot).Select(c => c.Id).ToHashSet();
        if (ids.Count == 0) { Notice(CommandText.Resolve(Texts, "failure.no-corps")); return; }
        ReplaceChild(new CatalogView { Title = CommandText.Resolve(Texts, "select.corps"), CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Textures = Textures, Service = Service,
            Pages = AdministrativeCatalog.Pages(InformationCategory.Corps),
            ReadRows = page => query.Read(InformationCategory.Corps,page).Rows.Where(r => ids.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id,r.Cells.ToArray())).ToArray(),
            InitialCurrentId = Draft.CorpsId, Activated = id => { Draft.SetOperation(operation); Draft.SelectCorps(id); CloseChild(); }, Closed = CloseChild });
    }
    private void SelectCities()
    {
        var ids = Draft.CityCandidates;
        SelectAdministrative(Session,Draft.View,InformationCategory.Cities,ids,CommandText.Resolve(Texts, "select.city"),null,
            new(ids,Draft.Cities,int.MaxValue),selected => { Draft.SetCities(selected); CloseChild(); });
    }
    private void Number(bool money) => ReplaceChild(new NumberInputDialog { Textures = Textures, Subject = money ? CommandText.Resolve(Texts, "corps.money") : CommandText.Resolve(Texts, "corps.food"),
        State = new(0,money ? Draft.MaximumMoney : Draft.MaximumFood,money ? Draft.Money : Draft.Food),
        Confirmed = value => { if (money) Draft.SetMoney(value); else Draft.SetFood(value); CloseChild(); }, Closed = CloseChild });
    private void SelectPolicy() => ReplaceChild(new CorpsChoiceDialog {
        Title = CommandText.Resolve(Texts, "label.corps-policy"), Hint = CommandText.Resolve(Texts, "hint.corps-policy"),
        Options = [CommandText.Resolve(Texts, "policy.general"), CommandText.Resolve(Texts, "policy.domestic"), CommandText.Resolve(Texts, "policy.defeat-force"), CommandText.Resolve(Texts, "policy.capture-facility")],
        Textures = Textures, Closed = CloseChild, Selected = id =>
    {
        if (id < 2) { Draft.SetPolicy(id == 0 ? new GeneralDelegation() : new DomesticDelegation()); CloseChild(); }
        else SelectPolicyTarget(id == 2);
    }, DisabledOptions = new[] { Draft.PolicyTargets(true).IsEmpty ? 2 : -1, Draft.PolicyTargets(false).IsEmpty ? 3 : -1 }.Where(id => id >= 0).ToHashSet() }, keepBodyVisible: true);
    private void SelectPolicyTarget(bool force)
    {
        if (force)
        {
            var forceIds = Draft.PolicyTargets(true);
            var forces = new CommandForceCatalog(Draft.View,Texts,forceIds);
            SelectAdministrative(Session,Draft.View,InformationCategory.Forces,forceIds,CommandText.Resolve(Texts, "select.force"),
                id => { Draft.SetPolicy(new DefeatForceDelegation(id)); CloseChild(); },
                pages: forces.Pages, readRows: forces.Read, hintText: CommandText.Resolve(Texts, "hint.target-force"), canceled: SelectPolicy);
            return;
        }
        var ids = Draft.PolicyTargets(false).ToHashSet();
        if (ids.Count == 0) { Notice(CommandText.Resolve(Texts, "failure.no-attack-target")); return; }
        var facilities = new CommandFacilityCatalog(Draft.View,Texts,"delegation",ids);
        SelectMapTargets(new(Draft.View,CommandMapTargetKind.Settlement,ids,CommandTargetMarker.Attack),CommandText.Resolve(Texts, "select.facility"),
            facilities.Pages,facilities.Read,id => { Draft.SetPolicy(new CaptureSettlementDelegation(id)); CloseChild(); },SelectPolicy);
    }
}

internal partial class CorpsBudgetHeadings : Control
{
    public TextureScope Textures { get; init; } = null!;
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        CommandPanelArt.Strip(this,Textures,"deployment-mode",new(0,0,48,24));
        CommandPanelArt.Strip(this,Textures,"deployment-mode",new(96,0,48,24));
    }
}
