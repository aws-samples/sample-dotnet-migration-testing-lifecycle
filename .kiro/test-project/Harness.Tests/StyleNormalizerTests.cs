using MigrationTests.Playwright;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>Style normalization (colors, px rounding, borders, keywords) and its use in the mapped comparison.</summary>
[TestFixture]
public class StyleNormalizerTests
{
    [TestCase("#fff", "rgba(255,255,255,1)")]
    [TestCase("#FFFFFF", "rgba(255,255,255,1)")]
    [TestCase("#0000ff80", "rgba(0,0,255,0.5)")]
    [TestCase("#f008", "rgba(255,0,0,0.53)")]
    [TestCase("rgb(0, 0, 0)", "rgba(0,0,0,1)")]
    [TestCase("rgb(0 128 0 / 50%)", "rgba(0,128,0,0.5)")]
    [TestCase("rgba(10, 20, 30, 0.25)", "rgba(10,20,30,0.25)")]
    [TestCase("white", "rgba(255,255,255,1)")]
    [TestCase("transparent", "rgba(0,0,0,0)")]
    [TestCase("rgba(255, 255, 255, 0)", "rgba(0,0,0,0)")]
    public void Color_Normalized(string input, string expected) =>
        Assert.That(StyleNormalizer.Color(input), Is.EqualTo(expected));

    [Test]
    public void Equivalent_ColorsAcrossFormats()
    {
        Assert.That(StyleNormalizer.Equivalent("color", "#000", "rgb(0, 0, 0)"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("background-color", "white", "rgba(255,255,255,1)"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("color", "rgba(0,0,0,0.5)", "rgb(0,0,0)"), Is.False, "alpha-aware");
        Assert.That(StyleNormalizer.Equivalent("hover.background-color", "#eee", "rgb(238, 238, 238)"), Is.True, "state prefix uses the base property");
    }

    [Test]
    public void Px_RoundedToHalfPixel()
    {
        Assert.That(StyleNormalizer.Px("12.26px"), Is.EqualTo("12.5px"));
        Assert.That(StyleNormalizer.Px("4.2px 8.74px"), Is.EqualTo("4px 8.5px"));
        Assert.That(StyleNormalizer.Equivalent("font-size", "13.1px", "13.2px"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("font-size", "13px", "14px"), Is.False);
    }

    [Test]
    public void Border_PerSide_NoBorderEqualRegardlessOfColor()
    {
        Assert.That(StyleNormalizer.Equivalent("border-top", "1px solid rgb(0, 0, 0)", "1px solid #000"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("border-top", "0px none rgb(0, 0, 0)", "0px none rgb(33, 33, 33)"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("border-left", "1px solid rgba(0, 0, 0, 0)", "0px none rgb(1, 2, 3)"), Is.True, "transparent border draws nothing");
        Assert.That(StyleNormalizer.Equivalent("border-bottom", "1px solid rgb(0, 0, 0)", "2px solid rgb(0, 0, 0)"), Is.False);
    }

    [Test]
    public void Keywords_Equivalent()
    {
        Assert.That(StyleNormalizer.Equivalent("font-weight", "normal", "400"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("font-weight", "bold", "700"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("text-align", "start", "left"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("text-decoration", "none solid rgb(0, 0, 0)", "none"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("font-family", "\"Segoe UI\", Arial", "'Segoe UI',Arial"), Is.True);
        Assert.That(StyleNormalizer.Equivalent("font-family", "Orange Sans", "orange sans"), Is.True, "family names are not parsed as colors");
    }

    [Test]
    public void IsNoiseDiff_UsesNormalizer()
    {
        Assert.That(CssCompareHelper.IsNoiseDiff("color", "#000000", "rgb(0, 0, 0)"), Is.True);
        Assert.That(CssCompareHelper.IsNoiseDiff("background-color", "transparent", "rgba(0, 0, 0, 0)"), Is.True);
        Assert.That(CssCompareHelper.IsNoiseDiff("color", "#000000", "rgb(0, 0, 1)"), Is.False);
    }

    private string _pageDir = "";

    [SetUp]
    public void SetUp()
    {
        _pageDir = Path.Combine(Path.GetTempPath(), "sn-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pageDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_pageDir)) Directory.Delete(_pageDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    private ModuleSummary RunMapped(Dictionary<string, string> refProps, Dictionary<string, string> tgtProps, Dictionary<string, string> mapProps)
    {
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "css-map.json"), new CssMapFile
        {
            Elements = new() { new CssMapElement { Name = "Panel", State = "page_load", Reference = ".p", Target = ".p", Properties = mapProps } }
        });
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Page = "P", Modules = new() { new PlanModule { Id = "A", CssStates = new() { "page_load" } } }
        });
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "css", "css_mapped_page_load.json"), new Dictionary<string, Dictionary<string, string>> { ["Panel"] = refProps });
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Target", "css", "css_mapped_page_load.json"), new Dictionary<string, Dictionary<string, string>> { ["Panel"] = tgtProps });
        return ModuleReportGenerator.Generate(_pageDir, "A", new VisualChecksSettings()).Single();
    }

    [Test]
    public void Mapped_EffectiveBackground_TransparentVsWhite_NotADiff()
    {
        var s = RunMapped(
            new() { ["background-color"] = "rgba(0, 0, 0, 0)", ["style.effectiveBackground"] = "rgb(255, 255, 255)" },
            new() { ["background-color"] = "rgb(255, 255, 255)", ["style.effectiveBackground"] = "rgb(255, 255, 255)" },
            new() { ["background-color"] = "MEDIUM" });
        Assert.That(s.CssDiffs.Where(d => d.Property == "background-color"), Is.Empty);
    }

    [Test]
    public void Mapped_RenderedFontEqual_StackDifferent_NotADiff()
    {
        var s = RunMapped(
            new() { ["font-family"] = "Tahoma, Verdana, sans-serif", ["style.renderedFont"] = "Tahoma" },
            new() { ["font-family"] = "Roboto, Tahoma, sans-serif", ["style.renderedFont"] = "Tahoma" },
            new() { ["font-family"] = "LOW" });
        Assert.That(s.CssDiffs.Where(d => d.Property == "font-family"), Is.Empty);

        var diff = RunMapped(
            new() { ["font-family"] = "Tahoma, sans-serif", ["style.renderedFont"] = "Tahoma" },
            new() { ["font-family"] = "Roboto, sans-serif", ["style.renderedFont"] = "Roboto" },
            new() { ["font-family"] = "LOW" });
        var row = diff.CssDiffs.Single(d => d.Property == "font-family");
        Assert.That((row.Reference, row.Target, row.Layer), Is.EqualTo(("Tahoma", "Roboto", "mapped")));
    }

    [Test]
    public void Mapped_CursorIgnored_OnlyOnNonInteractive()
    {
        var quiet = RunMapped(new() { ["cursor"] = "auto", ["style.interactive"] = "false" }, new() { ["cursor"] = "pointer", ["style.interactive"] = "false" }, new() { ["cursor"] = "MEDIUM" });
        Assert.That(quiet.CssDiffs, Is.Empty);
        var counted = RunMapped(new() { ["cursor"] = "auto", ["style.interactive"] = "true" }, new() { ["cursor"] = "pointer", ["style.interactive"] = "true" }, new() { ["cursor"] = "MEDIUM" });
        Assert.That(counted.CssDiffs.Single().Property, Is.EqualTo("cursor"));
        Assert.That(counted.CssDiffs.Any(d => d.Property.StartsWith("style.")), Is.False, "style.* keys are never compared raw");
    }
}
