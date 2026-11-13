using San9.Application.Persistence;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class ManualOfficerAppearancePersistenceTests
{
    [Fact]
    public void IndividualAssignmentsToDifferentForcesAreAtomic()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with { Officers = basis.Officers.Select(o => o.State.Id is 2 or 3
            ? o with { State = o.State with { Status = OfficerStatus.Dead, OwnerId = null, SettlementId = null } } : o).ToArray() };
        var session = GameSession.Start(content, 42);
        var operation = new ConfigureIndividualAppearancesOperation([
            new(2, new(191, 1, 1, 30) { KeepPendingIfForceUnavailable = true }),
            new(3, new(191, 2, 2, 30) { KeepPendingIfForceUnavailable = true })]);
        var invalid = new ConfigureIndividualAppearancesOperation([
            new(2, new(191, 1, 1, 30)), new(3, new(191, 1, 2, 30))]);
        Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(invalid)).Disposition);
        Assert.Equal(OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 2).Status);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(operation)).Disposition);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(1, restored.Snapshot.Officers.Single(p => p.Id == 2).ManualAppearance!.ForceId);
        Assert.Equal(2, restored.Snapshot.Officers.Single(p => p.Id == 3).ManualAppearance!.ForceId);
    }
    [Fact]
    public void BatchAppearanceIsAtomicAndUsesTheSamePlanForEverySelectedOfficer()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with { Officers = basis.Officers.Select(o => o.State.Id is 2 or 3
            ? o with { State = o.State with { Status = OfficerStatus.Dead, OwnerId = null, SettlementId = null } } : o).ToArray() };
        var session = GameSession.Start(content, 42);
        var plan = new ManualOfficerAppearance(191, 1, 1, 30);
        var invalid = session.Apply(session.Request(new ConfigureOfficerAppearancesOperation([2, 1], plan)));
        Assert.Equal(SessionDisposition.Rejected, invalid.Disposition);
        Assert.Equal(OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 2).Status);
        var operation = new ConfigureOfficerAppearancesOperation([3, 2], plan);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(operation)).Disposition);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.All(restored.Snapshot.Officers.Where(p => p.Id is 2 or 3), p => Assert.Equal(plan, p.ManualAppearance));
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<SessionOperation>(operation, StrictJson.Options);
        var roundTrip = Assert.IsType<ConfigureOfficerAppearancesOperation>(StrictJson.Deserialize<SessionOperation>(bytes));
        Assert.Equal(operation.OfficerIds.ToArray(), roundTrip.OfficerIds.ToArray());
        Assert.Equal(plan, roundTrip.Plan);
    }
    [Fact]
    public void ScheduledAppearanceAndOperationSurviveSerialization()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with { Officers = basis.Officers.Select(o => o.State.Id == 2
            ? o with { State = o.State with { Status = OfficerStatus.Dead, OwnerId = null, SettlementId = null } } : o).ToArray() };
        var session = GameSession.Start(content, 42);
        var operation = new ConfigureOfficerAppearanceOperation(2, new(191, 1, 1, 30));
        var result = session.Apply(session.Request(operation));
        Assert.NotEqual(SessionDisposition.Rejected, result.Disposition);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(operation.Plan, restored.Snapshot.Officers.Single(p => p.Id == 2).ManualAppearance);
        Assert.Equal(OfficerStatus.NotAppeared, restored.Snapshot.Officers.Single(p => p.Id == 2).Status);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<SessionOperation>(operation, StrictJson.Options);
        Assert.Equal(operation, StrictJson.Deserialize<SessionOperation>(bytes));
    }
}
