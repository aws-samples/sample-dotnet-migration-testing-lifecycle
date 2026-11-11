using System.Text.Json.Nodes;
using MigrationTests.Playwright;
using NUnit.Framework;
using static MigrationTests.Reporting.ElementMatcherTests;

namespace MigrationTests.Reporting;

/// <summary>Automatic inventory rows (match/style/state/layout/responsive), profile acceptance, ActionFor and report wiring.</summary>
[TestFixture]
public class AutoCssComparerTests
{
    private static InventoryFile Inv(params InventoryElement[] els) => new()
    {
        State = "page_load", Viewport = new InventoryViewport { Width = 1920, Height = 1080, ScrollWidth = 1920 }, Elements = els.ToList()
    };

    private static List<CssDiffEntry> Rows(InventoryFile r, InventoryFile t, StackProfile? p = null, VisualChecksSettings? vc = null) =>
        AutoCssComparer.Compare("page_load", r, t, p, vc).Rows;

    [Test]
    public void Missing_AlwaysHigh_Extra_NonInteractive_Low()
    {
        var rows = Rows(Inv(El(0, "button", "Delete", interactive: true)), Inv(El(0, "img", "Logo", x: 900)));
        var missing = rows.Single(d => d.Property == AutoCssComparer.MissingProperty);
        Assert.That((missing.Element, missing.Severity, missing.Reference, missing.Target, missing.Layer),
            Is.EqualTo(("button 'Delete'", "HIGH", "present", "missing", "match")));
        Assert.That(rows.Single(d => d.Property == AutoCssComparer.ExtraProperty).Severity, Is.EqualTo("LOW"));

        var rows2 = Rows(Inv(El(0, "heading", "Totals")), Inv(El(0, "button", "Export", interactive: true, x: 900)));
        // Any missing element is HIGH, interactive or not (a missing heading/section is a visible gap).
        Assert.That(rows2.Single(d => d.Property == AutoCssComparer.MissingProperty).Severity, Is.EqualTo("HIGH"));
        Assert.That(rows2.Single(d => d.Property == AutoCssComparer.ExtraProperty).Severity, Is.EqualTo("MEDIUM"));
    }

    [Test]
    public void LabelAndPlaceholderText_Medium()
    {
        var r = El(0, "textbox", "Name"); r.Label = "Payor Name"; r.Placeholder = "Search by name...";
        var t = El(0, "textbox", "Name"); t.Label = "Payor"; t.Placeholder = "Search...";
        var rows = Rows(Inv(r), Inv(t));
        Assert.That(rows.Single(d => d.Property == "text.label").Severity, Is.EqualTo("MEDIUM"));
        Assert.That(rows.Single(d => d.Property == "text.placeholder").Severity, Is.EqualTo("MEDIUM"));
    }

    [Test]
    public void Style_AutoPairsOnly_Normalized()
    {
        var r = El(0, "button", "Save"); r.Styles = new() { ["color"] = "#fff", ["font-size"] = "13px", ["background-color"] = "rgb(0, 0, 255)" };
        var t = El(0, "button", "Save"); t.Styles = new() { ["color"] = "rgb(255, 255, 255)", ["font-size"] = "14px", ["background-color"] = "rgb(0, 128, 0)" };
        var rows = Rows(Inv(r), Inv(t));
        Assert.That(rows.Where(d => d.Layer == "style").Select(d => (d.Property, d.Severity)),
            Is.EquivalentTo(new[] { ("font-size", "MEDIUM"), ("background-color", "HIGH") }));

        r.MapName = t.MapName = "Save button";
        Assert.That(Rows(Inv(r), Inv(t)).Where(d => d.Layer == "style"), Is.Empty, "css-map pairs get their mapped rows instead");
    }

    [Test]
    public void IgnoreProperties_OnlyNonInteractive()
    {
        var r = El(0, "heading", "Title"); r.Styles = new() { ["cursor"] = "auto" };
        var t = El(0, "heading", "Title"); t.Styles = new() { ["cursor"] = "pointer" };
        Assert.That(Rows(Inv(r), Inv(t)), Is.Empty);
        var rb = El(0, "button", "Go", interactive: true); rb.Styles = new() { ["cursor"] = "auto" };
        var tb = El(0, "button", "Go", interactive: true); tb.Styles = new() { ["cursor"] = "pointer" };
        Assert.That(Rows(Inv(rb), Inv(tb)).Single().Property, Is.EqualTo("cursor"));
    }

    [Test]
    public void StateRows_HoverColorHigh_FocusOutlineMedium()
    {
        var r = El(0, "button", "Save", interactive: true);
        r.States = new() { ["hover.background-color"] = "rgb(0, 0, 200)", ["focus.outline"] = "2px solid rgb(0, 0, 0)" };
        var t = El(0, "button", "Save", interactive: true);
        t.States = new() { ["hover.background-color"] = "rgb(0, 0, 255)", ["focus.outline"] = "0px none rgb(0, 0, 0)" };
        var rows = Rows(Inv(r), Inv(t)).Where(d => d.Layer == "state").ToList();
        Assert.That(rows.Select(d => (d.Property, d.Severity)), Is.EquivalentTo(new[] { ("hover.background-color", "HIGH"), ("focus.outline", "MEDIUM") }));
        Assert.That(ModuleReportGenerator.ActionFor(rows.Single(d => d.Property == "hover.background-color")),
            Is.EqualTo("Align to reference: set background-color: rgb(0, 0, 200) on :hover"));
    }

    [Test]
    public void Layout_Order_High()
    {
        var r = Inv(El(0, "button", "Search", x: 0, order: 0), El(1, "button", "Clear", x: 100, order: 1));
        var t = Inv(El(0, "button", "Clear", x: 0, order: 0), El(1, "button", "Search", x: 100, order: 1));
        var rows = Rows(r, t).Where(d => d.Property == "layout.order").ToList();
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows.All(d => d.Severity == "HIGH"));
        Assert.That(rows.Single(d => d.Element == "button 'Search'").Target, Is.EqualTo("position 2 of 2"));
    }

    [Test]
    public void Layout_AlignAndSpacing_Medium()
    {
        // Reference: stacked, left-aligned, 10px gap. Target: second element indented 30px and 60px below.
        var r = Inv(El(0, "textbox", "Name", x: 10, y: 0, order: 0), El(1, "textbox", "City", x: 10, y: 30, order: 1));
        var t = Inv(El(0, "textbox", "Name", x: 10, y: 0, order: 0), El(1, "textbox", "City", x: 40, y: 80, order: 1));
        var rows = Rows(r, t);
        var align = rows.Single(d => d.Property == "layout.align");
        Assert.That((align.Severity, align.Reference, align.Target), Is.EqualTo(("MEDIUM", "left+right aligned with textbox 'Name'", "not aligned with textbox 'Name'")));
        var spacing = rows.Single(d => d.Property == "layout.spacing");
        Assert.That((spacing.Severity, spacing.Reference, spacing.Target), Is.EqualTo(("MEDIUM", "10px", "60px")));
    }

    [Test]
    public void Layout_ReferenceLargerSpacing_ActionNeverAddsSpace()
    {
        var r = Inv(El(0, "textbox", "Name", y: 0, order: 0), El(1, "textbox", "City", y: 100, order: 1));
        var t = Inv(El(0, "textbox", "Name", y: 0, order: 0), El(1, "textbox", "City", y: 30, order: 1));
        var spacing = Rows(r, t).Single(d => d.Property == "layout.spacing");
        Assert.That(ModuleReportGenerator.ActionFor(spacing), Does.StartWith("Reference larger: layout.spacing Reference 80px vs Target 10px"));
    }

    [Test]
    public void Layout_OverflowLinesHiddenOverlap_High()
    {
        var r1 = El(0, "button", "Save changes", x: 0, w: 100, order: 0); r1.Lines = 1;
        var r2 = El(1, "button", "Cancel", x: 120, w: 100, order: 1);
        var t1 = El(0, "button", "Save changes", x: 0, w: 140, order: 0); t1.Lines = 2; t1.OverflowX = true; t1.Clipped = true;
        var t2 = El(1, "button", "Cancel", x: 120, w: 100, order: 1);
        var rows = Rows(Inv(r1, r2), Inv(t1, t2));
        foreach (var p in new[] { "layout.overflow", "layout.lines", "layout.hidden", "layout.overlap" })
            Assert.That(rows.Single(d => d.Property == p).Severity, Is.EqualTo("HIGH"), p);
        Assert.That(rows.Single(d => d.Property == "layout.lines").Target, Is.EqualTo("2 lines"));
    }

    [Test]
    public void Layout_LabelPosition_Medium()
    {
        var rl = El(0, "label", "Name", x: 0, y: 0, order: 0); var rc = El(1, "textbox", "Name", x: 100, y: 0, order: 1);
        rl.LabelFor = rc.DomPath;
        var tl = El(0, "label", "Name", x: 0, y: 0, order: 0); var tc = El(1, "textbox", "Name", x: 0, y: 25, order: 1);
        tl.LabelFor = tc.DomPath;
        var row = Rows(Inv(rl, rc), Inv(tl, tc)).Single(d => d.Property == "layout.labelPosition");
        Assert.That((row.Reference, row.Target, row.Severity), Is.EqualTo(("left of its control", "above its control", "MEDIUM")));
    }

    [Test]
    public void Responsive_ElementOverflowAndPageScroll_High()
    {
        var r = Inv(El(0, "button", "Search")); var t = Inv(El(0, "button", "Search"));
        var rs = El(0, "button", "Search"); var ts = El(0, "button", "Search"); ts.OverflowX = true;
        r.Responsive["768"] = new InventorySnapshot { Viewport = new() { Width = 768, ScrollWidth = 768 }, Elements = new() { rs } };
        t.Responsive["768"] = new InventorySnapshot { Viewport = new() { Width = 768, ScrollWidth = 1100 }, Elements = new() { ts } };
        var rows = Rows(r, t).Where(d => d.Layer == "responsive").ToList();
        Assert.That(rows.Single(d => d.Element == "button 'Search'").Property, Is.EqualTo("responsive.768.overflow"));
        var page = rows.Single(d => d.Element == "(page)");
        Assert.That((page.Severity, page.Reference, page.Target), Is.EqualTo(("HIGH", "fits 768px", "scrolls horizontally (1100px wide)")));
    }

    [Test]
    public void GridCells_GroupedIntoOneRowPerDifference()
    {
        InventoryElement Cell(int i, int row, int col, string color)
        {
            var e = El(i, "cell", "", x: col * 100, y: row * 30, row: row, col: col);
            e.Styles = new() { ["color"] = color };
            return e;
        }
        var r = Inv(Cell(0, 0, 0, "#000"), Cell(1, 0, 1, "#000"), Cell(2, 1, 0, "#000"), Cell(3, 1, 1, "#000"));
        var t = Inv(Cell(0, 0, 0, "#333"), Cell(1, 0, 1, "#333"), Cell(2, 1, 0, "#333"), Cell(3, 1, 1, "#333"));
        var row = Rows(r, t).Single();
        Assert.That(row.Element, Is.EqualTo("cell cells, columns 1, 2 (first 3 rows)"));
        Assert.That(row.Columns, Is.EqualTo(new[] { "column 1", "column 2" }), "no header: column numbers");
    }

    [Test]
    public void GridCells_ColumnHeaderInSeparateTableAbove_ResolvesHeaderText_ElementUnchanged()
    {
        // jqGrid style: the header row is its own table above the data table; cells are matched to headers by geometry.
        InventoryElement Header(int i, string name, double x, double w) => El(i, "columnheader", name, x: x, y: 0, w: w, h: 20);
        InventoryElement Cell(int i, int col, double x, double w) => El(i, "gridcell", col == 0 ? "1" : "Acme", x: x, y: 25, w: w, h: 30, row: 0, col: col);
        var r = Inv(Header(0, "ID", 0, 100), Header(1, "Payor Name", 100, 300), Cell(2, 0, 0, 100), Cell(3, 1, 100, 300));
        var t = Inv(Header(0, "ID", 0, 100), Header(1, "Payor Name", 100, 300), Cell(2, 0, 0, 100));
        var missing = Rows(r, t).Single(d => d.Property == AutoCssComparer.MissingProperty);
        Assert.That(missing.Element, Is.EqualTo("gridcell cells, column 2 (first 3 rows)"), "technical label and group key stay stable");
        Assert.That(missing.Columns, Is.EqualTo(new[] { "Payor Name" }));
        Assert.That(AutoCssComparer.ColumnHeader(r.Elements[2], r.Elements), Is.EqualTo("ID"));
    }

    [Test]
    public void LayoutTableCells_ExcludedFromMatching_ListedInMatchResult()
    {
        // WebForms/jqGrid pager: an outer header-less table whose td holds a nested table with the pager controls.
        const string pager = "html > body > div > table:nth-of-type(1)";
        InventoryElement At(int i, string role, string name, string path, bool interactive = false)
        {
            var e = El(i, role, name, x: i * 50, y: 500, interactive: interactive, row: 0, col: i);
            e.DomPath = path;
            return e;
        }
        var r = Inv(
            At(0, "cell", "", pager + " > tbody > tr > td:nth-of-type(1)"),
            At(1, "cell", "Page of 2", pager + " > tbody > tr > td:nth-of-type(2)"),
            At(2, "cell", "Page of 2", pager + " > tbody > tr > td:nth-of-type(2) > table:nth-of-type(1) > tbody > tr > td:nth-of-type(4)"),
            At(3, "textbox", "Page No.", pager + " > tbody > tr > td:nth-of-type(2) > table:nth-of-type(1) > tbody > tr > td:nth-of-type(4) > input", interactive: true));
        var result = AutoCssComparer.Compare("page_load", r, Inv(), null);
        // Cell 0 is empty and cell 1's text is fully covered by its nested cell: left out. Cell 2 carries "Page of 2" itself.
        Assert.That(result.Match.ExcludedLayoutCells.Select(x => (x.Side, x.Element.Index)), Is.EqualTo(new[] { ("Reference", 0), ("Reference", 1) }));
        Assert.That(result.Match.LayoutTextCells.Select(x => (x.Side, x.Element.Index, x.Text, x.Found)), Is.EqualTo(new[] { ("Reference", 2, "Page of 2", false) }));
        var missing = result.Rows.Where(d => d.Property == AutoCssComparer.MissingProperty).Select(d => d.Element).ToList();
        Assert.That(missing, Is.EqualTo(new[] { "textbox 'Page No.'" }), "controls inside the layout table stay compared");
        var text = result.Rows.Single(d => d.Property == AutoCssComparer.LayoutTextMissingProperty);
        Assert.That((text.Element, text.Reference, text.Target, text.Severity), Is.EqualTo(("text 'Page of 2'", "Page of 2", "not found", "LOW")));
    }

    [Test]
    public void LayoutTableTextOnlyCell_ComparedByText_FoundOrLowRow()
    {
        // Classic WebForms form: a nested layout table whose label cell holds only text ("Name:"), no control.
        const string form = "html > body > table:nth-of-type(1) > tbody > tr > td:nth-of-type(1) > table:nth-of-type(1) > tbody > tr";
        InventoryElement At(int i, string role, string name, string path, bool interactive = false)
        {
            var e = El(i, role, name, x: i * 50, y: 100, interactive: interactive);
            e.DomPath = path;
            return e;
        }
        var r = Inv(
            At(0, "cell", "Name:", form + " > td:nth-of-type(1)"),
            At(1, "cell", "", form + " > td:nth-of-type(2)"),
            At(2, "textbox", "Name:", form + " > td:nth-of-type(2) > input", interactive: true),
            At(3, "cell", "Total: 15", form + " > td:nth-of-type(3)"));
        // Target shows "Name" as the text box label and has no total.
        var t = Inv(At(0, "textbox", "Name", "html > body > div > input", interactive: true));
        var result = AutoCssComparer.Compare("page_load", r, t, null);
        Assert.That(result.Match.ExcludedLayoutCells.Select(x => x.Element.Index), Is.EqualTo(new[] { 1 }), "only the cell that just holds the control is left out");
        Assert.That(result.Match.LayoutTextCells.Select(x => (x.Element.Index, x.Text, x.Found)),
            Is.EqualTo(new[] { (0, "Name:", true), (3, "Total: 15", false) }));
        var rows = result.Rows.Where(d => d.Property == AutoCssComparer.LayoutTextMissingProperty).ToList();
        Assert.That(rows.Select(d => (d.Element, d.Severity, d.Layer)), Is.EqualTo(new[] { ("text 'Total: 15'", "LOW", CssLayers.Match) }),
            "text not found on the Target is listed (LOW, not counted), never dropped");
        Assert.That(ModuleReportGenerator.ActionFor(rows[0]), Does.StartWith("Check that the Target shows the text 'Total: 15'"));
    }

    // ─────────────────────────────── profiles ───────────────────────────────

    private static StackProfile Profile() => new()
    {
        Name = "test-stack",
        RoleEquivalences = new() { new() { "gridcell", "cell" } },
        Accepted = new()
        {
            new ProfileAcceptance { Property = "disabled.cursor", Reference = "^default$", Target = "^not-allowed$", Reason = "framework default" },
            new ProfileAcceptance { Property = "font-weight", Role = "columnheader", Reason = "Material header weight" }
        }
    };

    [Test]
    public void ProfileAcceptance_ReasonShown_NotActionable_CssMapOverrides()
    {
        var rows = new List<CssDiffEntry>
        {
            new() { Element = "button 'Go'", State = "page_load", Property = "disabled.cursor", Reference = "default", Target = "not-allowed", Severity = "MEDIUM", Layer = "state" },
            new() { Element = "Header", State = "page_load", Property = "font-weight", Reference = "600", Target = "500", Severity = "HIGH", Layer = "mapped", Role = "columnheader", Actionable = true },
            new() { Element = "Header", State = "page_load", Property = "color", Reference = "red", Target = "blue", Severity = "HIGH", Layer = "mapped", Role = "columnheader", Actionable = true },
            new() { Element = "Pager", State = "page_load", Property = "font-weight", Reference = "600", Target = "500", Severity = "HIGH", Layer = "mapped", Accepted = true, AcceptedReason = "page decision", AcceptedBy = "css-map" }
        };
        ModuleReportGenerator.ApplyAcceptance(rows, null, Profile());
        Assert.That((rows[0].Accepted, rows[0].Actionable, rows[0].AcceptedBy), Is.EqualTo((true, false, "profile test-stack")));
        Assert.That(ModuleReportGenerator.AcceptedNote(rows[0]), Is.EqualTo("accepted by profile test-stack: framework default"));
        Assert.That((rows[1].Accepted, rows[1].Actionable), Is.EqualTo((true, false)), "role-scoped acceptance");
        Assert.That((rows[2].Accepted, rows[2].Actionable), Is.EqualTo((false, true)));
        Assert.That((rows[3].AcceptedBy, rows[3].AcceptedReason), Is.EqualTo(("css-map", "page decision")), "css-map acceptance wins");
    }

    [Test]
    public void ProfileAcceptance_RoleMismatchOrValueMismatch_NotAccepted()
    {
        var p = Profile();
        Assert.That(p.TryAccept(new CssDiffEntry { Property = "font-weight", Reference = "600", Target = "500" }, "button"), Is.Null);
        Assert.That(p.TryAccept(new CssDiffEntry { Property = "disabled.cursor", Reference = "pointer", Target = "not-allowed" }), Is.Null);
    }

    [Test]
    public void StackProfile_Load_MergesExtendsChain_GenericLast()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "generic.json"), "{ \"name\": \"generic\", \"roleEquivalences\": [[\"status\",\"alert\"]], \"accepted\": [{ \"property\": \"text-align\", \"reason\": \"g\" }] }");
            File.WriteAllText(Path.Combine(dir, "a__b.json"), "{ \"name\": \"a__b\", \"extends\": \"generic\", \"roleEquivalences\": [[\"gridcell\",\"cell\"]], \"accepted\": [{ \"property\": \"hover.*\", \"reason\": \"child\" }] }");
            var p = StackProfile.Load(dir, "a__b");
            Assert.That(p.Chain, Is.EqualTo(new[] { "a__b", "generic" }));
            Assert.That(p.RolesEquivalent("gridcell", "cell") && p.RolesEquivalent("alert", "status"));
            Assert.That(p.TryAccept(new CssDiffEntry { Property = "hover.color" }), Is.EqualTo("child"));
            Assert.That(p.TryAccept(new CssDiffEntry { Property = "text-align" }), Is.EqualTo("g"));
            var missing = StackProfile.Load(dir, "nope");
            Assert.That(missing.Warnings.Single(), Does.Contain("'nope' not found"));
            Assert.That(missing.Chain, Is.EqualTo(new[] { "generic" }));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ShippedProfiles_Parse()
    {
        var p = StackProfile.Load(HarnessPaths.Resolve("profiles"), "webforms-jqgrid__blazor-mudblazor");
        Assert.That(p.Warnings, Is.Empty);
        Assert.That(p.Chain, Is.EqualTo(new[] { "webforms-jqgrid__blazor-mudblazor", "generic" }));
        Assert.That(p.Accepted.All(a => a.Reason.Length > 0), "every acceptance has a reason");
        Assert.That(p.RolesEquivalent("gridcell", "cell"));
    }

    [TestCase("600", "500", "columnheader", true)]
    [TestCase("500", "600", "columnheader", true)]
    [TestCase("700", "500", "columnheader", false)]
    [TestCase("bold", "500", "columnheader", false)]
    [TestCase("600", "500", "button", false)]
    public void ShippedMudBlazorProfile_HeaderWeight_OnlySemiboldVsMedium(string reference, string target, string role, bool accepted)
    {
        var p = StackProfile.Load(HarnessPaths.Resolve("profiles"), "webforms-jqgrid__blazor-mudblazor");
        var d = new CssDiffEntry { Property = "font-weight", Reference = reference, Target = target };
        Assert.That(p.TryAccept(d, role) != null, Is.EqualTo(accepted));
    }

    [Test]
    public void ShippedMudBlazorProfile_NotAllowedCursor_OnlyWhenDisabled()
    {
        var p = StackProfile.Load(HarnessPaths.Resolve("profiles"), "webforms-jqgrid__blazor-mudblazor");
        Assert.That(p.TryAccept(new CssDiffEntry { Property = "disabled.cursor", Reference = "default", Target = "not-allowed" }), Is.Not.Null);
        Assert.That(p.TryAccept(new CssDiffEntry { Property = "cursor", Reference = "default", Target = "not-allowed" }, "button"), Is.Null,
            "an enabled control showing not-allowed stays counted");
    }

    // ─────────────────────────────── actions ───────────────────────────────

    [Test]
    public void ActionFor_MissingExtraAndExistingTexts()
    {
        Assert.That(ModuleReportGenerator.ActionFor(new CssDiffEntry { Element = "button 'Delete'", Property = AutoCssComparer.MissingProperty }),
            Does.StartWith("Render button 'Delete' on Target with the same role and accessible name"));
        Assert.That(ModuleReportGenerator.ActionFor(new CssDiffEntry { Property = AutoCssComparer.ExtraProperty }),
            Does.StartWith("Confirm the extra Target element is intended or remove it"));
        Assert.That(ModuleReportGenerator.ActionFor(new CssDiffEntry { Property = "color", Reference = "red" }), Is.EqualTo("Align to reference: set color: red"));
        Assert.That(ModuleReportGenerator.ActionFor(new CssDiffEntry { Property = "layout.gapToPager", Reference = "2px", Target = "300px" }),
            Is.EqualTo("Fix Target layout: layout.gapToPager 300px vs Reference 2px (diff +298px, 14900%)"));
    }

    [TestCase("responsive.768.overflow", "scrolls horizontally (1884px wide)", "fits 768px", true)]
    [TestCase("responsive.768.hidden", "clipped by its container", "fully visible", true)]
    [TestCase("layout.overflow", "overflows (content cut off)", "fits", true)]
    [TestCase("responsive.768.overflow", "fits 768px", "scrolls horizontally (1884px wide)", false)]
    [TestCase("layout.overflow", "fits", "overflows (content cut off)", false)]
    [TestCase("responsive.768.lines", "2 lines", "1 line", false)]
    public void ActionFor_ProblemOnlyOnReference_NeverAsksTheTargetToReproduceIt(string property, string reference, string target, bool referenceOnly)
    {
        var action = ModuleReportGenerator.ActionFor(new CssDiffEntry { Element = "(page)", Property = property, Reference = reference, Target = target });
        if (referenceOnly)
            Assert.That(action, Does.StartWith($"Reference larger: {property} Reference {reference} vs Target {target}").And.Contain("never change the Target"));
        else
            Assert.That(action, Does.StartWith("Fix Target layout"));
    }

    // ─────────────────────────────── report wiring ───────────────────────────────

    private string _pageDir = "";

    [SetUp]
    public void SetUp()
    {
        _pageDir = Path.Combine(Path.GetTempPath(), "auto-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pageDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_pageDir)) Directory.Delete(_pageDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    [Test]
    public void Generate_WithInventories_WritesMatchFileAndCountsRows()
    {
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "css-map.json"), new CssMapFile());
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Page = "P", Modules = new() { new PlanModule { Id = "A", CssStates = new() { "page_load" } } }
        });
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "css", "css_mapped_page_load.json"), new Dictionary<string, object>());
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Target", "css", "css_mapped_page_load.json"), new Dictionary<string, object>());
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "css", "inventory_page_load.json"),
            Inv(El(0, "button", "Search", interactive: true), El(1, "button", "Delete", interactive: true, x: 200, order: 1)));
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Target", "css", "inventory_page_load.json"),
            Inv(El(0, "button", "Search", interactive: true)));

        var s = ModuleReportGenerator.Generate(_pageDir, "A", new VisualChecksSettings()).Single();
        var missing = s.CssDiffs.Single(d => d.Property == AutoCssComparer.MissingProperty);
        Assert.That((missing.Element, missing.Actionable, missing.Layer), Is.EqualTo(("button 'Delete'", true, "match")));
        Assert.That(missing.Action, Does.StartWith("Render button 'Delete' on Target"));
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(1));
        Assert.That(s.StackProfile, Is.EqualTo("generic"));

        var match = JsonNode.Parse(File.ReadAllText(Path.Combine(_pageDir, "runs", "ModuleA", "match_page_load.json")))!;
        Assert.That(match["counts"]!["pairs"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(match["missingOnTarget"]!.AsArray().Single()!["name"]!.GetValue<string>(), Is.EqualTo("Delete"));

        var md = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(md, Does.Contain("By layer: match 1 (1 actionable)"));
        Assert.That(md, Does.Contain("Stack profile: generic, 0 rows accepted by profile"));
        Assert.That(md, Does.Contain("Layout-table cells (tables that only position other elements): 0 left out of element matching (empty or holding only compared controls), 0 compared by their own text (0 not found on the other side: LOW, not counted); per state in `runs/ModuleA/match_<state>.json`"));
        Assert.That(md, Does.Contain("| button 'Delete' [page_load] | (missing on Target) | present | missing | HIGH | Render button 'Delete' on Target"));

        var json = File.ReadAllText(Path.Combine(_pageDir, "runs", "ModuleA", "summary.json"));
        Assert.That(json, Does.Contain("\"layer\": \"match\"").And.Contain("\"action\": \"Render button"));
    }

    [Test]
    public void LayoutCellCounts_SummedOverStateMatchFiles()
    {
        var run = Path.Combine(_pageDir, "runs", "ModuleA");
        ReportJson.Write(Path.Combine(run, "match_page_load.json"), new
        {
            Counts = new { ExcludedLayoutCells = 5, LayoutTextCells = 2 },
            LayoutTextCells = new[] { new { Found = false }, new { Found = true } }
        });
        ReportJson.Write(Path.Combine(run, "match_sorted.json"), new
        {
            Counts = new { ExcludedLayoutCells = 5, LayoutTextCells = 1 },
            LayoutTextCells = new[] { new { Found = false } }
        });
        Assert.That(ModuleReportGenerator.LayoutCellCounts(run, new[] { "page_load", "sorted", "filtered" }), Is.EqualTo((10, 3, 2)));
        Assert.That(ModuleReportGenerator.LayoutCellCounts(run, new[] { "filtered" }), Is.Null);
    }

    // ─────────────────────────────── health ───────────────────────────────

    private static RunResultsFile Results(string system, params ContentIssue[] issues) => new()
    {
        Module = "A", System = system,
        Steps = new() { new RunStep { Step = "Step1", Status = "PASS", ContentIssues = issues.ToList() } }
    };

    [Test]
    public void NewHealthKinds_TargetGapsWithSeverity_ReferenceDefectsNotCounted()
    {
        var tgt = Results("Target",
            new ContentIssue { Kind = ContentIssue.BrokenImage, Excerpt = "image did not load: /logo.png" },
            new ContentIssue { Kind = ContentIssue.FailedRequest, Excerpt = "404 GET http://x/app.css" },
            new ContentIssue { Kind = ContentIssue.EmptySection, Excerpt = "heading \"Totals\" has no content below it" },
            new ContentIssue { Kind = ContentIssue.Accessibility, Excerpt = "button without an accessible name: button#b1" });
        var refr = Results("Reference", new ContentIssue { Kind = ContentIssue.FailedRequest, Excerpt = "500 GET http://r/api" });
        var c = ModuleReportGenerator.ClassifyContent("A", refr, tgt);
        Assert.That(c.TargetFindings.Select(f => (f.Field, f.Severity)), Is.EquivalentTo(new[]
        {
            ("page.brokenImage", "HIGH"), ("page.failedRequest", "HIGH"), ("page.emptySection", "MEDIUM"), ("page.a11y", "MEDIUM")
        }));
        var d = c.ReferenceDefects.Single();
        Assert.That((d.Id, d.Kind, d.Severity), Is.EqualTo(("A-REF1", "failed-request", "HIGH")));
        Assert.That(ModuleReportGenerator.ContentKindText(d.Kind), Is.EqualTo("Failed network request"));
    }

    [Test]
    public void InventoryHealth_ToContentIssues()
    {
        var h = JsonNode.Parse("{\"brokenImages\":[\"/a.png\",\"/a.png\"],\"emptySections\":[\"section#s is visible but empty\"],\"a11y\":[\"img without alt\"]}");
        var issues = ElementInventory.HealthIssues(h);
        Assert.That(issues.Select(i => (i.Kind, i.Excerpt)), Is.EqualTo(new[]
        {
            ("broken-image", "image did not load: /a.png"), ("empty-section", "section#s is visible but empty"), ("a11y", "img without alt")
        }));
    }

    [TestCase("http://localhost:5200/p", "http://localhost:5200/api/x", "fetch", true)]
    [TestCase("http://localhost:5200/p", "http://cdn.example.com/x.js", "script", true)]
    [TestCase("http://localhost:5200/p", "http://tracker.example.com/beacon", "xhr", false)]
    [TestCase("http://localhost:5200/p", "http://localhost:5100/x", "fetch", false)]
    public void FailedRequest_Scope(string page, string url, string type, bool expected) =>
        Assert.That(PageHealth.ReportRequest(page, url, type), Is.EqualTo(expected));

    [TestCase("net::ERR_ABORTED", true)]
    [TestCase("NS_BINDING_ABORTED", true)]
    [TestCase("cancelled", true)]
    [TestCase("net::ERR_CONNECTION_REFUSED", false)]
    [TestCase("net::ERR_NAME_NOT_RESOLVED", false)]
    [TestCase(null, false)]
    public void FailedRequest_BrowserAborts_Ignored(string? failure, bool aborted) =>
        Assert.That(PageHealth.IsAborted(failure), Is.EqualTo(aborted));
}
