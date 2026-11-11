using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>PNG IHDR / JPEG SOF header reader used to scale screenshots in the PDF and Word documents.</summary>
[TestFixture]
public class ImageSizeTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "imagesize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    private string Write(string name, byte[] bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    /// <summary>PNG signature + IHDR chunk header (no IDAT needed: only the header is read).</summary>
    private static byte[] PngHeader(int w, int h) => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // signature
        0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w,
        (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    [Test]
    public void Png_ReadsIhdrWidthAndHeight()
    {
        Assert.That(ImageSize.Read(Write("a.png", PngHeader(1280, 720))), Is.EqualTo((1280, 720)));
        Assert.That(ImageSize.Read(Write("b.png", PngHeader(70000, 3))), Is.EqualTo((70000, 3)), "32-bit big-endian values");
    }

    [Test]
    public void Jpeg_ReadsSofDimensions_AfterSkippingApp0AndQuantTables()
    {
        var path = Write("a.jpg", Convert.FromBase64String(ReportDocumentsTests.TinyJpegBase64));
        Assert.That(ImageSize.Read(path), Is.EqualTo((32, 18)));
    }

    [Test]
    public void Jpeg_ProgressiveSof2_IsRecognised()
    {
        // SOI, SOF2 (len 11, precision 8, height 0x0100, width 0x0200, 1 component)
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xC2, 0x00, 0x0B, 0x08, 0x01, 0x00, 0x02, 0x00, 0x01, 0x01, 0x11, 0x00 };
        Assert.That(ImageSize.Read(Write("p.jpg", bytes)), Is.EqualTo((512, 256)));
    }

    [Test]
    public void Jpeg_DhtMarkerC4_IsNotMistakenForSof()
    {
        // SOI, DHT (C4, len 4, 2 payload bytes), then SOF0 3x2
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xC4, 0x00, 0x04, 0xAA, 0xBB,
                                 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x03, 0x01, 0x01, 0x11, 0x00 };
        Assert.That(ImageSize.Read(Write("d.jpg", bytes)), Is.EqualTo((3, 2)));
    }

    [Test]
    public void Garbage_TruncatedOrUnknown_ReturnsNull()
    {
        Assert.That(ImageSize.Read(Write("g.bin", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })), Is.Null);
        Assert.That(ImageSize.Read(Write("t.png", PngHeader(10, 10).Take(18).ToArray())), Is.Null, "truncated PNG");
        Assert.That(ImageSize.Read(Write("t.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 })), Is.Null, "truncated JPEG");
        Assert.That(ImageSize.Read(Write("e.bin", Array.Empty<byte>())), Is.Null, "empty file");
    }

    [Test]
    public void MissingFile_ReturnsNull()
    {
        Assert.That(ImageSize.Read(Path.Combine(_dir, "nope.png")), Is.Null);
    }
}
