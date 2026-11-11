using System.Text;
using System.Text.RegularExpressions;

namespace MigrationTests.Reporting;

/// <summary>Document block model shared by the PDF and Word writers (P8).</summary>
internal abstract record DocBlock;

internal sealed record HeadingBlock(int Level, string Text) : DocBlock;

internal sealed record ParagraphBlock(string Text) : DocBlock;

internal sealed record BulletBlock(IReadOnlyList<string> Items) : DocBlock;

internal sealed record TableBlock(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) : DocBlock;

/// <summary>Two screenshots side by side (Reference left, Target right). A null path renders "(no screenshot)".</summary>
internal sealed record ImagePairBlock(string LeftCaption, string? LeftPath, string RightCaption, string? RightPath) : DocBlock;

/// <summary>
/// Parses the controlled markdown subset the Reports tool writes (headings, paragraphs, '-' bullets,
/// pipe tables via <see cref="Md.SplitRow"/>). Inline markup (**bold**, `code`, [text](link)) is flattened to text.
/// </summary>
internal static class MarkdownBlocks
{
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Separator = new(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Link = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex FieldNote = new(@"\s*\((?:field|fields): [^)]*\)", RegexOptions.CultureInvariant);

    /// <summary>Removes the audit-only "(field: x)" notes (P7) so the documents read in plain English.</summary>
    public static string StripFieldNotes(string text) => FieldNote.Replace(text, "");

    public static string Inline(string s) => Link.Replace(s, "$1").Replace("**", "").Replace("`", "").Trim();

    public static List<DocBlock> Parse(string markdown)
    {
        var blocks = new List<DocBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var para = new StringBuilder();
        var bullets = new List<string>();

        void FlushPara()
        {
            if (para.Length > 0) blocks.Add(new ParagraphBlock(Inline(para.ToString())));
            para.Clear();
        }
        void FlushBullets()
        {
            if (bullets.Count > 0) blocks.Add(new BulletBlock(bullets.ToList()));
            bullets.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Trim().Length == 0) { FlushPara(); FlushBullets(); continue; }

            var h = Heading.Match(line);
            if (h.Success)
            {
                FlushPara(); FlushBullets();
                blocks.Add(new HeadingBlock(h.Groups[1].Value.Length, Inline(h.Groups[2].Value)));
                continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                FlushPara(); FlushBullets();
                var header = Md.SplitRow(line).Select(Inline).ToList();
                var rows = new List<IReadOnlyList<string>>();
                var j = i + 1;
                for (; j < lines.Length && lines[j].TrimStart().StartsWith('|'); j++)
                {
                    if (Separator.IsMatch(lines[j].Trim())) continue;
                    var cells = Md.SplitRow(lines[j]).Select(Inline).ToList();
                    while (cells.Count < header.Count) cells.Add("");
                    rows.Add(cells.Take(header.Count).ToList());
                }
                blocks.Add(new TableBlock(header, rows));
                i = j - 1;
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushPara();
                bullets.Add(Inline(line[2..]));
                continue;
            }
            FlushBullets();
            if (para.Length > 0) para.Append(' ');
            para.Append(line.Trim());
        }
        FlushPara();
        FlushBullets();
        return blocks;
    }
}
