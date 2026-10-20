using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class WorldEditorTests
{
    [Fact]
    public void CancelCurrentObjectRestoresItsSessionValuesWhileKeepingOtherDraftEdits()
    {
        var session = GameSession.Start(Fixture(), 190);
        var before = session.Capture();
        var draft = new WorldEditorDraft(session, WorldEditorKind.Officers, Texts());
        Set(draft, 1, "武力", 99);
        Set(draft, 4, "武力", 98);
        draft.Reset(1);
        Assert.Equal(session.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities.War,
            draft.Preview.World.Officers.Single(p => p.Id == 1).BaseAbilities.War);
        Assert.Equal(98, draft.Preview.World.Officers.Single(p => p.Id == 4).BaseAbilities.War);
        Assert.Equal(4, Assert.Single(draft.Operation.Edits).Id);
        Assert.Equal(before, session.Capture());
    }
    private static ScenarioInitialState Fixture()
    {
        var source = G01ScenarioTests.Fixture();
        return source with
        {
            RegionNameKeys = new() { [10] = "fort", [11] = "wall", [12] = "empty" },
            RegionCityIds = new() { [10] = 1, [11] = 1, [12] = 1 },
            Settlements = [..source.Settlements.Select(s => s with { Population = 30000 }),
                new(10, "fort", 1, 3, 0, 1000, 100, 100, 70, []) { Kind = SettlementKind.Facility },
                new(11, "wall", 1, 4, 0, 0, 100, 100, 0, []) { Kind = SettlementKind.Facility, CanHostPersons = false },
                new(12, "empty", null, 5, 0, 0, 100, 100, 0, []) { Kind = SettlementKind.Facility }],
            Military = new(6, 6, Enumerable.Repeat(0, 36).ToImmutableArray(), [new(0, 1, null)], [],
                [..source.Settlements.Select(s => new BattlefieldPlace(s.Id, new(s.X * 5 + 2, s.Y * 5 + 2), new(1, 1))
                    { FacilityTypeId = 9000, EnabledTacticIds = [7000] }),
                    new(10, new(17, 2), new(1, 1)) { FacilityTypeId = 9000 },
                    new(11, new(22, 2), new(1, 1)) { FacilityTypeId = 9000 },
                    new(12, new(27, 2), new(1, 1)) { FacilityTypeId = 9000 }])
            {
                Facilities = [new(9000, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, [])
                    { Tactics = new(10, 3, true, [7000]) }],
                Tactics = [new(7000, 3, TacticEffect.Martial, 1, 3, 0, true, true, []),
                    new(7001, null, TacticEffect.FacilityAttack, 0, 0, 0, false, false, []) { FacilityAttack = new(true, 0) }]
            },
            Information = source.Information with { Tactics = [new(7000, "tactic"), new(7001, "rockfall")] }
        };
    }

    private static TextCatalog Texts() => new(TextCatalog.CurrentSchemaVersion, new()
    {
        ["force.1"] = "甲", ["force.2"] = "乙", ["city.1"] = "甲城", ["city.2"] = "乙城",
        ["officer.1"] = "甲一", ["officer.2"] = "甲二", ["officer.3"] = "甲三",
        ["officer.4"] = "乙一", ["officer.5"] = "乙二", ["officer.6"] = "乙三",
        ["fort"] = "城塞", ["wall"] = "栅栏", ["empty"] = "空设施", ["tactic"] = "连弩", ["rockfall"] = "落石"
    });

    private static void Set(WorldEditorDraft draft, int id, string label, uint value) =>
        Assert.Single(draft.Fields(id), f => f.Label == label).Set(value);

    [Fact]
    public void EditorCatalogsKeepTheExistingInformationCatalogOrder()
    {
        var session = GameSession.Start(Fixture(), 190);
        var texts = Texts();
        texts.Entries["officer.1"] = "张飞";
        texts.Entries["officer.4"] = "曹操";
        var officers = new WorldEditorDraft(session, WorldEditorKind.Officers, texts);
        var candidates = officers.Targets.Select(t => t.Id).ToHashSet();
        foreach (int page in Enumerable.Range(0, officers.Pages.Count))
            Assert.Equal(OfficerCatalog.Read(session.View, texts, page, null, null).Rows
                .Where(r => candidates.Contains(r.Key.Id)).Select(r => r.Key.Id), officers.ReadRows(page).Select(r => r.Id));
        var corps = new WorldEditorDraft(session, WorldEditorKind.Corps, texts);
        var catalog = new AdministrativeCatalog(session.View, texts);
        foreach (int page in Enumerable.Range(0, corps.Pages.Count))
            Assert.Equal(catalog.Read(InformationCategory.Forces, page).Rows.Select(r => r.Key.Id),
                corps.ReadRows(page).Select(r => r.Id));
    }

    [Fact]
    public void OriginalCatalogFamiliesShowDraftValuesBeforeEnteringAnotherEditor()
    {
        var session = GameSession.Start(Fixture(), 190);
        var before = session.Capture();
        var officers = new WorldEditorDraft(session, WorldEditorKind.Officers, Texts());
        Assert.Equal(13, officers.Pages.Count);
        Set(officers, 1, "武力", 99);
        int war = Array.FindIndex(officers.Pages[0].Columns, c => c.Id == "war");
        Assert.Equal(99, officers.ReadRows(0).Single(r => r.Id == 1).Cells[war].Number);
        var corps = new WorldEditorDraft(session, WorldEditorKind.Corps, Texts());
        Assert.Equal(InformationCategory.Forces, corps.Category);
        Assert.Equal([1, 2], corps.ReadRows(0).Select(r => r.Id).Order().ToArray());
        Assert.Equal(2, corps.TargetFromRow(2));
        Assert.Equal(2, Assert.Single(corps.ForceCorps(2)).Id);
        Assert.Equal([1, 2], new WorldEditorDraft(session, WorldEditorKind.Cities, Texts()).ReadRows(0).Select(r => r.Id).Order().ToArray());
        var facilities = new WorldEditorDraft(session, WorldEditorKind.Facilities, Texts());
        Assert.Equal(["所属", "战力"], facilities.Pages.Select(p => p.Title).ToArray());
        Assert.Equal(["name", "city", "force", "corps-name", "commander", "serving", "captives", "free"], facilities.Pages[0].Columns.Select(c => c.Id).ToArray());
        Assert.Equal([10], facilities.ReadRows(0).Select(r => r.Id).ToArray());
        var obstacles = new WorldEditorDraft(session, WorldEditorKind.Obstacles, Texts());
        Assert.Equal("耐久", Assert.Single(obstacles.Pages).Title);
        Assert.Equal(["name", "city", "durability", "max-durability"], obstacles.Pages[0].Columns.Select(c => c.Id).ToArray());
        Assert.Equal([11], obstacles.ReadRows(0).Select(r => r.Id).ToArray());
        StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void NavigationAndDiscardNeverWriteAndRevertingArrayChangesClearsTheDraft()
    {
        var session = GameSession.Start(Fixture(), 190);
        var before = session.Capture();
        var draft = new WorldEditorDraft(session, WorldEditorKind.Officers, Texts());
        Set(draft, 1, "步兵熟练", 1000);
        Set(draft, 1, "步兵精通", 1);
        Set(draft, 1, "连弩", 1);
        _ = draft.Fields(2);
        Assert.Equal(1000u, draft.Fields(1).Single(f => f.Label == "步兵熟练").Value);
        StateAssert.Equal(before, session.Capture());
        Set(draft, 1, "步兵熟练", (uint)before.World.Officers.Single(p => p.Id == 1).TacticProficiencies[0]);
        Set(draft, 1, "步兵精通", 0);
        Set(draft, 1, "连弩", 0);
        Assert.False(draft.HasChanges);
        Assert.Empty(draft.Operation.Edits);
        Assert.False(new WorldEditorDraft(session, WorldEditorKind.Officers, Texts()).HasChanges);
    }

    [Fact]
    public void AllFiveEditorsUseRecordedSessionOperationsAndSurvivePersistence()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var before = session.Snapshot;
        foreach (var (kind, id, label, value) in new[]
        {
            (WorldEditorKind.Officers, 1, "武力", 99u), (WorldEditorKind.Corps, 2, "资金", 999999u),
            (WorldEditorKind.Cities, 1, "城防：落石", 1u), (WorldEditorKind.Facilities, 10, "士兵", 0u),
            (WorldEditorKind.Obstacles, 11, "耐久", 50u)
        })
        {
            var draft = new WorldEditorDraft(session, kind, Texts());
            Set(draft, id, label, value);
            var request = session.Request(draft.Operation);
            Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
            Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        }
        Assert.Equal(99, session.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities.War);
        Assert.Equal(999999u, session.Snapshot.Corps.Single(c => c.Id == 2).Treasury.Money);
        Assert.Equal([7000, 7001], session.Snapshot.Settlements.Single(s => s.Id == 1).EnabledTacticIds.ToArray());
        Assert.Equal(0, session.Snapshot.Settlements.Single(s => s.Id == 10).Morale);
        Assert.Equal(50, session.Snapshot.Settlements.Single(s => s.Id == 11).Durability);
        Assert.Equal(before.Clock, session.Snapshot.Clock);
        Assert.Equal(before.Simulation.NextSequence, session.Snapshot.Simulation.NextSequence);
        Assert.Equal(before.Simulation.RandomStreams, session.Snapshot.Simulation.RandomStreams);
        Assert.Equal(5, session.NextOperationSequence);
        Assert.True(session.HasUnsavedChanges);
        var restored = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), restored.Capture());
        var logPath = Path.Combine(Path.GetTempPath(), $"san9-editor-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(logPath, session);
            var replay = GameSession.Start(definition, 190);
            Assert.Null(CommandLogService.Replay(logPath, replay).FirstFailure);
            StateAssert.Equal(session.Capture(), replay.Capture());
        }
        finally { File.Delete(logPath); }
        var recording = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition), verifyEvents: true);
        StateAssert.Equal(session.Capture(), recording.Session.Capture());
        Assert.Equal([7000], GameSession.Start(definition, 190).Snapshot.Settlements.Single(s => s.Id == 1).EnabledTacticIds.ToArray());
    }

    [Fact]
    public void TreasuryBatchRejectsAtomicallyAndNonPlayerCannotEdit()
    {
        var session = GameSession.Start(Fixture(), 190);
        var before = session.Capture();
        var operation = new WorldEditOperation(WorldEditorKind.Corps, [new CorpsEdit(1, 100, 200), new CorpsEdit(2, 1000001, 200)]);
        Assert.Equal(SessionRejection.InvalidRequest, session.Apply(session.Request(operation)).Rejection);
        StateAssert.Equal(before, session.Capture());
        operation = operation with { Edits = [new CorpsEdit(2, 100, 200)] };
        Assert.Equal(SessionRejection.InvalidRequest, session.Apply(session.Request(operation, "ai:2")).Rejection);
        StateAssert.Equal(before, session.Capture());
    }

    [Fact]
    public void CityLimitsClampCurrentValuesAndGarrisonCrossingZeroSetsMorale()
    {
        var session = GameSession.Start(Fixture(), 190);
        var city = new WorldEditorDraft(session, WorldEditorKind.Cities, Texts());
        Set(city, 1, "最大收益", 100);
        Assert.Equal(100u, city.Fields(1).Single(f => f.Label == "收益").Value);
        Set(city, 1, "士兵", 0);
        Assert.DoesNotContain(city.Fields(1), f => f.Label == "士气");
        Set(city, 1, "士兵", 100);
        Assert.Equal(70u, city.Fields(1).Single(f => f.Label == "士气").Value);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(city.Operation)).Disposition);
        var rules = new MilitaryRulebook(Fixture().Military!);
        var current = session.Snapshot.Settlements.Single(s => s.Id == 1);
        Assert.Equal(7000, rules.FacilityTactic(current)!.Id);
        var tactics = new WorldEditorDraft(session, WorldEditorKind.Cities, Texts());
        Set(tactics, 1, "城防：连弩", 0);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(tactics.Operation)).Disposition);
        Assert.Null(rules.FacilityTactic(session.Snapshot.Settlements.Single(s => s.Id == 1)));
    }

    [Fact]
    public void EditorCategoriesAndFieldsCannotBeForged()
    {
        var session = GameSession.Start(Fixture(), 190);
        Assert.Equal([10], WorldEditing.Candidates(session.Snapshot, WorldEditorKind.Facilities));
        Assert.Equal([11], WorldEditing.Candidates(session.Snapshot, WorldEditorKind.Obstacles));
        var draft = new WorldEditorDraft(session, WorldEditorKind.Obstacles, Texts());
        Set(draft, 11, "耐久", 50);
        var edit = Assert.IsType<SettlementEdit>(Assert.Single(draft.Operation.Edits));
        var before = session.Capture();
        var forged = new WorldEditOperation(WorldEditorKind.Obstacles, [edit with { Soldiers = 100 }]);
        Assert.Equal(SessionRejection.InvalidRequest, session.Apply(session.Request(forged)).Rejection);
        forged = new(WorldEditorKind.Facilities, [edit]);
        Assert.Equal(SessionRejection.InvalidRequest, session.Apply(session.Request(forged)).Rejection);
        StateAssert.Equal(before, session.Capture());
    }
}
