using San9.Content.Interface;

namespace San9.Client.Godot.Tests;

public sealed class ScenarioSelectionStateTests
{
    [Fact]
    public void CategoryChangesResetPreviewAndRejectForeignEntriesWithoutLimitingCatalogSize()
    {
        var entries = Enumerable.Range(0, 13).Select(i => new ScenarioPreviewEntry($"a-{i}", "a", 184 + i, 1, "标题", "简介", []))
            .Append(new("b-0", "b", 281, 1, "英雄", "简介", [])).ToArray();
        var scene = new ScenarioSelectionPresentation("选择", "提示", "返回", [], null!, null!, null!, null!, "", null!, null!, [], [], [],
            [new("a", "一般", "提示", new(0, 0)), new("b", "if", "提示", new(0, 0))], entries);
        var state = new ScenarioSelectionState(scene);
        Assert.Equal(13, state.Entries.Count);
        Assert.True(state.Preview("a-12"));
        Assert.False(state.Preview("b-0"));
        Assert.Equal("a-12", state.Selected.Id);
        Assert.True(state.SelectCategory("b"));
        Assert.Equal("b-0", state.Selected.Id);
        Assert.False(state.SelectCategory("missing"));
        Assert.True(state.SelectCategory("a"));
        Assert.Equal("a-0", state.Selected.Id);
        Assert.Equal("a-12", entries[12].Id);
    }
}
