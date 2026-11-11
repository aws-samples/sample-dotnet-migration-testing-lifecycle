using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace MigrationTests.Reporting;

/// <summary>
/// Writes <see cref="DocBlock"/>s to a PDF with PDFsharp/MigraDoc (MIT, pinned in the Harness csproj).
/// A4 landscape so wide tables and side-by-side screenshots fit. Deterministic content: only the given blocks,
/// fixed fonts, metadata dates set to <paramref name="generatedAt"/>.
/// </summary>
internal static class PdfDocumentWriter
{
    private const double BaseFont = 8;
    private const double MarginPt = 24;
    private const double PageWidthPt = 841.89;  // A4 landscape
    private const double ContentWidthPt = PageWidthPt - 2 * MarginPt;
    private const double CellPadPt = 3;
    private const double ImageGapPt = 10;
    private const double MaxImageHeightPt = 230;

    // Material palette used by the previous engine, so the output looks the same.
    private static readonly Color HeadingBlue = new(0x15, 0x65, 0xC0);
    private static readonly Color GreyDarken2 = new(0x61, 0x61, 0x61);
    private static readonly Color GreyDarken1 = new(0x75, 0x75, 0x75);
    private static readonly Color GreyMedium = new(0x9E, 0x9E, 0x9E);
    private static readonly Color GreyLighten1 = new(0xBD, 0xBD, 0xBD);
    private static readonly Color GreyLighten2 = new(0xE0, 0xE0, 0xE0);
    private static readonly Color GreyLighten3 = new(0xEE, 0xEE, 0xEE);
    private static readonly Color GreyLighten4 = new(0xF5, 0xF5, 0xF5);
    private static readonly Color RedDarken2 = new(0xD3, 0x2F, 0x2F);
    private static readonly Color GreenDarken2 = new(0x38, 0x8E, 0x3C);

    private static readonly object FontLock = new();
    private static bool _fontsReady;

    /// <summary>The runner is Windows-only: let PDFsharp resolve Arial etc. from the Windows font folder (once per process).</summary>
    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsReady) return;
            if (GlobalFontSettings.FontResolver == null) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _fontsReady = true;
        }
    }

    public static void Write(string path, string title, IReadOnlyList<DocBlock> blocks, DateTimeOffset generatedAt)
    {
        EnsureFonts();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var doc = new Document();
        doc.Info.Title = title;
        doc.Info.Author = "MigrationTests.Reports";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = BaseFont;

        var sec = doc.AddSection();
        sec.PageSetup = doc.DefaultPageSetup.Clone();
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.Orientation = Orientation.Landscape;
        sec.PageSetup.TopMargin = sec.PageSetup.BottomMargin = Unit.FromPoint(MarginPt + 18);
        sec.PageSetup.LeftMargin = sec.PageSetup.RightMargin = Unit.FromPoint(MarginPt);
        sec.PageSetup.HeaderDistance = sec.PageSetup.FooterDistance = Unit.FromPoint(MarginPt);

        var header = sec.Headers.Primary.AddParagraph(title);
        header.Format.Font.Size = 9;
        header.Format.Font.Color = GreyDarken2;

        var footer = sec.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = GreyDarken1;
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        foreach (var b in blocks) Compose(sec, b);

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        var info = renderer.PdfDocument.Info;
        info.Title = title;
        info.Author = "MigrationTests.Reports";
        info.Creator = "MigrationTests.Reports";
        info.CreationDate = generatedAt.LocalDateTime;
        info.ModificationDate = generatedAt.LocalDateTime;
        renderer.PdfDocument.Save(path);
    }

    private static void Compose(Section sec, DocBlock block)
    {
        switch (block)
        {
            case HeadingBlock h:
                var size = h.Level switch { 1 => 16d, 2 => 13d, 3 => 11d, _ => 9.5d };
                var hp = sec.AddParagraph(h.Text);
                hp.Format.SpaceBefore = Unit.FromPoint(h.Level <= 2 ? 8 : 4);
                hp.Format.SpaceAfter = Unit.FromPoint(4);
                hp.Format.KeepWithNext = true;
                hp.Format.Font.Size = size;
                hp.Format.Font.Bold = true;
                hp.Format.Font.Color = HeadingBlue;
                break;
            case ParagraphBlock p:
                sec.AddParagraph(p.Text).Format.SpaceAfter = Unit.FromPoint(4);
                break;
            case BulletBlock bl:
                for (var i = 0; i < bl.Items.Count; i++)
                {
                    var bp = sec.AddParagraph();
                    bp.Format.LeftIndent = Unit.FromPoint(10);
                    bp.Format.FirstLineIndent = Unit.FromPoint(-10);
                    bp.Format.TabStops.AddTabStop(Unit.FromPoint(10));
                    bp.Format.SpaceAfter = Unit.FromPoint(i == bl.Items.Count - 1 ? 4 : 0);
                    bp.AddText("•");
                    bp.AddTab();
                    bp.AddText(bl.Items[i]);
                }
                break;
            case TableBlock t:
                ComposeTable(sec, t);
                break;
            case ImagePairBlock ip:
                ComposeImagePair(sec, ip);
                break;
        }
    }

    private static void ComposeImagePair(Section sec, ImagePairBlock ip)
    {
        var colWidth = (ContentWidthPt - ImageGapPt) / 2;
        var table = sec.AddTable();
        table.Borders.Visible = false;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(0);
        table.TopPadding = Unit.FromPoint(2);
        table.AddColumn(Unit.FromPoint(colWidth));
        table.AddColumn(Unit.FromPoint(ImageGapPt));
        table.AddColumn(Unit.FromPoint(colWidth));
        var row = table.AddRow();
        row.KeepWith = 0;
        ImageCell(row.Cells[0], ip.LeftCaption, ip.LeftPath, colWidth);
        ImageCell(row.Cells[2], ip.RightCaption, ip.RightPath, colWidth);
        sec.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void ImageCell(Cell cell, string caption, string? path, double width)
    {
        var cp = cell.AddParagraph(caption);
        cp.Format.Font.Bold = true;
        cp.Format.SpaceAfter = Unit.FromPoint(2);
        if (path != null && File.Exists(path))
        {
            // Fit inside width x MaxImageHeight keeping the aspect ratio (16:9 when the header is unreadable).
            var (pw, ph) = ImageSize.Read(path) ?? (16, 9);
            var w = width;
            var h = width * ph / pw;
            if (h > MaxImageHeightPt) { w = w * MaxImageHeightPt / h; h = MaxImageHeightPt; }
            var img = cell.AddParagraph().AddImage(path);
            img.Width = Unit.FromPoint(w);
            img.Height = Unit.FromPoint(h);
            img.LockAspectRatio = false;
            img.LineFormat.Width = Unit.FromPoint(0.5);
            img.LineFormat.Color = GreyLighten1;
        }
        else
        {
            var np = cell.AddParagraph("(no screenshot)");
            np.Format.Alignment = ParagraphAlignment.Center;
            np.Format.Font.Color = GreyDarken1;
            np.Format.Shading.Color = GreyLighten4;
            np.Format.Borders.Color = GreyLighten4;
            np.Format.Borders.Width = Unit.FromPoint(0.5);
            np.Format.Borders.Distance = Unit.FromPoint(14);
            np.Format.SpaceBefore = np.Format.SpaceAfter = Unit.FromPoint(14);
        }
    }

    /// <summary>Column weights from the average cell length (bounded) so description columns get the room.</summary>
    internal static float[] Weights(TableBlock t)
    {
        var n = t.Header.Count;
        var w = new float[n];
        for (var i = 0; i < n; i++)
        {
            var lens = t.Rows.Select(r => i < r.Count ? r[i].Length : 0).Append(t.Header[i].Length).ToList();
            w[i] = Math.Clamp((float)lens.Average(), 4f, 60f);
        }
        return w;
    }

    private static void ComposeTable(Section sec, TableBlock t)
    {
        if (t.Header.Count == 0) return;
        var weights = Weights(t);
        var total = weights.Sum();
        var table = sec.AddTable();
        table.Borders.Visible = false;
        table.Format.Font.Size = BaseFont - 0.5;
        table.LeftPadding = table.RightPadding = table.TopPadding = table.BottomPadding = Unit.FromPoint(CellPadPt);
        foreach (var w in weights) table.AddColumn(Unit.FromPoint(ContentWidthPt * w / total));

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = GreyLighten3;
        head.Format.Font.Bold = true;
        head.Borders.Bottom.Width = Unit.FromPoint(0.5);
        head.Borders.Bottom.Color = GreyMedium;
        for (var i = 0; i < t.Header.Count; i++) head.Cells[i].AddParagraph(t.Header[i]);

        foreach (var r in t.Rows)
        {
            var row = table.AddRow();
            row.Borders.Bottom.Width = Unit.FromPoint(0.25);
            row.Borders.Bottom.Color = GreyLighten2;
            for (var i = 0; i < Math.Min(r.Count, t.Header.Count); i++)
            {
                var cell = r[i];
                var p = row.Cells[i].AddParagraph(cell);
                if (cell is "FAIL" or "UNRESOLVED" || cell.StartsWith("Compile failed", StringComparison.Ordinal))
                { p.Format.Font.Color = RedDarken2; p.Format.Font.Bold = true; }
                else if (cell is "PASS" or "RESOLVED")
                { p.Format.Font.Color = GreenDarken2; p.Format.Font.Bold = true; }
            }
        }
        sec.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }
}
