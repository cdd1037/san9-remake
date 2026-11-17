using Godot;
using San9.Content.Authoring;
using San9.Content.Scenarios;

namespace San9.Client.Godot;

public partial class OfficerEditorPage : SharedCatalogEditorPage
{
    public TextureService? PortraitService { get; init; }
    public OfficerFlagCatalog? Flags { get; init; }
    private int _tab;
    protected override bool ScrollForm => false;
    protected override string Subject => "武将";
    protected override IEnumerable<(int Id, string Name)> Entries => Workspace.Officers.Select(p => (p.Id, p.Name));
    protected override bool Changed(int id) => Workspace.IsOfficerChanged(id);
    protected override void Restore(int id) => Workspace.RestoreOfficer(id);
    private void Edit(int id, Func<OfficerDefaults, OfficerDefaults> change) => Workspace.SetOfficer(change(Workspace.GetOfficer(id)));

    protected override void BuildForm(int id)
    {
        var tabs = new TabContainer { Name = "OfficerPages", SizeFlagsVertical = SizeFlags.ExpandFill };
        Form.AddChild(tabs);
        var tactics = Workspace.Tactics.Where(t => t.IsOfficerTactic)
            .Select(t => new OfficerTacticChoice(t.Id, Workspace.GetTacticCatalog(t.Id).Name, t.Category!.Value)).ToArray();
        OfficerEditorForm.Build(this, tabs, () => OfficerEditorValues.From(Workspace.GetOfficer(id)),
            Guard<Func<OfficerEditorValues, OfficerEditorValues>>(change => Edit(id, p => change(OfficerEditorValues.From(p)).Apply(p))),
            tactics, PortraitService, parent => BuildFlags(parent, id), parent => BuildRelationships(parent, id), Rebuild,
            Workspace.Definitions.OfficerGrowth.TacticProficiencyCap);
        tabs.CurrentTab = _tab;
        tabs.TabChanged += index => _tab = (int)index;
    }

    private void BuildFlags(Node parent, int id)
    {
        var person = Workspace.GetOfficer(id);
        var change = Guard<int>(glyph => { Edit(id, p => p with { FlagGlyph = glyph }); Rebuild(); });
        OfficerFlagField.Add(parent, PortraitService, Flags, person.FlagGlyph, () =>
        {
            if (Flags is { } flags && PortraitService is { } service)
                OfficerFlagField.Show(this, service, flags, Workspace.GetOfficer(id).FlagGlyph, change);
        }, () => { if (Flags is { } flags) change(flags.Resolve(Workspace.GetOfficer(id).Surname)); });
    }

    private void BuildRelationships(Node parent, int id)
    {
        var person = Workspace.GetOfficer(id);
        void Reference(string title, int? current, bool allowSelf, Action<int?> changed)
        {
            var candidates = Workspace.Officers.Where(p => allowSelf || p.Id != id)
                .Select(p => ($"{p.Name}（{p.Id}）", (int?)p.Id)).Prepend(("无" + title, (int?)null)).ToArray();
            SearchableReferenceField.Add(parent, title, candidates, current, Guard(changed),
                allowSelf ? "ChooseBloodline" : "ChooseParent", "按姓名搜索共享目录中的人物。",
                $"人物 {current}（不在可选人物目录中）");
        }
        Reference("亲代", person.ParentId, false, value => Edit(id, p => p with { ParentId = value }));
        Reference("血缘", person.BloodlineId, true, value => Edit(id, p => p with { BloodlineId = value }));
    }
}
