using San9.Application.Presentation;

namespace San9.Application.Tests;

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
        Assert.Equal([3, 8, 7, 1], model.Rows.Select(r => r.Id));
        Assert.Equal(1, model.CurrentIndex);
        model.Sort(0);
        Assert.Equal([8, 7, 3, 1], model.Rows.Select(r => r.Id));
        Assert.Equal(1, model.CurrentIndex);
    }

    [Fact]
    public void ChangingColumnsPreservesSortedOrderSelectionAndViewport()
    {
        var model = new CatalogTableState();
        var firstPage = Enumerable.Range(0, 20).Select(id => new CatalogRow(id, [id])).ToArray();
        model.SetRows(firstPage);
        model.Sort(0);
        model.Scroll(4);
        model.PointAt(8);
        int[] expected = Enumerable.Range(0, 20).Reverse().ToArray();
        var nextPage = firstPage.Select(row => new CatalogRow(row.Id, [$"next-{row.Id}", 1])).ToArray();

        model.RefreshRows(nextPage, columnsChanged: true);

        Assert.Equal(expected, model.Rows.Select(row => row.Id));
        Assert.Equal("next-11", model.Current!.Cells[0].Text);
        Assert.Equal(8, model.CurrentIndex);
        Assert.Equal(4, model.Offset);
        Assert.Equal(-1, model.SortColumn);
        model.Sort(1); // Equal values keep the ordering carried from the previous page.
        Assert.Equal(expected, model.Rows.Select(row => row.Id));
        model.RefreshRows(firstPage, columnsChanged: true);
        model.RefreshRows(firstPage); // Recreating the view after closing a detail must not reset order.
        Assert.Equal(expected, model.Rows.Select(row => row.Id));
        Assert.Equal(11, model.Current!.Id);
        Assert.Equal(4, model.Offset);
    }

    [Fact]
    public void RefreshKeepsStableSortTiesAndReconcilesChangedCandidates()
    {
        var model = new CatalogTableState();
        model.SetRows([new(1, [10, 1]), new(2, [20, 1]), new(3, [30, 2])]);
        model.Sort(0);
        model.Sort(1);
        Assert.Equal([3, 2, 1], model.Rows.Select(row => row.Id));
        model.RefreshRows([new(1, [11, 1]), new(2, [21, 1]), new(3, [31, 2])]);
        Assert.Equal([3, 2, 1], model.Rows.Select(row => row.Id));
        Assert.Equal(1, model.SortColumn);
        model.RefreshRows([new(1, [12, 1]), new(4, [40, 3]), new(3, [32, 2])]);
        Assert.Equal([3, 1, 4], model.Rows.Select(row => row.Id));
        Assert.Equal("32", model.Rows[0].Cells[0].Text);
        model.RefreshRows([]);
        Assert.Null(model.Current);
        Assert.Equal(0, model.Offset);
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
