using System.Text.Json.Nodes;
using MigrationTests.Playwright;
using NUnit.Framework;
using static MigrationTests.Reporting.ElementMatcherTests;

namespace MigrationTests.Reporting;

/// <summary>
/// Page-level CSS groups (CssGrouper): grouping key, theme vs individual, page-level dedupe and ownership, audit
/// coverage (every raw row in exactly one group), fixer task text, and the reports / chat / final views built on them.
/// </summary>
[TestFixture]
public class CssGroupingTests
{
    private static CssDiffEntry Row(string element, string property, string reference, string target, string? role = "button",
        string severity = "MEDIUM", string layer = CssLayers.Style, string state = "page_load", bool accepted = false) => new()
    {
        Element = element, State = state, Property = property, Reference = reference, Target = target, Role = role,
        Severity = severity, Layer = layer, Accepted = accepted, AcceptedReason = accepted ? "agreed" : null,
        AcceptedBy = accepted ? "css-map" : null, Actionable = !accepted && severity is "HIGH" or "MEDIUM"
    };

    private static readonly string[] Order = { "A", "B", "C" };

    private static StackProfile Profile() => new() { RoleEquivalences = { new() { "gridcell", "cell" }, new() { "textbox", "combobox" } } };

    [Test]
    public void ThemeKey_RoleClassAndNormalizedValues()
    {
        var rows = new List<(string, CssDiffEntry)>
        {
            ("A", Row("button 'Save'", "border-radius", "3px", "4px")),
            ("A", Row("button 'Add'", "border-radius", "3px", "4.1px")),              // 4.1px normalizes to 4px
            ("A", Row("button 'Undo'", "border-radius", "3px", "8px")),               // other values: own group
            ("A", Row("gridcell cells", "color", "#000", "rgb(66, 66, 66)", role: "gridcell")),
            ("A", Row("cell cells", "color", "rgb(0, 0, 0)", "rgb(66,66,66)", role: "cell")), // role equivalence + color normalization
            ("A", Row("Header", "color", "red", "blue", role: null, layer: CssLayers.Mapped)),
            ("A", Row("Footer", "color", "red", "blue", role: null, layer: CssLayers.Mapped)), // role-less rows never merge
        };
        var r = CssGrouper.Build(rows, Order, Profile());
        Assert.That(r.Groups, Has.Count.EqualTo(5));
        var radius = r.Groups.Single(g => g.Property == "border-radius" && g.ElementCount == 2);
        Assert.That((radius.Kind, radius.RoleClass, radius.Elements), Is.EqualTo((CssGroup.Theme, "button", new List<string> { "button 'Add'", "button 'Save'" })));
        var cells = r.Groups.Single(g => g.RoleClass == "cell");
        Assert.That(cells.ElementCount, Is.EqualTo(2));
        Assert.That(r.Groups.Count(g => g.RoleClass.StartsWith("el:")), Is.EqualTo(2));
    }

    [Test]
    public void PageLevelDedupe_OwnerIsFirstModuleInPlanOrder()
    {
        var rows = new List<(string, CssDiffEntry)>
        {
            ("C", Row("button 'Save'", "font-size", "16px", "24px")),
            ("C", Row("button 'Save'", "font-size", "16px", "24px", state: "sorted")),
            ("A", Row("button 'Save'", "font-size", "16px", "24px")),
            ("C", Row("button 'Save'", AutoCssComparer.MissingProperty, "present", "missing", severity: "HIGH", layer: CssLayers.Match)),
            ("C", Row("button 'Save'", AutoCssComparer.MissingProperty, "present", "missing", severity: "HIGH", layer: CssLayers.Match, state: "sorted")),
            ("A", Row("button 'Save'", AutoCssComparer.MissingProperty, "present", "missing", severity: "HIGH", layer: CssLayers.Match)),
            ("C", Row("button 'Go'", AutoCssComparer.MissingProperty, "present", "missing", severity: "HIGH", layer: CssLayers.Match)),
        };
        var r = CssGrouper.Build(rows, Order, null);
        Assert.That(r.Groups, Has.Count.EqualTo(3), "theme row and the identical missing rows collapse across states and modules");
        var font = r.Groups.Single(g => g.Property == "font-size");
        Assert.That((font.Owner, font.Modules, font.States, font.RowCount), Is.EqualTo(("A", new List<string> { "A", "C" }, new List<string> { "page_load", "sorted" }, 3)));
        var missing = r.Groups.Where(g => g.Kind == CssGroup.Individual).ToList();
        Assert.That(missing.Select(g => (g.Elements.Single(), g.Owner)), Is.EquivalentTo(new[] { ("button 'Save'", "A"), ("button 'Go'", "C") }));
    }

    [Test]
    public void EveryRawRowMapsToExactlyOneGroup_AndAuditKeepsAllRows()
    {
        var rows = new List<(string, CssDiffEntry)>();
        foreach (var m in Order)
            for (var i = 0; i < 7; i++)
            {
                rows.Add((m, Row($"button 'B{i}'", "padding", "1px", $"{12 + i % 3}px")));
                rows.Add((m, Row($"button 'B{i}'", "layout.order", $"position {i + 1} of 7", "position 1 of 7", severity: "HIGH", layer: CssLayers.Layout)));
                rows.Add((m, Row($"button 'B{i}'", "cursor", "default", "pointer", accepted: i % 2 == 0)));
            }
        var r = CssGrouper.Build(rows, Order, null);
        Assert.That(r.Rows, Has.Count.EqualTo(rows.Count));
        Assert.That(r.Rows.All(x => x.GroupId.Length > 0 && x.GroupKey.Length == 10));
        var ids = r.Groups.Select(g => g.Id).ToHashSet();
        Assert.That(r.Rows.Select(x => x.GroupId).All(ids.Contains));
        Assert.That(r.Groups.Sum(g => g.RowCount), Is.EqualTo(rows.Count));
        Assert.That(rows.All(x => x.Item2.GroupId != null && r.Groups.Single(g => g.Id == x.Item2.GroupId).Key == x.Item2.GroupKey));
        // Accepted rows never merge with open ones.
        var cursor = r.Groups.Where(g => g.Property == "cursor").ToList();
        Assert.That(cursor.Select(g => (g.Accepted, g.ElementCount)), Is.EquivalentTo(new[] { (true, 4), (false, 3) }));
        Assert.That(cursor.Single(g => g.Accepted).Actionable, Is.False);
    }

    [Test]
    public void GroupSeverity_IsMaxOfMembers_AndIdsAreDeterministic()
    {
        var rows = new List<(string, CssDiffEntry)>
        {
            ("A", Row("button 'A'", "color", "red", "blue", severity: "LOW")),
            ("B", Row("button 'B'", "color", "red", "blue", severity: "HIGH")),
            ("A", Row("button 'C'", "font-size", "14px", "14.4px", severity: "LOW")),
        };
        var r1 = CssGrouper.Build(rows, Order, null);
        var color = r1.Groups.Single(g => g.Property == "color");
        Assert.That((color.Severity, color.Actionable, color.Id), Is.EqualTo(("HIGH", true, "G1")));
        var low = r1.Groups.Single(g => g.Property == "font-size");
        Assert.That((low.Severity, low.Actionable), Is.EqualTo(("LOW", false)));
        Assert.That(r1.Rows.Where(x => x.Severity == "HIGH").All(x => r1.Groups.Single(g => g.Id == x.GroupId).Severity == "HIGH"));

        var again = CssGrouper.Build(rows.AsEnumerable().Reverse().Select(x => (x.Item1, Clone(x.Item2))).ToList(), Order, null);
        Assert.That(again.Groups.Select(g => (g.Id, g.Key)), Is.EqualTo(r1.Groups.Select(g => (g.Id, g.Key))));
    }

    private static CssDiffEntry Clone(CssDiffEntry d) => new()
    {
        Element = d.Element, State = d.State, Property = d.Property, Reference = d.Reference, Target = d.Target, Role = d.Role,
        Severity = d.Severity, Layer = d.Layer, Accepted = d.Accepted, Actionable = d.Actionable
    };

    [Test]
    public void FixerTask_OneThemeRulePerGroup_ReferenceLargerKept()
    {
        var rows = Enumerable.Range(0, 46).Select(i => ("A", Row($"button 'B{i}'", "border-radius", "3px", "4px"))).ToList();
        var g = CssGrouper.Build(rows, Order, null).Groups.Single();
        Assert.That(g.Action, Does.StartWith("Buttons: border-radius 4px -> 3px, 46 elements: change one CSS rule / theme token in the Target"));

        var spacing = Row("button 'Go'", "layout.spacing", "80px", "10px", layer: CssLayers.Layout);
        spacing.Action = ModuleReportGenerator.ActionFor(spacing);
        var ind = CssGrouper.Build(new List<(string, CssDiffEntry)> { ("A", spacing) }, Order, null).Groups.Single();
        Assert.That(ind.Action, Does.StartWith("Reference larger: layout.spacing Reference 80px vs Target 10px"));
        Assert.That(ind.Kind, Is.EqualTo(CssGroup.Individual));
    }

    [TestCase("combobox", "Comboboxes")]
    [TestCase("checkbox", "Checkboxes")]
    [TestCase("button", "Buttons")]
    [TestCase("columnheader", "Columnheaders")]
    [TestCase("", "Elements")]
    [TestCase("el:Grid pager", "Grid pager")]
    public void Plural_RoleClassLabel(string roleClass, string expected)
        => Assert.That(CssGrouper.Plural(roleClass), Is.EqualTo(expected));

    // ─────────────────────────── page pass through Generate ───────────────────────────

    private string _pageDir = "";

    [SetUp]
    public void SetUp()
    {
        _pageDir = Path.Combine(Path.GetTempPath(), "grp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pageDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_pageDir)) Directory.Delete(_pageDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    private static InventoryFile Inv(params InventoryElement[] els) => new()
    {
        State = "page_load", Viewport = new InventoryViewport { Width = 1920, Height = 1080, ScrollWidth = 1920 }, Elements = els.ToList()
    };

    /// <summary>Module X with n buttons whose font-size differs (16px vs 16+2k px: one group per button) plus a shared radius diff.</summary>
    private void WriteModule(string x, int buttons)
    {
        var r = new List<InventoryElement>();
        var t = new List<InventoryElement>();
        for (var k = 0; k < buttons; k++)
        {
            var rb = El(k, "button", $"B{k}", x: k * 100, order: k, interactive: true);
            rb.Styles = new() { ["font-size"] = "16px", ["border-radius"] = "3px" };
            var tb = El(k, "button", $"B{k}", x: k * 100, order: k, interactive: true);
            tb.Styles = new() { ["font-size"] = $"{18 + 2 * k}px", ["border-radius"] = "8px" };
            r.Add(rb);
            t.Add(tb);
        }
        var run = Path.Combine(_pageDir, "runs", $"Module{x}");
        ReportJson.Write(Path.Combine(run, "Reference", "css", "inventory_page_load.json"), Inv(r.ToArray()));
        ReportJson.Write(Path.Combine(run, "Target", "css", "inventory_page_load.json"), Inv(t.ToArray()));
        ReportJson.Write(Path.Combine(run, "Reference", "css", "css_mapped_page_load.json"), new Dictionary<string, object>());
        ReportJson.Write(Path.Combine(run, "Target", "css", "css_mapped_page_load.json"), new Dictionary<string, object>());
    }

    private void WritePlan(params string[] modules)
    {
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "css-map.json"), new CssMapFile());
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Page = "P", Modules = modules.Select(m => new PlanModule { Id = m, Name = "Module " + m, CssStates = new() { "page_load" } }).ToList()
        });
    }

    [Test]
    public void Generate_TwoModulesSharingRows_OwnerCountsGroups_OtherReferencesThem()
    {
        WritePlan("A", "B", "C");
        WriteModule("A", 1);
        WriteModule("C", 1);
        var vc = new VisualChecksSettings();

        // C reports first (parallel batch finish order): it owns the groups until A reports.
        var c0 = ModuleReportGenerator.Generate(_pageDir, "C", vc).Single();
        Assert.That(c0.ActionableCssDiffs, Is.EqualTo(2));

        var a = ModuleReportGenerator.Generate(_pageDir, "A", vc).Single();
        var c = ReportJson.Read<ModuleSummary>(Path.Combine(_pageDir, "runs", "ModuleC", "summary.json"))!;
        Assert.That((a.ActionableCssDiffs, a.ActionableCssRows), Is.EqualTo((2, 2)));
        Assert.That((c.ActionableCssDiffs, c.ActionableCssRows), Is.EqualTo((0, 2)), "C's rows belong to A's groups");
        Assert.That(c.TargetStatus, Is.EqualTo("FAIL"), "a module with raw rows never passes");
        Assert.That(c.CssGroupsSeen, Is.EqualTo(a.CssGroupsSeen));
        Assert.That(c.CssGroupsOwned, Is.Empty);

        var cMd = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleC_CSS_Diff.md"));
        Assert.That(cMd, Does.Contain("| Group | Elements | Property | Reference | Target | Severity | Seen in | Action |"));
        Assert.That(cMd, Does.Contain("see page-level group G1 (owned by Module A)"));
        Assert.That(cMd, Does.Contain("Actionable CSS groups owned by Module C: 0"));
        var aMd = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(aMd, Does.Contain("| 1: button 'B0' | font-size | 16px | 18px | MEDIUM | A, C [page_load] | Buttons: font-size 18px -> 16px, 1 element: change one CSS rule / theme token in the Target"));
        Assert.That(aMd, Does.Contain("## Individual findings"));
        Assert.That(aMd, Does.Contain("runs/css_rows_all.md"));

        var groups = ReportJson.Read<CssGroupsFile>(Path.Combine(_pageDir, "runs", "css_groups.json"))!;
        Assert.That((groups.RawRows, groups.ActionableGroups, groups.Modules), Is.EqualTo((4, 2, new List<string> { "A", "C" })));
        var audit = JsonNode.Parse(File.ReadAllText(Path.Combine(_pageDir, "runs", "css_rows_all.json")))!.AsArray();
        Assert.That(audit.Count, Is.EqualTo(a.CssDiffs.Count + c.CssDiffs.Count));
        Assert.That(audit.All(n => n!["groupId"]!.GetValue<string>().StartsWith("G")));
        var auditMd = File.ReadAllText(Path.Combine(_pageDir, "runs", "css_rows_all.md"));
        Assert.That(auditMd, Does.Contain("font-size diff 2px > 1px -> MEDIUM"), "audit shows the rule that set the severity");
        Assert.That(File.ReadAllText(Path.Combine(_pageDir, "reports", "CSS_Groups.md")), Does.Contain("Actionable groups: 2 of 2"));
        // Summary keys for lifecycle.ps1.
        var json = File.ReadAllText(Path.Combine(_pageDir, "runs", "ModuleC", "summary.json"));
        Assert.That(json, Does.Contain("\"actionableCssRows\": 2").And.Contain("\"groupKey\""));
    }

    [Test]
    public void ChatAndFinal_TopGroupsCapped_FinalReportHasGroupsNotRawRows()
    {
        WritePlan("A");
        WriteModule("A", 20);
        ModuleReportGenerator.Generate(_pageDir, "ALL", new VisualChecksSettings());
        var groups = ReportJson.Read<CssGroupsFile>(Path.Combine(_pageDir, "runs", "css_groups.json"))!;
        Assert.That(groups.ActionableGroups, Is.EqualTo(21), "20 font-size groups + 1 shared border-radius group");

        var (parts, _) = ChatRenderer.RenderParts(_pageDir, "module-result", "A", "Phase 3", ChatRendererTests.Now);
        var md = string.Join(Environment.NewLine, parts.Select(p => p.Markdown));
        var css = md[md.IndexOf("### CSS Diffs", StringComparison.Ordinal)..];
        Assert.That(css, Does.Contain("| Group | Elements | Property | Reference | Target | Severity | Seen in |"));
        Assert.That(css.Split('\n').Count(l => GroupRow.IsMatch(l)), Is.EqualTo(15));
        Assert.That(css, Does.Contain("6 more in `reports/CSS_Groups.md`"));
        Assert.That(css, Does.Contain("| G1 | 20: button 'B0', button 'B1', button 'B10', ... | "), "most elements first within a severity");

        var (fparts, _) = ChatRenderer.RenderParts(_pageDir, "final", null, "Phase 4", ChatRendererTests.Now);
        var fmd = string.Join(Environment.NewLine, fparts.Select(p => p.Markdown));
        var fcss = fmd[fmd.IndexOf("### CSS Comparison", StringComparison.Ordinal)..];
        Assert.That(fcss.Split('\n').Count(l => GroupRow.IsMatch(l)), Is.EqualTo(15));
        Assert.That(fcss, Does.Contain("6 more in reports/CSS_Groups.md"));

        var final = File.ReadAllText(Path.Combine(_pageDir, "reports", "FinalReport.md"));
        var section = final[final.IndexOf("### CSS Comparison", StringComparison.Ordinal)..final.IndexOf("### Bug Fixes", StringComparison.Ordinal)];
        Assert.That(section.Split('\n').Count(l => GroupRow.IsMatch(l)), Is.EqualTo(21), "uncapped: every actionable group");
        Assert.That(final, Does.Contain("actionable CSS groups 21 (raw rows 40)"));
        Assert.That(final, Does.Not.Contain("| Module | Element | State | Property |"), "raw rows stay in runs/css_rows_all.md/json");
        var unresolved = final[final.IndexOf("## UNRESOLVED", StringComparison.Ordinal)..];
        Assert.That(unresolved, Does.Contain("| Issue | Group | Owner | Elements | States | Property | Reference | Target | Severity | Seen in |"));
    }

    [Test]
    public void FixRound_CssGroupWhat_UsesIssueSentence_EarlierKeysSummarized()
    {
        WritePlan("A");
        WriteModule("A", 1);
        ModuleReportGenerator.Generate(_pageDir, "ALL", new VisualChecksSettings());
        var g = ReportJson.Read<CssGroupsFile>(Path.Combine(_pageDir, "runs", "css_groups.json"))!.Groups.First(x => x.Actionable);
        Assert.That(g.Issue, Is.Not.Empty, "css_groups.json carries the Issue sentence for the fixer");
        File.WriteAllText(Path.Combine(_pageDir, "status.json"), "{\"fixLoop\":{\"round\":1,\"maxRounds\":2}}");
        ReportJson.Write(Path.Combine(_pageDir, "fixes.json"), new List<FixEntry>
        {
            new() { Round = 1, Module = "A", FindingIds = new() { "css:group:" + g.Key, "css:group:0000000000" }, Kind = "app", File = "x.razor", Change = "c", Before = "a", After = "b" }
        });
        var md = ChatRenderer.Render(_pageDir, "fix-round", null, "Phase 4", ChatRendererTests.Now).Markdown;
        Assert.That(md, Does.Contain($"CSS group {g.Id}: {g.Issue.TrimEnd('.')}; 1 CSS issue from an earlier run"));
        Assert.That(md, Does.Contain("1 CSS issue from an earlier run, no longer in the current CSS groups"));
    }

    /// <summary>A group row: the Issue sentence first, then the group id.</summary>
    private static readonly System.Text.RegularExpressions.Regex GroupRow = new(@"^\| [^|]+ \| G\d+ \|");

    // ─────────────────────────── grouping refinements ───────────────────────────

    [Test]
    public void BorderSides_IdenticalValues_MergeIntoOneBorderGroup()
    {
        var rows = new[] { "top", "right", "bottom", "left" }
            .Select(s => ("A", Row("button 'Save'", "border-" + s, "1px solid rgb(204, 204, 204)", "0px none rgb(0, 0, 0)"))).ToList();
        rows.Add(("A", Row("button 'Add'", "border-top", "1px solid rgb(204, 204, 204)", "0px none rgb(0, 0, 0)")));
        rows.Add(("A", Row("button 'Add'", "border-bottom", "1px solid rgb(204, 204, 204)", "0px none rgb(0, 0, 0)")));
        var r = CssGrouper.Build(rows, Order, null);
        var all = r.Groups.Single(g => g.Property == "border");
        Assert.That((all.Elements.Single(), all.RowCount), Is.EqualTo(("button 'Save'", 4)));
        var two = r.Groups.Single(g => g.Property == "border (top, bottom)");
        Assert.That(two.Elements.Single(), Is.EqualTo("button 'Add'"));
        Assert.That(all.Issue, Is.EqualTo("The border of the 'Save' button differs when the page first loads: a 1px solid light gray line on the old page (Reference) and no border on the new page (Target)."));
        Assert.That(two.Issue, Does.StartWith("The top and bottom border of the 'Add' button differs"));
        Assert.That(all.Action, Does.Contain("Align to reference: set border: 1px solid rgb(204, 204, 204)"));
        Assert.That(two.Action, Does.Contain("set border-top, border-bottom: 1px solid rgb(204, 204, 204)"));
        Assert.That(r.Rows.Select(x => x.Property), Is.EquivalentTo(rows.Select(x => x.Item2.Property)), "raw audit rows keep their side");
    }

    [Test]
    public void BorderSides_DifferentValues_StaySeparate()
    {
        var rows = new List<(string, CssDiffEntry)>
        {
            ("A", Row("button 'Save'", "border-top", "1px solid rgb(0, 0, 0)", "2px solid rgb(0, 0, 0)")),
            ("A", Row("button 'Save'", "border-bottom", "1px solid rgb(0, 0, 0)", "3px solid rgb(0, 0, 0)")),
        };
        var r = CssGrouper.Build(rows, Order, null);
        Assert.That(r.Groups.Select(g => g.Property), Is.EquivalentTo(new[] { "border-top", "border-bottom" }));
        Assert.That(r.Groups.Single(g => g.Property == "border-top").Issue, Does.StartWith("The top border of the 'Save' button differs"));
    }

    [Test]
    public void StateFold_HoverEqualToBase_JoinsBaseGroup_DifferentHoverStaysSeparate()
    {
        var rows = new List<(string, CssDiffEntry)>
        {
            ("A", Row("button 'Save'", "background-color", "rgb(255, 255, 255)", "rgb(25, 118, 210)")),
            ("A", Row("button 'Save'", "hover.background-color", "rgb(255, 255, 255)", "rgb(25, 118, 210)", layer: CssLayers.State, severity: "HIGH")),
            ("A", Row("button 'Save'", "focus.background-color", "rgb(255, 255, 255)", "rgb(25, 118, 210)", layer: CssLayers.State)),
            ("A", Row("button 'Add'", "background-color", "rgb(255, 255, 255)", "rgb(25, 118, 210)")),
            ("A", Row("button 'Add'", "hover.background-color", "rgb(230, 230, 230)", "rgb(21, 101, 192)", layer: CssLayers.State, severity: "HIGH")),
        };
        var r = CssGrouper.Build(rows, Order, null);
        var bg = r.Groups.Single(g => g.Property == "background-color");
        Assert.That((bg.ElementCount, bg.RowCount, bg.AlsoIn), Is.EqualTo((2, 4, new List<string> { "hover", "focus" })));
        Assert.That(bg.Severity, Is.EqualTo("HIGH"), "a folded HIGH row keeps the group HIGH");
        Assert.That(bg.Issue, Does.EndWith(" Also when hovered and when focused."));
        Assert.That(bg.Action, Does.Contain("(same values on :hover/:focus)"));
        var hover = r.Groups.Single(g => g.Property == "hover.background-color");
        Assert.That((hover.Elements.Single(), hover.AlsoIn.Count), Is.EqualTo(("button 'Add'", 0)));
        Assert.That(hover.Issue, Does.StartWith("The 'Add' button looks different when hovered"));
        Assert.That(r.Rows.All(x => x.GroupId.Length > 0), "every raw row still has one group");

        var again = CssGrouper.Build(rows.AsEnumerable().Reverse().Select(x => (x.Item1, Clone(x.Item2))).ToList(), Order, null);
        Assert.That(again.Groups.Select(g => (g.Key, g.Property, g.RowCount)), Is.EquivalentTo(r.Groups.Select(g => (g.Key, g.Property, g.RowCount))), "deterministic on reversed input");
    }

    [Test]
    public void ReferenceWorseResponsive_NotActionable_ListedSeparately()
    {
        var page = new CssDiffEntry
        {
            Element = "(page)", State = "page_load", Property = "responsive.768.overflow", Reference = "scrolls horizontally (1884px wide)",
            Target = "fits 768px", Severity = "HIGH", Layer = CssLayers.Responsive
        };
        var clipped = new CssDiffEntry
        {
            Element = "checkbox 'Active'", State = "page_load", Property = "responsive.768.hidden", Reference = "fully visible",
            Target = "clipped by its container", Severity = "HIGH", Layer = CssLayers.Responsive, Role = "checkbox"
        };
        var rows = new List<CssDiffEntry> { page, clipped };
        ModuleReportGenerator.ApplyAcceptance(rows, null, new StackProfile());
        foreach (var d in rows) d.Action = ModuleReportGenerator.ActionFor(d);
        Assert.That((page.Actionable, clipped.Actionable), Is.EqualTo((false, true)));
        Assert.That(page.Action, Does.StartWith("Reference larger: responsive.768.overflow"));

        var r = CssGrouper.Build(rows.Select(d => ("A", d)).ToList(), Order, null);
        var g = r.Groups.Single(x => x.Elements.Single() == "(page)");
        Assert.That((g.ReferenceWorse, g.Actionable), Is.EqualTo((true, false)));
        Assert.That(g.Issue, Is.EqualTo("At a narrow window (768px) the old page (Reference) scrolls sideways (1884px wide) but the new page (Target) fits when the page first loads. This is an old-page problem; nothing to fix on the new page."));
        var c = r.Groups.Single(x => x.Elements.Single() == "checkbox 'Active'");
        Assert.That(c.Issue, Is.EqualTo("At a narrow window (768px) the 'Active' checkbox is partly cut off (clipped by its container) on the new page (Target) when the page first loads; on the old page (Reference) it is fully visible."));

        var md = ModuleReportGenerator.BuildCssGroupsReport("P", "generic", r);
        var worse = md[md.IndexOf("## Reference worse (not counted)", StringComparison.Ordinal)..md.IndexOf("## Accepted groups", StringComparison.Ordinal)];
        Assert.That(worse, Does.Contain("| (page) [page_load] | responsive.768.overflow |"));
        var individual = md[md.IndexOf("## Individual findings", StringComparison.Ordinal)..md.IndexOf("## Reference worse", StringComparison.Ordinal)];
        Assert.That(individual, Does.Not.Contain("(page)"));
        Assert.That(individual, Does.Contain("checkbox 'Active'"));
    }
}
