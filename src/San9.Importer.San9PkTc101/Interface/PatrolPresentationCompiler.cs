using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class PatrolPresentationCompiler
{
    internal static readonly (string Role, string File)[] SoundSources =
        [("patrol-citizen", "SE/SE_G008.WAV"), ("domestic-result", "SE/SE_C006.WAV")];

    internal static CommandPresentation Compile(IReadOnlyList<San9ScenarioPersonRecord> people)
    {
        int Person(int biography) => people.Take(700).Select((person, id) => (person, id))
            .Single(p => p.person.BiographyResourceCode == biography).id;
        string Key(int source) => $"messages.{source}";
        // RE-0095: table order is short-circuit priority; city identities are normalized source definitions.
        return new(SoundSources.ToDictionary(s => s.Role, s => $"audio/{s.Role}.wav"),
            new(new() { ["normal"] = Key(5592), ["cooperative"] = Key(5826), ["citizen"] = Key(5832),
                ["pair-first"] = Key(5861), ["pair-second"] = Key(5862), ["result"] = Key(5598) }, Person(674),
                [new(Key(5873), Person(671), [], AdviceMetric.PublicOrder, 0),
                 new(Key(5874), Person(669), [17,18,20,19], AdviceMetric.PublicOrder, 2000),
                 new(Key(5875), Person(673), [24,25,44,43], AdviceMetric.Commerce, 2500),
                 new(Key(5876), Person(670), [1,0,18,25,43], AdviceMetric.Durability, 1500, true),
                 new(Key(5877), Person(674), [15,29,16,40,23], AdviceMetric.PublicOrder, 4000)]));
    }
}
