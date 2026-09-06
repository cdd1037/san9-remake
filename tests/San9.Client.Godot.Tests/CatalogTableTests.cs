using San9.Application.Presentation;

namespace San9.Client.Godot.Tests;

public sealed class CatalogTableTests
{
    [Fact]
    public void AbilityColumnsFollowMeasuredDescriptorWidths()
    {
        Assert.Equal(13, OfficerCatalog.Pages.Count);
        var ability = OfficerCatalog.Pages[0];
        Assert.Equal(["武将", "统率", "武力", "智力", "政治", "学得", "健康", "宝物", "身分"], ability.Columns.Select(c => c.Title));
        Assert.Equal(520, ability.Columns.Sum(c => c.Width));
        Assert.Equal(72, ability.Columns[0].Width);
        Assert.All(ability.Columns.Skip(1), c => Assert.Equal(56, c.Width));
    }

    [Fact]
    public void StableSortKeepsUnavailableLastAndPreservesViewPosition()
    {
        CatalogRow Row(int id, CatalogCell value) => new(id, [value]);
        var model = new CatalogTableState();
        model.SetRows([Row(8, 9), Row(3, 100), Row(7, 9), Row(1, CatalogCell.Unavailable)], 3);
        model.Sort(0);
        Assert.Equal([8, 7, 3, 1], model.Rows.Select(r => r.Id));
        Assert.Equal(1, model.CurrentIndex);
        model.Sort(0);
        Assert.Equal([3, 8, 7, 1], model.Rows.Select(r => r.Id));
        Assert.Equal(1, model.CurrentIndex);
    }

    [Fact]
    public void NavigationClampsScrollAndEmptyDataHasNoCurrentRow()
    {
        var model = new CatalogTableState();
        model.SetRows(Enumerable.Range(0, 40).Select(i => new CatalogRow(i, [i])).ToArray());
        model.MoveTo(15); Assert.Equal(1, model.Offset); Assert.Equal(15, model.Current!.Id);
        model.MoveTo(100); Assert.Equal(25, model.Offset); Assert.Equal(39, model.Current!.Id);
        model.Scroll(-1); Assert.Equal(0, model.Offset); Assert.Equal(39, model.Current!.Id);
        model.MoveTo(0); Assert.Equal(0, model.Offset);
        model.SetRows([]); model.MoveTo(5); Assert.Null(model.Current); Assert.Equal(0, model.Offset);
    }
}
