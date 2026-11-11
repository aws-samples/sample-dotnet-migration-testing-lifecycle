using System.Text;
using MigrationTests.Reporting;

namespace MigrationTests.Reports;

/// <summary>
/// The only report and chat-render engine (design §1.4). Invoked by lifecycle.ps1:
///   MigrationTests.Reports module --page-dir &lt;abs&gt; --module X
///   MigrationTests.Reports all    --page-dir &lt;abs&gt; [--documents]         (also writes reports/FinalReport.md;
///                                                                     --documents: + FinalReport.pdf/.docx, TestCaseResults.pdf/.docx)
///   MigrationTests.Reports render --page-dir &lt;abs&gt; --view V [--module X] --out &lt;abs file&gt;
/// Exit codes: 0 OK; 2 ReportInputException / bad arguments / missing input (message on stderr);
/// 1 unexpected exception (stack on stderr). render prints 'SUMMARY: ...' as its last stdout line.
/// The render progress line comes from env LIFECYCLE_PROGRESS_LINE (computed by lifecycle.ps1).
/// </summary>
public static class Program
{
    public const string Usage =
        "usage: MigrationTests.Reports <module|all|render> --page-dir <abs> [--module X] [--view plan|tests-authored|live-status|module-result|run-summary|fix-round|final] [--out <abs file>] [--documents]";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            return Run(args);
        }
        catch (Exception ex) when (ex is ReportInputException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException(Usage);
        var command = args[0].Trim().ToLowerInvariant();
        var opts = ParseOptions(args.Skip(1).ToArray());
        opts.TryGetValue("page-dir", out var pageDir);
        if (string.IsNullOrWhiteSpace(pageDir)) throw new ArgumentException("--page-dir is required. " + Usage);
        pageDir = Path.GetFullPath(pageDir);
        if (!Directory.Exists(pageDir)) throw new ReportInputException($"page dir not found: {pageDir}");
        opts.TryGetValue("module", out var module);

        switch (command)
        {
            case "module":
                if (string.IsNullOrWhiteSpace(module)) throw new ArgumentException("--module is required for 'module'. " + Usage);
                PrintSummaries(ModuleReportGenerator.Generate(pageDir, module));
                return 0;

            case "all":
                PrintSummaries(ModuleReportGenerator.Generate(pageDir, "ALL"));
                Console.WriteLine($"FinalReport: {Path.Combine(pageDir, "reports", "FinalReport.md")}");
                if (opts.ContainsKey("documents"))
                {
                    // P8: FinalReport.pdf/.docx + TestCaseResults.pdf/.docx (deterministic, from the files just written).
                    var docs = ReportDocuments.WriteAll(pageDir);
                    foreach (var f in docs.Files) Console.WriteLine($"Document: {f} {new FileInfo(f).Length}");
                    foreach (var w in docs.Warnings) Console.WriteLine("DocumentWarning: " + w);
                }
                return 0;

            case "render":
                opts.TryGetValue("view", out var view);
                opts.TryGetValue("out", out var outFile);
                if (string.IsNullOrWhiteSpace(view)) throw new ArgumentException("--view is required for 'render'. " + Usage);
                if (string.IsNullOrWhiteSpace(outFile)) throw new ArgumentException("--out is required for 'render'. " + Usage);
                var progress = Environment.GetEnvironmentVariable("LIFECYCLE_PROGRESS_LINE");
                if (string.IsNullOrWhiteSpace(progress)) progress = LifecycleTimings.NoLifecycleLine;
                var (parts, summary) = ChatRenderer.RenderParts(pageDir, view, module, progress);
                outFile = Path.GetFullPath(outFile);
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                File.WriteAllText(outFile, parts[0].Markdown, new UTF8Encoding(false));
                Console.WriteLine($"Rendered {view}{(string.IsNullOrWhiteSpace(module) ? "" : " " + module.Trim().ToUpperInvariant())}: {outFile}");
                // P4: each further part (plan: one per module) is its own file, relayed as its own CHAT-RENDER.
                var stem = outFile.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? outFile[..^3] : outFile;
                foreach (var part in parts.Skip(1))
                {
                    var partFile = $"{stem}-{part.Suffix}.md";
                    File.WriteAllText(partFile, part.Markdown, new UTF8Encoding(false));
                    Console.WriteLine("PART: " + partFile);
                }
                Console.WriteLine("SUMMARY: " + summary);
                return 0;

            default:
                throw new ArgumentException($"unknown command '{args[0]}'. " + Usage);
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"unexpected argument '{a}'. " + Usage);
            var key = a[2..];
            if (key == "documents") { d[key] = "true"; continue; } // flag without a value
            if (key is not ("page-dir" or "module" or "view" or "out")) throw new ArgumentException($"unknown option '{a}'. " + Usage);
            if (i + 1 >= args.Length) throw new ArgumentException($"option '{a}' needs a value. " + Usage);
            d[key] = args[++i];
        }
        return d;
    }

    /// <summary>One line per module summary.</summary>
    private static void PrintSummaries(IReadOnlyList<ModuleSummary> summaries)
    {
        foreach (var s in summaries)
            Console.WriteLine($"Module {s.Module}: Reference {s.ReferenceStatus}, Target {s.TargetStatus}, open findings {s.OpenFindings}, " +
                              $"actionable CSS groups {s.ActionableCssDiffs} (raw rows {s.ActionableCssRows} actionable of {s.CssDiffsTotal}), NOT TESTED {s.NotTested}, Reference defects {s.ReferenceDefectCount}");
        if (summaries.Count == 0) Console.WriteLine("No modules found under runs/Module*");
    }
}
