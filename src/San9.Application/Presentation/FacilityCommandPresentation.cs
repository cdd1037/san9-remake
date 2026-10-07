using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class FacilityCommandPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        CommandPresentation content, string kind, DomainEvent result, Func<int,int> random)
    {
        var definition = content.Facilities[kind];
        var development = result as DevelopmentCompletedEvent;
        var trade = result as FoodTradeCompletedEvent;
        if (development is null && trade is null) throw new ArgumentException("Expected facility command result.",nameof(result));
        int homeId = development?.Command.SettlementId ?? trade!.SettlementId;
        int forceId = development?.Command.ForceId ?? trade!.ForceId;
        var members = development?.Command.OfficerIds ?? trade!.OfficerIds;
        var home = before.World.Settlements.Single(s => s.Id == homeId);
        var force = before.World.Forces.Single(f => f.Id == forceId);
        var ordered = DomesticDevelopment.OrderParticipants(before.World.Officers.Where(p => members.Contains(p.Id)),
            before.Officers.ToDictionary(o => o.Id,o => o.Abilities),development?.Command.Activity ?? DomesticActivity.Commerce);
        int first = ordered[0].Id, ruler = force.LeaderId ?? first;
        List<CommandMessage> sequence = [];
        void Dialogue(string key,int speaker,int recipient,string? sound = null) => sequence.Add(new(context.Produce(key,new Dictionary<string,MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person,speaker),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person,recipient),
            ["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement,homeId) },random),speaker,sound));
        void Result(string role,string sound,params long[] values)
        {
            var bindings = values.Select((v,i) => (v,i)).ToDictionary(v => "number."+v.i,v => new MessageBinding(v.v));
            bindings["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement,homeId);
            sequence.Add(new(context.Produce(definition.Messages[role],bindings,random),null,sound));
        }
        switch (development?.Bonus)
        {
            case { Kind: DomesticBonusKind.SpecialPair, PartnerId: int second } bonus:
                Dialogue(definition.Messages["pair-first"],bonus.SpeakerId,second);
                Dialogue(definition.Messages["pair-second"],second,bonus.SpeakerId); break;
            case { Kind: DomesticBonusKind.Affinity } bonus:
                Dialogue(definition.Messages["cooperative"],bonus.SpeakerId,ruler);
                Dialogue(definition.Messages["citizen"],definition.CitizenId!.Value,bonus.SpeakerId,"patrol-citizen"); break;
            default: Dialogue(definition.Messages["normal"],first,ruler,trade is null ? null : "trade-start"); break;
        }
        if (trade is not null)
        {
            var treasury = before.World.Corps.Single(c => home.CorpsId is int id ? c.Id == id : c.OwnerId == forceId && c.IsRoot).Treasury;
            long sign = trade.Direction == FoodTradeDirection.Buy ? 1 : -1;
            Result("result","facility-result",treasury.Money-sign*trade.MoneyAmount,trade.MoneyAmount,treasury.Food+sign*trade.FoodAmount,trade.FoodAmount);
        }
        else
        {
            int increment = development!.Increment;
            switch (development.Command.Activity)
            {
                case DomesticActivity.Commerce:
                    var commerce = home with { Commerce = home.Commerce + increment };
                    Result("result","facility-result",commerce.Commerce,increment,EconomyMath.Revenue(commerce),EconomyMath.Revenue(commerce)-EconomyMath.Revenue(home)); break;
                case DomesticActivity.Cultivation:
                    var cultivated = home with { Cultivation = home.Cultivation + increment };
                    Result("result","facility-result",cultivated.Cultivation,increment,EconomyMath.Harvest(cultivated),EconomyMath.Harvest(cultivated)-EconomyMath.Harvest(home)); break;
                case DomesticActivity.Repair: Result("result","facility-result",home.Durability+increment,increment); break;
                case DomesticActivity.Train: Result("result","facility-result",home.Morale+increment,increment); break;
                case DomesticActivity.Recruit: Result("result","recruit-result",home.Soldiers+increment,increment); break;
                default: throw new ArgumentException("Unsupported development result.",nameof(result));
            }
            if (development.Command.Activity != DomesticActivity.Recruit && content.Patrol is {} patrol)
                PatrolPresentation.AppendAdvice(before,patrol.Advice,force,first,random,(key,speaker,recipient) => Dialogue(key,speaker,recipient));
            if (development.Command.Activity == DomesticActivity.Repair && home.Durability+increment >= home.MaxDurability)
            {
                Result("complete","repair-complete");
                Dialogue(definition.Messages["complete-dialogue"],development.Bonus?.SpeakerId ?? first,ruler);
            }
        }
        return sequence.AsReadOnly();
    }
}
