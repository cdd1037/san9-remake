using San9.Content.Maps;

namespace San9.Content.Interface;

public sealed record MapBodySounds(string[] Movement, string[] Melee, string[] Ranged);
public sealed record MapAudioDefinition(string ExecutionTitleSound, string[] SeasonalMusic,
    string[] RankedSeasonalMusic, int[] RankedMusicRankIds, Dictionary<string, MapBodySounds> Bodies,
    Dictionary<int, string> ConstructionSounds, string[] BattleAmbience)
{
    [System.Text.Json.Serialization.JsonRequired] public string RecordNotificationSound { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired] public string[] FacilityArrowSounds { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public string[] RockfallImpactSounds { get; init; } = [];
    public IEnumerable<string> SoundRoles => new[] { ExecutionTitleSound, RecordNotificationSound }.Concat(ConstructionSounds.Values)
        .Concat(FacilityArrowSounds).Concat(RockfallImpactSounds)
        .Concat(SeasonalMusic).Concat(RankedSeasonalMusic).Concat(BattleAmbience)
        .Concat(Bodies.Values.SelectMany(b => b.Movement.Concat(b.Melee).Concat(b.Ranged)));

    public void Validate(IReadOnlyDictionary<string, string> sounds)
    {
        if (SeasonalMusic is null || RankedSeasonalMusic is null || RankedMusicRankIds is null ||
            Bodies is null || ConstructionSounds is null || BattleAmbience is null || FacilityArrowSounds is null || RockfallImpactSounds is null ||
            SeasonalMusic.Length != 4 || RankedSeasonalMusic.Length != 4 || BattleAmbience.Length == 0 ||
            RankedMusicRankIds.Any(r => r < 0) || RankedMusicRankIds.Distinct().Count() != RankedMusicRankIds.Length ||
            Bodies.Any(b => string.IsNullOrWhiteSpace(b.Key) || b.Value is null || b.Value.Movement is null || b.Value.Melee is null || b.Value.Ranged is null) ||
            ConstructionSounds.Keys.Any(id => id < 0) ||
            SoundRoles.Any(r => string.IsNullOrWhiteSpace(r) || !sounds.ContainsKey(r)))
            throw new InvalidDataException("Invalid map audio presentation.");
    }

    public void ValidateReferences(MapUnitCatalog units, IEnumerable<int> ranks, IEnumerable<int> facilityTypes)
    {
        if (!Bodies.Keys.ToHashSet().SetEquals(units.Bodies.Select(b => b.Id)) || RankedMusicRankIds.Except(ranks).Any() ||
            ConstructionSounds.Keys.Except(facilityTypes).Any())
            throw new InvalidDataException("Unknown map audio body, rank or facility reference.");
        if (units.Projectiles.ContainsKey(MapProjectileKind.Arrow) && FacilityArrowSounds.Length == 0 ||
            units.Projectiles.ContainsKey(MapProjectileKind.Rockfall) && RockfallImpactSounds.Length == 0)
            throw new InvalidDataException("Missing facility projectile sound binding.");
    }
}
