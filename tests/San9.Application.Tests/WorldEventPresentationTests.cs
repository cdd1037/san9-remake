using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class WorldEventPresentationTests
{
    private static (GameSession Session, TextCatalog Texts, CommandPresentation Content) Fixture()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var messages = EventPresentationDefinition.RequiredRoles.ToDictionary(role => role, role => new MessageDefinition([new MessageLiteral(role)]));
        var definitions = messages.Keys.ToDictionary(role => role, role => new DecisionMessageDefinition(role, [],
            role is "founder-declaration" or "successor-declaration" or "consolidation-offer" or "alliance-formed-declaration" ? "actor" :
            role is "consolidation-reply" or "visit-greeting" or "visit-departure" or "women-teaching" ? "other" : null,
            role is "force-founded" or "city-independent" ? "founded" : null,
            role is "ruler-death" or "officer-death" or "captive-death" or "free-officer-death" ? "actor" : null));
        return (session, MissionScenario.Texts(session.View) with { Messages = messages }, CommandPresentation.Empty with
        { Events = new(definitions) { People = new() { ["emperor-first"] = 1, ["emperor-second"] = 21, ["messenger"] = 3, ["recommender"] = 2 } } });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FoundingCapturesItsOccurrenceIdentityEvenWhenTheFounderLaterChangesAllegiance(bool independent)
    {
        var (session, texts, content) = Fixture(); var before = session.View; var clock = before.World.Clock.NextDay();
        var force = new ForceState(1000, 100) { LeaderId = 22, ColorCode = 7, Origin = new(independent ? ForceCreationKind.CityIndependence : ForceCreationKind.EmptyCityUprising, 2, 1, clock) };
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        { Forces = before.World.Forces.Append(force).ToImmutableArray(), Officers = before.World.Officers.Select(p => p.Id == 2 ? p with { OwnerId = 3, Status = OfficerStatus.Serving } : p).ToImmutableArray() });
        string resultRole = independent ? "city-independent" : "force-founded";
        texts.Messages[resultRole] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        content.Events!.Messages[resultRole] = content.Events.Messages[resultRole] with { Bindings = new() { ["person.0"] = "actor" } };
        DomainEvent fact = independent ? new CityIndependenceEvent(clock, 1, 1, 1000, 2, [2]) : new ForceFoundedEvent(clock, 1000, 1, 2, [2]);
        var output = ExecutionPublication.Read(before, after, session.Messages(texts, before.World), texts, content, [fact]);
        Assert.Equal(new[] { "founder-declaration", resultRole }, output.Messages.Select(m => m.Message.Key));
        var declaration = output.Messages[0];
        Assert.True(declaration.WaitForMapSpeech); Assert.Equal(before.PlaceCell(1), declaration.FocusLocation);
        Assert.Equal(before.PlaceCell(1), declaration.MapSpeech!.Location);
        Assert.Equal("actor", declaration.Speaker!.ForceName);
        Assert.Equal("actor", output.Messages[1].Message.BbCode); Assert.Equal("founded", output.Messages[1].SoundRole);
        Assert.Equal(clock, Assert.Single(output.Records).Message.Clock);
        StateAssert.Equal(before.World, session.Snapshot);
    }

    [Fact]
    public void ConsolidationRetainsBothOriginalAffiliationsAndMessages()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        var after = new StrategySnapshot(MissionScenario.Create(), before.World with
        {
            Officers = before.World.Officers
            .Select(p => p.OwnerId == 3 ? p with { OwnerId = 2 } : p).ToImmutableArray(),
            Forces = before.World.Forces
            .Select(f => f.Id == 3 ? f with { IsEliminated = true, LeaderId = null } : f).ToImmutableArray()
        });
        DomainEvent[] facts = [new ForcesConsolidatedEvent(before.World.Clock, 2, 3, 10, 10, 20, 20)];
        var first = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, facts);
        var speeches = first.Messages.Where(m => m.WaitForMapSpeech).ToArray();
        Assert.Equal(new[] { "enemy", "third" }, speeches.Select(m => m.Speaker!.ForceName));
        var repeated = ExecutionPublication.Read(before, after, session.Messages(texts), texts, content, facts);
        Assert.Equal(first.Messages, repeated.Messages); Assert.Equal(first.Records, repeated.Records);
        Assert.Single(first.Records);
    }

    [Fact]
    public void ResultRecordRetainsItsRoleWhenDialogueSharesTheSameTemplate()
    {
        var (session, texts, content) = Fixture();
        var before = session.View;
        texts.Messages["shared"] = new([new MessageValue(new("property", "person.name",
            Arguments: [new("binding", "person.0")]))]);
        content.Events!.Messages["consolidation-offer"] = new("shared", new() { ["person.0"] = "actor" }, "actor");
        content.Events.Messages["consolidation-result"] = new("shared", new() { ["person.0"] = "other" });
        DomainEvent[] facts = [new ForcesConsolidatedEvent(before.World.Clock, 2, 3, 10, 10, 20, 20)];

        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);

        Assert.NotEqual(output.Messages[1].Message.BbCode, output.Messages[3].Message.BbCode);
        Assert.Same(output.Messages[3].Message, Assert.Single(output.Records).Message);
    }

    [Fact]
    public void ExecutionNarratesBeforeDeathAndSuccessionAndPreservesThePrisonersIdentity()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        foreach (string role in new[] { "execution-start", "execution-last-words", "execution-result" }) texts.Messages[role] = new([new MessageLiteral(role)]);
        content = content with { Execution = new(3, "execution-start", "execution-last-words", "execution-result", "execute") };
        DomainEvent[] facts = [new PrisonerExecutedEvent(before.World.Clock,1,21,new(2,true,1,PrisonerExecutionPath.CapturedRulerDisposition)),
            new OfficerDiedEvent(before.World.Clock,21,OfficerDeathCause.Execution,OfficerStatus.Ruler,2,2,null,null),
            new ForceLeaderChangedEvent(before.World.Clock,2,21,22),
            new ForceEliminatedEvent(before.World.Clock,3,false)];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(new[] { "execution-start", "execution-last-words", "execution-result", "ruler-death", "successor-declaration", "force-eliminated" },
            output.Messages.Select(m => m.Message.Key));
        Assert.Equal("enemy", output.Messages[1].Speaker!.ForceName);
        Assert.Equal(21, output.Messages[3].IllustrationPersonId); Assert.True(output.Messages[3].IllustrationDeceased);
        Assert.Equal("force-eliminated-music", output.Messages[^1].BackgroundMusicRole);
        Assert.False(output.Messages[^1].BackgroundMusicLoop);
        Assert.Empty(ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts, presentedFacts: new HashSet<int> { 0, 1, 2, 3 }).Messages);
    }

    [Fact]
    public void CoupOwnsItsVictimsAndSuccessionInsteadOfOpeningTheirScenesTwice()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        DomainEvent[] facts = [new OfficerDiedEvent(before.World.Clock,1,OfficerDeathCause.HistoricalEvent,OfficerStatus.Ruler,1,1,null,null),
            new ForceLeaderChangedEvent(before.World.Clock,1,1,2),new ChainedPlotEvent(before.World.Clock,ChainedPlotStep.Coup,1,1,2,[1])];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(new[] { "chained-coup", "ruler-death", "chained-result", "successor-declaration" }, output.Messages.Select(m => m.Message.Key));
        Assert.Equal("chained-coup", output.Messages[0].MovieRole);
        Assert.Single(output.Records);
    }

    [Fact]
    public void AnEliminationAfterSuccessionUsesTheLeaderAtThatOccurrence()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        texts.Messages["force-eliminated"] = new([new MessageValue(new("property", "force.leader-name", Arguments: [new("binding", "force.0")]))]);
        content.Events!.Messages["force-eliminated"] = content.Events.Messages["force-eliminated"] with { Bindings = new() { ["force.0"] = "force" } };
        DomainEvent[] facts = [new ForceLeaderChangedEvent(before.World.Clock, 1, 1, 2), new ForceEliminatedEvent(before.World.Clock, 1, true)];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal("actor", output.Messages[^1].Message.BbCode);
        Assert.Equal("actor", Assert.Single(ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts,
            presentedFacts: new HashSet<int> { 0 }).Messages).Message.BbCode);
        Assert.Equal(1, before.World.Forces.Single(f => f.Id == 1).LeaderId);
    }

    [Fact]
    public void ExplorationUsesUnbuiltRegionGeographyAndKeepsTheVisitorAsSpeaker()
    {
        var definition = MissionScenario.Create() with
        {
            UnbuiltPlaces = [new(99, new(60, 20))],
            RegionNameKeys = new(MissionScenario.Create().RegionNameKeys) { [99] = "home" },
            RegionCityIds = new(MissionScenario.Create().RegionCityIds) { [99] = 1 }
        };
        var session = GameSession.Start(definition, 42); var (_, texts, content) = Fixture(); var before = session.View;
        DomainEvent[] facts = [new WomenVisitResolvedEvent(before.World.Clock, "women", 1, 2, 22, 99, 0)];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        var speeches = output.Messages.Where(m => m.MapSpeech is not null).ToArray();
        Assert.NotEmpty(speeches);
        Assert.All(speeches, m =>
        { Assert.Equal(22, m.SpeakerId); Assert.Equal(MapCoordinates.Cell(new(60, 20)), m.MapSpeech!.Location); });
    }

    [Fact]
    public void AllianceDecisionOwnsItsResultWhileAnAutomaticFormationStillHasACompleteScene()
    {
        var (session, texts, content) = Fixture(); var before = session.View;
        var pending = new StrategySnapshot(MissionScenario.Create(), before.World with
        {
            Simulation = before.World.Simulation with
            { PendingEvent = new AllianceEventSession(new(3, [1, 2], [new(1, null)])) }
        });
        DomainEvent[] facts = [new AllianceFormedEvent(before.World.Clock, new(3, 2, [1, 2], 12))];
        Assert.Empty(ExecutionPublication.Read(pending, before, session.Messages(texts), texts, content, facts).Messages);
        var automatic = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Contains(automatic.Messages, m => m.WaitForMapSpeech);
        Assert.Same(automatic.Messages.Last().Message, Assert.Single(automatic.Records).Message);
    }

    [Fact]
    public void AllianceEndingUsesTheLeaderOfAnAllianceFormedInTheSamePublication()
    {
        var (session, texts, content) = Fixture();
        var before = session.View;
        texts.Messages["alliance-leader-died"] = new([new MessageValue(new("property", "force.leader-name",
            Arguments: [new("binding", "force.0")]))]);
        content.Events!.Messages["alliance-leader-died"] = new("alliance-leader-died", new() { ["force.0"] = "leader-force" });
        DomainEvent[] facts = [new AllianceFormedEvent(before.World.Clock, new(3, 2, [1, 2], 12)),
            new AllianceEndedEvent(before.World.Clock, new(3, 2, [1, 2], 12), AllianceEndReason.LeaderDied, null)];

        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);

        Assert.Equal("enemy-ruler", output.Messages.Last().Message.BbCode);
    }

    [Fact]
    public void ConsecutiveBetrayalsUseMembershipBeforeEachDeparture()
    {
        var (session, texts, content) = Fixture();
        var before = new StrategySnapshot(MissionScenario.Create(), session.Snapshot with
        {
            Simulation = session.Snapshot.Simulation with { Alliance = new(4, 1, [1, 2, 3], 12) }
        });
        DomainEvent[] facts = [new AllianceMemberRemovedEvent(before.World.Clock, 2, new(4, 1, [1, 2, 3], 12), AllianceRemovalReason.Betrayal),
            new WarDeclaredEvent(before.World.Clock, 2, 1, true),
            new AllianceEndedEvent(before.World.Clock, new(4, 1, [1, 3], 12), AllianceEndReason.Betrayal, 3),
            new WarDeclaredEvent(before.World.Clock, 3, 1, true)];

        var output = ExecutionPublication.Read(before, before, session.Messages(texts, before.World), texts, content, facts);

        Assert.Equal(new[] { "alliance-member-left", "alliance-member-ended" }, output.Messages
            .Select(m => m.Message.Key).Where(key => key.StartsWith("alliance-member-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AuthoredEventBindingsEvaluateAgainstTheOptionalRealClientPackage()
    {
        if (Environment.GetEnvironmentVariable("SAN9_PRESENTATION_CONTENT") is not { Length: > 0 } root) return;
        var package = await ClientContentFiles.LoadAsync(root);
        var session = GameSession.Start(package.Scenario, 42); var before = session.View; var clock = before.World.Clock;
        int force = before.PlayerForceId, ruler = before.World.Forces.Single(f => f.Id == force).LeaderId!.Value;
        int otherForce = before.World.Forces.First(f => f.Id != force && !f.IsDormant && f.LeaderId is not null).Id;
        int other = before.World.Forces.Single(f => f.Id == otherForce).LeaderId!.Value;
        int actor = before.World.Officers.First(p => p.OwnerId == force && p.IsServing && p.Id != ruler).Id;
        int place = before.World.Settlements.First(p => p.OwnerId == force && !p.IsDormant).Id;
        int rank = before.Information.Ranks.First().Id;
        var content = package.Presentation.Events!;
        DomainEvent[] facts = [new ForcesConsolidatedEvent(clock,force,otherForce,0,0,0,0),new ForceEliminatedEvent(clock,otherForce,false),
            new ForceLeaderChangedEvent(clock,force,ruler,actor),new OfficerDiedEvent(clock,actor,OfficerDeathCause.Natural,OfficerStatus.Serving,force,place,null,null),
            new EmperorDeathEvent(clock,content.People["emperor-first"],null,null),
            new RedCliffsEvent(clock,force,otherForce,[],[],0,10,10),new GuanduEvent(clock,force,otherForce,actor,other,[],[],0,0,10,10),
            new QiaoMarriageEvent(clock,force,ruler,actor,1,1,1,otherForce),new SunMarriageEvent(clock,actor,otherForce,force,place,null,true),
            new FiveTigersRecognizedEvent(clock,force,[actor],10),new ExpeditionMemorialEvent(clock,force,actor,[actor],10,true),
            new ForceActivatedBySearchEvent(clock,"activation",force,actor,otherForce,place,[],[]),
            new ImperialCeremonyResolvedEvent(clock,"ceremony",force,ruler,place,[actor]),new OfficerAbilityGrownEvent(clock,ruler,"leadership",90,95),
            new SageVisitResolvedEvent(clock,"sage",force,actor,other,place,[]),new DivinerVisitResolvedEvent(clock,"diviner",force,actor,other,place),
            new PhysicianVisitResolvedEvent(clock,"physician",force,actor,other,place,before.Information.Treasures.First().Id),
            new WomenVisitResolvedEvent(clock,"women",force,actor,other,place,before.Information.Tactics.First().Id),
            new SearchHealingResolvedEvent(clock,"healing",actor,place,other,[]),new TacticLearnedEvent(clock,actor,before.Information.Tactics.First().Id),
            .. Enum.GetValues<ChainedPlotStep>().Select(step=>(DomainEvent)new ChainedPlotEvent(clock,step,force,ruler,actor,[])),
            .. Enum.GetValues<ThreeVisitsStep>().Select(step=>(DomainEvent)new ThreeVisitsEvent(clock,step,ruler,actor,place)),
            .. Enum.GetValues<PeerageChangeKind>().Where(k=>k!=PeerageChangeKind.ShanyangGong).Select(kind=>(DomainEvent)new PeerageChangedEvent(clock,force,null,rank,kind,null)),
            new AllianceFormedEvent(clock,new(otherForce,force,[force],12)),new AllianceFormationFailedEvent(clock,otherForce,AllianceFormationFailure.InsufficientMembers),
            new AllianceVictoryEvent(clock,otherForce,force,AllianceVictoryCause.ForceDefeated,15),
            .. Enum.GetValues<AllianceEndReason>().Select(reason=>(DomainEvent)new AllianceEndedEvent(clock,new(otherForce,force,[force],12),reason,reason == AllianceEndReason.Betrayal ? force : null)),
            new EmperorTransferResolvedEvent(clock,force,before.EmperorId!.Value,place,EmperorTransferChoice.Support,true,false),
            new EmperorTransferResolvedEvent(clock,force,before.EmperorId!.Value,place,EmperorTransferChoice.Depose,true,false)];
        var occurrence = new StrategySnapshot(package.Scenario, before.World with
        {
            Simulation = before.World.Simulation with { Alliance = new(otherForce, force, [force], 12) }
        });
        var rewards = facts.Where(f => f is OfficerAbilityGrownEvent or TacticLearnedEvent).ToArray();
        foreach (var fact in facts)
        {
            // These are independent binding samples, not a valid chronological domain event stream.
            var output = WorldEventPresentation.Read(occurrence, occurrence, session.Messages(package.Text, occurrence.World),
                package.Text, content, [fact, .. rewards], 0);
            Assert.All(output.Messages, m => Assert.False(string.IsNullOrWhiteSpace(m.Message.BbCode)));
            if (fact is PeerageChangedEvent { Kind: PeerageChangeKind.Investiture or PeerageChangeKind.Abdication })
            {
                var music = output.Messages.Where(m => m.BackgroundMusicRole is not null).ToArray();
                Assert.NotEmpty(music);
                Assert.All(music, m =>
                {
                    Assert.True(m.BackgroundMusicLoop);
                    Assert.Equal(package.Presentation.Sounds["diplomacy-music"], package.Presentation.Sounds[m.BackgroundMusicRole!]);
                });
            }
            foreach (var item in ChronicleHistory.Read(occurrence, occurrence, [fact]))
                Assert.False(string.IsNullOrWhiteSpace(ChroniclePresentation.Render(item,
                    session.Messages(package.Text, occurrence.World), occurrence, package.Text, package.Presentation.Chronicle!).BbCode));
        }
        int enemyPlace = before.World.Settlements.First(p => p.OwnerId == otherForce && p.Kind == SettlementKind.City).Id;
        int thirdForce = before.World.Forces.First(f => f.Id != force && f.Id != otherForce && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null).Id;
        var source = new BattleObjectContext(0, false, force, actor, null, place, false);
        var request = new AttackRequestState(otherForce, force, enemyPlace, thirdForce, 3);
        DomainEvent[] journalSamples = [new AdviserAppointedEvent(clock, force, null, actor),
            new SettlementCapturedEvent(clock, enemyPlace, otherForce, new(0, false), false) { Source = source },
            new SettlementTransferredEvent(clock, enemyPlace, otherForce, force), new ForceSurrenderedEvent(clock, otherForce, force, 0, 0, 0, 0, [], []),
            new SettlementDefectedEvent(clock, enemyPlace, otherForce, force, SettlementDefectionKind.Abandoned, other, []),
            new PersonnelTerritoryTransferredEvent(clock, enemyPlace, otherForce, force, other),
            new PrisonerExecutedEvent(clock, force, other, new(otherForce, false, ruler, PrisonerExecutionPath.StrategicCommand)),
            new PrisonerExecutedEvent(clock, force, other, new(otherForce, true, ruler, PrisonerExecutionPath.CapturedRulerDisposition)),
            new ForceFoundedEvent(clock, force, place, ruler, []), new CityIndependenceEvent(clock, place, otherForce, force, ruler, []),
            new AttackRequestEstablishedEvent(clock, request),
            .. Enum.GetValues<AttackRequestEndReason>().Select(reason => (DomainEvent)new AttackRequestEndedEvent(clock, request, reason, null)),
            new ImperialCeremonyResolvedEvent(clock, "test", force, ruler, place, []),
            new ChainedPlotEvent(clock, ChainedPlotStep.Prelude, force, ruler, actor, []),
            new ChainedPlotEvent(clock, ChainedPlotStep.Coup, force, ruler, actor, []),
            .. new[] { ThreeVisitsStep.Recommendation, ThreeVisitsStep.FirstVisit, ThreeVisitsStep.Recruitment }.Select(step => (DomainEvent)new ThreeVisitsEvent(clock, step, ruler, actor, place))];
        foreach (var fact in journalSamples)
        {
            foreach (var item in ChronicleHistory.Read(before, before, [fact]))
                Assert.NotEmpty(ChroniclePresentation.Render(item, session.Messages(package.Text), before, package.Text, package.Presentation.Chronicle!).BbCode);
        }
        if (before.Settlements.FirstOrDefault(p => p.OwnerId == force && p.FacilityTypeId is not null && p.Kind == SettlementKind.Facility) is { } facility)
        {
            foreach (DomainEvent fact in new DomainEvent[] { new FacilityDemolishedEvent(clock, facility.Id, place, force, facility.FacilityTypeId!.Value), new FacilityConstructionChangedEvent(clock, facility.Id, 0, "completed", 1, new(force, facility.FacilityTypeId!.Value, place, new(facility.X, facility.Y))) })
            foreach (var item in ChronicleHistory.Read(before, before, [fact]))
                Assert.NotEmpty(ChroniclePresentation.Render(item, session.Messages(package.Text), before, package.Text, package.Presentation.Chronicle!).BbCode);
        }
        // Detailed reports are real producer output, independent of player-only battle scenes.
        var npcSource = new BattleObjectContext(100003, false, otherForce, other, new(12, 7), enemyPlace, false);
        var npcTarget = new BattleObjectContext(100004, false, thirdForce,
            before.World.Forces.Single(f => f.Id == thirdForce).LeaderId, new(17, 7), enemyPlace, false);
        DomainEvent[] detailedFacts = [
            new SettlementCapturedEvent(clock, enemyPlace, otherForce, new(npcTarget.Id, false), false) { Source = npcTarget },
            new FacilityDestroyedEvent(clock, enemyPlace, new(npcTarget.Id, false)) { Source = npcTarget },
            new UnitTerminatedEvent(clock, npcSource.Id, new(npcTarget.Id, false), 0) { Target = npcSource, Source = npcTarget }];
        var reports = ProgressMessages.Read(before, before, session.Messages(package.Text), package.Text, package.Presentation.Progress!, detailedFacts);
        Assert.Equal(new[] { "messages.10120", "messages.9916", "messages.9994" }, reports.Select(r => r.Message.Key));
        Assert.All(reports, r => { Assert.Equal(MapReportImportance.Detailed, r.Importance); Assert.Null(r.ForceColorCode); });
        int tacticId = package.Presentation.Battle!.Tactics.First(p => !p.Value.Support).Key;
        var activation = new TacticActivatedEvent(clock, npcSource.Id, false, npcTarget.Id, false, tacticId, [other], false, 10)
            { Occurrence = new(npcSource, npcTarget, 0, 0, UnitCondition.Normal, null, null, null, false) };
        var battle = BattlePresentationPlan.Create(before, before, [activation], session.Messages(package.Text), package.Text, package.Presentation);
        Assert.Equal("messages.9972", Assert.Single(battle.Records).Record.Message.Key);
        Assert.Equal(MapReportImportance.Detailed, battle.Records[0].Record.Importance);
        Assert.Empty(battle.Messages); Assert.Empty(battle.Visuals);
        var chronicle = ChroniclePresentation.Read(session, package.Text, package.Presentation.Chronicle!);
        Assert.NotEmpty(chronicle.Lines);
        foreach (string role in ChroniclePresentationDefinition.HintRoles)
            Assert.NotEmpty(ChroniclePresentation.Hint(session, package.Text, package.Presentation.Chronicle!, role));
        StateAssert.Equal(before.World, session.Snapshot);
    }

}
