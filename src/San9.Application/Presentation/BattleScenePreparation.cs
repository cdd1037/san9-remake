using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Application.Presentation;

/// <summary>Resolves scene resources and fixes visual randomness for one captured occurrence.</summary>
internal sealed record BattleScenePreparation(BattleZoomDefinition Zoom, BattleZoomPlayback Playback,
    BattleOfficerPlayback? Officers, ImmutableArray<BattleSceneLayer> Backdrop)
{
    public static BattleScenePreparation Create(StrategySnapshot before, TacticOccurrence occurrence,
        BattleZoomDefinition zoom, BattlePresentationDefinition battle, BattleDamageEvent? hit,
        TacticSnipingResolvedEvent? sniping, bool blocked, GameClock clock, int index,
        IReadOnlyDictionary<int, int>? buildingTypes, MapPackage? map)
    {
        var source = occurrence.Source;
        var target = occurrence.Target;
        MapCell Location(BattleObjectContext obj)
        {
            if (obj.Position is { } position) return MapCoordinates.Cell(position);
            var place = before.Settlements.First(s => s.Id == obj.ServicePlaceId);
            return new(place.X, place.Y);
        }
        int? BuildingType(BattleObjectContext obj) => obj.IsSettlement
            ? before.Settlements.First(s => s.Id == obj.Id).FacilityTypeId ?? buildingTypes?.GetValueOrDefault(obj.Id) : null;
        var sourceBody = source.IsSettlement && BuildingType(source) is int sourceType ? zoom.FacilitySources.GetValueOrDefault(sourceType, zoom.Source) :
            occurrence.SourceFormationId is int formation ? zoom.SourceFormations.GetValueOrDefault(formation, zoom.Source) : zoom.Source;
        if (!source.IsSettlement && !(occurrence.SourceFormationId is int selectedFormation && zoom.SourceFormations.ContainsKey(selectedFormation)) && source.ForceId is int sourceForce)
        {
            var force = before.World.Forces.Single(f => f.Id == sourceForce);
            int? imperialRank = before.Information.Ranks.OrderBy(r => r.Precedence).LastOrDefault()?.Id;
            sourceBody = zoom.ImperialSource is { } imperial && force.RankId == imperialRank && imperialRank is not null ? imperial :
                force.CountryTitleId is int country ? zoom.CountrySources.GetValueOrDefault(country, sourceBody) : sourceBody;
        }
        zoom = zoom with
        {
            Source = sourceBody,
            AdvanceSound = sourceBody.AdvanceSound ?? zoom.AdvanceSound,
            AttackSound = sourceBody.AttackSound ?? zoom.AttackSound,
            Numbers = zoom.Numbers is { } digits && target.IsSettlement && BuildingType(target) is int numberFacility
                ? digits with { Position = zoom.FacilityNumberPositions.GetValueOrDefault(numberFacility, digits.Position) } : zoom.Numbers,
            Target = target.IsSettlement && BuildingType(target) is int targetType ? zoom.FacilityTargets.GetValueOrDefault(targetType, zoom.Target) :
                occurrence.TargetFormationId is int defense ? zoom.TargetFormations.GetValueOrDefault(defense, zoom.Target) : zoom.Target
        };
        var visualRandom = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{index}:formation"));
        var officerRandom = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{index}:officers"));
        var officerDisplay = source.IsSettlement && BuildingType(source) is int officerFacility
            ? zoom.FacilityOfficers.GetValueOrDefault(officerFacility) ?? zoom.Officers : zoom.Officers;
        var officers = officerDisplay is null ? null : new BattleOfficerPlayback(officerDisplay,
            occurrence.SourceMemberIds.Select(id => new BattleOfficerVisual(id,
                before.Forces.Single(f => f.Id == source.ForceId).ColorCode,
                before.Officers.Single(p => p.Id == id).FlagGlyph,
                (int)(officerRandom.NextUInt64() % (ulong)officerDisplay.Idle.Columns))).ToImmutableArray());
        var playback = new BattleZoomPlayback(zoom, blocked, hit?.SoldierLoss ?? 0,
            n => (int)(visualRandom.NextUInt64() % (ulong)n), battle.HitSound, battle.BlockSound,
            sniping is null || battle.SnipingEffect.Length == 0 ? 0 :
                battle.SnipingEffect.Sum(s => s.Columns * s.FrameMilliseconds) / 1000.0 +
                battle.SnipingEffect[^1].FrameMilliseconds / 1000.0 + BattleEffectFade.Duration, hit?.CalculatedSoldierLoss);
        MapOccupancy? Area(BattleObjectContext obj) => obj.IsGarrison && obj.ServicePlaceId is int place
            ? before.BattleArea(place, true) : before.BattleArea(obj.Id, obj.IsSettlement, obj.Position);
        ImmutableArray<BattleSceneLayer> backdrop = [];
        if (map is not null)
        {
            var left = Location(source); var right = Location(target);
            string season = new[] { "spring", "summer", "autumn", "winter" }[(clock.Month - 1) / 3];
            bool touching = Area(source) is { } a && Area(target) is { } b && a.IntersectsExpanded(b, MapCoordinates.UnitsPerCell);
            backdrop = BattleSceneComposition.Compose(battle.Scene, map.Squares[left.Y * map.Width + left.X],
                map.Squares[right.Y * map.Width + right.X], season, BuildingType(source), BuildingType(target), touching);
        }
        return new(zoom, playback, officers, backdrop);
    }
}
