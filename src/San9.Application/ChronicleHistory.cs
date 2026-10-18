using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Project committed facts into independent, persistent force histories.</summary>
internal static class ChronicleHistory
{
    internal static ImmutableArray<ChronicleFact> Initialize(StrategySnapshot view)
    {
        var builder = new Builder(view, view);
        foreach (var force in view.World.Forces.Where(f => !f.IsEliminated && !f.IsDormant && f.LeaderId is not null))
        {
            var leader = view.World.Officers.Single(p => p.Id == force.LeaderId);
            if (leader.SettlementId is int place)
            {
                var rank = force.RankId is int id ? view.Information.Ranks.Single(r => r.Id == id) : null;
                builder.Add(view.World.Clock, force.Id, rank is null ? "rule" : "rule-ranked", place: place,
                    textKeys: rank is null ? null : new() { ["name"] = rank.NameKey });
            }
            if (force.AdviserId is int adviser)
                builder.Add(view.World.Clock, force.Id, "adviser", actor: adviser);
        }
        return builder.Facts.ToImmutableArray();
    }

    internal static ImmutableArray<ChronicleFact> Append(ImmutableArray<ChronicleFact> history, IReadOnlyList<ChronicleFact> additions)
    {
        if (additions.Count == 0) return history;
        var pending = additions.Select(f => f.ForceId).ToHashSet();
        var last = new Dictionary<int, ChronicleFact>();
        for (int index = history.Length - 1; index >= 0 && pending.Count > 0; index--)
            if (pending.Remove(history[index].ForceId)) last[history[index].ForceId] = history[index];
        var result = history.ToBuilder();
        foreach (var fact in additions)
        {
            if (last.TryGetValue(fact.ForceId, out var previous) && previous.Clock == fact.Clock && previous.Role == fact.Role &&
                previous.ForceColorCode == fact.ForceColorCode && previous.Values.Count == fact.Values.Count && previous.TextKeys.Count == fact.TextKeys.Count &&
                previous.Values.All(p => fact.Values.TryGetValue(p.Key, out var value) && value == p.Value) &&
                previous.TextKeys.All(p => fact.TextKeys.TryGetValue(p.Key, out var value) && value == p.Value)) continue;
            result.Add(fact); last[fact.ForceId] = fact;
        }
        return result.ToImmutable();
    }

    internal static IReadOnlyList<ChronicleFact> Read(StrategySnapshot before, StrategySnapshot after, IReadOnlyList<DomainEvent> events, TerminalDefinition? terminal = null)
    {
        var b = new Builder(before, after);
        for (int index = 0; index < events.Count; index++)
        {
            var fact = events[index];
            if (fact is SettlementTransferredEvent first)
            {
                var transfers = new List<SettlementTransferredEvent> { first };
                while (index + 1 < events.Count && events[index + 1] is SettlementTransferredEvent next &&
                    next.Clock == first.Clock && next.PreviousOwnerId == first.PreviousOwnerId && next.OwnerId == first.OwnerId)
                    transfers.Add((SettlementTransferredEvent)events[++index]);
                foreach (var group in transfers.GroupBy(e => b.IsCity(e.SettlementId)))
                    b.Add(first.Clock, first.OwnerId, group.Key ? group.Count() > 1 ? "cities-surrendered" : "city-surrendered"
                        : group.Count() > 1 ? "facilities-surrendered" : "facility-surrendered",
                        place: group.First().SettlementId, otherNameForce: first.PreviousOwnerId);
                continue;
            }
            switch (fact)
            {
                case AdviserAppointedEvent e:
                    b.Add(e.Clock, e.ForceId, "adviser-appointed", actor: e.OfficerId); break;
                case FacilityDemolishedEvent e:
                    b.Facility(e.Clock, e.ForceId, e.FacilityTypeId, e.DestinationCityId, "demolition", e.SettlementId); break;
                case FacilityConstructionChangedEvent { Phase: "completed" } e:
                    var occurrence = e.Occurrence ?? throw new InvalidDataException("Completed construction has no occurrence context.");
                    b.Facility(e.Clock, occurrence.ForceId, occurrence.FacilityTypeId, occurrence.CityId, "construction"); break;
                case SettlementCapturedEvent { Destroyed: false, Source.ForceId: int winner } e:
                {
                    bool city = b.IsCity(e.SettlementId);
                    b.Add(e.Clock, winner, city ? e.PreviousOwnerId is null ? "city-captured" : "city-attacked"
                        : e.PreviousOwnerId is null ? "facility-captured" : "facility-attacked", place: e.SettlementId, nameForce: e.PreviousOwnerId);
                    if (e.PreviousOwnerId is int loser)
                        b.Add(e.Clock, loser, city ? "city-lost" : "facility-lost", place: e.SettlementId, nameForce: winner, colorForce: winner);
                    break;
                }
                case ForceLeaderChangedEvent { PreviousId: int previous } e:
                    b.ChangeLeader(e.ForceId, e.OfficerId);
                    b.Broadcast(e.Clock, "succession", e.ForceId, actor: previous, other: e.OfficerId); break;
                case OfficerDiedEvent e when e.Cause is OfficerDeathCause.Natural or OfficerDeathCause.Duel or OfficerDeathCause.Sniping or OfficerDeathCause.Melee:
                {
                    string role = e.Cause switch { OfficerDeathCause.Duel => "duel-death", OfficerDeathCause.Sniping => "sniping-death", OfficerDeathCause.Melee => "melee-death", _ => "natural-death" };
                    if ((e.PreviousStatus == OfficerStatus.Ruler || before.World.Forces.FirstOrDefault(f => f.Id == e.PreviousForceId)?.LeaderId == e.OfficerId) && e.Cause == OfficerDeathCause.Natural)
                        b.Broadcast(e.Clock, role, e.PreviousForceId, actor: e.OfficerId);
                    else if (e.PreviousForceId is int owner) b.Add(e.Clock, owner, role, actor: e.OfficerId);
                    break;
                }
                case PrisonerExecutedEvent e:
                {
                    if (e.Occurrence.Path == PrisonerExecutionPath.CapturedRulerDisposition)
                        b.Broadcast(e.Clock, "execution", e.ExecutingForceId, actor: e.OfficerId, other: e.Occurrence.ExecutingRulerId);
                    else if (e.Occurrence.PreviousForceId is int owner)
                        b.Add(e.Clock, owner, "execution-local", actor: e.OfficerId, other: e.Occurrence.ExecutingRulerId, colorForce: e.ExecutingForceId);
                    break;
                }
                case ForceEliminatedEvent e:
                    b.Eliminate(e.ForceId);
                    b.Broadcast(e.Clock, "eliminated-named", e.ForceId, nameForce: e.ForceId); break;
                case ForceSurrenderedEvent e:
                    b.Add(e.Clock, e.RecipientId, "force-absorbed", nameForce: e.ForceId);
                    b.Add(e.Clock, e.ForceId, "force-surrendered", nameForce: e.RecipientId, colorForce: e.RecipientId); break;
                case SettlementDefectedEvent e:
                    b.Add(e.Clock, e.PreviousForceId, b.IsCity(e.SettlementId) ? "city-defected" : "facility-defected", place: e.SettlementId, nameForce: e.ForceId, colorForce: e.ForceId);
                    b.Add(e.Clock, e.ForceId, b.IsCity(e.SettlementId) ? "city-surrendered" : "facility-surrendered", place: e.SettlementId, otherNameForce: e.PreviousForceId); break;
                case PersonnelTerritoryTransferredEvent e:
                    b.Add(e.Clock, e.ForceId, b.IsCity(e.PlaceId) ? "city-enticed" : "facility-enticed", actor: e.OfficerId, place: e.PlaceId, nameForce: e.PreviousForceId);
                    b.Add(e.Clock, e.PreviousForceId, b.IsCity(e.PlaceId) ? "city-enticed-lost" : "facility-enticed-lost", place: e.PlaceId, nameForce: e.ForceId, colorForce: e.ForceId); break;
                case ForceFoundedEvent e:
                    b.Activate(e.ForceId, e.FounderId);
                    b.Add(e.Clock, e.ForceId, "founding", actor: e.FounderId, place: e.SettlementId); break;
                case CityIndependenceEvent e:
                    b.Activate(e.ForceId, e.FounderId);
                    b.Add(e.Clock, e.ForceId, "independence", actor: e.FounderId, place: e.SettlementId, nameForce: e.PreviousForceId); break;
                case ForceActivatedBySearchEvent e:
                    b.Activate(e.ActivatedForceId, after.World.Forces.Single(f => f.Id == e.ActivatedForceId).LeaderId);
                    b.Broadcast(e.Clock, "wa-discovered", e.SearchingForceId, actor: e.SearchingOfficerId); break;
                case ForcesConsolidatedEvent e:
                    b.Broadcast(e.Clock, "consolidation", e.SurvivorId, nameForce: e.SurvivorId, otherNameForce: e.AbsorbedId); break;
                case AttackRequestEstablishedEvent e:
                    b.Request(e.Clock, e.Request, "request-accepted", "request-promised"); break;
                case AttackRequestEndedEvent e:
                    b.RequestEnded(e); break;
                case AllianceFormedEvent e:
                    b.AllianceFormed(e); break;
                case AllianceVictoryEvent e:
                    b.Broadcast(e.Clock, "alliance-victory", e.VictorForceId, actor: b.Leader(e.TargetForceId), nameForce: e.VictorForceId); break;
                case AllianceEndedEvent e:
                    b.AllianceEnded(e); break;
                case AllianceMemberRemovedEvent e:
                    b.Broadcast(e.Clock, e.Reason == AllianceRemovalReason.Betrayal ? "alliance-member-expelled" : "alliance-member-left",
                        e.ForceId, actor: b.Leader(e.Alliance.TargetForceId), nameForce: e.ForceId);
                    break;
                case PeerageChangedEvent e:
                    b.Peerage(e); break;
                case EmperorTransferResolvedEvent { Invalidated: false } e:
                    b.Broadcast(e.Clock, e.Choice == EmperorTransferChoice.Support ? "emperor-supported" : "emperor-deposed", e.ForceId,
                        actor: b.Leader(e.ForceId), other: e.EmperorId); break;
                case EmperorDeathEvent { PreviousEmperorId: int previous, CurrentEmperorId: not null } e:
                    b.Broadcast(e.Clock, "emperor-succession", null, actor: previous, other: e.CurrentEmperorId); break;
                case ChainedPlotEvent { Step: ChainedPlotStep.Prelude or ChainedPlotStep.Coup } e:
                    b.Broadcast(e.Clock, e.Step == ChainedPlotStep.Prelude ? "chained-prelude" : "chained-coup", e.ForceId); break;
                case ThreeVisitsEvent e when e.Step != ThreeVisitsStep.Expired:
                    if (b.PersonOwner(e.RulerId) is int visitsOwner)
                        b.Add(e.Clock, visitsOwner, e.Step switch { ThreeVisitsStep.Recommendation => "visits-recommendation", ThreeVisitsStep.FirstVisit => "visits-first", _ => "visits-recruited" });
                    break;
                case ImperialCeremonyResolvedEvent e:
                    b.Add(e.Clock, e.ForceId, "ceremony", actor: e.RulerId); break;
                case ShanyangGongEvent e:
                    b.Broadcast(e.Clock, e.Protected ? "abdication-han" : "emperor-killed", e.ForceId, actor: b.Leader(e.ForceId), other: e.EmperorId); break;
                case ExpeditionMemorialEvent e:
                    b.Add(e.Clock, e.ForceId, "memorial"); break;
                case FiveTigersRecognizedEvent e:
                    b.Add(e.Clock, e.ForceId, "five-tigers"); break;
                case QiaoMarriageEvent e:
                    b.Add(e.Clock, e.ForceId, "qiao-marriage"); break;
                case SunMarriageEvent e:
                    b.Add(e.Clock, e.RecipientForceId, "sun-marriage"); break;
                case GuanduEvent e:
                    b.Add(e.Clock, e.ForceId, "guandu-won", nameForce: e.OpposingForceId);
                    b.Add(e.Clock, e.OpposingForceId, "guandu-lost", nameForce: e.ForceId); break;
                case RedCliffsEvent e:
                    b.Add(e.Clock, e.ForceId, "red-cliffs-won", nameForce: e.OpposingForceId);
                    b.Add(e.Clock, e.OpposingForceId, "red-cliffs-lost", nameForce: e.ForceId); break;
                case SimulationEndedEvent e when e.Reason == "conquest":
                    if (TerminalEvaluation.Evaluate(after.World, terminal).WinningForceId is int winning)
                        b.Add(e.Clock, winning, "unified", nameForce: winning);
                    break;
                case SimulationEndedEvent e when e.Reason == "time-limit":
                    b.Broadcast(e.Clock, "story-ended", null); break;
            }
        }
        return b.Facts.OrderBy(f => f.Clock.DayNumber).ToArray();
    }

    private sealed class Builder(StrategySnapshot before, StrategySnapshot after)
    {
        internal List<ChronicleFact> Facts { get; } = [];
        private readonly Dictionary<int, ForceState> _changed = [];
        private ForceState? Force(int? id) => id is int force && _changed.TryGetValue(force, out var current) ? current
            : before.World.Forces.FirstOrDefault(f => f.Id == id) ?? after.World.Forces.FirstOrDefault(f => f.Id == id);
        internal void ChangeLeader(int id, int leader) => _changed[id] = Force(id)! with { LeaderId = leader };
        internal void Eliminate(int id) => _changed[id] = Force(id)! with { IsEliminated = true };
        internal void Activate(int id, int? leader) => _changed[id] = after.World.Forces.Single(f => f.Id == id) with { LeaderId = leader, IsDormant = false, IsEliminated = false };
        private IEnumerable<ForceState> ActiveForces() => before.World.Forces.Select(f => f.Id).Concat(_changed.Keys).Distinct()
            .Select(id => Force(id)!).Where(f => !f.IsEliminated && !f.IsDormant && f.LeaderId is not null);
        internal int? Leader(int? force) => Force(force)?.LeaderId;
        internal int? PersonOwner(int person) => before.World.Officers.FirstOrDefault(p => p.Id == person)?.OwnerId ?? after.World.Officers.Single(p => p.Id == person).OwnerId;
        internal bool IsCity(int place) => (before.World.Settlements.FirstOrDefault(s => s.Id == place) ?? after.World.Settlements.Single(s => s.Id == place)).Kind == SettlementKind.City;
        internal void Add(GameClock clock, int owner, string role, int? actor = null, int? other = null, int? place = null,
            int? nameForce = null, int? otherNameForce = null, Dictionary<string, string>? textKeys = null, int? colorForce = null)
        {
            var values = ImmutableDictionary.CreateBuilder<string, MessageBinding>();
            void Person(string key, int? id) { if (id is int person) values[key] = MessageBinding.Reference(San9.Content.Text.MessageEntityKind.Person, person); }
            Person("ruler", Leader(owner)); Person("actor", actor); Person("other", other);
            if (place is int building) values["place"] = MessageBinding.Reference(San9.Content.Text.MessageEntityKind.Settlement, building);
            var keys = (textKeys ?? []).ToImmutableDictionary().ToBuilder();
            void Name(string roleName, int? forceId)
            {
                if (keys.ContainsKey(roleName) || Force(forceId) is not { } force) return;
                if (force.CountryTitleId is int title) keys[roleName] = before.Information.CountryTitles.Single(t => t.Id == title).NameKey;
                else Person(roleName, force.LeaderId);
            }
            Name("name", nameForce ?? owner); Name("other-name", otherNameForce);
            Facts.Add(new(clock, owner, role, Force(colorForce ?? owner)?.ColorCode, values.ToImmutable(), keys.ToImmutable()));
        }
        internal void Broadcast(GameClock clock, string role, int? sourceForce, int? actor = null, int? other = null, int? nameForce = null, int? otherNameForce = null)
        {
            foreach (var force in ActiveForces())
            {
                Add(clock, force.Id, role, actor, other, nameForce: nameForce, otherNameForce: otherNameForce, colorForce: sourceForce);
                if (sourceForce is null) Facts[^1] = Facts[^1] with { ForceColorCode = null };
            }
        }
        internal void Facility(GameClock clock, int owner, int? type, int city, string role, int? namedFacility = null)
        {
            Add(clock, owner, role, place: city);
            Facts[^1] = Facts[^1] with { Values = Facts[^1].Values.Add("facility-name", type is int id ? new(id)
                : MessageBinding.Reference(San9.Content.Text.MessageEntityKind.Settlement, namedFacility!.Value)) };
        }
        internal void Request(GameClock clock, AttackRequestState request, string requesterRole, string requestedRole, int? requesterSource = null, int? requestedSource = null)
        {
            Add(clock, request.RequesterForceId, requesterRole, place: request.TargetSettlementId, nameForce: request.RequestedForceId, colorForce: requesterSource ?? request.RequestedForceId);
            Add(clock, request.RequestedForceId, requestedRole, place: request.TargetSettlementId, nameForce: request.RequesterForceId, colorForce: requestedSource ?? request.RequestedForceId);
        }
        internal void RequestEnded(AttackRequestEndedEvent e)
        {
            int? eliminated = e.Reason switch
            {
                AttackRequestEndReason.RequestedForceEliminated => e.Request.RequestedForceId,
                AttackRequestEndReason.RequesterForceEliminated => e.Request.RequesterForceId,
                AttackRequestEndReason.TargetForceEliminated => e.Request.TargetForceId,
                _ => null
            };
            if (eliminated is int lost)
            {
                Add(e.Clock, e.Request.RequesterForceId, "request-force-eliminated", place: e.Request.TargetSettlementId, nameForce: lost, colorForce: lost);
                Add(e.Clock, e.Request.RequestedForceId, "request-force-eliminated-peer", place: e.Request.TargetSettlementId, nameForce: lost, colorForce: lost);
                return;
            }
            string role = e.Reason switch
            {
                AttackRequestEndReason.Fulfilled => "request-fulfilled",
                AttackRequestEndReason.Expired => "request-expired",
                AttackRequestEndReason.Broken or AttackRequestEndReason.AllianceRealignment => "request-broken",
                AttackRequestEndReason.TargetDestroyed => "request-place-destroyed",
                AttackRequestEndReason.TargetOwnerChanged => "request-owner-changed",
                _ => throw new ArgumentOutOfRangeException(nameof(e))
            };
            int requesterSource = e.Reason == AttackRequestEndReason.Expired ? e.Request.RequesterForceId
                : e.LiableForceId ?? e.Request.RequestedForceId;
            int requestedSource = e.LiableForceId ?? e.Request.RequestedForceId;
            Request(e.Clock, e.Request, role, role + "-peer", requesterSource, requestedSource);
        }
        internal void AllianceFormed(AllianceFormedEvent e)
        {
            foreach (int force in e.Alliance.MemberForceIds)
                Add(e.Clock, force, force == e.Alliance.LeaderForceId ? "alliance-led" : "alliance-joined", actor: Leader(e.Alliance.TargetForceId), other: Leader(e.Alliance.LeaderForceId));
            Add(e.Clock, e.Alliance.TargetForceId, "alliance-formed", actor: Leader(e.Alliance.TargetForceId), other: Leader(e.Alliance.LeaderForceId));
        }
        internal void AllianceEnded(AllianceEndedEvent e)
        {
            if (e.Reason == AllianceEndReason.SubjugationSucceeded) return;
            var a = e.Alliance;
            if (e.Reason == AllianceEndReason.Betrayal && e.BetrayingForceId != a.LeaderForceId)
            {
                int betrayer = e.BetrayingForceId ?? throw new InvalidDataException("Alliance betrayal has no actor.");
                Broadcast(e.Clock, "alliance-member-left", betrayer, actor: Leader(a.TargetForceId), nameForce: betrayer);
                Broadcast(e.Clock, "alliance-empty", null, actor: Leader(a.TargetForceId));
                return;
            }
            string role = e.Reason switch { AllianceEndReason.Expired => "alliance-expired", AllianceEndReason.TargetEliminated => "alliance-target-eliminated",
                AllianceEndReason.LeaderEliminated => "alliance-leader-eliminated", AllianceEndReason.InsufficientMembers => "alliance-empty",
                AllianceEndReason.Betrayal => "alliance-betrayed", AllianceEndReason.LeaderDied => "alliance-leader-died", _ => "alliance-target-died" };
            int? source = e.Reason switch
            {
                AllianceEndReason.Expired or AllianceEndReason.InsufficientMembers => null,
                AllianceEndReason.TargetDied or AllianceEndReason.TargetEliminated => a.TargetForceId,
                _ => a.LeaderForceId
            };
            Broadcast(e.Clock, role, source, actor: Leader(a.TargetForceId), other: Leader(a.LeaderForceId),
                nameForce: e.Reason == AllianceEndReason.TargetEliminated ? a.TargetForceId : a.LeaderForceId);
        }
        internal void Peerage(PeerageChangedEvent e)
        {
            _changed[e.ForceId] = Force(e.ForceId)! with { RankId = e.RankId, CountryTitleId = e.CountryTitleId };
            if (e.Kind == PeerageChangeKind.ShanyangGong) return;
            var rank = before.Information.Ranks.Single(r => r.Id == e.RankId);
            var keys = new Dictionary<string, string> { ["name"] = rank.NameKey };
            bool emperor = e.Kind is PeerageChangeKind.Abdication or PeerageChangeKind.Proclamation;
            if (!emperor)
                Add(e.Clock, e.ForceId, e.Kind == PeerageChangeKind.Investiture ? "rank-appointed" : "rank-proclaimed", textKeys: keys);
            else
            {
                string role = e.Kind == PeerageChangeKind.Abdication ? "emperor-abdication" : "emperor-proclamation";
                if (e.CountryTitleId is int title) keys["name"] = before.Information.CountryTitles.Single(t => t.Id == title).NameKey;
                else role += "-untitled";
                foreach (var force in ActiveForces())
                    Add(e.Clock, force.Id, role, actor: Leader(e.ForceId), textKeys: keys, colorForce: e.ForceId);
            }
        }
    }
}
