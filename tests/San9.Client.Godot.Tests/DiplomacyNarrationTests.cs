using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class DiplomacyNarrationTests
{
    [Fact]
    public void DisabledScenesKeepOrderAcknowledgementsRecommendationsAndResultsWithoutStartingAMeeting()
    {
        CommandMessage Message(string text) => new(new(text, new GameClock(190, 1, 1), text), null, null);
        var arrival = Message("arrival") with { DiplomacyDialogue = true };
        var advice = Message("advice") with { SpeakerId = 1, SoundRole = "advice" };
        var acknowledgement = Message("gift-start") with { SpeakerId = 2 };
        var report = Message("request-already-agreed-actor") with { SpeakerId = 2 };
        var speech = Message("farewell") with { SpeakerId = 2, DiplomacyDialogue = true,
            Diplomacy = new(Guid.NewGuid(), 1, 2), DiplomacyFarewell = true, BackgroundMusicRole = "diplomacy-music" };
        var result = Message("result") with { SoundRole = "result-sound" };
        var receptionAdvice = advice with { DiplomacyDialogue = true, Diplomacy = speech.Diplomacy,
            BackgroundMusicRole = "diplomacy-music", BackgroundMusicLoop = true };
        var termsAdvice = advice with { Message = new("terms-advice", new GameClock(190, 1, 1), "terms-advice") };
        var elimination = Message("elimination") with { IllustrationRole = "diplomacy-eliminated" };
        var other = Message("other") with { SpeakerId = 3 };
        CommandMessage[] sequence = [advice, acknowledgement, report, arrival, receptionAdvice, termsAdvice, speech, result, elimination, other];

        Assert.Same(sequence, DiplomacyNarration.Filter(sequence, enabled: true));
        var filtered = DiplomacyNarration.Filter(sequence, enabled: false);
        Assert.Equal(new[] { advice, acknowledgement, report, termsAdvice, result, elimination, other }, filtered);
        Assert.All(filtered, message => Assert.Null(message.Diplomacy));
        Assert.All(filtered, message => Assert.Null(message.BackgroundMusicRole));
        Assert.Empty(DiplomacyNarration.Filter([arrival, receptionAdvice, speech], enabled: false));
    }
}
