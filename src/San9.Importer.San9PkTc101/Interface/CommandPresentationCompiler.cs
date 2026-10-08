using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class CommandPresentationCompiler
{
    internal static readonly (string Role, string File)[] SoundSources =
        [("decision-announcement", "SE/SE_G001.WAV"), ("decision-success", "SE/SE_G008.WAV"), ("decision-failure", "SE/SE_G009.WAV"), ("decision-increase", "SE/SE_G005.WAV"), ("decision-decrease", "SE/SE_G006.WAV"), ("decision-study", "SE/SE_C004.WAV"), ("patrol-citizen", "SE/SE_G008.WAV"), ("domestic-result", "SE/SE_C006.WAV"),
         ("personnel-departure", "SE/SE_C004.WAV"), ("personnel-territory", "SE/SE_G012.WAV"), ("facility-result", "SE/SE_G005.WAV"), ("recruit-result", "SE/SE_C002.WAV"),
         ("trade-start", "SE/SE_C001.WAV"), ("repair-complete", "SE/se_m002.WAV"), ("reward-result", "SE/SE_G005.WAV"), ("exile-result", "SE/SE_G001.WAV"), ("loyalty-decrease", "SE/SE_G006.WAV"), ("execution-result", "SE/SE_G002.WAV")];

    internal static CommandPresentation Compile(IReadOnlyList<San9ScenarioPersonRecord> people)
    {
        int Person(int biography) => people.Take(700).Select((person, id) => (person, id))
            .Single(p => p.person.BiographyResourceCode == biography).id;
        string Key(int source) => $"messages.{source}";
        // RE-0095: table order is short-circuit priority; city identities are normalized source definitions.
        FacilityPresentationDefinition Development(int normal, int cooperative, int citizen, int biography, int pair, int result) =>
            new(new() { ["normal"] = Key(normal), ["cooperative"] = Key(cooperative), ["citizen"] = Key(citizen),
                ["pair-first"] = Key(pair), ["pair-second"] = Key(pair+1), ["result"] = Key(result) }, Person(biography));
        return new(SoundSources.ToDictionary(s => s.Role, s => $"audio/{s.Role}.wav"),
            new(new() { ["normal"] = Key(5592), ["cooperative"] = Key(5826), ["citizen"] = Key(5832),
                ["pair-first"] = Key(5861), ["pair-second"] = Key(5862), ["result"] = Key(5598) }, Person(674),
                [new(Key(5873), Person(671), [], AdviceMetric.PublicOrder, 0),
                 new(Key(5874), Person(669), [17,18,20,19], AdviceMetric.PublicOrder, 2000),
                 new(Key(5875), Person(673), [24,25,44,43], AdviceMetric.Commerce, 2500),
                 new(Key(5876), Person(670), [1,0,18,25,43], AdviceMetric.Durability, 1500, true),
                 new(Key(5877), Person(674), [15,29,16,40,23], AdviceMetric.PublicOrder, 4000)]))
        {
            Decisions = DecisionPresentationCompiler.Compile(people),
            Personnel = PersonnelPresentationCompiler.Compile(),
            TreasureTransfer = new(Key(5535), Key(5574), Key(5542), Key(5788), Key(5575), Key(7176), Key(7177), Key(7407), "reward-result", "loyalty-decrease"),
            Execution = new(Person(669), Key(5753), Key(5468), Key(5754), "execution-result"),
            Offices = new(Key(5572), Key(5573)),
            Strategist = new(Key(5515), Key(5523), Key(5530), Key(5534)),
            Exile = new(Person(669), Key(5755), Key(5496), Key(5756), Key(5503), Key(5857), "exile-result"),
            Reward = new(Key(5646), Key(5859), "reward-result"),
            Facilities = new() {
                ["commerce"] = Development(5599,5805,5811,669,5863,5605),
                ["cultivate"] = Development(5606,5798,5804,673,5865,5612),
                ["repair"] = Development(5613,5812,5818,671,5867,5619) with { Messages = new(Development(5613,5812,5818,671,5867,5619).Messages)
                    { ["complete"] = Key(5620), ["complete-dialogue"] = Key(5621) } },
                ["train"] = Development(5633,5819,5825,670,5869,5639),
                ["recruit"] = new(new() { ["normal"] = Key(5627), ["result"] = Key(7173) }),
                ["buy"] = new(new() { ["normal"] = Key(5640), ["result"] = Key(5777) }),
                ["sell"] = new(new() { ["normal"] = Key(5640), ["result"] = Key(5776) }) }
        };
    }
}
