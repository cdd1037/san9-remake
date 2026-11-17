using Godot;
using San9.Content.Authoring;

namespace San9.Client.Godot;

public partial class CreatedOfficerEditor
{
    private void BuildPages(CreatedOfficer person)
    {
        var rules = _definitions?.Military?.Tactics.Where(t => t.IsOfficerTactic).ToDictionary(t => t.Id);
        var tactics = _definitions?.Information.Tactics.Where(t => rules?.ContainsKey(t.Id) == true)
            .Select(t => new OfficerTacticChoice(t.Id, t.Name, rules![t.Id].Category!.Value)).ToArray() ?? [];
        OfficerEditorValues Read() => OfficerEditorValues.From(Library.Officers.Single(p => p.Id == person.Id));
        OfficerEditorForm.Build(this, _tabs, Read, edit => Change(person.Id, p => edit(OfficerEditorValues.From(p)).Apply(p)),
            tactics, _portraitService, parent => BuildFlags(parent, person), parent => BuildRelationships(parent, person),
            ShowCurrent, (_definitions?.OfficerGrowth ?? San9.Core.Domain.OfficerGrowthRules.Default).TacticProficiencyCap, surname =>
            {
                surname.Editable = !Preferences.Current.AutoAssignOfficerFlag || Workspace.FlagCatalog is not null;
                if (!surname.Editable) surname.TooltipText = "公共旗字目录载入后可编辑姓氏。";
                surname.TextSubmitted += _ => FinishSurnameEdit(person.Id);
                surname.FocusExited += () => FinishSurnameEdit(person.Id);
            });
    }

    private void BuildRelationships(Node parent, CreatedOfficer person)
    {
        var references = Library.Officers.Select(o => (Label: "自创：" + o.FullName, Created: (Guid?)o.Id, Historical: (int?)null))
            .Concat(_historicalOfficers.Select(o => (Label: "历史：" + o.Name, Created: (Guid?)null, Historical: (int?)o.Id))).ToArray();
        void Reference(string title, Guid? created, int? historical, bool allowSelf,
            Action<(Guid? Created, int? Historical)> changed)
        {
            var candidates = references.Where(r => allowSelf || r.Created != person.Id)
                .Select(r => (Label: r.Label, Value: (Created: r.Created, Historical: r.Historical)))
                .Prepend((Label: allowSelf ? "随亲代／独立家族" : "无亲代", Value: (Created: (Guid?)null, Historical: (int?)null))).ToArray();
            SearchableReferenceField.Add(parent, title, candidates, (Created: created, Historical: historical), changed,
                allowSelf ? "ChooseBloodline" : "ChooseParent", "按姓名搜索人物库或共享目录中的人物。",
                historical is int id ? $"历史人物 {id}（共享目录中不可用）" : "人物库引用不可用");
        }
        Reference("亲代", person.ParentId, person.ScenarioParentId, false,
            value => Change(person.Id, p => p with { ParentId = value.Created, ScenarioParentId = value.Historical }, rebuild: true));
        Reference("血缘", person.BloodlineRootId, person.HistoricalBloodlineRootId, true,
            value => Change(person.Id, p => p with { BloodlineRootId = value.Created, HistoricalBloodlineRootId = value.Historical }, rebuild: true));
    }
}
