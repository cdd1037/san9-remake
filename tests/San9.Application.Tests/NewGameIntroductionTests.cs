using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class NewGameIntroductionTests
{
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
