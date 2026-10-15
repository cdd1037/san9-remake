using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.AI;

/// <summary>Individual alternatives and complementary crews, without enumerating the power set.</summary>
internal static class CampaignCrews
{
    internal static IEnumerable<(ImmutableArray<int> Members, bool Seed)> Enumerate(int[] people, int capacity,
        ImmutableDictionary<int, OfficerAbilities> abilities, Func<int, ImmutableArray<TacticRule>> tactics)
    {
        var orders = new List<int[]>
        {
            people.OrderByDescending(p => abilities[p].Leadership).ThenBy(p => p).ToArray(),
            people.OrderByDescending(p => abilities[p].War).ThenBy(p => p).ToArray(),
            people.OrderByDescending(p => abilities[p].Intelligence).ThenBy(p => p).ToArray()
        };
        foreach (var category in people.SelectMany(p => tactics(p)).Where(t => t.Category is not null).Select(t => t.Category!.Value).Distinct().Order())
            orders.Add(people.OrderByDescending(p => tactics(p).Where(t => t.Category == category).Select(t => t.Power * (long)abilities[p].War).DefaultIfEmpty(0).Max())
                .ThenByDescending(p => abilities[p].Leadership).ThenBy(p => p).ToArray());
        HashSet<string> seen = [];
        if (people.Length > 0)
        {
            int leader = orders[0][0];
            foreach (var order in orders)
            {
                var single = ImmutableArray.Create(order[0]);
                if (seen.Add(string.Join(",", single))) yield return (single, true);
                var pair = new[] { leader, order[0] }.Distinct().Take(capacity).Order().ToImmutableArray();
                if (seen.Add(string.Join(",", pair))) yield return (pair, true);
                var group = order.Take(capacity).Order().ToImmutableArray();
                if (seen.Add(string.Join(",", group))) yield return (group, true);
            }
        }
        foreach (int person in people)
            if (seen.Add(person.ToString(System.Globalization.CultureInfo.InvariantCulture))) yield return ([person], false);
        foreach (var order in orders)
        {
            if (order.Length == 0) continue;
            var crew = new List<int> { order[0] };
            while (crew.Count < Math.Min(capacity, people.Length))
            {
                int leadership = crew.Max(p => abilities[p].Leadership);
                int intelligence = crew.Max(p => abilities[p].Intelligence);
                int next = order.Where(p => !crew.Contains(p)).OrderByDescending(p =>
                    Math.Max(0, abilities[p].Leadership - leadership) + Math.Max(0, abilities[p].Intelligence - intelligence))
                    .ThenBy(p => Array.IndexOf(order, p)).First();
                crew.Add(next);
                var members = crew.Order().ToImmutableArray();
                if (seen.Add(string.Join(",", members))) yield return (members, false);
            }
        }
        // An external commander can improve a weak local crew, not only fill an empty base.
        yield return ([], false);
    }
}
