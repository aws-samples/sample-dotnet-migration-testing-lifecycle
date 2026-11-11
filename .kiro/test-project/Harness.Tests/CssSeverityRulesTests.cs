using MigrationTests.Playwright;
using NUnit.Framework;
using static MigrationTests.Reporting.ElementMatcherTests;

namespace MigrationTests.Reporting;

/// <summary>Severity recalibration (CssSeverityRules) and the VisualChecks.Severity / Groups settings.</summary>
[TestFixture]
public class CssSeverityRulesTests
{
    private static CssDiffEntry Row(string property, string reference, string target, string severity = "MEDIUM",
        string layer = CssLayers.Style, string? role = "button", bool interactive = false) => new()
    {
        Element = "button 'Save'", State = "page_load", Property = property, Reference = reference, Target = target,
        Severity = severity, Layer = layer, Role = role, Interactive = interactive, Actionable = severity is "HIGH" or "MEDIUM"
    };

    private static CssDiffEntry Apply(CssDiffEntry d)
    {
        CssSeverityRules.Apply(new[] { d }, new CssSeveritySettings());
        return d;
    }

    [TestCase("font-size", "14.4px", "14px", "LOW", "font-size diff 0.4px <= 1px -> LOW")]
    [TestCase("font-size", "13.3333px", "14px", "LOW", "font-size diff 0.67px <= 1px -> LOW")]
    [TestCase("font-size", "14.4px", "16px", "MEDIUM", "font-size diff 1.6px > 1px -> MEDIUM")]
    [TestCase("border-radius", "3px", "4px", "LOW", "border-radius diff 1px <= 1px -> LOW")]
    [TestCase("border-radius", "3px", "6px", "MEDIUM", "border-radius diff 3px > 1px -> MEDIUM")]
    [TestCase("padding", "6px 16px", "6px 16px 6px 15px", "LOW", "padding diff 1px <= 1px -> LOW")]
    [TestCase("padding", "1px", "12px", "MEDIUM", "padding diff 11px > 1px -> MEDIUM")]
    [TestCase("margin-top", "0px", "1px", "LOW", "margin-top diff 1px <= 1px -> LOW")]
    [TestCase("border-top-width", "1px", "2px", "LOW", "border-top-width diff 1px <= 1px -> LOW")]
    [TestCase("line-height", "20px", "20.5px", "LOW", "line-height diff 0.5px <= 1px -> LOW")]
    [TestCase("letter-spacing", "0px", "2px", "MEDIUM", "letter-spacing diff 2px > 1px -> MEDIUM")]
    [TestCase("font-weight", "500", "600", "LOW", "font-weight 500 vs 600 -> LOW")]
    [TestCase("font-weight", "600", "500", "LOW", "font-weight 600 vs 500 -> LOW")]
    public void PxAndWeightRules(string property, string reference, string target, string severity, string rule)
    {
        var d = Apply(Row(property, reference, target, "HIGH"));
        Assert.That((d.Severity, d.SeverityRule), Is.EqualTo((severity, rule)));
        Assert.That(d.Actionable, Is.EqualTo(severity != "LOW"));
    }

    [TestCase("line-height", "normal", "24px")]
    [TestCase("border-radius", "3px", "50%")]
    [TestCase("font-weight", "600", "400")]
    [TestCase("opacity", "0.35", "1")]
    [TestCase("color", "inherit", "rgb(0, 0, 0)")]
    public void UnparsableOrUncovered_KeepsSeverity(string property, string reference, string target)
    {
        var d = Apply(Row(property, reference, target, "HIGH"));
        Assert.That((d.Severity, d.SeverityRule, d.Actionable), Is.EqualTo(("HIGH", (string?)null, true)));
    }

    [Test]
    public void Color_DeltaEBands()
    {
        var low = Apply(Row("color", "rgb(69, 69, 69)", "rgb(66, 66, 66)", "HIGH", interactive: true));
        Assert.That(low.Severity, Is.EqualTo("LOW"));
        Assert.That(low.SeverityRule, Does.StartWith("deltaE ").And.EndWith("< 3 -> LOW"));

        var mid = Apply(Row("color", "rgb(51, 51, 51)", "rgb(66, 66, 66)", "HIGH", interactive: true));
        Assert.That(mid.Severity, Is.EqualTo("MEDIUM"));
        Assert.That(mid.SeverityRule, Does.EndWith("3-10 -> MEDIUM"));

        var high = Apply(Row("background-color", "rgb(41, 128, 185)", "rgb(200, 30, 30)", "MEDIUM", interactive: true));
        Assert.That(high.Severity, Is.EqualTo("HIGH"));
        Assert.That(high.SeverityRule, Does.EndWith("> 10 on interactive element -> HIGH"));

        var primary = Apply(Row("hover.color", "red", "blue", "MEDIUM", layer: CssLayers.State, role: "heading"));
        Assert.That(primary.Severity, Is.EqualTo("HIGH"));
        Assert.That(primary.SeverityRule, Does.EndWith("on primary element -> HIGH"));

        var plain = Apply(Row("color", "red", "blue", "HIGH", role: "label"));
        Assert.That(plain.Severity, Is.EqualTo("MEDIUM"), "deltaE > 10 on a non-interactive, non-primary element");

        var border = Apply(Row("border-top-color", "red", "blue", "HIGH", interactive: true));
        Assert.That(border.Severity, Is.EqualTo("MEDIUM"), "HIGH only for text/background colors");
    }

    [Test]
    public void ContrastDrop_High_WinsOverDeltaE()
    {
        var d = Row("color", "rgb(68, 68, 68)", "rgb(120, 120, 120)", "MEDIUM", role: "label");
        d.ContrastReference = 9.7;
        d.ContrastTarget = 4.42;
        Apply(d);
        Assert.That((d.Severity, d.SeverityRule), Is.EqualTo(("HIGH", "contrast 9.7:1 -> 4.42:1 below 4.5:1 -> HIGH")));

        var both = Row("color", "rgb(68, 68, 68)", "rgb(66, 66, 66)", "HIGH", role: "label");
        both.ContrastReference = 3.9; // Reference already fails: no contrast rule
        both.ContrastTarget = 3.8;
        Assert.That(Apply(both).Severity, Is.EqualTo("LOW"));
    }

    [Test]
    public void AutoComparer_SetsContrastAndInteractive_OnColorRows()
    {
        var r = El(0, "button", "Save", interactive: true);
        r.Styles = new() { ["color"] = "rgb(0, 0, 0)", ["background-color"] = "rgb(255, 255, 255)" };
        var t = El(0, "button", "Save");
        t.Styles = new() { ["color"] = "rgb(150, 150, 150)", ["background-color"] = "rgb(255, 255, 255)" };
        var row = AutoCssComparer.Compare("page_load", new InventoryFile { Elements = { r } }, new InventoryFile { Elements = { t } }, null).Rows
            .Single(x => x.Property == "color");
        Assert.That(row.Interactive, Is.True);
        Assert.That(row.ContrastReference, Is.EqualTo(21));
        Assert.That(row.ContrastTarget, Is.LessThan(4.5));
        Apply(row);
        Assert.That(row.Severity, Is.EqualTo("HIGH"));
        Assert.That(row.SeverityRule, Does.StartWith("contrast 21:1 -> "));
    }

    [TestCase(CssLayers.Match, "(missing on Target)", "present", "missing", "HIGH")]
    [TestCase(CssLayers.Layout, "layout.order", "position 1 of 2", "position 2 of 2", "HIGH")]
    [TestCase(CssLayers.Layout, "layout.spacing", "10px", "10.5px", "MEDIUM")]
    [TestCase(CssLayers.Geometry, "layout.rowHeight", "23px", "24px", "MEDIUM")]
    [TestCase(CssLayers.Responsive, "responsive.768.overflow", "fits", "overflows (content cut off)", "HIGH")]
    [TestCase(CssLayers.Mapped, "layout.height", "100px", "101px", "MEDIUM")]
    public void NonStyleLayers_Untouched(string layer, string property, string reference, string target, string severity)
    {
        var d = Apply(Row(property, reference, target, severity, layer: layer));
        Assert.That((d.Severity, d.SeverityRule), Is.EqualTo((severity, (string?)null)));
    }

    [Test]
    public void CssMapExplicitSeverity_Kept()
    {
        var d = Row("font-size", "14.4px", "14px", "HIGH", layer: CssLayers.Mapped);
        d.SeverityRule = "css-map.json severity HIGH";
        Assert.That(Apply(d).Severity, Is.EqualTo("HIGH"));
    }

    [Test]
    public void AcceptedRow_RecalibratedButNeverActionable()
    {
        var d = Row("color", "red", "blue", "MEDIUM", interactive: true);
        d.Accepted = true;
        Apply(d);
        Assert.That((d.Severity, d.Actionable), Is.EqualTo(("HIGH", false)));
    }

    [Test]
    public void Settings_ThresholdsConfigurable_AndLoadReadsBlocks()
    {
        var d = Row("font-size", "14px", "16px", "MEDIUM");
        CssSeverityRules.Apply(new[] { d }, new CssSeveritySettings { FontSizeLowPx = 2 });
        Assert.That(d.Severity, Is.EqualTo("LOW"));

        var path = Path.Combine(Path.GetTempPath(), "vc-sev-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                { "VisualChecks": { "Severity": { "DeltaELow": 2, "LowFontWeightPairs": [["400", "500"]], "PrimaryRoles": ["tab"] },
                                    "Groups": { "ChatTop": 5 } } }
                """);
            var vc = VisualChecksSettings.Load(path);
            Assert.That(vc.Severity.DeltaELow, Is.EqualTo(2));
            Assert.That(vc.Severity.DeltaEHigh, Is.EqualTo(10), "absent value keeps its default");
            Assert.That(vc.Severity.ContrastMin, Is.EqualTo(4.5));
            Assert.That(vc.Severity.LowFontWeightPairs.Single(), Is.EqualTo(new[] { "400", "500" }));
            Assert.That(vc.Severity.PrimaryRoles, Is.EqualTo(new[] { "tab" }));
            Assert.That((vc.Groups.ChatTop, vc.Groups.ExampleElements), Is.EqualTo((5, 3)));
        }
        finally { File.Delete(path); }

        var defaults = VisualChecksSettings.Load(null);
        Assert.That((defaults.Severity.FontSizeLowPx, defaults.Severity.BoxLowPx, defaults.Severity.TextMetricLowPx), Is.EqualTo((1.0, 1.0, 1.0)));
        Assert.That((defaults.Severity.DeltaELow, defaults.Severity.DeltaEHigh, defaults.Severity.ContrastMin), Is.EqualTo((3.0, 10.0, 4.5)));
        Assert.That(defaults.Groups.ChatTop, Is.EqualTo(15));
        var shipped = VisualChecksSettings.Load(HarnessPaths.Resolve("appsettings.test.json"));
        Assert.That((shipped.Severity.DeltaELow, shipped.Severity.DeltaEHigh, shipped.Groups.ChatTop), Is.EqualTo((3.0, 10.0, 15)));
    }
}
