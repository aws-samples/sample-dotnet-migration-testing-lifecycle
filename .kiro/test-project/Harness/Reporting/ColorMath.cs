using System.Globalization;

namespace MigrationTests.Reporting;

/// <summary>
/// Color math for the CSS severity rules (pure, no package): CSS color → sRGB, alpha compositing, sRGB → CIE Lab (D65),
/// CIEDE2000 color difference (Sharma, Wu, Dalal 2005) and the WCAG 2.x relative luminance / contrast ratio.
/// </summary>
public static class ColorMath
{
    public readonly record struct Rgba(double R, double G, double B, double A);
    public readonly record struct Lab(double L, double A, double B);

    private static readonly Rgba White = new(255, 255, 255, 1);

    /// <summary>Parses any color StyleNormalizer understands (hex, rgb/rgba, names, transparent).</summary>
    public static bool TryRgba(string? value, out Rgba c)
    {
        c = default;
        var n = StyleNormalizer.Color(value);
        if (!n.StartsWith("rgba(", StringComparison.Ordinal) || !n.EndsWith(')')) return false;
        var parts = n[5..^1].Split(',');
        if (parts.Length != 4) return false;
        var v = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        c = new Rgba(v[0], v[1], v[2], v[3]);
        return true;
    }

    /// <summary>fg drawn over an opaque bg (a semi-transparent bg is first composited over white).</summary>
    public static Rgba Composite(Rgba fg, Rgba bg)
    {
        if (bg.A < 1) bg = Composite(bg, White);
        var a = Math.Clamp(fg.A, 0, 1);
        return new Rgba(fg.R * a + bg.R * (1 - a), fg.G * a + bg.G * (1 - a), fg.B * a + bg.B * (1 - a), 1);
    }

    private static double Linear(double channel255)
    {
        var c = channel255 / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>sRGB (opaque) → CIE L*a*b*, D65 white point.</summary>
    public static Lab ToLab(Rgba c)
    {
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        var y = (0.2126729 * r + 0.7151522 * g + 0.0721750 * b) / 1.00000;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
        static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
        double fx = F(x), fy = F(y), fz = F(z);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double Deg(double rad) => rad * 180 / Math.PI;
    private static double Rad(double deg) => deg * Math.PI / 180;

    /// <summary>CIEDE2000 (kL = kC = kH = 1).</summary>
    public static double DeltaE2000(Lab p, Lab q)
    {
        const double p25 = 6103515625.0; // 25^7
        var c1 = Math.Sqrt(p.A * p.A + p.B * p.B);
        var c2 = Math.Sqrt(q.A * q.A + q.B * q.B);
        var cBar7 = Math.Pow((c1 + c2) / 2, 7);
        var g = 0.5 * (1 - Math.Sqrt(cBar7 / (cBar7 + p25)));
        var a1 = (1 + g) * p.A;
        var a2 = (1 + g) * q.A;
        var c1p = Math.Sqrt(a1 * a1 + p.B * p.B);
        var c2p = Math.Sqrt(a2 * a2 + q.B * q.B);
        double Hue(double b, double a) { if (a == 0 && b == 0) return 0; var h = Deg(Math.Atan2(b, a)); return h < 0 ? h + 360 : h; }
        var h1 = Hue(p.B, a1);
        var h2 = Hue(q.B, a2);

        var dL = q.L - p.L;
        var dC = c2p - c1p;
        double dh;
        if (c1p * c2p == 0) dh = 0;
        else
        {
            dh = h2 - h1;
            if (dh > 180) dh -= 360;
            else if (dh < -180) dh += 360;
        }
        var dH = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dh / 2));

        var lBar = (p.L + q.L) / 2;
        var cBarP = (c1p + c2p) / 2;
        double hBar;
        if (c1p * c2p == 0) hBar = h1 + h2;
        else if (Math.Abs(h1 - h2) <= 180) hBar = (h1 + h2) / 2;
        else hBar = h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;

        var t = 1 - 0.17 * Math.Cos(Rad(hBar - 30)) + 0.24 * Math.Cos(Rad(2 * hBar))
                + 0.32 * Math.Cos(Rad(3 * hBar + 6)) - 0.20 * Math.Cos(Rad(4 * hBar - 63));
        var dTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
        var cBarP7 = Math.Pow(cBarP, 7);
        var rc = 2 * Math.Sqrt(cBarP7 / (cBarP7 + p25));
        var l50 = (lBar - 50) * (lBar - 50);
        var sl = 1 + 0.015 * l50 / Math.Sqrt(20 + l50);
        var sc = 1 + 0.045 * cBarP;
        var sh = 1 + 0.015 * cBarP * t;
        var rt = -Math.Sin(Rad(2 * dTheta)) * rc;
        var x = dL / sl;
        var y = dC / sc;
        var z = dH / sh;
        return Math.Sqrt(x * x + y * y + z * z + rt * y * z);
    }

    /// <summary>
    /// CIEDE2000 between two CSS colors; semi-transparent colors are composited over bg (default white).
    /// null when a value is not a parsable color.
    /// </summary>
    public static double? DeltaE(string? c1, string? c2, string? bg = null)
    {
        if (!TryRgba(c1, out var a) || !TryRgba(c2, out var b)) return null;
        var back = TryRgba(bg, out var k) ? k : White;
        return DeltaE2000(ToLab(Composite(a, back)), ToLab(Composite(b, back)));
    }

    /// <summary>
    /// Plain-English color word for report sentences (deterministic buckets, no package): "black", "dark gray", "gray",
    /// "light gray", "white", hue words (red, orange, yellow, green, teal, blue, purple, pink, brown) with a "dark" /
    /// "pale" prefix, "faded gray" / "faded &lt;hue&gt;" for a color with alpha below 0.6, "transparent" for alpha 0.
    /// The color is composited over bg (default white) first. null when the value is not a parsable color.
    /// </summary>
    public static string? Name(string? css, string? bg = null)
    {
        if (!TryRgba(css, out var c)) return null;
        if (c.A <= 0) return "transparent";
        var back = TryRgba(bg, out var k) ? k : White;
        var lab = ToLab(Composite(c, back));
        if (c.A < 0.6)
        {
            // Semi-transparent text/background reads as "faded": name the hue of the color itself.
            var own = ToLab(c with { A = 1 });
            var hue = Chroma(own) < AchromaticChroma ? "gray" : HueWord(own);
            return "faded " + hue;
        }
        if (Chroma(lab) < AchromaticChroma)
            return lab.L < 15 ? "black" : lab.L < 40 ? "dark gray" : lab.L < 70 ? "gray" : lab.L < 98.5 ? "light gray" : "white";
        var word = HueWord(lab);
        if (word == "brown") return word;
        return lab.L > 85 ? "pale " + word : lab.L < 35 ? "dark " + word : word;
    }

    /// <summary>Lab chroma below this is a gray (black, gray, white).</summary>
    private const double AchromaticChroma = 6;

    private static double Chroma(Lab l) => Math.Sqrt(l.A * l.A + l.B * l.B);

    /// <summary>Hue word from the CIE Lab hue angle (degrees).</summary>
    private static string HueWord(Lab l)
    {
        var h = Deg(Math.Atan2(l.B, l.A));
        if (h < 0) h += 360;
        if (h < 50) return l.L > 75 ? "pink" : "red";
        if (h < 85) return l.L < 50 ? "brown" : "orange";
        if (h < 115) return l.L < 50 ? "brown" : "yellow";
        if (h < 170) return "green";
        if (h < 225) return "teal";
        if (h < 315) return "blue";
        if (h < 345) return "purple";
        return l.L > 60 ? "pink" : "red";
    }

    /// <summary>WCAG 2.x relative luminance of an opaque sRGB color.</summary>
    public static double RelativeLuminance(Rgba c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>WCAG contrast ratio (1..21) of text color fg on background bg; semi-transparent colors composited (bg over white). null when unparsable.</summary>
    public static double? Contrast(string? fg, string? bg)
    {
        if (!TryRgba(fg, out var f) || !TryRgba(bg, out var b)) return null;
        var back = Composite(b, White);
        var front = Composite(f, back);
        var l1 = RelativeLuminance(front);
        var l2 = RelativeLuminance(back);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
