using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class EndingContentCompiler
{
    // RE-SAN9PK-0142/0143: source geography is converted here, never inferred from runtime identity ranges.
    public static TerminalDefinition Terminal() => new(Enumerable.Range(0, 45).ToImmutableArray(), 350);

    public static EndingDefinition Ending(IEnumerable<TreasureDefinition> treasures) => new(
        [17, 18, 20, 19], [48, 49], [24, 25, 44, 43, 42, 23],
        [1, 0, 18, 25, 43], [46, 47], [15, 29, 16, 40, 23],
        treasures.Where(t => t.Category is 11 or 12).Select(t => t.Id).Order().ToImmutableArray());
}
