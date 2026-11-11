namespace MigrationTests.Reporting;

/// <summary>Files written by <see cref="ReportDocuments.WriteAll"/> plus non-fatal warnings.</summary>
public sealed record DocumentsResult(IReadOnlyList<string> Files, IReadOnlyList<string> Warnings);

/// <summary>
/// P8: PDF and Word versions of reports/FinalReport.md, and the test case results document
/// (per module, per step: plain-English description, status, expected vs actual, Reference and Target
/// screenshots side by side). Deterministic: built only from files under the page dir; no LLM.
/// </summary>
public static class ReportDocuments
{
    public const int MaxChecksPerStep = 6;

    /// <summary>Writes reports/FinalReport.pdf/.docx and reports/TestCaseResults.pdf/.docx.</summary>
    public static DocumentsResult WriteAll(string pageDir, DateTimeOffset? now = null)
    {
        var files = new List<string>();
        var warnings = new List<string>();
        files.AddRange(WriteFinal(pageDir, now));
        var d = ReportSources.Load(pageDir, now);
        files.AddRange(WriteTestCaseResults(d, warnings));
        return new DocumentsResult(files, warnings);
    }

    /// <summary>FinalReport.md → same sections as PDF and DOCX (audit "(field: ...)" notes removed).</summary>
    public static IReadOnlyList<string> WriteFinal(string pageDir, DateTimeOffset? now = null)
    {
        pageDir = Path.GetFullPath(pageDir);
        var md = Path.Combine(pageDir, "reports", "FinalReport.md");
        if (!File.Exists(md)) throw new ReportInputException($"FinalReport.md missing: {md}");
        var text = MarkdownBlocks.StripFieldNotes(File.ReadAllText(md));
        var blocks = MarkdownBlocks.Parse(text);
        // The md starts with '# Final Report - <Page>': that is the document title.
        var title = blocks.FirstOrDefault() is HeadingBlock { Level: 1 } h ? h.Text : "Final Report";
        if (blocks.FirstOrDefault() is HeadingBlock { Level: 1 }) blocks.RemoveAt(0);
        var at = now ?? File.GetLastWriteTime(md);
        var pdf = Path.Combine(pageDir, "reports", "FinalReport.pdf");
        var docx = Path.Combine(pageDir, "reports", "FinalReport.docx");
        PdfDocumentWriter.Write(pdf, title, blocks, at);
        DocxDocumentWriter.Write(docx, title, blocks);
        return new[] { pdf, docx };
    }

    internal static IReadOnlyList<string> WriteTestCaseResults(ReportSources d, List<string> warnings)
    {
        var blocks = BuildTestCaseBlocks(d, warnings);
        var title = d.RunLabel is { } run ? $"Test Case Results - {d.Page} - {run}" : $"Test Case Results - {d.Page}";
        var pdf = Path.Combine(d.PageDir, "reports", "TestCaseResults.pdf");
        var docx = Path.Combine(d.PageDir, "reports", "TestCaseResults.docx");
        PdfDocumentWriter.Write(pdf, title, blocks, d.Now);
        DocxDocumentWriter.Write(docx, title, blocks);
        return new[] { pdf, docx };
    }

    internal static List<DocBlock> BuildTestCaseBlocks(ReportSources d, List<string>? warnings = null)
    {
        var blocks = new List<DocBlock>();
        if (d.RunId != null) blocks.Add(new ParagraphBlock(d.RunDetails));
        blocks.AddRange(new DocBlock[]
        {
            new ParagraphBlock($"Generated: {d.Now:yyyy-MM-dd HH:mm:ss} by the Reports tool from runs/Module*/Reference|Target/results.json and the step screenshots (no hand-written content)."),
            new ParagraphBlock("For every step: what the user did and what was checked, the Reference and Target status, the expected value (from the Reference run) against the actual Target value, and a screenshot of each system after the step."),
            new ParagraphBlock("Reference = the old page (the original app), Target = the new page (the migrated app). A step passes when every check holds; the Target must give the same values as the Reference.")
        });

        // Overview table
        var overview = new List<IReadOnlyList<string>>();
        var ids = d.PlannedIds().Concat(d.ModuleIds()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var x in ids)
        {
            var r = d.Results(x, "Reference");
            var t = d.Results(x, "Target");
            var s = d.Summary(x);
            overview.Add(new[]
            {
                x, d.ModuleName(x),
                r == null ? "not run" : $"{r.Totals.StepsPass}/{r.Steps.Count} {s?.ReferenceStatus ?? (ModuleReportGenerator.ReferencePasses(r) ? "PASS" : "FAIL")}",
                t == null ? "not run" : $"{t.Totals.StepsPass}/{t.Steps.Count} {s?.TargetStatus ?? (ModuleReportGenerator.ReferencePasses(t) ? "PASS" : "FAIL")}",
                s?.OpenFindings.ToString() ?? "-"
            });
        }
        blocks.Add(new HeadingBlock(2, "Modules"));
        blocks.Add(new ParagraphBlock("One row per module: steps passed of the total on each side, the module result, and the open functional gaps (behavior or data that differs)."));
        blocks.Add(new TableBlock(new[] { "Module", "Workflow", "Old page (Reference) steps passed", "New page (Target) steps passed", "Open functional gaps" }, overview));

        var missingShots = new List<string>();
        foreach (var x in ids)
        {
            var reference = d.Results(x, "Reference");
            var target = d.Results(x, "Target");
            blocks.Add(new HeadingBlock(2, $"Module {x} - {d.ModuleName(x)}"));
            if (d.PlanModule(x)?.UserStory is { Length: > 0 } story) blocks.Add(new ParagraphBlock(story));
            if (reference == null && target == null)
            {
                blocks.Add(new ParagraphBlock("Not run: no results.json for Reference or Target."));
                continue;
            }
            var planSteps = d.PlanModule(x)?.Steps ?? new List<PlanStep>();
            var n = 0;
            foreach (var name in ModuleReportGenerator.StepNames(reference, target))
            {
                n++;
                var rs = ModuleReportGenerator.FindStep(reference, name);
                var ts = ModuleReportGenerator.FindStep(target, name);
                var any = rs ?? ts!;
                var title = string.IsNullOrWhiteSpace(any.Title) ? name : any.Title;
                blocks.Add(new HeadingBlock(3, $"{n}. {name} - {title}"));

                var rStatus = rs == null ? "not run" : ModuleReportGenerator.StepStatus(rs);
                var tStatus = ts == null ? "not run" : ModuleReportGenerator.StepStatus(ts);
                var planned = planSteps.FirstOrDefault(p => p.Id == name);
                var rows = new List<IReadOnlyList<string>>();
                if (planned != null && planned.ChecksText.Count > 0)
                    rows.Add(new[] { "Planned checks", ChatRenderer.CheckText(planned, d.Labels), "" });
                rows.Add(new[] { "Status", rStatus, tStatus });
                rows.Add(new[]
                {
                    "What happened",
                    rs == null ? "-" : MarkdownBlocks.StripFieldNotes(d.Labels.Describe(rs, rStatus, isTarget: false)),
                    ts == null ? "-" : MarkdownBlocks.StripFieldNotes(d.Labels.Describe(ts, tStatus, isTarget: true))
                });
                blocks.Add(new TableBlock(new[] { "", "Old page (Reference)", "New page (Target)" }, rows));

                var checks = ExpectedVsActual(d.Labels, rs, ts);
                if (checks.Count > 0)
                    blocks.Add(new TableBlock(new[] { "Check", "Expected (Reference)", "Actual (Target)", "Result" }, checks));

                var refShot = Shot(d, rs);
                var tgtShot = Shot(d, ts);
                if (refShot == null || tgtShot == null) missingShots.Add($"{x} {name}");
                blocks.Add(new ImagePairBlock($"Reference after {name}", refShot, $"Target after {name}", tgtShot));
            }
        }
        if (missingShots.Count > 0)
            warnings?.Add($"TestCaseResults: {missingShots.Count} step(s) without a Reference or Target screenshot: " +
                          $"{string.Join(", ", missingShots.Take(10))}{(missingShots.Count > 10 ? ", ..." : "")} " +
                          "(steps recorded without ExecuteStep have no page to capture; -Screenshots failed/none also skips steps)");
        return blocks;
    }

    /// <summary>Absolute screenshot path of a step when the file exists.</summary>
    private static string? Shot(ReportSources d, RunStep? s)
    {
        if (string.IsNullOrWhiteSpace(s?.Screenshot)) return null;
        var p = Path.Combine(d.PageDir, s!.Screenshot!.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(p) ? p : null;
    }

    /// <summary>
    /// Target checks joined to the Reference check with the same (field, type): expected = the Reference value,
    /// actual = the Target value. Failed checks first, at most <see cref="MaxChecksPerStep"/>.
    /// </summary>
    internal static List<IReadOnlyList<string>> ExpectedVsActual(FieldLabels labels, RunStep? rs, RunStep? ts)
    {
        var rows = new List<(bool Failed, string[] Row)>();
        var source = ts?.Assertions ?? rs?.Assertions ?? new List<RunAssertion>();
        foreach (var a in source)
        {
            var refA = rs?.Assertions.FirstOrDefault(r => r.Field == a.Field && r.Type == a.Type);
            var check = $"{labels.Label(a.Field)} {TypeWord(a.Type)}";
            string expected, actual;
            if (ts == null)
            {
                expected = Value(a, reference: true);
                actual = "-";
            }
            else
            {
                expected = refA != null ? Value(refA, reference: true) : Value(a, reference: true) + " (no Reference check)";
                actual = Value(a, reference: false);
            }
            rows.Add((!a.Passed, new[] { check, expected, actual, a.Passed ? "PASS" : "FAIL" }));
        }
        return rows.OrderByDescending(r => r.Failed).Take(MaxChecksPerStep).Select(r => (IReadOnlyList<string>)r.Row).ToList();
    }

    private static string TypeWord(string type) => type switch
    {
        "CHANGES" => "changes",
        "STAYS SAME" => "stays the same",
        "EQUALS" => "value",
        "CONTAINS" => "contains",
        _ => "check"
    };

    /// <summary>CHANGES/STAYS SAME: "before -> after"; EQUALS/CONTAINS: the expected (Reference) or actual value; BOOL: message.</summary>
    private static string Value(RunAssertion a, bool reference) => a.Type switch
    {
        "CHANGES" => $"{V(a.Field, a.Expected)} -> {V(a.Field, a.Actual)}",
        "STAYS SAME" => V(a.Field, a.Actual),
        "EQUALS" or "CONTAINS" => reference ? V(a.Field, a.Expected) : V(a.Field, a.Actual),
        _ => string.IsNullOrWhiteSpace(a.Message) ? (a.Passed ? "check passed" : "check failed") : a.Message
    };

    /// <summary>Quoted value, or its plain word for sort states and .enabled/.visible booleans ("ascending", "disabled").</summary>
    private static string V(string field, string? value)
    {
        var plain = FieldLabels.ValueText(field, value);
        return plain != (value ?? "") ? plain : FieldLabels.Q(value);
    }
}
