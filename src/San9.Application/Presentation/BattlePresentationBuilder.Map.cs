using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class BattlePresentationBuilder
{
    private readonly List<BattleMapChangeCue> _mapChanges = [];
    private readonly Dictionary<int, double> _captureTimes = [];
    private string? _mapDialogue;

    private SettlementView Project(int id) => _identities.View.Settlements.Single(s => s.Id == id);

    private void PresentMapChange(DomainEvent fact)
    {
        switch (fact)
        {
            case UnitTerminatedEvent terminated:
                _mapChanges.Add(new(_impact, _index, terminated.UnitId, null, _mapDialogue));
                break;
            case SettlementNeutralizedEvent neutralized:
                _mapChanges.Add(new(_impact, _index, null, Project(neutralized.SettlementId), _mapDialogue));
                break;
            case SettlementCapturedEvent captured:
                var dialogue = _messages.LastOrDefault(c => c.Id == Identity("capture"));
                if (dialogue is not null) _mapDialogue = dialogue.Id;
                double time = dialogue?.Time ?? _impact;
                _captureTimes[captured.SettlementId] = time;
                _mapChanges.Add(new(time, _index, null, Project(captured.SettlementId), _mapDialogue));
                break;
            case UnitAbsorbedEvent absorbed:
                _mapChanges.Add(new(_captureTimes.GetValueOrDefault(absorbed.SettlementId, 1), _index,
                    absorbed.UnitId, Project(absorbed.SettlementId), _mapDialogue));
                break;
            case FacilityDestroyedEvent destroyed:
                _mapChanges.Add(new(_impact, _index, null, Project(destroyed.SettlementId), _mapDialogue));
                break;
            case FacilityConstructionChangedEvent { Result: { } result } construction:
                _mapChanges.Add(new(construction.Phase == "cancelled" ? _impact : 1, _index, null, Project(result.Id), _mapDialogue));
                break;
        }
    }
}
