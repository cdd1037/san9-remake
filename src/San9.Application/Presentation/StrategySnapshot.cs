using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record ForceView(int Id,string NameKey,int ColorCode,int FlagGlyph,uint Money,uint Food,int Trust)
{
    public int? LeaderId { get; init; }
    public int? LeaderSettlementId { get; init; }
}
public sealed record SettlementView(int Id,string NameKey,int? OwnerId,int X,int Y,uint Soldiers,int Durability,int MaxDurability,
    int Morale,bool HasAvailableOfficer,int? CityMarkerColor,IReadOnlyList<string> Icons,int Appearance,int PublicOrder,
    int Commerce,int MaxCommerce,int Cultivation,int MaxCultivation)
{
    public int? CorpsId { get; init; }
    public bool IsFacilityListEntry { get; init; }
}

public sealed class StrategySnapshot
{
    public StrategySnapshot(ScenarioInitialState definition, WorldSnapshot world)
    {
        World=world; PlayerForceId=definition.PlayerForceId;
        RegionNameKeys=new System.Collections.ObjectModel.ReadOnlyDictionary<int,string>(new Dictionary<int,string>(definition.RegionNameKeys));
        RegionCityIds=new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(new Dictionary<int,int>(definition.RegionCityIds));
        var officerDefinitions = definition.Officers.ToDictionary(o => o.State.Id);
        var settlements = world.Settlements.ToDictionary(s => s.Id);
        Officers = Array.AsReadOnly(world.Officers.Select(o =>
        {
            var location = settlements[o.SettlementId];
            return new OfficerView(o.Id, officerDefinitions[o.Id].NameKey, o.OwnerId, location.CorpsId,
                o.SettlementId, o.SettlementId < 50, !o.Acted, o.Abilities);
        }).ToArray());
        Forces=Array.AsReadOnly(world.Forces.Select(f=>
        {
            var d=definition.Forces.Single(d=>d.Id==f.Id);
            return new ForceView(f.Id,d.NameKey,d.ColorCode,d.FlagGlyph,f.Money,f.Food,f.Trust)
            { LeaderId = f.LeaderId, LeaderSettlementId = world.Officers.SingleOrDefault(o => o.Id == f.LeaderId)?.SettlementId };
        }).ToArray());
        Settlements=Array.AsReadOnly(world.Settlements.Select(s=>
        {
            var d=definition.Settlements.Single(d=>d.Id==s.Id);
            return new SettlementView(s.Id,d.NameKey,s.OwnerId,d.X,d.Y,s.Soldiers,s.Durability,s.MaxDurability,s.Morale,
                world.Officers.Any(o=>o.SettlementId==s.Id && o.OwnerId==s.OwnerId && !o.Acted),
                s.Id<50 ? s.OwnerId is int owner ? definition.Forces.Single(f=>f.Id==owner).ColorCode : 50 : null,
                Array.AsReadOnly(d.Icons.ToArray()),d.Appearance,s.PublicOrder,s.Commerce,s.MaxCommerce,s.Cultivation,s.MaxCultivation)
            { CorpsId = s.CorpsId, IsFacilityListEntry = d.IsFacilityListEntry };
        }).ToArray());
    }
    public WorldSnapshot World { get; }
    public int Year => World.Clock.Year;
    public int Month => World.Clock.Month;
    public int Day => 1+10*((int)World.Clock.Xun-1);
    public int PlayerForceId { get; }
    public IReadOnlyDictionary<int,string> RegionNameKeys { get; }
    public IReadOnlyDictionary<int,int> RegionCityIds { get; }
    public IReadOnlyList<ForceView> Forces { get; }
    public IReadOnlyList<OfficerView> Officers { get; }
    public IReadOnlyList<SettlementView> Settlements { get; }
}
