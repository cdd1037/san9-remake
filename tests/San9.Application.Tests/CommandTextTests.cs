using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CommandTextTests
{
    [Fact]
    public void CommandLabelsAndRejectionsUseAuthoredTextWithoutChangingCandidateOrder()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var draft = new MissionDraft(session, 1, "alienate");
        var texts = MissionScenario.Texts(draft.View);
        var before = session.Snapshot;
        int[] candidates = MissionOfficerCatalog.Read(draft, texts, 0, targets: true).Select(r => r.Id).ToArray();
        texts.Entries[CommandText.Key("title.alienate")] = "Custom command";
        texts.Entries[CommandText.Key("label.arrival")] = "Custom arrival";
        texts.Entries[CommandText.Key("failure.controller")] = "Custom authority explanation";
        var page = MissionOfficerCatalog.Pages(draft, texts, targets: true)[0];
        Assert.Equal("Custom command", page.Title);
        Assert.Equal("Custom arrival", page.Columns.Single(c => c.Id == "estimate").Title);
        Assert.Equal("Custom authority explanation", CommandFailureText.Describe(texts, CommandRejection.NotActiveController));
        Assert.Equal(candidates, MissionOfficerCatalog.Read(draft, texts, 0, targets: true).Select(r => r.Id));
        Assert.Same(before, session.Snapshot);
    }
}
