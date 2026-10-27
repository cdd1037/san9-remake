using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class NewGameIntroductionTests
{
    [Theory]
    [InlineData(100, 5, 0, 60, 3)]
    [InlineData(70, 0, 30, 100, 2)]
    public void WithoutAnAdviserOpeningSelectsTheHighestCurrentIntelligence(int intelligence, int injury, int bonus, int current, int expected)
    {
        var definition = G01ScenarioTests.Fixture();
        definition = definition with
        {
            Officers = definition.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with
                { BaseAbilities = p.State.BaseAbilities with { Intelligence = intelligence }, Injury = injury, OfficeId = 7 } } : p).ToArray(),
            Information = definition.Information with { Offices = [new OfficeDefinition(7, "office", 10000, 0) { AbilityBonuses = new(0, bonus, 0, 0) }] }
        };
        var session = GameSession.Start(definition, 123);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = ProgressPresentationDefinition.IntroductionRoles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var content = new ProgressPresentationDefinition([], [])
        { Introduction = ProgressPresentationDefinition.IntroductionRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], "actor")) };
        var before = session.Snapshot;
        Assert.Equal(current, session.View.Officers.Single(p => p.Id == 2).Abilities.Intelligence);
        var messages = NewGameIntroduction.Produce(session.View, session.Messages(texts), texts, content);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(expected, m.SpeakerId));
        Assert.Same(before, session.Snapshot);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void OpeningSelectsItsSpeakerAndKeepsPresentationReadOnly(bool alone, bool adviser)
    {
        var definition = G01ScenarioTests.Fixture();
        definition = definition with
        {
            Forces = definition.Forces.Select(f => f.Id == 1 ? f with { AdviserId = adviser ? 2 : null } : f).ToArray(),
            Officers = definition.Officers.Where(p => !alone || p.State.OwnerId != 1 || p.State.Id == 1)
                .Select(p => adviser && p.State.Id == 2 ? p with { State = p.State with { Status = OfficerStatus.Strategist } } : p).ToArray()
        };
        var session = GameSession.Start(definition, 123);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = ProgressPresentationDefinition.IntroductionRoles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        var content = new ProgressPresentationDefinition([], [])
        { Introduction = ProgressPresentationDefinition.IntroductionRoles.ToDictionary(r => r, r => new DecisionMessageDefinition(r, [], "actor")) };
        var before = session.Snapshot;
        var messages = NewGameIntroduction.Produce(session.View, session.Messages(texts), texts, content);
        Assert.Equal(new[] { "opening", "enemies", "friends", "economy", "ambition" }, messages.Select(m => m.Message.Key));
        Assert.All(messages, m => Assert.Equal(alone ? 1 : adviser ? 2 : 3, m.SpeakerId));
        Assert.Same(before, session.Snapshot);
    }
}
