using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>CIEDE2000 against the Sharma, Wu, Dalal (2005) reference pairs, sRGB parsing/compositing and WCAG contrast.</summary>
[TestFixture]
public class ColorMathTests
{
    // Sharma et al. 2005, Table 1 (selected pairs incl. the hue-wrap and low-chroma cases).
    [TestCase(50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425)]
    [TestCase(50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615)]
    [TestCase(50.0000, 0.0000, 0.0000, 50.0000, -1.0000, 2.0000, 2.3669)]
    [TestCase(50.0000, -1.0000, 2.0000, 50.0000, 0.0000, 0.0000, 2.3669)]
    [TestCase(50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0009, 7.1792)]
    [TestCase(50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492)]
    [TestCase(50.0000, 2.5000, 0.0000, 56.0000, -27.0000, -3.0000, 31.9030)]
    [TestCase(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    [TestCase(22.7233, 20.0904, -46.6940, 23.0331, 14.9730, -42.5619, 2.0373)]
    [TestCase(90.8027, -2.0831, 1.4410, 91.1528, -1.6435, 0.0447, 1.4441)]
    [TestCase(2.0776, 0.0795, -1.1350, 0.9033, -0.0636, -0.5514, 0.9082)]
    public void DeltaE2000_MatchesSharmaReferencePairs(double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        var de = ColorMath.DeltaE2000(new ColorMath.Lab(l1, a1, b1), new ColorMath.Lab(l2, a2, b2));
        Assert.That(de, Is.EqualTo(expected).Within(1e-4));
        // Symmetric.
        Assert.That(ColorMath.DeltaE2000(new ColorMath.Lab(l2, a2, b2), new ColorMath.Lab(l1, a1, b1)), Is.EqualTo(expected).Within(1e-4));
    }

    [Test]
    public void DeltaE_CssColors()
    {
        Assert.That(ColorMath.DeltaE("#336699", "rgb(51, 102, 153)"), Is.EqualTo(0).Within(1e-9));
        Assert.That(ColorMath.DeltaE("rgb(69, 69, 69)", "rgb(66, 66, 66)"), Is.LessThan(3));
        Assert.That(ColorMath.DeltaE("rgb(0, 0, 0)", "rgb(255, 255, 255)"), Is.GreaterThan(90));
        Assert.That(ColorMath.DeltaE("red", "blue"), Is.GreaterThan(10));
        Assert.That(ColorMath.DeltaE("normal", "red"), Is.Null, "unparsable value");
        // 26% black over white is a light grey: far from black.
        Assert.That(ColorMath.DeltaE("rgb(0, 0, 0)", "rgba(0, 0, 0, 0.26)"), Is.GreaterThan(10));
    }

    [Test]
    public void ToLab_WhiteAndBlack()
    {
        var w = ColorMath.ToLab(new ColorMath.Rgba(255, 255, 255, 1));
        Assert.That(w.L, Is.EqualTo(100).Within(0.01));
        Assert.That(w.A, Is.EqualTo(0).Within(0.01));
        Assert.That(w.B, Is.EqualTo(0).Within(0.01));
        Assert.That(ColorMath.ToLab(new ColorMath.Rgba(0, 0, 0, 1)).L, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Contrast_Wcag()
    {
        Assert.That(ColorMath.Contrast("#000", "#fff"), Is.EqualTo(21).Within(1e-9));
        Assert.That(ColorMath.Contrast("#fff", "#fff"), Is.EqualTo(1).Within(1e-9));
        var grey = ColorMath.Contrast("#777777", "white")!.Value;
        Assert.That(grey, Is.EqualTo(4.48).Within(0.01));
        Assert.That(grey, Is.LessThan(4.5));
        Assert.That(ColorMath.Contrast("#767676", "#ffffff"), Is.GreaterThanOrEqualTo(4.5));
        // Semi-transparent text is composited over its background first (26% black on white ~ 1.9:1).
        Assert.That(ColorMath.Contrast("rgba(0, 0, 0, 0.26)", "rgb(255, 255, 255)"), Is.LessThan(2.5).And.GreaterThan(1.5));
        // Transparent background = white page.
        Assert.That(ColorMath.Contrast("#000", "rgba(0, 0, 0, 0)"), Is.EqualTo(21).Within(1e-9));
        Assert.That(ColorMath.Contrast("inherit", "#fff"), Is.Null);
    }

    [TestCase("#000", "black")]
    [TestCase("rgb(0, 0, 0)", "black")]
    [TestCase("#424242", "dark gray")]
    [TestCase("rgb(51, 51, 51)", "dark gray")]
    [TestCase("#808080", "gray")]
    [TestCase("rgba(0, 0, 0, 0.26)", "faded gray")]
    [TestCase("#f6f6f6", "light gray")]
    [TestCase("rgb(237, 237, 237)", "light gray")]
    [TestCase("#fff", "white")]
    [TestCase("#1565c0", "blue")]
    [TestCase("rgb(25, 118, 210)", "blue")]
    [TestCase("red", "red")]
    [TestCase("#2e7d32", "green")]
    [TestCase("orange", "orange")]
    [TestCase("rgb(254, 249, 231)", "pale yellow")]
    [TestCase("navy", "dark blue")]
    [TestCase("#9c27b0", "purple")]
    [TestCase("rgb(128, 0, 128)", "dark purple")]
    [TestCase("rgb(139, 69, 19)", "brown")]
    [TestCase("transparent", "transparent")]
    public void Name_DeterministicColorWords(string css, string expected)
        => Assert.That(ColorMath.Name(css), Is.EqualTo(expected));

    [Test]
    public void Name_UnparsableIsNull_AndCompositedOverBackground()
    {
        Assert.That(ColorMath.Name("inherit"), Is.Null);
        // 50% white over black reads as gray (alpha >= 0.6 is needed to keep the plain name; below it is "faded").
        Assert.That(ColorMath.Name("rgba(255, 255, 255, 0.8)", "#000"), Is.EqualTo("light gray"));
    }
}
