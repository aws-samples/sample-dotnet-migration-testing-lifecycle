using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>Plain-English Issue sentences of CSS groups (IssueText): templates per layer/property, naming, colors, states, plurals.</summary>
[TestFixture]
public class IssueTextTests
{
    private static readonly string[] AllStates =
        { "page_load", "sorted", "filter_initial", "filtered", "cell_edit_open", "dirty", "after_save", "after_cancel" };

    private static readonly IssueContext Ctx = new() { AllStates = AllStates };

    private static (CssGroup G, List<CssDiffEntry> Members) Group(string property, string reference, string target, string[] elements,
        string[]? states = null, string layer = CssLayers.Style, string severity = "MEDIUM", string roleClass = "", List<string>? columns = null)
    {
        states ??= AllStates;
        var members = elements.SelectMany(e => states.Select(s => new CssDiffEntry
        {
            Element = e, State = s, Property = property, Reference = reference, Target = target, Layer = layer, Severity = severity, Columns = columns
        })).ToList();
        var g = new CssGroup
        {
            Property = property, Reference = reference, Target = target, Layer = layer, Severity = severity, RoleClass = roleClass,
            Elements = elements.ToList(), ElementCount = elements.Length, States = states.ToList()
        };
        return (g, members);
    }

    private static string Issue(string property, string reference, string target, string[] elements, string[]? states = null,
        string layer = CssLayers.Style, string severity = "MEDIUM", string roleClass = "", List<string>? columns = null)
    {
        var (g, m) = Group(property, reference, target, elements, states, layer, severity, roleClass, columns);
        return IssueText.For(g, m, Ctx);
    }

    [Test]
    public void MissingCells_NamedByColumnHeader_WithState()
    {
        var text = Issue(AutoCssComparer.MissingProperty, "present", "missing", new[] { "gridcell cells, column 2 (first 3 rows)" },
            new[] { "cell_edit_open" }, CssLayers.Match, "HIGH", columns: new List<string> { "Payor Name" });
        Assert.That(text, Is.EqualTo("The Payor Name column cells (first 3 rows) exist on the old page (Reference) but not on the new page (Target) while a cell is being edited."));
    }

    [Test]
    public void LayoutTableText_NotFound_PlainSentence()
    {
        var text = Issue(AutoCssComparer.LayoutTextMissingProperty, "View 1 - 10 of 15", "not found", new[] { "text 'View 1 - 10 of 15'" },
            layer: CssLayers.Match, severity: "LOW");
        Assert.That(text, Is.EqualTo("The 'View 1 - 10 of 15' text (in a layout table on the old page (Reference)) was not found in any compared element of the new page (Target). " +
                                     "Check that the new page shows it; not counted, because plain text outside compared elements is not checked."));
    }

    [Test]
    public void Cells_SeveralColumns_AndUnresolvedColumns()
    {
        Assert.That(IssueText.CellsText(new[] { "ID", "Payor Name", "Code" }, "3"), Is.EqualTo("The ID, Payor Name and Code column cells (first 3 rows)"));
        Assert.That(IssueText.CellsText(new[] { "ID", "Payor Name", "Code", "Category", "Status" }, "3"),
            Is.EqualTo("Cells in 5 columns (ID, Payor Name, Code and 2 more) (first 3 rows)"));
        Assert.That(IssueText.CellsText(new[] { "column 2" }, "3"), Is.EqualTo("The cells in column 2 (first 3 rows)"));
        Assert.That(IssueText.CellsText(Array.Empty<string>(), "3"), Is.EqualTo("Grid cells (first 3 rows)"));
    }

    [Test]
    public void SingularAndPlural_Verbs()
    {
        Assert.That(Issue(AutoCssComparer.MissingProperty, "present", "missing", new[] { "combobox 'Records per Page'" }, layer: CssLayers.Match, severity: "HIGH"),
            Is.EqualTo("The 'Records per Page' drop-down exists on the old page (Reference) but not on the new page (Target)."));
        Assert.That(Issue(AutoCssComparer.MissingProperty, "present", "missing", new[] { "button 'Edit'", "button 'Delete'" }, layer: CssLayers.Match, severity: "HIGH"),
            Is.EqualTo("Buttons (Edit, Delete) exist on the old page (Reference) but not on the new page (Target)."));
        Assert.That(Issue(AutoCssComparer.ExtraProperty, "missing", "present", new[] { "img (svg)" }, layer: CssLayers.Match, severity: "LOW"),
            Is.EqualTo("An unnamed image appears on the new page (Target) but not on the old page (Reference). Confirm it is intended."));
    }

    [Test]
    public void PagerButtonColors_ColorWords_AndContrastImpact()
    {
        var (g, m) = Group("color", "rgb(0, 0, 0)", "rgba(0, 0, 0, 0.26)",
            new[] { "button 'First Page'", "button 'Previous Page'", "button 'Next Page'", "button 'Last Page'" }, new[] { "page_load" }, severity: "HIGH");
        foreach (var r in m) { r.ContrastReference = 21; r.ContrastTarget = 1.9; }
        Assert.That(IssueText.For(g, m, Ctx), Is.EqualTo(
            "Buttons (First Page, Previous Page, Next Page, Last Page) look different when the page first loads: text is black on the old page (Reference) and faded gray on the new page (Target) - harder to read (contrast 1.9:1, below the 4.5:1 minimum)."));

        var bg = Issue("background-color", "rgb(246, 246, 246)", "rgb(255, 255, 255)", new[] { "button 'Next Page'" }, new[] { "page_load" });
        Assert.That(bg, Is.EqualTo("The 'Next Page' button looks different when the page first loads: background is light gray on the old page (Reference) and white on the new page (Target)."));

        var shade = Issue("color", "rgb(51, 51, 51)", "rgb(43, 43, 43)", new[] { "label 'Active'" }, new[] { "dirty" });
        Assert.That(shade, Is.EqualTo("The 'Active' label looks slightly different when there are unsaved changes: text is a slightly different shade of dark gray on the new page (Target)."));
    }

    [Test]
    public void LabelWeight_Words()
    {
        var text = Issue("font-weight", "700", "400", new[] { "label 'Code'", "label 'Payor Name'", "label 'Status'" });
        Assert.That(text, Is.EqualTo("Labels (Code, Payor Name, Status) are bold on the old page (Reference) and normal weight on the new page (Target)."));
        Assert.That(IssueText.WeightWord("600"), Is.EqualTo("semi-bold"));
        Assert.That(IssueText.WeightWord("500"), Is.EqualTo("medium weight"));
        Assert.That(IssueText.WeightWord("bold"), Is.EqualTo("bold"));
    }

    [Test]
    public void Sizes_BiggerSmallerByPx()
    {
        Assert.That(Issue("font-size", "14.4px", "14px", new[] { "label 'Active'" }),
            Is.EqualTo("Text size of the 'Active' label is smaller by 0.4px on the new page (Target) (14.4px on the old page (Reference) vs 14px)."));
        Assert.That(Issue("padding", "0px 2px", "2px", new[] { "columnheader 'ID'", "columnheader 'Code'" }, roleClass: "columnheader"),
            Is.EqualTo("Inner spacing (padding) of column headers (ID, Code) is bigger by 2px on the top and bottom on the new page (Target) (0px top/bottom and 2px left/right on the old page (Reference) vs 2px on every side)."));
        Assert.That(Issue("margin", "4px 8px 4px 0px", "4px", new[] { "button 'Save'" }),
            Is.EqualTo("Outer spacing (margin) of the 'Save' button differs on the new page (Target): smaller by 4px on the right and bigger by 4px on the left (top 4px, right 8px, bottom 4px, left 0px on the old page (Reference) vs 4px on every side)."));
        Assert.That(IssueText.Sides("1px 2px 3px"), Is.EqualTo(new[] { 1.0, 2, 3, 2 }));
        Assert.That(IssueText.Sides("auto 2px"), Is.Null);
        Assert.That(Issue("border-radius", "3px", "4px", new[] { "button 'Save'" }),
            Is.EqualTo("Corner rounding of the 'Save' button is bigger by 1px on the new page (Target) (3px on the old page (Reference) vs 4px)."));
    }

    [Test]
    public void Responsive_NarrowWindow_CutOff_AndWrap()
    {
        var hidden = Issue("responsive.768.hidden", "fully visible", "clipped by its container", new[] { "checkbox 'Active'" }, new[] { "page_load" },
            CssLayers.Responsive, "HIGH");
        Assert.That(hidden, Is.EqualTo("At a narrow window (768px) the 'Active' checkbox is partly cut off (clipped by its container) on the new page (Target) when the page first loads; on the old page (Reference) it is fully visible."));
        var lines = Issue("responsive.1366.lines", "1 line", "2 lines", new[] { "label 'Active'" }, new[] { "page_load" }, CssLayers.Responsive, "HIGH");
        Assert.That(lines, Is.EqualTo("At a 1366px wide window the 'Active' label wraps onto 2 lines on the new page (Target) instead of 1 line on the old page (Reference) when the page first loads."));
        var page = Issue("responsive.768.overflow", "fits 768px", "scrolls horizontally (1100px wide)", new[] { "(page)" }, new[] { "page_load" }, CssLayers.Responsive, "HIGH");
        Assert.That(page, Is.EqualTo("At a narrow window (768px) the new page (Target) scrolls sideways (1100px wide) but the old page (Reference) fits when the page first loads."));
    }

    [Test]
    public void Geometry_ReferenceLargerGap_IsOldPageDefect_TargetLarger_Bigger()
    {
        var gap = Issue("layout.gapToPager", "508.5px", "13.5px", new[] { "Grid pager" }, new[] { "page_load" }, CssLayers.Geometry, "HIGH");
        Assert.That(gap, Is.EqualTo("The old page (Reference) has a large empty gap (508.5px) between the last grid row and the pager; the new page (Target) has 13.5px when the page first loads. This is an old-page defect; do not copy it."));
        var medium = Issue("layout.gapToGrid", "32px", "16px", new[] { "Filter panel" }, new[] { "page_load" }, CssLayers.Geometry, "MEDIUM");
        Assert.That(medium, Does.StartWith("The gap between the filter panel and the grid is smaller on the new page (Target) by 16px (32px on the old page (Reference) vs 16px) when the page first loads."));
        Assert.That(medium, Does.EndWith("Check the old page first; do not add extra space to the new page just to match."));
        var taller = Issue("layout.rowHeight", "23px", "38px", new[] { "Grid data row" }, new[] { "page_load" }, CssLayers.Geometry, "HIGH");
        Assert.That(taller, Is.EqualTo("The row height is bigger on the new page (Target) by 15px (38px vs 23px on the old page (Reference)) when the page first loads."));
    }

    [Test]
    public void Layout_LinesLabelPositionOverlapSpacing()
    {
        Assert.That(Issue("layout.lines", "1 line", "2 lines", new[] { "label 'Active'" }, layer: CssLayers.Layout, severity: "HIGH"),
            Is.EqualTo("The 'Active' label wraps onto 2 lines on the new page (Target) instead of 1 line on the old page (Reference)."));
        Assert.That(Issue("layout.labelPosition", "left of its control", "inside its control", new[] { "label 'Code:'" }, layer: CssLayers.Layout),
            Is.EqualTo("The 'Code:' label sits left of its control on the old page (Reference) but inside its control on the new page (Target)."));
        Assert.That(Issue("layout.overlap", "no overlap with checkbox 'Active'", "overlaps checkbox 'Active' by 46px x 7px", new[] { "checkbox 'Active'" }, layer: CssLayers.Layout, severity: "HIGH"),
            Is.EqualTo("The 'Active' checkbox overlaps another 'Active' checkbox by 46px x 7px on the new page (Target); on the old page (Reference) they do not overlap."));
        Assert.That(Issue("layout.overlap", "no overlap with button 'Save'", "overlaps button 'Save' by 10px x 4px", new[] { "checkbox 'Active'" }, layer: CssLayers.Layout, severity: "HIGH"),
            Does.StartWith("The 'Active' checkbox overlaps the 'Save' button by 10px x 4px"), "a different element keeps 'the'");
        Assert.That(Issue("layout.overlap", "no overlap with Payor Name filter input", "overlaps Payor Name filter input by 63.4px x 9px", new[] { "label 'Payor Name:'" }, layer: CssLayers.Layout, severity: "HIGH"),
            Does.StartWith("The 'Payor Name:' label overlaps the Payor Name filter input by 63.4px x 9px"), "a css-map name gets its article");
        Assert.That(Issue("layout.spacing", "160px", "4px", new[] { "button 'Next Page'" }, layer: CssLayers.Layout),
            Does.StartWith("The space before the 'Next Page' button is smaller by 156px on the new page (Target) (160px on the old page (Reference) vs 4px)."));
    }

    [Test]
    public void TextRows_AndFallback()
    {
        Assert.That(Issue("text.name", "", "Payor Name", new[] { "Cell edit input (Name)" }, new[] { "cell_edit_open" }, CssLayers.Match),
            Is.EqualTo("The cell edit input (Name) has a different accessible name while a cell is being edited: nothing on the old page (Reference) and 'Payor Name' on the new page (Target)."));
        Assert.That(Issue("text-transform", "uppercase", "none", new[] { "button 'Save'" }),
            Is.EqualTo("Text-transform of the 'Save' button differs: uppercase on the old page (Reference) and none on the new page (Target)."));
        Assert.That(Issue("cursor", "default", "not-allowed", new[] { "button 'Save'" }, new[] { "page_load" }),
            Is.EqualTo("Pointing at the 'Save' button shows an arrow on the old page (Reference) and a 'not allowed' sign on the new page (Target) when the page first loads."));
    }

    [Test]
    public void States_Phrases()
    {
        Assert.That(IssueText.When(AllStates, AllStates), Is.EqualTo(""), "every planned state: no state clause");
        Assert.That(IssueText.When(new[] { "sorted", "filtered" }, AllStates), Is.EqualTo(" after sorting and after filtering"));
        Assert.That(IssueText.When(new[] { "page_load", "sorted", "filtered", "dirty" }, AllStates), Is.EqualTo(" in 4 page states (page load, sorted, filtered, ...)"));
        Assert.That(IssueText.StatePhrase("S_p2"), Is.EqualTo("on page 2"));
        Assert.That(IssueText.StatePhrase("wizard_step_3"), Is.EqualTo("in the 'wizard step 3' state"));
    }

    [Test]
    public void Value_RoleLabelsMadePlain()
        => Assert.That(IssueText.Value("left+top aligned with label 'Active'"), Is.EqualTo("left+top aligned with the 'Active' label"));
}
