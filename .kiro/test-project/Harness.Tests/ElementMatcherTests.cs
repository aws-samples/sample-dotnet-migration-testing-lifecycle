using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>Deterministic Reference/Target element pairing (css-map pairs, role + name + order + geometry score).</summary>
[TestFixture]
public class ElementMatcherTests
{
    internal static InventoryElement El(int index, string role, string name, double x = 0, double y = 0, string container = "c",
        int order = 0, bool interactive = false, string? map = null, int? row = null, int? col = null, double w = 80, double h = 20) => new()
    {
        Index = index, Role = role, Name = name, Tag = role, Box = new InventoryBox { X = x, Y = y, W = w, H = h },
        DomPath = $"html > body > e{index}", Container = container, Order = order, Interactive = interactive, MapName = map,
        RowIndex = row, ColIndex = col
    };

    [Test]
    public void MapPair_WinsOverBetterAutoScore()
    {
        var r = new List<InventoryElement> { El(0, "button", "Search", map: "Search button") };
        var t = new List<InventoryElement>
        {
            El(0, "button", "Search"),                                 // perfect auto score
            El(1, "button", "Find", x: 300, map: "Search button")     // explicit css-map pair
        };
        var m = ElementMatcher.Match(r, t);
        Assert.That(m.Pairs.Single().How, Is.EqualTo("map"));
        Assert.That(m.Pairs.Single().Target.Name, Is.EqualTo("Find"));
        Assert.That(m.ExtraOnTarget.Single().Name, Is.EqualTo("Search"));
    }

    [Test]
    public void AutoPair_ByRoleAndName()
    {
        var r = new List<InventoryElement> { El(0, "button", "Save", order: 0), El(1, "link", "Help", order: 1) };
        var t = new List<InventoryElement> { El(0, "link", "Help", order: 0, x: 50), El(1, "button", "Save", order: 1, x: 50) };
        var m = ElementMatcher.Match(r, t);
        Assert.That(m.Pairs.Select(p => (p.Reference.Name, p.Target.Name)), Is.EqualTo(new[] { ("Save", "Save"), ("Help", "Help") }));
        Assert.That(m.MissingOnTarget, Is.Empty);
        Assert.That(m.ExtraOnTarget, Is.Empty);
    }

    [Test]
    public void DifferentRole_NeverPaired_UnlessProfileEquivalent()
    {
        var r = new List<InventoryElement> { El(0, "gridcell", "Acme") };
        var t = new List<InventoryElement> { El(0, "cell", "Acme") };
        var none = ElementMatcher.Match(r, t, new StackProfile());
        Assert.That(none.Pairs, Is.Empty);
        var profile = new StackProfile { RoleEquivalences = new() { new() { "gridcell", "cell" } } };
        Assert.That(ElementMatcher.Match(r, t, profile).Pairs.Count, Is.EqualTo(1));
    }

    [Test]
    public void MissingAndExtra_Reported()
    {
        var r = new List<InventoryElement> { El(0, "button", "Delete", interactive: true), El(1, "heading", "Payors") };
        var t = new List<InventoryElement> { El(0, "heading", "Payors"), El(1, "button", "Export", interactive: true, x: 600) };
        var m = ElementMatcher.Match(r, t);
        Assert.That(m.Pairs.Single().Reference.Name, Is.EqualTo("Payors"));
        Assert.That(m.MissingOnTarget.Single().Name, Is.EqualTo("Delete"));
        Assert.That(m.ExtraOnTarget.Single().Name, Is.EqualTo("Export"));
    }

    [Test]
    public void DuplicateNames_ResolvedByOrderAndPosition()
    {
        var r = new List<InventoryElement> { El(0, "button", "Edit", order: 0, y: 0), El(1, "button", "Edit", order: 1, y: 100) };
        var t = new List<InventoryElement> { El(0, "button", "Edit", order: 0, y: 5), El(1, "button", "Edit", order: 1, y: 105) };
        var m = ElementMatcher.Match(r, t);
        Assert.That(m.Pairs.Select(p => (p.Reference.Index, p.Target.Index)), Is.EqualTo(new[] { (0, 0), (1, 1) }));
    }

    [Test]
    public void GridCells_PairedByRowAndColumn()
    {
        var r = new List<InventoryElement> { El(0, "cell", "", row: 0, col: 0), El(1, "cell", "", row: 0, col: 1, x: 100), El(2, "cell", "", row: 1, col: 0, y: 30) };
        var t = new List<InventoryElement> { El(0, "cell", "", row: 1, col: 0, y: 40), El(1, "cell", "", row: 0, col: 0), El(2, "cell", "", row: 0, col: 1, x: 110) };
        var m = ElementMatcher.Match(r, t);
        Assert.That(m.Pairs.Select(p => (p.Reference.RowIndex, p.Reference.ColIndex, p.Target.RowIndex, p.Target.ColIndex)),
            Is.EqualTo(new (int?, int?, int?, int?)[] { (0, 0, 0, 0), (0, 1, 0, 1), (1, 0, 1, 0) }));
    }

    [Test]
    public void SimilarNames_PairedByTokenJaccard_BelowThresholdNot()
    {
        Assert.That(ElementMatcher.NameScore("Payor Name:", "payor name"), Is.EqualTo(ElementMatcher.NameWeight));
        Assert.That(ElementMatcher.NameScore("Search payors by name", "Search payors by the name"), Is.GreaterThan(0));
        Assert.That(ElementMatcher.NameScore("Save", "Cancel"), Is.EqualTo(0));
    }

    private static InventoryElement At(int i, string role, string path)
    {
        var e = El(i, role, "");
        e.DomPath = path;
        return e;
    }

    [Test]
    public void LayoutCell_NestedHeaderlessTable_IsLayout()
    {
        const string outer = "html > body > form > div > table:nth-of-type(1)";
        var outerCell = At(0, "cell", outer + " > tbody > tr > td:nth-of-type(1)");
        var innerCell = At(1, "cell", outer + " > tbody > tr > td:nth-of-type(2) > table:nth-of-type(1) > tbody > tr > td:nth-of-type(3)");
        var inv = new List<InventoryElement> { outerCell, innerCell };
        Assert.That(ElementMatcher.IsLayoutCell(outerCell, inv), Is.True, "contains a nested table");
        Assert.That(ElementMatcher.IsLayoutCell(innerCell, inv), Is.True, "nested inside a table cell");
    }

    [Test]
    public void LayoutCell_DataTablesKept()
    {
        // Header-less data table without nesting: content.
        const string plain = "html > body > table:nth-of-type(1)";
        var plainCell = At(0, "cell", plain + " > tbody > tr > td:nth-of-type(1)");
        Assert.That(ElementMatcher.IsLayoutCell(plainCell, new List<InventoryElement> { plainCell }), Is.False);

        // Nested table WITH column headers: a real (sub)grid.
        const string nested = "html > body > table:nth-of-type(1) > tbody > tr > td:nth-of-type(1) > table:nth-of-type(1)";
        var head = At(1, "columnheader", nested + " > thead > tr > th:nth-of-type(1)");
        var cell = At(2, "cell", nested + " > tbody > tr > td:nth-of-type(1)");
        Assert.That(ElementMatcher.IsLayoutCell(cell, new List<InventoryElement> { head, cell }), Is.False);

        // Explicit gridcell (role=grid) is always content, even nested.
        var grid = At(3, "gridcell", nested + " > tbody > tr > td:nth-of-type(2)");
        Assert.That(ElementMatcher.IsLayoutCell(grid, new List<InventoryElement> { grid }), Is.False);
    }

    [Test]
    public void IgnoredRoles_Excluded()
    {
        var profile = new StackProfile { IgnoreRoles = new() { "presentation" } };
        var m = ElementMatcher.Match(new List<InventoryElement> { El(0, "presentation", "") }, new List<InventoryElement>(), profile);
        Assert.That(m.MissingOnTarget, Is.Empty);
    }
}
