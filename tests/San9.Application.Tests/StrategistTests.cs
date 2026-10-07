using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class StrategistTests
{
    private static ScenarioInitialState Fixture(int intelligence, bool incumbent)
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with {
            Forces = basis.Forces.Select(f => f.Id == 1 ? f with { AdviserId = incumbent ? 3 : null } : f).ToArray(),
            Officers = basis.Officers.Select(p => p with { State = p.State.Id switch {
                2 => p.State with { BaseAbilities = p.State.BaseAbilities with { Intelligence = intelligence }, Acted = true },
                3 => p.State with { BaseAbilities = p.State.BaseAbilities with { Intelligence = 90 }, Status = incumbent ? OfficerStatus.Strategist : OfficerStatus.Serving },
                _ => p.State } }).ToArray()
        };
    }

    [Theory]
    [InlineData(89, true, false)]
    [InlineData(90, true, true)]
    [InlineData(91, true, true)]
    [InlineData(91, false, true)]
    public void DefaultUsesEffectiveIntelligenceButLowerCandidateRemainsSelectable(int intelligence, bool incumbent, bool selected)
    {
        var session = GameSession.Start(Fixture(intelligence, incumbent), 190);
        string hash = session.Snapshot.StateHash;
        var draft = new AppointmentDraft(session, 1, "strategist");
        Assert.Equal(selected, draft.CanExecute);
        if (selected) Assert.Equal(new[] { 2 }, draft.Members.ToArray());
        else Assert.Empty(draft.Members);
        Assert.Contains(2, draft.Candidates); // Already acted is not an appointment exclusion.
        if (incumbent) Assert.DoesNotContain(3, draft.Candidates);
        Assert.False(draft.SetMembers([1]));
        Assert.True(draft.SetMembers([2]));
        Assert.True(draft.CanExecute);
        Assert.Equal(hash, session.Snapshot.StateHash);
        Assert.Equal(CommandRejection.None, draft.Submit().Rejection);
        Assert.Equal(2, session.Snapshot.Forces.Single(f => f.Id == 1).AdviserId);
    }

    [Theory]
    [InlineData(89, true, "other")]
    [InlineData(90, true, "other")]
    [InlineData(91, true, "superior")]
    [InlineData(91, false, null)]
    public void NarrationUsesCommittedAppointmentAndCorrectSpeakersWithoutChangingWorld(int intelligence, bool incumbent, string? reply)
    {
        var definition = Fixture(intelligence, incumbent);
        var session = GameSession.Start(definition, 190);
        var draft = new AppointmentDraft(session, 1, "strategist");
        Assert.True(draft.SetMembers([2]));
        var result = Assert.Single(draft.Submit().Events.OfType<AdviserAppointedEvent>());
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.ToDictionary(p => p.NameKey, p => p.NameKey));
        MessageValue Name(string slot) => new(new("property", "person.name", Arguments: [new("binding", slot)]));
        texts.Messages["appointed"] = new([Name("person.0"), new MessageLiteral("/"), Name("person.1")]);
        foreach (string key in new[] { "superior", "other" })
            texts.Messages[key] = new([Name("person.0"), new MessageLiteral("/"), Name("person.1"), new MessageLiteral("/"), Name("person.2")]);
        texts.Messages["result"] = new([Name("person.0")]);
        var content = new StrategistPresentationDefinition("appointed", "superior", "other", "result");
        string hash = session.Snapshot.StateHash;
        var messages = StrategistPresentation.Create(draft.View, session.Messages(texts), content, result, _ => throw new InvalidOperationException());
        Assert.Equal(incumbent ? 3 : 2, messages.Count);
        Assert.Equal(2, messages[0].SpeakerId);
        Assert.Equal("officer.2/officer.1", messages[0].Message.BbCode);
        if (incumbent)
        {
            Assert.Equal(reply, messages[1].Message.Key);
            Assert.Equal(3, messages[1].SpeakerId);
            Assert.Equal("officer.3/officer.1/officer.2", messages[1].Message.BbCode);
        }
        Assert.Null(messages[^1].SpeakerId);
        Assert.Equal("result", messages[^1].Message.Key);
        Assert.All(messages, message => Assert.Null(message.SoundRole));
        Assert.Equal(hash, session.Snapshot.StateHash);
    }
}
