using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class SessionValidation
{
    internal static void Validate(ScenarioSessionDefinition definition, SessionSnapshot snapshot)
    {
        if (!snapshot.World.Simulation.CanPersist)
            throw new ArgumentException("Session is not at a save boundary.");
        if (snapshot.Records.IsDefault || snapshot.Chronicle.IsDefault || snapshot.History.IsDefault || snapshot.Records.Length > GameSession.RecentRecordLimit ||
            snapshot.NextOperationSequence < 0)
            throw new ArgumentException("Missing session state.");
        if (!Enum.IsDefined(snapshot.PlayerForceController)) throw new ArgumentException("Invalid player force controller.");
        if (snapshot.OriginalAi is null) throw new ArgumentException("Missing original AI state.");
        snapshot.OriginalAi.Validate(snapshot.World, definition.Content);
        var initialClock = new GameClock(definition.Content.Year, definition.Content.Month, definition.Content.Day);
        var generated = snapshot.World.Forces.Where(f => f.Origin is not null).OrderBy(f => f.Id).ToArray();
        long nextForceId = (long)definition.Content.Forces.Max(f => f.Id) + 1;
        foreach (var force in generated)
        {
            var origin = force.Origin!;
            if (force.Id != nextForceId++ || force.IsBarbarian ||
                (origin.Kind switch
                {
                    ForceCreationKind.EmptyCityUprising => !snapshot.World.Simulation.ForceEmergenceEnabled || origin.PredecessorId is not null,
                    ForceCreationKind.CityIndependence => !snapshot.World.Simulation.CityIndependenceEnabled ||
                        origin.PredecessorId is not int previous || previous >= force.Id || !snapshot.World.Forces.Any(f => f.Id == previous),
                    _ => true
                }) ||
                !definition.Content.Officers.Any(o => o.State.Id == origin.FounderId) ||
                !definition.Content.Settlements.Any(s => s.Id == origin.SettlementId && s.Kind == SettlementKind.City) ||
                origin.Clock.DayNumber < initialClock.DayNumber + 30 || origin.Clock.DayNumber > snapshot.World.Clock.DayNumber)
                throw new ArgumentException("Invalid runtime force provenance.");
        }
        var expectedControls = definition.Content.Forces.OrderBy(f => f.Id == definition.Content.PlayerForceId ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => new ControlSeat(f.Id == definition.Content.PlayerForceId ? "player" : $"ai:{f.Id}", f.Id,
                f.Id == definition.Content.PlayerForceId ? snapshot.PlayerForceController : ControllerKind.Automatic))
            .Concat(generated.Select(f => new ControlSeat($"ai:{f.Id}", f.Id, ControllerKind.Automatic)))
            .Where(c => snapshot.World.Forces.Any(f => f.Id == c.ForceId && !f.IsDormant && !f.IsEliminated));
        if (snapshot.World.Simulation.Started != initialClock ||
            !snapshot.World.Simulation.Controls.SequenceEqual(expectedControls))
            throw new ArgumentException("Scenario date or control configuration mismatch.");
        var content = definition.Content;
        bool ContextPlace(int? id) => id is null || snapshot.World.Settlements.Any(s => s.Id == id) || content.RegionNameKeys.ContainsKey(id.Value);
        var context = snapshot.StrategicContext;
        if (context is null || context.ReportedEnemyUnits.IsDefault || context.Prompts.IsDefault || context.Clues.IsDefault ||
            context.Clock is { } entry && (!entry.IsXunBoundary || entry.DayNumber < snapshot.World.Simulation.Started.DayNumber || entry.DayNumber > snapshot.World.Clock.DayNumber) ||
            context.Clock is null && (!context.Prompts.IsEmpty || !context.Clues.IsEmpty || !context.ReportedEnemyUnits.IsEmpty) ||
            context.ReportedEnemyUnits.Any(id => id < 0) || context.ReportedEnemyUnits.Distinct().Count() != context.ReportedEnemyUnits.Length ||
            context.Prompts.Any(p => !San9.Content.Interface.ProgressPresentationDefinition.ContextRoles.Contains(p.Role) ||
                !snapshot.World.Officers.Any(o => o.Id == p.ActorId) || !snapshot.World.Officers.Any(o => o.Id == p.RulerId) ||
                !snapshot.World.Forces.Any(f => f.Id == p.ForceId) ||
                !ContextPlace(p.PlaceId) || !ContextPlace(p.CityId) || !ContextPlace(p.OtherPlaceId) ||
                p.EnemyForceId is int enemy && !snapshot.World.Forces.Any(f => f.Id == enemy) ||
                p.OtherOfficerId is int other && !snapshot.World.Officers.Any(o => o.Id == other) ||
                p.TreasureId is int treasure && !snapshot.World.Treasures.Any(t => t.Id == treasure)) ||
            context.Clues.Any(c => !Enum.IsDefined(c.Kind) || c.Kind == Presentation.StrategicClueKind.Person && c.TreasureId is not null ||
                c.Kind == Presentation.StrategicClueKind.Treasure && c.OfficerId is not null || c.Clock.DayNumber > snapshot.World.Clock.DayNumber || c.Clock.DayNumber < snapshot.World.Simulation.Started.DayNumber ||
                !ContextPlace(c.PlaceId) || c.RegionOnly && (c.OfficerId is not null || c.TreasureId is not null) ||
                c.OfficerId is int person && !snapshot.World.Officers.Any(o => o.Id == person) ||
                c.TreasureId is int item && !snapshot.World.Treasures.Any(t => t.Id == item)))
            throw new ArgumentException("Invalid strategic context publication.");
        var activated = content.SearchEvents.OfType<ForceActivationSearchEventDefinition>()
            .Where(d => snapshot.World.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey)).ToArray();
        var activatedForces = activated.Select(d => d.ForceId).ToHashSet();
        var activatedSettlements = activated.SelectMany(d => d.AttachedSettlementIds.Prepend(d.CityId)).ToHashSet();
        var activatedOfficers = activated.SelectMany(d => d.OfficerIds).ToHashSet();
        var withdrawnOfficers = content.SearchEvents.OfType<ScholarPunishmentSearchEventDefinition>().SelectMany(d => d.Sites)
            .Where(s => snapshot.World.Simulation.ConsumedEventKeys.Contains(s.WithdrawalKey)).Select(s => s.ScholarOfficerId).ToHashSet();
        if (snapshot.World.Forces.Any(f => f.IsDormant != (content.Forces.Any(i => i.Id == f.Id && i.IsDormant) && !activatedForces.Contains(f.Id))) ||
            snapshot.World.Settlements.Any(s => s.IsDormant != (content.Settlements.Any(i => i.Id == s.Id && i.IsDormant) && !activatedSettlements.Contains(s.Id))) ||
            snapshot.World.Officers.Any(p => p.IsDormant != (withdrawnOfficers.Contains(p.Id) || content.Officers.Any(i => i.State.Id == p.Id && i.State.IsDormant) && !activatedOfficers.Contains(p.Id))))
            throw new ArgumentException("Dormant state does not match initial content and completed participation events.");
        if (snapshot.World.Simulation.Boundary == SimulationBoundary.Terminal)
        {
            _ = TerminalEvaluation.Evaluate(snapshot.World, content.Terminal);
            if (snapshot.World.Simulation.TerminalReason == "time-limit" &&
                (content.Terminal?.EndYear is not int endYear || snapshot.World.Clock.Year < endYear))
                throw new ArgumentException("Year-limit result does not match the campaign.");
        }
        if (snapshot.Ending is not null) _ = EndingSession.Resolve(content, snapshot.World, snapshot.Ending);
        static void Same(IEnumerable<int> a, IEnumerable<int> b)
        {
            if (!a.Order().SequenceEqual(b.Order())) throw new ArgumentException("Scenario identity set mismatch.");
        }
        Same(content.Forces.Select(f => f.Id), snapshot.World.Forces.Where(f => f.Origin is null).Select(f => f.Id));
        if (content.Settlements.Any(s => !snapshot.World.Settlements.Any(current => current.Id == s.Id)) ||
            snapshot.World.Settlements.Any(s => !content.Settlements.Any(initial => initial.Id == s.Id) &&
                (s.Facility is null || !content.UnbuiltPlaces.Any(region => region.Id == s.Id))))
            throw new ArgumentException("Unknown or missing settlement identity.");
        Same(content.Officers.Select(o => o.State.Id), snapshot.World.Officers.Select(o => o.Id));
        // Field units are created and absorbed at runtime; identities are allocated by the world.
        if (snapshot.World.Units.Any(u => u.ServicePlaceId is int home &&
            !content.Settlements.Any(s => s.Id == home) && !content.UnbuiltPlaces.Any(p => p.Id == home)))
            throw new ArgumentException("Unknown unit service place.");
        // Succession can promote a child corps to the ruler's corps. The treasury belongs to the force,
        // so validate its surviving owner rather than requiring the initial corps identity.
        Same(content.Information.Corps.Where(c => c.IsRoot && snapshot.World.Forces.Any(f => f.Id == c.OwnerId && !f.IsEliminated)).Select(c => c.OwnerId).Concat(generated.Where(f => !f.IsEliminated).Select(f => f.Id)),
            snapshot.World.Corps.Where(c => c.IsRoot).Select(c => c.OwnerId));
        Same(content.Information.TreasureStates.Select(t => t.Id), snapshot.World.Treasures.Select(t => t.Id));
        if (content.Information.Relations.Any(initial => !snapshot.World.Relations.Any(current =>
            current.FirstId == initial.FirstId && current.SecondId == initial.SecondId)))
            throw new ArgumentException("Missing initial relation identity.");
        if (snapshot.World.Officers.Any(o => o.RegionId is int region && !content.UnbuiltPlaces.Any(p => p.Id == region) ||
            o.OfficeId is int office && !content.Information.Offices.Any(d => d.Id == office)) ||
            snapshot.World.Forces.Any(f => f.RankId is int rank && !content.Information.Ranks.Any(r => r.Id == rank) ||
                f.CountryTitleId is int title && !content.Information.CountryTitles.Any(t => t.Id == title)))
            throw new ArgumentException("Unknown content reference.");

        long previousDay = snapshot.World.Simulation.Started.DayNumber;
        foreach (var record in snapshot.Records)
        {
            _ = new GameClock(record.Clock.Year, record.Clock.Month, record.Clock.Day);
            if (record.Clock.DayNumber < previousDay || record.Clock.DayNumber > snapshot.World.Clock.DayNumber ||
                !snapshot.World.Forces.Any(f => f.Id == record.ForceId) ||
                !snapshot.World.Settlements.Any(s => s.Id == record.SettlementId) ||
                !snapshot.World.Officers.Any(o => o.Id == record.OfficerId) ||
                !Enum.IsDefined(record.Activity) || record.Increment < 0 || record.Cost < 0)
                throw new ArgumentException("Invalid recent development record.");
            previousDay = record.Clock.DayNumber;
        }
        previousDay = snapshot.World.Simulation.Started.DayNumber;
        foreach (var fact in snapshot.History)
        {
            if (fact is null || fact.Clock.DayNumber < previousDay || fact.Clock.DayNumber > snapshot.World.Clock.DayNumber ||
                !snapshot.World.Forces.Any(f => f.Id == fact.ForceId) ||
                !San9.Content.Interface.ChroniclePresentationDefinition.MessageRoles.Contains(fact.Role) ||
                fact.ForceColorCode is < 0 or > 50 || fact.Values is null || fact.TextKeys is null ||
                fact.TextKeys.Any(v => string.IsNullOrWhiteSpace(v.Key) || string.IsNullOrWhiteSpace(v.Value)))
                throw new ArgumentException("Invalid chronicle fact.");
            foreach (var value in fact.Values.Values)
            {
                if (value is null || value.Entity is { } entity && !(entity.Kind switch
                {
                    San9.Content.Text.MessageEntityKind.Person => snapshot.World.Officers.Any(p => p.Id == entity.Id),
                    San9.Content.Text.MessageEntityKind.Settlement => snapshot.World.Settlements.Any(p => p.Id == entity.Id),
                    San9.Content.Text.MessageEntityKind.Force => snapshot.World.Forces.Any(p => p.Id == entity.Id),
                    _ => false
                })) throw new ArgumentException("Invalid chronicle reference.");
            }
            previousDay = fact.Clock.DayNumber;
        }
        previousDay = snapshot.World.Simulation.Started.DayNumber;
        foreach (var note in snapshot.Chronicle)
        {
            var clock = new GameClock(note.Year, note.Month, note.Day);
            if (clock.DayNumber < previousDay || clock.DayNumber > snapshot.World.Clock.DayNumber ||
                note.ForceId != content.PlayerForceId || string.IsNullOrWhiteSpace(note.Text) ||
                note.Text.Length > 240 || note.Text != note.Text.Trim())
                throw new ArgumentException("Invalid player chronicle.");
            previousDay = clock.DayNumber;
        }
    }
}
