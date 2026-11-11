namespace MigrationTests.Reporting;

/// <summary>
/// Reads pixel dimensions from a PNG (IHDR) or JPEG (SOFn) header without decoding the image.
/// Used to scale screenshots in the PDF and Word documents. Returns null for anything else.
/// </summary>
internal static class ImageSize
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static (int W, int H)? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            return Read(fs);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static (int W, int H)? Read(Stream s)
    {
        var head = new byte[24];
        if (ReadFully(s, head, 0, 2) < 2) return null;

        // JPEG: starts with SOI (FF D8).
        if (head[0] == 0xFF && head[1] == 0xD8) return ReadJpeg(s);

        // PNG: 8-byte signature, IHDR chunk; width at offset 16, height at 20 (big-endian).
        if (ReadFully(s, head, 2, 22) < 22) return null;
        if (!head.AsSpan(0, 8).SequenceEqual(PngSignature)) return null;
        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R') return null;
        var w = BigEndian32(head, 16);
        var h = BigEndian32(head, 20);
        return w > 0 && h > 0 ? (w, h) : null;
    }

    private static (int W, int H)? ReadJpeg(Stream s)
    {
        var buf = new byte[7];
        while (true)
        {
            // Find the next marker (skip fill bytes 0xFF).
            var b = s.ReadByte();
            if (b < 0) return null;
            if (b != 0xFF) return null;
            int marker;
            do { marker = s.ReadByte(); } while (marker == 0xFF);
            if (marker < 0) return null;

            // Standalone markers without a length.
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
            if (marker == 0xD9 || marker == 0xDA) return null; // EOI / start of scan before any SOF

            if (ReadFully(s, buf, 0, 2) < 2) return null;
            var len = (buf[0] << 8) | buf[1];
            if (len < 2) return null;

            // SOF0-SOF15 except DHT (C4), JPG (C8) and DAC (CC).
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (ReadFully(s, buf, 0, 5) < 5) return null;
                var h = (buf[1] << 8) | buf[2];
                var w = (buf[3] << 8) | buf[4];
                return w > 0 && h > 0 ? (w, h) : null;
            }

            if (!Skip(s, len - 2)) return null;
        }
    }

    private static int BigEndian32(byte[] b, int o) =>
        (int)(((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3]);

    private static int ReadFully(Stream s, byte[] buf, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = s.Read(buf, offset + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static bool Skip(Stream s, int count)
    {
        if (s.CanSeek)
        {
            if (s.Position + count > s.Length) return false;
            s.Seek(count, SeekOrigin.Current);
            return true;
        }
        var tmp = new byte[Math.Min(count, 4096)];
        while (count > 0)
        {
            var n = s.Read(tmp, 0, Math.Min(count, tmp.Length));
            if (n <= 0) return false;
            count -= n;
        }
        return true;
    }
}
