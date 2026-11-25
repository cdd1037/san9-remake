using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class SessionValidation
{
    internal static void Validate(ScenarioSessionDefinition definition, SessionSnapshot snapshot)
    {
        if (snapshot.ContentSetId != definition.Content.ContentSetId) throw new ArgumentException($"存档需要资料集 {snapshot.ContentSetId}。");
        if (snapshot.Definitions is null) throw new ArgumentException("Missing session definitions.");
        var addedPeople = snapshot.Definitions.OfficerIds.ToHashSet();
        var addedForces = snapshot.Definitions.Forces.Select(f => f.Id).ToHashSet();
        var initialPeople = definition.Content.Officers.Select(p => p.State.Id).Union(addedPeople).ToHashSet();
        var initialForces = definition.Content.Forces.Select(f => f.Id).Union(addedForces).ToHashSet();
        if (!snapshot.World.Simulation.CanPersist)
            throw new ArgumentException("Session is not at a save boundary.");
        if (snapshot.DeploymentPresets.IsDefault || snapshot.DeploymentPresets.Any(p => p is null) ||
            snapshot.DeploymentPresets.Select(p => p.Id).Distinct().Count() != snapshot.DeploymentPresets.Length)
            throw new ArgumentException("Invalid deployment preset collection.");
        if (!snapshot.DeploymentPresets.IsEmpty)
        {
            var presetView = new Presentation.StrategySnapshot(new Presentation.StrategySnapshotSource(definition.Content, definition.Rules, definition.Definitions), snapshot.World);
            foreach (var preset in snapshot.DeploymentPresets) preset.Validate(presetView);
        }
        if (snapshot.Records.IsDefault || snapshot.Chronicle.IsDefault || snapshot.History is null || snapshot.Records.Length > GameSession.RecentRecordLimit ||
            snapshot.NextOperationSequence < 0)
            throw new ArgumentException("Missing session state.");
        if (snapshot.InitialPlayerForceIds.IsDefault || snapshot.InitialPlayerForceIds.Distinct().Count() != snapshot.InitialPlayerForceIds.Length ||
            snapshot.InitialPlayerForceIds.Any(id => !initialForces.Contains(id)))
            throw new ArgumentException("Invalid initial player selection.");
        if (!Enum.IsDefined(snapshot.PlayerForceController)) throw new ArgumentException("Invalid player force controller.");
        if (snapshot.OriginalAi is null) throw new ArgumentException("Missing original AI state.");
        snapshot.OriginalAi.Validate(snapshot.World, definition.Content);
        var initialClock = new GameClock(definition.Content.Year, definition.Content.Month, definition.Content.Day);
        var generated = snapshot.World.Forces.Where(f => f.Origin is not null).OrderBy(f => f.Id).ToArray();
        long nextForceId = (long)initialForces.Max() + 1;
        foreach (var force in generated)
        {
            var origin = force.Origin!;
            if (force.Id != nextForceId++ || force.IsBarbarian ||
                !initialPeople.Contains(origin.FounderId) ||
                !definition.Content.Settlements.Any(s => s.Id == origin.SettlementId && s.Kind == SettlementKind.City) ||
                origin.Clock.DayNumber < initialClock.DayNumber || origin.Clock.DayNumber > snapshot.World.Clock.DayNumber)
                throw new ArgumentException("Invalid runtime force provenance.");
        }
        var controls = snapshot.World.Simulation.Controls;
        if (snapshot.World.Simulation.Started != initialClock ||
            controls.Any(c => c.ControllerId != (c.Kind == ControllerKind.Player ? "player" : $"ai:{c.ForceId}") ||
                c.Kind == ControllerKind.Player && snapshot.World.Forces.Single(f => f.Id == c.ForceId).IsBarbarian) ||
            !controls.Select(c => c.ForceId).Order().SequenceEqual(snapshot.World.Forces.Where(f => !f.IsDormant && !f.IsEliminated).Select(f => f.Id).Order()))
            throw new ArgumentException("Scenario date or control configuration mismatch.");
        var content = definition.Content;
        bool KnownMapPlace(int? id) => id is null || snapshot.World.Settlements.Any(s => s.Id == id) || content.RegionNameKeys.ContainsKey(id.Value);
        if (snapshot.World.Simulation.Boundary == SimulationBoundary.Terminal)
        {
            _ = TerminalEvaluation.Evaluate(snapshot.World, content.Terminal);
            if (snapshot.World.Simulation.TerminalReason == "time-limit" &&
                (content.Terminal?.EndYear is not int endYear || snapshot.World.Clock.Year < endYear))
                throw new ArgumentException("Year-limit result does not match the campaign.");
        }
        if (snapshot.Ending is not null) _ = EndingSession.Resolve(definition, snapshot.World, snapshot.Ending);
        static void Same(IEnumerable<int> a, IEnumerable<int> b)
        {
            if (!a.Order().SequenceEqual(b.Order())) throw new ArgumentException("Scenario identity set mismatch.");
        }
        Same(initialForces, snapshot.World.Forces.Where(f => f.Origin is null).Select(f => f.Id));
        if (content.Settlements.Any(s => !snapshot.World.Settlements.Any(current => current.Id == s.Id)) ||
            snapshot.World.Settlements.Any(s => !content.Settlements.Any(initial => initial.Id == s.Id) &&
                (s.Facility is null || !content.UnbuiltPlaces.Any(region => region.Id == s.Id))))
            throw new ArgumentException("Unknown or missing settlement identity.");
        Same(initialPeople, snapshot.World.Officers.Select(o => o.Id));
        // Field units are created and absorbed at runtime; identities are allocated by the world.
        if (snapshot.World.Units.Any(u => u.ServicePlaceId is int home &&
            !content.Settlements.Any(s => s.Id == home) && !content.UnbuiltPlaces.Any(p => p.Id == home)))
            throw new ArgumentException("Unknown unit service place.");
        // Succession can promote a child corps to the ruler's corps. The treasury belongs to the force,
        // so validate its surviving owner rather than requiring the initial corps identity.
        Same(content.Information.Corps.Where(c => c.IsRoot && snapshot.World.Forces.Any(f => f.Id == c.OwnerId && !f.IsEliminated)).Select(c => c.OwnerId).Union(addedForces.Where(id => snapshot.World.Forces.Any(f => f.Id == id && !f.IsEliminated))).Concat(generated.Where(f => !f.IsEliminated).Select(f => f.Id)),
            snapshot.World.Corps.Where(c => c.IsRoot).Select(c => c.OwnerId));
        if (snapshot.World.Treasures.Any(t => !KnownMapPlace(t.RegionId)))
            throw new ArgumentException("Unknown treasure region.");
        if (content.Information.Relations.Any(initial => !snapshot.World.Relations.Any(current =>
            current.FirstId == initial.FirstId && current.SecondId == initial.SecondId)))
            throw new ArgumentException("Missing initial relation identity.");
        if (snapshot.World.Officers.Any(o => o.RegionId is int region && !definition.Rules.Personnel.Places.ContainsKey(region) ||
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
                    San9.Content.Text.MessageEntityKind.Rank => definition.Content.Information.Ranks.Any(r => r.Id == entity.Id),
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
                !snapshot.World.Forces.Any(f => f.Id == note.ForceId) || string.IsNullOrWhiteSpace(note.Text) ||
                note.Text.Length > 240 || note.Text != note.Text.Trim())
                throw new ArgumentException("Invalid player chronicle.");
            previousDay = clock.DayNumber;
        }
    }
}
