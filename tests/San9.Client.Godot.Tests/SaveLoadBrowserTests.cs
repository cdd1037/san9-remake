using San9.Client.Godot.Persistence;
using San9.Application.Persistence;

namespace San9.Client.Godot.Tests;

public sealed class SaveLoadBrowserTests
{
    private static readonly SaveSlotSummary Saved = new("君主", null, "190年1月", "剧本", "190年4月上旬",
        DateTimeOffset.UnixEpoch, 1, true, "初级", "原备注");

    [Fact]
    public void LoadRejectsEmptyAndOffPageSlotsAndKeepsCatalogAfterConfirmation()
    {
        var browser = new SaveLoadBrowser();
        browser.SetEntries(new Dictionary<int, SaveSlotListing> { [24] = new(Saved, null) });
        browser.Open(SaveLoadMode.Load);
        Assert.False(browser.Select(24));
        Assert.True(browser.ChangePage(4));
        Assert.False(browser.Select(23));
        Assert.True(browser.Select(24));
        Assert.False(browser.ChangePage(0));
        Assert.False(browser.Select(25));
        Assert.Equal(new(SaveLoadMode.Load, 24, "原备注"), browser.Confirm("不能修改存档备注"));
        Assert.Same(Saved, browser.Entry(24));
        Assert.Null(browser.Confirm("重复确认"));
    }

    [Fact]
    public void CancellingSaveDiscardsSelectionAndSwitchingModeRetainsPage()
    {
        var browser = new SaveLoadBrowser();
        browser.Open(SaveLoadMode.Save); browser.ChangePage(4);
        Assert.True(browser.Select(23));
        browser.CancelSelection();
        Assert.Null(browser.Confirm("已取消"));
        Assert.Null(browser.Entry(23));
        Assert.True(browser.Select(23));
        Assert.Equal(new(SaveLoadMode.Save, 23, "备注不限原版字节长度：" + new string('字', 100)),
            browser.Confirm("备注不限原版字节长度：" + new string('字', 100)));
        Assert.Null(browser.Entry(23));
        browser.Close();
        Assert.False(browser.Select(23));
        browser.Open(SaveLoadMode.Load);
        Assert.Equal(4, browser.Page);
        Assert.False(browser.Select(23));
    }

    [Fact]
    public void CatalogReplacementInvalidatesPendingSelectionAndCapacityCanGrow()
    {
        var browser = new SaveLoadBrowser(43);
        browser.Open(SaveLoadMode.Save); browser.ChangePage(8);
        Assert.False(browser.Select(44));
        Assert.True(browser.Select(43));
        browser.SetEntries(new Dictionary<int, SaveSlotListing> { [43] = new(Saved, null) });
        Assert.Null(browser.Confirm("过期请求"));
        browser.Open(SaveLoadMode.Load);
        Assert.True(browser.Select(43));
        Assert.Equal(43, browser.Confirm("")!.Slot);
        Assert.Throws<ArgumentOutOfRangeException>(() => browser.SetEntries(new Dictionary<int, SaveSlotListing> { [44] = new(Saved, null) }));
        Assert.Same(Saved, browser.Entry(43));
    }

    [Fact]
    public void UnreadableSlotsRemainSelectableForLoadErrorsAndSaveReplacement()
    {
        var browser = new SaveLoadBrowser();
        var catalog = new Dictionary<int, SaveSlotListing>
        { [1] = new(null, new SaveException(SaveFailure.InvalidFormat, "Corrupt slot")) };
        browser.SetEntries(catalog);
        catalog.Clear();
        Assert.True(browser.Occupied(1));
        Assert.NotNull(browser.Error(1));
        browser.Open(SaveLoadMode.Load);
        Assert.True(browser.Select(1));
        Assert.Equal(new(SaveLoadMode.Load, 1, ""), browser.Confirm(""));
        browser.Open(SaveLoadMode.Save);
        Assert.True(browser.Select(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => browser.SetEntries(
            new Dictionary<int, SaveSlotListing> { [41] = new(null, new SaveException(SaveFailure.IoFailure, "Unavailable")) }));
        Assert.Equal(new(SaveLoadMode.Save, 1, "替换"), browser.Confirm("替换"));
    }
}
