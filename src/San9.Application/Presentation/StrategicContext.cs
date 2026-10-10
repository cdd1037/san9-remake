using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record StrategicContextPrompt(string Role, int ActorId, int RulerId, int ForceId,
    int? PlaceId = null, int? CityId = null, int? OtherOfficerId = null, int? TreasureId = null,
    int? EnemyForceId = null, int? OtherPlaceId = null);
public enum StrategicClueKind { Person, Treasure }
public sealed record StrategicClue(StrategicClueKind Kind, GameClock Clock, int? OfficerId, int? TreasureId, int PlaceId, bool RegionOnly);
public sealed record StrategicContextState(GameClock? Clock, ImmutableArray<int> ReportedEnemyUnits,
    ImmutableArray<StrategicContextPrompt> Prompts, ImmutableArray<StrategicClue> Clues)
{
    public static StrategicContextState Empty => new(null, [], [], []);
}

/// <summary>Once-per-strategy-entry publication owned by the session, including authorized hidden-location clues.</summary>
public static class StrategicContext
{
    public static StrategicContextState Prepare(StrategySnapshot view, StrategicContextState previous,
        Func<int, int>? choose = null)
    {
        var world = view.World;
        if (!world.Clock.IsXunBoundary || world.Simulation.Boundary != SimulationBoundary.Stable ||
            world.Clock.DayNumber <= world.Simulation.Started.DayNumber || previous.Clock == world.Clock) return previous;
        var force = world.Forces.Single(f => f.Id == view.PlayerForceId);
        var available = world.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == force.Id).ToArray();
        var actor = available.FirstOrDefault(p => p.Id == force.AdviserId);
        var ruler = available.FirstOrDefault(p => p.Id == force.LeaderId);
        var reported = previous.ReportedEnemyUnits.Where(id => world.Units.Any(u => u.Id == id)).ToHashSet();
        List<StrategicContextPrompt> prompts = [];
        var clues = previous.Clues;
        if (actor is null || ruler is null) return new(world.Clock, reported.Order().ToImmutableArray(), [], clues);
        var random = new StableRandom(StableRandom.DeriveSeed(world.Simulation.InitialSeed,
            FormattableString.Invariant($"strategic-context:{world.Clock.DayNumber}:{force.Id}")));
        int Pick(int length) => choose?.Invoke(length) ?? (int)(random.NextUInt64() % (ulong)length);
        bool Chance(int chance) => Pick(100) < Math.Clamp(chance, 0, 100);
        void Add(string role, int? place = null, int? city = null, int? other = null, int? treasure = null,
            int? enemy = null, int? otherPlace = null, int? speaker = null) => prompts.Add(new(role, speaker ?? actor.Id,
                ruler.Id, force.Id, place, city, other, treasure, enemy, otherPlace));
        int City(int place) => view.Settlements.Any(s => s.Id == place && s.Kind == SettlementKind.City) ? place : view.RegionCityIds[place];
        var owned = view.Settlements.Where(s => s.OwnerId == force.Id && !s.IsDestroyed && !s.IsUnderConstruction).ToArray();
        var threats = world.Units.Where(u => u.OwnerId != force.Id && !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            u.EffectiveOrderClass == UnitOrderClass.Military && !reported.Contains(u.Id) &&
            u.EffectiveOrder is { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int target } && owned.Any(s => s.Id == target))
            .OrderByDescending(u => u.Soldiers).ThenBy(u => u.Id).ToArray();
        foreach (var enemyGroup in threats.GroupBy(u => (u.EffectiveOrder!.SettlementId,u.OwnerId)))
        {
            var enemy = enemyGroup.First();
            var place = owned.Single(s => s.Id == enemy.EffectiveOrder!.SettlementId);
            foreach (var unit in enemyGroup) reported.Add(unit.Id);
            Add(place.Kind == SettlementKind.City ? "enemy-city" : "enemy-place", place.Id, City(place.Id), enemy: enemy.OwnerId);
            var local = available.Where(p => p.Id != actor.Id && p.SettlementId == place.Id).ToArray();
            string? reaction = null;
            OfficerState[] candidates = [];
            void Select(string role, Func<OfficerState, bool> predicate)
            { var pool = local.Where(predicate).ToArray(); if (pool.Length > 0) { reaction = role; candidates = pool; } }
            if (place.Soldiers <= 1000 && place.Soldiers <= enemy.Soldiers)
                Select("defense-desperate", p => p.Personality == 3);
            var military = view.MilitaryRules;
            var formation = military?.Formation(enemy);
            bool strong = place.Soldiers >= 3000 && 3L * place.Soldiers >= enemy.Soldiers;
            if (reaction is null && strong && place.Kind == SettlementKind.City && formation?.TravelMode == TravelMode.Water)
                Select("defense-rockfall", p => p.Personality <= 1 && p.TacticProficiencies[4] > 300 &&
                    p.LearnedTactics.Any(id => military!.Tactics[id].FacilityAttack?.UsesRockfallPower == true));
            if (reaction is null && strong && formation?.Attack?.DamagesWalls == true)
                Select("defense-siege", p => p.Personality <= 1);
            if (reaction is null && 3L * place.Soldiers <= enemy.Soldiers && enemy.Soldiers >= 5000)
            {
                bool defended = place.Kind == SettlementKind.City || world.Settlements.Single(s => s.Id == place.Id).CanHostPersons;
                if (!defended || place.Durability > place.MaxDurability / 5)
                    Select(defended ? "defense-city" : "defense-place", p => p.Personality >= 2);
            }
            if (reaction is null && strong) Select("defense-counterattack", p => p.Personality <= 1);
            if (reaction is not null) Add(reaction, place.Id, City(place.Id), speaker: candidates[Pick(candidates.Length)].Id);
        }
        if (prompts.Count > 0) return Finish();
        int intelligence = actor.BaseAbilities.Intelligence;
        if (!Chance(10 + Math.Max(0,intelligence - 70) + Math.Max(0,intelligence - 90))) return Finish();
        if (world.Clock.Boundary.Quarter)
        {
            var borders = view.TerritoryConnections();
            SettlementView[] Neighbors(SettlementView source) => owned.Where(s => s.Id != source.Id && world.Settlements.Single(p => p.Id == s.Id).CanHostPersons &&
                borders.Any(b => b.FromRegionId == source.Id && b.ToRegionId == s.Id || b.ToRegionId == source.Id && b.FromRegionId == s.Id)).ToArray();
            // Frontier candidates are friendly places adjacent to a foreign occupied place; an active attack is not required.
            var threatened = owned.Where(s => borders.Any(b => (b.FromRegionId == s.Id || b.ToRegionId == s.Id) &&
                view.Settlements.Any(other => other.Id == (b.FromRegionId == s.Id ? b.ToRegionId : b.FromRegionId) &&
                    other.OwnerId is int owner && MilitaryDiplomacy.AutomaticEnemy(world,force.Id,owner) && !other.IsDestroyed && !other.IsUnderConstruction))).ToArray();
            var largest = threatened.OrderByDescending(s => s.Soldiers).ThenBy(s => s.Id).FirstOrDefault();
            if (largest is { Soldiers: >= 20000 } && Neighbors(largest).OrderBy(s => s.Soldiers).FirstOrDefault() is { } weak &&
                3L * weak.Soldiers <= largest.Soldiers)
            { Add("reinforce-from", largest.Id, City(largest.Id), otherPlace: weak.Id); return Finish(); }
            var undefended = threatened.FirstOrDefault(s => !world.Units.Any(u => u.OwnerId == force.Id && u.ServicePlaceId == s.Id));
            if (undefended is not null && Neighbors(undefended).OrderByDescending(s => s.Soldiers).FirstOrDefault() is { } strongNeighbor &&
                strongNeighbor.Soldiers >= 3L * undefended.Soldiers)
            { Add("reinforce-to", undefended.Id, City(undefended.Id), otherPlace: strongNeighbor.Id); return Finish(); }
            var outlook = view.PlayerEconomicOutlook;
            if (outlook?.Food.Sufficient == false) { Add("food-shortage"); return Finish(); }
            if (outlook?.Money.Sufficient == false) { Add("money-shortage"); return Finish(); }
        }
        bool EligiblePlace(int id) => owned.Any(s => s.Id == id) || !world.Settlements.Any(s => s.Id == id &&
            (s.OwnerId is not null || s.IsDormant || s.IsDestroyed));
        int accuracy = Math.Min(100,60 + Math.Max(0,intelligence - 70) + Math.Max(0,intelligence - 90));
        bool Accurate(int place) => new StableRandom(StableRandom.DeriveSeed(world.Simulation.InitialSeed,
            FormattableString.Invariant($"strategic-clue-accuracy:{place}"))).NextUInt64() % 100 < (ulong)accuracy;
        bool Hint(string subject, int location, int? officer = null, int? treasure = null)
        {
            if (!Accurate(location) || !Chance(50)) return false;
            bool city = view.Settlements.Any(s => s.Id == location && s.Kind == SettlementKind.City);
            string role = subject + (intelligence >= 100 ? city ? "-city" : "-place" :
                intelligence >= 90 ? city ? "-city-anonymous" : "-place-anonymous" : "-region");
            Add(role, location, City(location), other: officer, treasure: treasure);
            var clue = new StrategicClue(subject == "person" ? StrategicClueKind.Person : StrategicClueKind.Treasure,world.Clock,intelligence >= 100 ? officer : null,
                intelligence >= 100 ? treasure : null,intelligence < 90 ? City(location) : location,intelligence < 90);
            if (!clues.Any(c => c.Kind == clue.Kind && c.OfficerId == clue.OfficerId && c.TreasureId == clue.TreasureId && c.PlaceId == clue.PlaceId && c.RegionOnly == clue.RegionOnly))
                clues = clues.Add(clue);
            return true;
        }
        var hidden = world.Officers.Where(p => !p.IsDormant && p.Status == OfficerStatus.Undiscovered &&
            (p.SettlementId ?? p.RegionId) is int place && EligiblePlace(place)).OrderBy(p => p.Id).ToArray();
        if (hidden.Length > 0)
        { var person = hidden[Pick(hidden.Length)]; if (Hint("person",(person.SettlementId ?? person.RegionId)!.Value,officer: person.Id)) return Finish(); }
        var buried = world.Treasures.Where(t => t.Discovery == TreasureDiscovery.Buried && t.DiscoveryRegionId is int place && EligiblePlace(place)).OrderBy(t => t.Id).ToArray();
        if (buried.Length > 0)
        { var item = buried[Pick(buried.Length)]; if (Hint("treasure",item.DiscoveryRegionId!.Value,treasure: item.Id)) return Finish(); }
        if (world.Clock.Boundary.Quarter && world.Simulation.NaturalDeathsEnabled)
        {
            var aging = available.Where(p => p.BirthYear > 0 && NaturalMortality.Chance(world.Clock.Year,p) > 0).OrderBy(p => p.Id).ToArray();
            if (aging.Length > 0 && Chance(50))
            { var person = aging[Pick(aging.Length)]; Add(person.Id == ruler.Id ? "ruler-health" : person.Id == actor.Id ? "adviser-health" : "officer-health", other: person.Id); }
        }
        return Finish();

        StrategicContextState Finish() => new(world.Clock, reported.Order().ToImmutableArray(), prompts.ToImmutableArray(), clues);
    }

    public static IReadOnlyList<CommandMessage> Produce(StrategicContextState state, StrategySnapshot view, WorldMessageContext context,
        TextCatalog texts, ProgressPresentationDefinition content)
    {
        if (state.Clock is not { } clock) return [];
        return state.Prompts.Select((prompt,index) =>
        {
            Dictionary<string,MessageBinding> values = new()
            {
                ["actor"] = ProgressMessageProducer.Person(prompt.ActorId), ["ruler"] = ProgressMessageProducer.Person(prompt.RulerId),
                ["force"] = ProgressMessageProducer.Force(prompt.ForceId)
            };
            void Add(string role,int? id,MessageEntityKind kind) { if (id is int value) values[role] = MessageBinding.Reference(kind,value); }
            Add("other",prompt.OtherOfficerId,MessageEntityKind.Person); Add("treasure",prompt.TreasureId,MessageEntityKind.Treasure);
            MessageEntityKind PlaceKind(int? id) => view.Settlements.Any(s => s.Id == id) ? MessageEntityKind.Settlement : MessageEntityKind.Region;
            Add("place",prompt.PlaceId,PlaceKind(prompt.PlaceId)); Add("city",prompt.CityId,MessageEntityKind.Settlement);
            Add("other-place",prompt.OtherPlaceId,PlaceKind(prompt.OtherPlaceId)); Add("enemy-force",prompt.EnemyForceId,MessageEntityKind.Force);
            // Constructed and fixed settlements use the settlement contract; unbuilt locations remain regions.
            var definition = content.Context[prompt.Role];
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)clock.DayNumber,$"context-text:{prompt.ForceId}:{index}"));
            var message = ProgressMessageProducer.Produce(definition,context.AtOccurrence(clock),texts,values,n => (int)(random.NextUInt64() % (ulong)n));
            // Strategy-entry advice is dialogue; disclosed clues are retained in the session, not the execution log.
            return message with { RecordInJournal = false };
        }).ToArray();
    }
}
