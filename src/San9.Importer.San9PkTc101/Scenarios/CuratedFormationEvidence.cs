using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>Remake choices for positively identified source-format people, with the historical-to-game mapping marked as inference.</summary>
public static class CuratedFormationEvidence
{
    public static ScenarioInitialState Apply(ScenarioInitialState scenario, TextCatalog texts)
    {
        var people = scenario.Officers.Select(person =>
        {
            if (person.InitialFormationEvidence is not null || person.State.FormationProficiencies is not null ||
                person.CourtesyNameKey is not string courtesy || !texts.Entries.TryGetValue(person.NameKey, out var name) ||
                !texts.Entries.TryGetValue(courtesy, out var courtesyName)) return person;
            FormationEvidence? record = (person.State.Id, name, courtesyName) switch
            {
                (278, "诸葛亮", "孔明") when scenario.Year >= 214 => new(28, 600, FormationEvidenceKind.ExperienceInference,
                    "《三国志·诸葛亮传》：https://ctext.org/text.pl?if=gb&node=603517&remap=gb&show=parallel",
                    "记载推演兵法、作八阵图；将防守型偃月列为初始掌握是游戏推定，不能认定八阵图即偃月。"),
                (642, "吕布", "奉先") when scenario.Year >= 190 => new(31, 600, FormationEvidenceKind.ExperienceInference,
                    "《三国志·吕布传》：https://ctext.org/text.pl?if=gb&node=602263&remap=gb",
                    "记载便于弓马；将骑兵型车悬列为初始掌握是游戏推定，并无其使用此阵的记载。"),
                (422, "张辽", "文远") when scenario.Year >= 216 => new(30, 600, FormationEvidenceKind.ExperienceInference,
                    "《三国志·张辽传》：https://ctext.org/text.pl?if=gb&node=602710&remap=gb",
                    "记载合肥募士出击；依据其战斗经历推定掌握钩行，史书并未记载此阵或据此证明步骑协同。"),
                (324, "曹仁", "子孝") when scenario.Year >= 210 => new(28, 600, FormationEvidenceKind.ExperienceInference,
                    "《三国志·曹仁传》：https://www.shidianguji.com/zh/book/LS0004/chapter/1j7fnutukl9o8_33",
                    "记载曹仁驻守江陵、抵御周瑜；依据防守经历推定掌握偃月，史书并未记载其使用此阵。"),
                (113, "甘宁", "兴霸") when scenario.Year >= 210 => new(29, 600, FormationEvidenceKind.ExperienceInference,
                    "《三国志·甘宁传》：https://ctext.org/text.pl?if=gb&node=604280&remap=gb",
                    "记载其部众携带弓弩并参与战斗；据此推定掌握利于弓弩的衡轭，史书并未记载其使用此阵。"),
                _ => null
            };
            return record is null || scenario.Military?.Formations.Any(f => f.Id == record.FormationId && f.TravelMode == TravelMode.Land) != true
                ? person : person with { InitialFormationEvidence = [record] };
        }).ToArray();
        return scenario with { Officers = people };
    }
}
