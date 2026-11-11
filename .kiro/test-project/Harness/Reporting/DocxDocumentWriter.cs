using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MigrationTests.Reporting;

/// <summary>
/// Writes <see cref="DocBlock"/>s to a .docx with DocumentFormat.OpenXml.
/// A4 landscape; tables with borders and a shaded header row; screenshots side by side in a 2-cell table,
/// each image with a unique DocProperties id. Image sizes come from the PNG/JPEG header (<see cref="ImageSize"/>).
/// </summary>
internal static class DocxDocumentWriter
{
    // A4 landscape in twentieths of a point; 0.5 inch margins.
    private const int PageW = 16838, PageH = 11906, Margin = 720;
    private const long HalfWidthEmu = 4_500_000; // ~4.9 in per screenshot
    private const long MaxImageHeightEmu = 2_800_000;

    public static void Write(string path, string title, IReadOnlyList<DocBlock> blocks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        doc.PackageProperties.Title = title; // file metadata (names the run when there is one)
        doc.PackageProperties.Creator = "MigrationTests.Reports";
        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());
        uint imageId = 1;

        body.AppendChild(Para(title, bold: true, size: 32, color: "1F3864"));
        foreach (var b in blocks)
        {
            switch (b)
            {
                case HeadingBlock h:
                    var size = h.Level switch { 1 => 30, 2 => 26, 3 => 22, _ => 20 };
                    body.AppendChild(Para(h.Text, bold: true, size: size, color: "1F3864", spaceBefore: h.Level <= 2 ? 240 : 120));
                    break;
                case ParagraphBlock p:
                    body.AppendChild(Para(p.Text));
                    break;
                case BulletBlock bl:
                    foreach (var item in bl.Items) body.AppendChild(Para("• " + item, indent: 240));
                    break;
                case TableBlock t:
                    body.AppendChild(TableOf(t));
                    body.AppendChild(new Paragraph());
                    break;
                case ImagePairBlock ip:
                    body.AppendChild(ImagePair(main, ip, ref imageId));
                    body.AppendChild(new Paragraph());
                    break;
            }
        }

        body.AppendChild(new SectionProperties(
            new PageSize { Width = (UInt32Value)(uint)PageW, Height = (UInt32Value)(uint)PageH, Orient = PageOrientationValues.Landscape },
            new PageMargin { Top = Margin, Bottom = Margin, Left = (UInt32Value)(uint)Margin, Right = (UInt32Value)(uint)Margin }));
        main.Document.Save();
    }

    private static Paragraph Para(string text, bool bold = false, int size = 18, string color = "000000", int spaceBefore = 0, int indent = 0, bool italic = false)
    {
        var props = new ParagraphProperties(new SpacingBetweenLines { Before = spaceBefore.ToString(), After = "60" });
        if (indent > 0) props.AppendChild(new Indentation { Left = indent.ToString() });
        var rp = new RunProperties();
        rp.AppendChild(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" });
        if (bold) rp.AppendChild(new Bold());
        if (italic) rp.AppendChild(new Italic());
        rp.AppendChild(new Color { Val = color });
        rp.AppendChild(new FontSize { Val = size.ToString() });
        return new Paragraph(props, new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table TableOf(TableBlock t)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" })));
        var weights = PdfDocumentWriter.Weights(t);
        var total = weights.Sum();
        var grid = new TableGrid();
        foreach (var w in weights) grid.AppendChild(new GridColumn { Width = ((int)((PageW - 2 * Margin) * w / total)).ToString() });
        table.AppendChild(grid);

        var header = new TableRow(new TableRowProperties(new TableHeader()));
        foreach (var h in t.Header) header.AppendChild(Cell(h, bold: true, shade: "E7E6E6"));
        table.AppendChild(header);
        foreach (var row in t.Rows)
        {
            var tr = new TableRow();
            foreach (var c in row)
            {
                var color = c is "FAIL" or "UNRESOLVED" || c.StartsWith("Compile failed", StringComparison.Ordinal) ? "C00000" : c is "PASS" or "RESOLVED" ? "2E7D32" : "000000";
                tr.AppendChild(Cell(c, bold: color != "000000", color: color));
            }
            table.AppendChild(tr);
        }
        return table;
    }

    private static TableCell Cell(string text, bool bold = false, string? shade = null, string color = "000000")
    {
        var props = new TableCellProperties();
        if (shade != null) props.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Fill = shade, Color = "auto" });
        return new TableCell(props, Para(text, bold: bold, size: 16, color: color));
    }

    private static Table ImagePair(MainDocumentPart main, ImagePairBlock ip, ref uint imageId)
    {
        var table = new Table(new TableProperties(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));
        var row = new TableRow();
        foreach (var (caption, path) in new[] { (ip.LeftCaption, ip.LeftPath), (ip.RightCaption, ip.RightPath) })
        {
            var cell = new TableCell(new TableCellProperties(new TableCellWidth { Width = "2500", Type = TableWidthUnitValues.Pct }));
            cell.AppendChild(Para(caption, bold: true, size: 16));
            if (path != null && File.Exists(path))
            {
                var part = main.AddImagePart(path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ImagePartType.Png : ImagePartType.Jpeg);
                using (var fs = File.OpenRead(path)) part.FeedData(fs);
                cell.AppendChild(new Paragraph(new Run(Image(main.GetIdOfPart(part), path, imageId++))));
            }
            else cell.AppendChild(Para("(no screenshot)", italic: true, size: 16, color: "808080"));
            row.AppendChild(cell);
        }
        table.AppendChild(row);
        return table;
    }

    /// <summary>Inline picture scaled to half the page width (aspect from the image header), capped in height.</summary>
    private static Drawing Image(string relId, string path, uint id)
    {
        long cx = HalfWidthEmu, cy = HalfWidthEmu * 9 / 16; // default 16:9 when the header is unreadable
        if (ImageSize.Read(path) is var (w, h))
        {
            cy = cx * h / w;
            if (cy > MaxImageHeightEmu) { cx = cx * MaxImageHeightEmu / cy; cy = MaxImageHeightEmu; }
        }

        var name = $"screenshot{id}{Path.GetExtension(path)}";
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)id, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = (UInt32Value)id, Name = name },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }
}
