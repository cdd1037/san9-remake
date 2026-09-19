using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class SessionValidation
{
    internal static void Validate(ScenarioSessionDefinition definition, SessionSnapshot snapshot)
    {
        if (!snapshot.World.Simulation.CanPersist)
            throw new ArgumentException("Session is not at a save boundary.");
        if (snapshot.Records.IsDefault || snapshot.Chronicle.IsDefault || snapshot.Records.Length > GameSession.RecentRecordLimit ||
            snapshot.NextOperationSequence < 0)
            throw new ArgumentException("Missing session state.");
        var expected = definition.CreateWorld(snapshot.World.Simulation.InitialSeed);
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
                origin.Clock.DayNumber < expected.Clock.DayNumber + 30 || origin.Clock.DayNumber > snapshot.World.Clock.DayNumber)
                throw new ArgumentException("Invalid runtime force provenance.");
        }
        var expectedControls = expected.Simulation.Controls.Concat(generated.Select(f => new ControlSeat($"ai:{f.Id}", f.Id, ControllerKind.Automatic)))
            .Where(c => snapshot.World.Forces.Any(f => f.Id == c.ForceId && !f.IsEliminated));
        if (snapshot.World.Simulation.Started != expected.Clock ||
            snapshot.World.Simulation.AnnualAppearancesEnabled != expected.Simulation.AnnualAppearancesEnabled ||
            snapshot.World.Simulation.FreeOfficerMigrationEnabled != expected.Simulation.FreeOfficerMigrationEnabled ||
            snapshot.World.Simulation.OfficeRequestsEnabled != expected.Simulation.OfficeRequestsEnabled ||
            snapshot.World.Simulation.ForceConsolidationEnabled != expected.Simulation.ForceConsolidationEnabled ||
            snapshot.World.Simulation.ForceEmergenceEnabled != expected.Simulation.ForceEmergenceEnabled ||
            snapshot.World.Simulation.CityIndependenceEnabled != expected.Simulation.CityIndependenceEnabled ||
            snapshot.World.Simulation.NaturalDeathsEnabled != expected.Simulation.NaturalDeathsEnabled ||
            expected.Simulation.ImperialCourtEnded && !snapshot.World.Simulation.ImperialCourtEnded ||
            !snapshot.World.Simulation.Controls.SequenceEqual(expectedControls))
            throw new ArgumentException("Scenario date or control configuration mismatch.");
        var content = definition.Content;
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
        Same(content.Information.Corps.Where(c => c.Treasury is null && snapshot.World.Forces.Any(f => f.Id == c.OwnerId && !f.IsEliminated)).Select(c => c.OwnerId).Concat(generated.Where(f => !f.IsEliminated).Select(f => f.Id)),
            snapshot.World.Corps.Where(c => c.Treasury is null).Select(c => c.OwnerId));
        Same(content.Information.TreasureStates.Select(t => t.Id), snapshot.World.Treasures.Select(t => t.Id));
        if (content.Information.Relations.Any(initial => !snapshot.World.Relations.Any(current =>
            current.FirstId == initial.FirstId && current.SecondId == initial.SecondId)))
            throw new ArgumentException("Missing initial relation identity.");
        if (snapshot.World.Officers.Any(o => o.RegionId is int region && !content.UnbuiltPlaces.Any(p => p.Id == region) ||
            o.OfficeId is int office && !content.Information.Offices.Any(d => d.Id == office)) ||
            snapshot.World.Forces.Any(f => f.RankId is int rank && !content.Information.Ranks.Any(r => r.Id == rank)))
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
