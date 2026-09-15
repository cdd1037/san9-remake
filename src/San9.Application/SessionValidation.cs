using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class SessionValidation
{
    internal static void Validate(ScenarioSessionDefinition definition, SessionSnapshot snapshot)
    {
        if (snapshot.Records.IsDefault || snapshot.Chronicle.IsDefault || snapshot.Records.Length > GameSession.RecentRecordLimit ||
            snapshot.NextOperationSequence < 0)
            throw new ArgumentException("Missing session state.");
        var expected = definition.CreateWorld(snapshot.World.Simulation.InitialSeed);
        if (snapshot.World.Simulation.Started != expected.Clock ||
            !snapshot.World.Simulation.Controls.SequenceEqual(expected.Simulation.Controls.Where(c =>
                snapshot.World.Forces.Any(f => f.Id == c.ForceId && !f.IsEliminated))))
            throw new ArgumentException("Scenario date or control configuration mismatch.");
        var content = definition.Content;
        static void Same(IEnumerable<int> a, IEnumerable<int> b)
        {
            if (!a.Order().SequenceEqual(b.Order())) throw new ArgumentException("Scenario identity set mismatch.");
        }
        Same(content.Forces.Select(f => f.Id), snapshot.World.Forces.Select(f => f.Id));
        if (content.Settlements.Any(s => !snapshot.World.Settlements.Any(current => current.Id == s.Id)) ||
            snapshot.World.Settlements.Any(s => !content.Settlements.Any(initial => initial.Id == s.Id) &&
                (s.Facility is null || !content.UnbuiltPlaces.Any(region => region.Id == s.Id))))
            throw new ArgumentException("Unknown or missing settlement identity.");
        Same(content.Officers.Select(o => o.State.Id), snapshot.World.Officers.Select(o => o.Id));
        // Field units are created and absorbed at runtime; identities are allocated by the world.
        if (snapshot.World.Units.Any(u => u.ServicePlaceId is int home &&
            !content.Settlements.Any(s => s.Id == home) && !content.UnbuiltPlaces.Any(p => p.Id == home)))
            throw new ArgumentException("Unknown unit service place.");
        // Corps are runtime entities; only the force treasury anchors survive ordinary reorganization.
        Same(content.Information.Corps.Where(c => c.Treasury is null && snapshot.World.Forces.Any(f => f.Id == c.OwnerId && !f.IsEliminated)).Select(c => c.Id),
            snapshot.World.Corps.Where(c => c.Treasury is null).Select(c => c.Id));
        if (content.Information.Corps.Where(c => c.Treasury is null && snapshot.World.Forces.Any(f => f.Id == c.OwnerId && !f.IsEliminated)).Any(c =>
            !snapshot.World.Corps.Any(saved => saved.Id == c.Id && saved.OwnerId == c.OwnerId)))
            throw new ArgumentException("Primary corps ownership mismatch.");
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
