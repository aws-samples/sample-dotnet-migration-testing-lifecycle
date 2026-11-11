using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>P7: plain-English step descriptions from plan.json fieldLabels with a humanizing fallback.</summary>
[TestFixture]
public class FieldLabelsTests
{
    private static readonly FieldLabels PayorLabels = new(new Dictionary<string, string>
    {
        ["row[{n}].name.text"] = "Payor Name in row ID {n}",
        ["saveButton.enabled"] = "Save button",
        ["cancelButton.enabled"] = "Cancel button",
        ["pager.info"] = "Pager text",
        ["grid.rowCount"] = "Rows shown",
        ["changeCount"] = "Unsaved change count",
        ["message.text"] = "Message",
        ["sort.indicator[{name}]"] = "Sort indicator on {name}"
    });

    private static RunAssertion A(string field, string type, string expected, string actual, bool passed = true, string message = "") =>
        new() { Field = field, Type = type, Expected = expected, Actual = actual, Passed = passed, Message = message };

    [TestCase("layout.order", "reading order")]
    [TestCase("hover.background-color", "background-color on hover")]
    [TestCase("focus.outline", "outline on focus")]
    [TestCase("disabled.cursor", "cursor when disabled")]
    [TestCase("responsive.768.overflow", "overflows at 768px width")]
    [TestCase("responsive.768.lines", "wraps differently at 768px width")]
    [TestCase("(missing on Target)", "missing on Target")]
    [TestCase("(extra on Target)", "extra on Target")]
    [TestCase("page.failedRequest", "failed network request")]
    [TestCase("text.placeholder", "placeholder text")]
    [TestCase("color", "color")]
    public void PropertyText_PlainEnglish(string property, string expected) =>
        Assert.That(FieldLabels.PropertyText(property), Is.EqualTo(expected));

    [Test]
    public void PropertyAudit_KeepsRawProperty()
    {
        Assert.That(FieldLabels.PropertyAudit("layout.order"), Is.EqualTo("reading order (layout.order)"));
        Assert.That(FieldLabels.PropertyAudit("font-weight"), Is.EqualTo("font-weight"));
        Assert.That(new FieldLabels().Label("page.brokenImage"), Is.EqualTo("Broken image"));
    }

    [Test]
    public void Label_PatternWithPlaceholders()
    {
        Assert.That(PayorLabels.Label("row[3].name.text"), Is.EqualTo("Payor Name in row ID 3"));
        Assert.That(PayorLabels.Label("row[12].name.text"), Is.EqualTo("Payor Name in row ID 12"));
        Assert.That(PayorLabels.Label("sort.indicator[Code]"), Is.EqualTo("Sort indicator on Code"));
        Assert.That(PayorLabels.Label("grid.rowCount"), Is.EqualTo("Rows shown"));
        Assert.That(PayorLabels.Label("grid.row[3].name.text"), Is.EqualTo("Grid row 3 name"), "anchored: pattern must not match a longer field");
    }

    [Test]
    public void Label_FallbackHumanizes()
    {
        Assert.That(FieldLabels.Humanize("grid.rowCount"), Is.EqualTo("Grid row count"));
        Assert.That(FieldLabels.Humanize("row[3].name.text"), Is.EqualTo("Row 3 name"));
        Assert.That(FieldLabels.Humanize("sort.indicator[Name]"), Is.EqualTo("Sort indicator Name"));
        Assert.That(FieldLabels.Humanize("pager.info.emptyIndicator"), Is.EqualTo("Pager info empty indicator"));
        var none = new FieldLabels();
        Assert.That(none.Label("saveButton.enabled"), Is.EqualTo("Save button enabled"));
    }

    [Test]
    public void Describe_ModuleDStep2_PlainEnglish()
    {
        // Assertions as recorded by ModuleTestRunner for PayorList Module D Step2 (Click Cancel).
        var step = new RunStep
        {
            Step = "Step2", Status = "PASS", Title = "Click Cancel",
            Assertions =
            {
                A("message.text", "CHANGES", "", "Changes discarded."),
                A("message.text", "EQUALS", "Changes discarded.", "Changes discarded."),
                A("row[3].name.text", "CHANGES", "Citrine EDITED", "Citrine Valley Healthcare"),
                A("row[3].name.text", "EQUALS", "Citrine Valley Healthcare", "Citrine Valley Healthcare"),
                A("changeCount", "CHANGES", "1", "0"),
                A("changeCount", "EQUALS", "0", "0"),
                A("saveButton.enabled", "CHANGES", "true", "false"),
                A("cancelButton.enabled", "CHANGES", "true", "false")
            }
        };
        var text = PayorLabels.Describe(step, "PASS", isTarget: false);
        Assert.That(text, Does.StartWith("Click Cancel. "));
        Assert.That(text, Does.Contain("Payor Name in row ID 3 changed from 'Citrine EDITED' to 'Citrine Valley Healthcare'."));
        Assert.That(text, Does.Contain("Save button and Cancel button became disabled."));
        Assert.That(text, Does.Contain("Message shown: 'Changes discarded.' "));
        Assert.That(text, Does.Not.Contain(".'."));
        Assert.That(text, Does.Contain("Unsaved change count changed from 1 to 0."));
        Assert.That(text, Does.Not.Contain("row["));
        Assert.That(text, Does.Not.Contain("is 'Citrine Valley Healthcare'"), "EQUALS on a changed field is not repeated");

        var audit = PayorLabels.Describe(step, "PASS", isTarget: false, withFieldNames: true);
        Assert.That(audit, Does.Contain("Save button and Cancel button became disabled (fields: saveButton.enabled, cancelButton.enabled)."));
    }

    [Test]
    public void Describe_FailLeadsWithMismatch()
    {
        var step = new RunStep
        {
            Step = "Step1", Status = "FAIL", Title = "Open the page",
            Assertions =
            {
                A("grid.rowCount", "EQUALS", "10", "5", passed: false),
                A("pager.info", "STAYS SAME", "View 1 - 10 of 15", "View 1 - 5 of 15", passed: false),
                A("saveButton.enabled", "EQUALS", "false", "false")
            }
        };
        var text = PayorLabels.Describe(step, "FAIL", isTarget: true);
        Assert.That(text, Does.StartWith("Rows shown is 5, expected 10 (Reference). Pager text changed unexpectedly from 'View 1 - 10 of 15' to 'View 1 - 5 of 15'. Open the page."));
        Assert.That(text, Does.EndWith("Save button is disabled."));
    }

    [Test]
    public void Describe_BooleansAndClearedValues()
    {
        var step = new RunStep
        {
            Step = "Step2", Status = "PASS", Title = "Click Clear",
            Assertions =
            {
                A("nameFilter.value", "CHANGES", "Mesa", ""),
                A("row[1].name.editControl.visible", "CHANGES", "false", "true"),
                A("saveButton.enabled", "STAYS SAME", "false", "false"),
                A("grid.visible", "BOOL", "true", "true", message: "Grid is visible")
            }
        };
        var text = new FieldLabels().Describe(step, "PASS", isTarget: false);
        Assert.That(text, Is.EqualTo("Click Clear. Name filter value cleared. Row 1 name edit control is now visible. Save button stayed disabled. Grid is visible."));
    }

    [Test]
    public void ModuleResult_ChatHasNoTechnicalFieldNames()
    {
        var dir = ChatRendererTests.CopyFixture();
        try
        {
            var md = ChatRenderer.Render(dir, "module-result", "A", "progress", ChatRendererTests.Now).Markdown;
            Assert.That(md, Does.Contain("Pager text is '1-10 of 15', expected 'View 1 - 10 of 15' (Reference)."));
            Assert.That(md, Does.Contain("Payor Name in grid row 1 changed from 'Acme' to 'Bluebird'."));
            Assert.That(md, Does.Not.Contain("row["));
            Assert.That(md, Does.Not.Contain("grid.row1.name"));
            Assert.That(md, Does.Not.Contain("CHANGED:"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); }
    }

    [Test]
    public void SortStates_PlainWords_InStepText()
    {
        var step = new RunStep
        {
            Step = "Step10", Status = "PASS", Title = "Sort by Payor Name",
            Assertions = { A("sort.indicator[Name]", "EQUALS", "asc", "asc"), A("sort.indicator[Code]", "CHANGES", "desc", "none") }
        };
        Assert.That(PayorLabels.Describe(step, "PASS", isTarget: false),
            Is.EqualTo("Sort by Payor Name. Sort indicator on Name is ascending. Sort indicator on Code changed from descending to not sorted."));
    }

    [TestCase("sort.indicator[Name]", "asc", "ascending")]
    [TestCase("sort.indicator[Name]", "none", "not sorted")]
    [TestCase("saveButton.enabled", "false", "disabled")]
    [TestCase("grid.visible", "true", "visible")]
    [TestCase("pager.info", "(not asserted)", "not checked on the new page (no matching check in the Target test)")]
    [TestCase("pager.info", "View 1 - 10 of 15", "View 1 - 10 of 15")]
    [TestCase("grid.column[Name].sequence", "asc", "asc")]
    public void ValueText_PlainWordsForTokens(string field, string value, string expected)
        => Assert.That(FieldLabels.ValueText(field, value), Is.EqualTo(expected));
}
