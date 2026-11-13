using System.Collections.Immutable;

namespace San9.Client.Godot;

internal static class TacticCategoryLabels
{
    internal static ImmutableArray<string> Names { get; } = ["步兵", "骑兵", "弓骑", "弓弩", "水军", "攻城", "智略", "谋略", "策略"];
}
