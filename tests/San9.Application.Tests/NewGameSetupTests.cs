using San9.Application.Authoring;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Authoring;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class NewGameSetupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewGameModuleChoicesSurviveSaveAndControlNaturalDeaths(bool enabled)
    {
        var source = Source();
        var settings = new NewGameSettings { NaturalDeathsEnabled = enabled, EquipmentDevelopmentEnabled = enabled,
            FormationLearningEnabled = enabled, PersonalPeerageEnabled = enabled };
        var session = GameSession.Start(source, 123, settings: settings);
        var restored = SaveService.Decode(SaveService.Encode(session), source);
        Assert.Equal(enabled, restored.Snapshot.Simulation.NaturalDeathsEnabled);
        Assert.Equal(settings, restored.Snapshot.Simulation.NewGame);
    }

    [Fact]
    public void IndependentModuleChoicesSurviveSaveAndLegacyMissingChoicesLoad()
    {
        var source = Source();
        for (var flags = 0; flags < 16; flags++)
        {
            var settings = new NewGameSettings
            {
                NaturalDeathsEnabled = (flags & 1) != 0,
                EquipmentDevelopmentEnabled = (flags & 2) != 0,
                FormationLearningEnabled = (flags & 4) != 0,
                PersonalPeerageEnabled = (flags & 8) != 0
            };
            var session = GameSession.Start(source, 123, settings: settings);
            var bytes = SaveService.Encode(session);
            var restored = SaveService.Decode(bytes, source);
            Assert.Equal(settings, restored.Snapshot.Simulation.NewGame);
            Assert.Equal(settings.NaturalDeathsEnabled, restored.Snapshot.Simulation.NaturalDeathsEnabled);

            var json = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
            var newGame = json["session"]!["world"]!["simulation"]!["newGame"]!.AsObject();
            newGame.Remove("naturalDeathsEnabled");
            newGame.Remove("equipmentDevelopmentEnabled");
            newGame.Remove("formationLearningEnabled");
            newGame.Remove("personalPeerageEnabled");
            var legacy = SaveService.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), source);
            Assert.Null(legacy.Snapshot.Simulation.NewGame.NaturalDeathsEnabled);
            Assert.Null(legacy.Snapshot.Simulation.NewGame.EquipmentDevelopmentEnabled);
            Assert.Null(legacy.Snapshot.Simulation.NewGame.FormationLearningEnabled);
            Assert.Null(legacy.Snapshot.Simulation.NewGame.PersonalPeerageEnabled);
        }
    }

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, null)]
    public void NewForceUsesTheUnrankedIdentityWithoutInferringAnInitialRank(bool marked, int? expected)
    {
        var source = Source();
        source = source with { Information = source.Information with
            { Ranks = source.Information.Ranks.Select(r => r with { IsUnranked = marked }).ToArray() } };
        var setup = NewGameSetup.Empty with
        {
            Library = CreatedOfficerLibrary.Empty with { Officers = [First] },
            Forces = [new(First.Id, 1)]
        };
        var composed = NewGameComposer.Compose(source, setup);
        Assert.Equal(expected, composed.Scenario.Forces.Single(f => f.Id != 12).RankId);
    }

    private static readonly CreatedOfficer First = new(Guid.Parse("10000000-0000-0000-0000-000000000000"), "甲")
        { Surname = "新", FlagGlyph = 42, BirthYear = 160, AppearanceYear = 180, Abilities = new(91, 92, 93, 94), PortraitKey = "portrait.42" };
    private static readonly CreatedOfficer Second = new(Guid.Parse("20000000-0000-0000-0000-000000000000"), "乙")
        { Surname = "新", FlagGlyph = 66, BirthYear = 175, AppearanceYear = 189, ParentId = First.Id };
    private static ScenarioInitialState Source() => new(ScenarioInitialState.CurrentSchemaVersion, "created-start", 190, 1, 1, 12,
        [new(12, "force.old", 5, 100) { LeaderId = 1000 }],
        [new(1, "city.empty", null, 0, 0, 8000, 100, 100, 0, [])
            { Kind = SettlementKind.City, Population = 100000, DraftPopulation = 20000, Commerce = 300, MaxCommerce = 1000, Cultivation = 600, MaxCultivation = 1000 },
         new(2, "city.old", 12, 1, 0, 10000, 100, 100, 50, []) { Kind = SettlementKind.City, CorpsId = 70 }],
        new() { [1] = "city.empty", [2] = "city.old" }, new() { [1] = 1, [2] = 2 })
    {
        ForceEmergenceEnabled = false, ForceConsolidationEnabled = false, CityIndependenceEnabled = false,
        OfficeRequestsEnabled = false, NaturalDeathsEnabled = true, FreeOfficerMigrationEnabled = false,
        Officers = [new("officer.old", new(1000, 12, 2, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Ruler, BloodlineId = 9000 })],
        Information = new([new(4, "rank.none", 10000, 0, 0, 0) { IsUnranked = true }], [], [],
            [new(70, 12, 1000, new(10000, 100000)) { IsRoot = true }], [])
    };
    private static TextCatalog Text() => new(TextCatalog.CurrentSchemaVersion, new()
    {
        ["scenario.title"] = "测试", ["force.old"] = "旧势力", ["officer.old"] = "旧君主",
        ["city.empty"] = "空城", ["city.old"] = "旧城", ["rank.none"] = "无"
    });
    private static NewGameDraft Draft(params CreatedOfficer[] people) => new(Source(), Text(),
        new Dictionary<string, string> { ["catalog-person-1000"] = "portrait.1" }, CreatedOfficerLibrary.Empty with { Officers = [..people] });

    [Fact]
    public void AuthoredTraitsRelativeAgeAndExplicitFamilySurviveLibraryAndSaveRoundTrips()
    {
        var person = First with { BirthYear = 0, AgeAtScenarioStart = 30, SuccessionPriority = 42,
            ForceFounderPriority = 37, TacticLinkBonus = 25, DuelAdvantages = DuelAdvantages.DominantAttack,
            PrefersConsolidationLeadership = true, AutomaticSurrenderAllowed = false, HasRecruitmentPoliticsBonus = true,
            HistoricalBloodlineRootId = 1000 };
        var library = CreatedOfficerLibrary.Decode((CreatedOfficerLibrary.Empty with { Officers = [person] }).Encode());
        var draft = Draft(library.Officers[0]);
        draft.Deploy([person.Id], 2, true);
        var session = GameSession.Start(draft.Source, 123, setup: draft.Setup);
        var restored = SaveService.Decode(SaveService.Encode(session), draft.Source);
        var state = Assert.Single(restored.Snapshot.Officers, p => p.Id != 1000);
        Assert.Equal(160, state.BirthYear);
        Assert.Equal(9000, state.BloodlineId);
        Assert.Equal(42, state.SuccessionPriority);
        Assert.Equal(37, state.ForceFounderPriority);
        Assert.Equal(25, state.TacticLinkBonus);
        Assert.Equal(DuelAdvantages.DominantAttack, state.DuelAdvantages);
        Assert.True(state.PrefersConsolidationLeadership);
        Assert.False(state.AutomaticSurrenderAllowed);
        Assert.True(state.HasRecruitmentPoliticsBonus);
    }

    [Fact]
    public void ExplicitLibraryFamilyKeepsItsUndeployedRootAndRejectsCycles()
    {
        var root = First with { BloodlineRootId = First.Id };
        var child = Second with { ParentId = null, BloodlineRootId = root.Id };
        var draft = Draft(root, child);
        draft.Deploy([child.Id], 2, true);
        var composed = NewGameComposer.Compose(draft.Source, draft.Setup);
        var people = composed.Scenario.Officers.Where(p => p.State.Id != 1000).ToArray();
        Assert.Equal(2, people.Length);
        Assert.Single(people.Select(p => p.State.BloodlineId).Distinct());
        Assert.Single(people, p => p.State.IsDormant);
        var cyclic = CreatedOfficerLibrary.Empty with { Officers = [root with { BloodlineRootId = child.Id }, child] };
        Assert.Throws<InvalidDataException>(cyclic.Validate);
    }

    [Fact]
    public void CreatedForceFlagFollowsItsCurrentRulerWithCountryTitleTakingPriority()
    {
        var original = Source();
        var source = original with
        {
            Officers = [original.Officers[0] with { FlagGlyph = 77, State = original.Officers[0].State with { Surname = "新" } }],
            Information = original.Information with { CountryTitles = [new(new(5000, true, true, false, [], []), "title", 37)] }
        };
        var draft = new NewGameDraft(source, Text(), new Dictionary<string, string>(),
            CreatedOfficerLibrary.Empty with { Officers = [First, Second with { FlagGlyph = -1 }] });
        draft.SetForces([new(First.Id, 1)]); draft.Deploy([Second.Id], 1, true);
        var session = GameSession.Start(source, 123, playerForceIds: [13], setup: draft.Setup);
        var display = new StrategySnapshotSource(source, new ScenarioRules(source), session.Definitions);
        var force = session.Snapshot.Forces.Single(f => f.Id == 13);

        Assert.Equal(-1, session.View.Officers.Single(p => p.Id == 1002).FlagGlyph);
        Assert.Equal(42, display.Force(force).FlagGlyph);
        Assert.Equal(-1, display.Force(force with { LeaderId = 1002 }).FlagGlyph);
        Assert.Equal(-1, display.Force(force with { LeaderId = null }).FlagGlyph);
        Assert.Equal(37, display.Force(force with { LeaderId = 1002, CountryTitleId = 5000 }).FlagGlyph);
        var historical = session.Snapshot.Forces.Single(f => f.Id == 12);
        Assert.Equal(77, display.Force(historical).FlagGlyph);
        Assert.Equal(42, display.Force(historical with { LeaderId = 1001 }).FlagGlyph);
        Assert.Equal(-1, display.Force(historical with { LeaderId = 1002 }).FlagGlyph);
    }

    [Fact]
    public void OpeningCityQueriesReflectAssignmentsWithoutApplyingStartupOrMutatingTheDraft()
    {
        var draft = Draft(First, Second);
        draft.SetForces([new(First.Id, 1)]); draft.Deploy([Second.Id], 1, true);
        var before = draft.Setup;
        var catalog = new San9.Application.Presentation.OpeningCityCatalog(draft);
        Assert.Equal("新甲", catalog.Cell(1, "force").Text);
        Assert.Equal(2, catalog.Cell(1, "serving").Number);
        Assert.Equal(120000, catalog.Cell(1, "population").Number);
        Assert.Equal(20000, catalog.Cell(1, "draft").Number);
        Assert.Equal(8000, catalog.Cell(1, "soldiers").Number);
        Assert.Null(draft.Preview().PlayerForceId);
        Assert.Same(before, draft.Setup);
        Assert.Equal(8000u, draft.Source.Settlements[0].Soldiers);
        Assert.Null(draft.Source.Settlements[0].OwnerId);
    }

    [Theory]
    [InlineData(1, false, OfficerStatus.Free, null)]
    [InlineData(2, false, OfficerStatus.Free, null)]
    [InlineData(2, true, OfficerStatus.Serving, 12)]
    public void DeploymentPreservesExplicitAttributesAndCanBeUndone(int city, bool join, OfficerStatus status, int? owner)
    {
        var draft = Draft(First, Second);
        draft.Deploy([Second.Id], city, join);
        var result = draft.Compose();
        var ids = NewGameComposer.OfficerIds(draft.Source, draft.Setup);
        var person = result.Officers.Single(p => p.State.Id == ids[Second.Id]).State;
        Assert.Equal(status, person.Status); Assert.Equal(owner, person.OwnerId); Assert.Equal(city, person.SettlementId);
        Assert.Equal(ids[First.Id], person.ParentId); Assert.True(person.BloodlineId > 9000);
        Assert.True(result.Officers.Single(p => p.State.Id == ids[First.Id]).State.IsDormant);
        draft.CancelDeployment([Second.Id]);
        Assert.DoesNotContain(draft.Compose().Officers, p => p.State.Id == ids[Second.Id]);
        Assert.Equal(OfficerStatus.Ruler, draft.Source.Officers[0].State.Status);
        Assert.Null(draft.Source.Settlements[0].OwnerId);
    }

    [Fact]
    public void FuturePlacementUsesAnnualAppearanceWithoutPrematureService()
    {
        var draft = Draft(First with { AppearanceYear = 192 });
        draft.Deploy([First.Id], 2, true);
        var person = draft.Compose().Officers.Last().State;
        Assert.Equal(OfficerStatus.NotAppeared, person.Status); Assert.Null(person.OwnerId); Assert.Equal(2, person.SettlementId);
        Assert.False(person.IsDormant);
    }

    [Theory]
    [InlineData(1, 20, false)]
    [InlineData(189, 200, false)]
    [InlineData(220, 234, false)]
    [InlineData(1, 20, true)]
    [InlineData(220, 234, true)]
    public void AgelessScenariosAdmitOldYoungAndFuturePeopleImmediately(int birth, int appearance, bool ruler)
    {
        var source = Source() with { NaturalDeathsEnabled = false };
        var person = First with { BirthYear = birth, AppearanceYear = appearance, LifespanTier = 0 };
        var draft = new NewGameDraft(source, Text(), new Dictionary<string, string>(), CreatedOfficerLibrary.Empty with { Officers = [person] });
        Assert.True(NewGameComposer.CanDeploy(person, source));
        if (ruler) draft.SetForces([new(person.Id, 1)]); else draft.Deploy([person.Id], 2, true);
        var state = draft.Compose().Officers.Single(o => o.State.Id == 1001).State;
        Assert.Equal(ruler ? OfficerStatus.Ruler : OfficerStatus.Serving, state.Status);
        Assert.Equal(birth, state.BirthYear); Assert.Equal(appearance, state.AppearanceYear);
    }

    [Fact]
    public void AutomaticFoundedForceReceivesRecruitmentBeforeAdvancedDifficultyBonus()
    {
        var draft = Draft(First); draft.SetForces([new(First.Id, 1)]);
        var session = GameSession.Start(draft.Source, 123, playerForceIds: [12], setup: draft.Setup,
            settings: new NewGameSettings { Difficulty = GameDifficulty.Advanced, SoldierBonus = 3000, MoraleBonus = 20 });
        Assert.Equal(28000u, session.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(70, session.Snapshot.Settlements.Single(s => s.Id == 1).Morale);
        Assert.Equal(18000u, session.Snapshot.Settlements.Single(s => s.Id == 1).DraftPopulation);
        Assert.Equal(10000u, session.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
    }

    [Fact]
    public void CreatedForceIsSelectableAndStartsWithTreasuryAndOneRecruitmentBoost()
    {
        var draft = Draft(First, Second);
        draft.SetForces([new(First.Id, 1)]);
        draft.Deploy([Second.Id], 1, true);
        var scenario = draft.Compose();
        var force = scenario.Forces.Single(f => f.Id != 12);
        Assert.Equal(13, force.Id); Assert.Equal(100, force.Trust); Assert.Equal(4, force.RankId);
        Assert.Equal(0, force.ColorCode);
        var choice = draft.Selection().Forces.Single(f => f.Id == force.Id);
        Assert.Equal("新甲", choice.Name); Assert.Equal(2, choice.Officers); Assert.Equal("portrait.42", choice.Portrait);
        var session = GameSession.Start(draft.Source, 123, playerForceIds: [force.Id], setup: draft.Setup);
        Assert.Equal(force.Id, session.View.PlayerForceId);
        var city = session.Snapshot.Settlements.Single(s => s.Id == 1);
        Assert.Equal(15000u, city.Soldiers); Assert.Equal(18000u, city.DraftPopulation);
        Assert.Equal(50, city.Morale);
        var root = session.Snapshot.Corps.Single(c => c.OwnerId == force.Id);
        Assert.True(root.IsRoot); Assert.True(root.Treasury.Money >= 3000); Assert.True(root.Treasury.Food >= 30000);
        Assert.Equal(50, Assert.Single(scenario.Information.Relations).Value);
        Assert.Equal(50, scenario.Officers.Last().State.Loyalty);
        Assert.InRange(session.Snapshot.Officers.Single(p => p.Id == 1002).Loyalty, 95, 99);
    }

    [Fact]
    public void PromotionAndForceRemovalReleaseMembersAndPreserveOtherForceIdentities()
    {
        var source = Source() with { Settlements = [..Source().Settlements, new(3, "city.empty", null, 2, 0, 0, 100, 100, 0, []) { Kind = SettlementKind.City }] };
        var draft = new NewGameDraft(source, Text(), new Dictionary<string, string>(), CreatedOfficerLibrary.Empty with { Officers = [First, Second] });
        draft.Deploy([First.Id], 2, true);
        draft.SetForces([new(First.Id, 1), new(Second.Id, 3)]);
        Assert.Empty(draft.Setup.Placements);
        int remaining = draft.Compose().Forces.Single(f => f.LeaderId == 1002).Id;
        draft.SetForces([new(Second.Id, 3)]);
        Assert.Equal(remaining, draft.Compose().Forces.Single(f => f.LeaderId == 1002).Id);
        Assert.Null(draft.Compose().Settlements.Single(s => s.Id == 1).OwnerId);
        Assert.True(draft.Compose().Officers.Single(p => p.State.Id == 1001).State.IsDormant);
    }

    [Fact]
    public void InvalidAssignmentsAreAtomicAndSourceReferencesAreValidated()
    {
        var draft = Draft(First, Second);
        var before = draft.Setup;
        Assert.Throws<InvalidDataException>(() => draft.SetForces([new(First.Id, 2)]));
        Assert.Throws<InvalidDataException>(() => draft.SetForces([new(First.Id, 1), new(Second.Id, 1)]));
        Assert.Throws<InvalidDataException>(() => draft.Deploy([Guid.NewGuid()], 1, false));
        Assert.Throws<InvalidDataException>(() => draft.Deploy([First.Id], 999, false));
        Assert.Same(before, draft.Setup);
        var invalid = Draft(First with { ScenarioParentId = 999 });
        Assert.Throws<InvalidDataException>(() => invalid.Deploy([First.Id], 1, false));
        var young = Draft(First with { BirthYear = 180, AppearanceYear = 195 });
        Assert.Throws<InvalidDataException>(() => young.SetForces([new(First.Id, 1)]));
    }

    [Theory]
    [InlineData(GameMode.Historical)]
    [InlineData(GameMode.Fictional)]
    public void SaveAndCommandScriptUseWorldStateAndLocalDefinitionsAgainstTheBaseScenario(GameMode mode)
    {
        var original = Source();
        var source = original with { Officers = [original.Officers[0] with
            { FlagGlyph = 42, State = original.Officers[0].State with { Surname = "新" } }] };
        var draft = new NewGameDraft(source, Text(), new Dictionary<string, string>(),
            CreatedOfficerLibrary.Empty with { Officers = [First, Second] });
        draft.SetForces([new(First.Id, 1)]); draft.Deploy([Second.Id], 1, true);
        var session = GameSession.Start(draft.Source, 123, playerForceIds: [13], setup: draft.Setup, settings: new() { Mode = mode });
        Assert.Equal(42, draft.Preview().Forces.Single(f => f.Id == 13).FlagGlyph);
        Assert.Equal(42, session.View.Forces.Single(f => f.Id == 13).FlagGlyph);
        foreach (var expected in draft.Compose().Officers.Where(p => p.State.Id != 1000).Select(p => p.State))
        {
            var initialized = session.Snapshot.Officers.Single(p => p.Id == expected.Id);
            Assert.Equal(expected with { ForceLoyalties = initialized.ForceLoyalties }, initialized);
            Assert.Equal(expected.LoyaltyTo(12), initialized.LoyaltyTo(12));
            Assert.InRange(initialized.Loyalty, initialized.Status == OfficerStatus.Ruler ? 100 : 95,
                initialized.Status == OfficerStatus.Ruler ? 100 : 99);
        }
        var initialCity = session.Snapshot.Settlements.Single(s => s.Id == 1);
        Assert.Equal(50, initialCity.Morale);
        var capture = session.Capture();
        session = GameSession.Restore(source, capture with { World = capture.World with {
            Settlements = capture.World.Settlements.Select(s => s.Id == 1 ? s with { Morale = 23 } : s).ToArray()
        } });
        byte[] save = SaveService.Encode(session);
        var changedContent = source with { Officers = [source.Officers[0] with { FlagGlyph = 99 }] };
        var restored = SaveService.Decode(save, changedContent);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(23, restored.Snapshot.Settlements.Single(s => s.Id == 1).Morale);
        Assert.Equal(42, restored.View.Forces.Single(f => f.Id == 13).FlagGlyph);
        Assert.Equal(42, restored.View.Officers.Single(p => p.Id == 1001).FlagGlyph);
        Assert.Equal(66, restored.View.Officers.Single(p => p.Id == 1002).FlagGlyph);
        Assert.Equal(First.Abilities, restored.Snapshot.Officers.Single(o => o.Id == 1001).BaseAbilities);
        var header = CommandLogService.CreateHeader(session);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            CommandLogService.Write(path, header, []);
            Assert.Null(CommandLogService.Replay(path, restored).FirstFailure);
            StateAssert.Equal(session.Capture(), restored.Capture());
            var wrong = GameSession.Start(source, 123);
            Assert.Throws<CommandLogException>(() => CommandLogService.Replay(path, wrong));
        }
        finally { File.Delete(path); }
        // A player load changes random streams but retains the startup definitions.
        var loaded = SaveService.Load(save, changedContent);
        Assert.Equal(42, loaded.View.Forces.Single(f => f.Id == 13).FlagGlyph);
        Assert.Equal(First.FullName, loaded.Definitions.Officers[0].Surname + loaded.Definitions.Officers[0].Name);
        var actual = session.AdvanceXun(); var next = restored.AdvanceXun();
        EventAssert.Equal(actual.Events, next.Events); StateAssert.Equal(session.Capture(), restored.Capture());
    }

    [Fact]
    public void TextCompositionKeepsNamesLiteralAndDoesNotChangeTheBaseCatalog()
    {
        const string biography = "[wave amp=50 freq=5]程家军[/wave]";
        var draft = Draft(First with { Name = "[甲]", CourtesyName = "[乙]", Biography = biography });
        draft.Deploy([First.Id], 1, false);
        var text = draft.Composition.Definitions.AddText(draft.Text);
        var person = draft.Compose().Officers.Last();
        Assert.Equal("新[甲]", text.Resolve(person.NameKey));
        Assert.Equal("[乙]", text.Resolve(person.CourtesyNameKey!));
        Assert.Equal(biography, text.ResolveRich(person.BiographyKey!));
        var session = GameSession.Start(draft.Source, 7, setup: draft.Setup);
        Assert.Equal(biography, OfficerDetails.Read(session.View, text, person.State.Id).Biography);
        Assert.False(draft.Text.Entries.ContainsKey(person.NameKey));
        text.Validate();
    }

    [Fact]
    public void SaveContainsOnlyParticipatingDefinitionsAndRequiredAncestors()
    {
        var unrelated = First with { Id = Guid.Parse("30000000-0000-0000-0000-000000000000"), Name = "unrelated-library-person" };
        var draft = Draft(First, Second, unrelated);
        draft.Deploy([Second.Id], 2, true);
        var session = GameSession.Start(draft.Source, 7, setup: draft.Setup);
        var snapshot = session.Capture();
        Assert.Equal(2, snapshot.Definitions.Officers.Length);
        Assert.Equal(3, snapshot.World.Officers.Count);
        Assert.DoesNotContain(snapshot.Definitions.Officers, p => p.Name == unrelated.Name);
        Assert.Contains(snapshot.World.Officers, p => p.Id == 1001 && p.IsDormant);
        using var json = System.Text.Json.JsonDocument.Parse(SaveService.Encode(session));
        var saved = json.RootElement.GetProperty("session");
        Assert.False(saved.TryGetProperty("setup", out _));
        foreach (var person in saved.GetProperty("definitions").GetProperty("officers").EnumerateArray())
            Assert.Equal(new[] { "biography", "courtesyName", "flagGlyph", "id", "name", "portraitKey", "surname" },
                person.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain("unrelated-library-person", json.RootElement.GetRawText());
        Assert.DoesNotContain(First.Id.ToString(), json.RootElement.GetRawText());
    }

    [Fact]
    public void RestorationUsesTheCurrentWorldAndDoesNotReapplyStartupAssignmentsOrAbilities()
    {
        var draft = Draft(First, Second); draft.SetForces([new(First.Id, 1)]); draft.Deploy([Second.Id], 1, true);
        var original = GameSession.Start(draft.Source, 123, playerForceIds: [13], setup: draft.Setup).Capture();
        var changed = original with { World = original.World with
        {
            Officers = original.World.Officers.Select(p => p.Id == 1002 ? p with
            {
                OwnerId = 12, SettlementId = 2, BaseAbilities = new(133, 122, 111, 100), Affinity = 42,
                ForceLoyalties = p.ForceLoyalties.SetItem(12, 91)
            } : p).ToArray(),
            Corps = original.World.Corps.Select(c => c.OwnerId == 13 ? c with { Treasury = new(777, 888) } : c).ToArray()
        } };
        var restored = GameSession.Restore(Source(), changed);
        var savedAgain = SaveService.Decode(SaveService.Encode(restored), Source());
        StateAssert.Equal(changed, savedAgain.Capture());
        var child = savedAgain.View.Officers.Single(p => p.Id == 1002);
        Assert.Equal(12, child.OwnerId); Assert.Equal(2, child.SettlementId); Assert.Equal(133, child.Abilities.War);
        Assert.Equal(777u, savedAgain.Snapshot.Corps.Single(c => c.OwnerId == 13).Treasury.Money);
        var text = savedAgain.Definitions.AddText(Text());
        Assert.Equal("新乙", text.Resolve(child.NameKey));
    }

    [Fact]
    public void SavedDefinitionsCannotOverrideBasePeopleOrOmitLiveIdentities()
    {
        var draft = Draft(First); draft.Deploy([First.Id], 2, false);
        var save = SaveService.Capture(GameSession.Start(draft.Source, 7, setup: draft.Setup));
        var collision = save with { Session = save.Session with { Definitions = save.Session.Definitions with
            { Officers = [save.Session.Definitions.Officers[0] with { Id = 1000 }] } } };
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Load(collision, Source())).Failure);
        var missing = save with { Session = save.Session with { Definitions = SessionDefinitions.Empty } };
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Load(missing, Source())).Failure);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveService.Encode(GameSession.Start(draft.Source, 7, setup: draft.Setup)))!;
        json["session"]!["definitions"]!["officers"]![0]!["abilities"] = new System.Text.Json.Nodes.JsonObject();
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() =>
            SaveService.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), Source())).Failure);
    }
}
