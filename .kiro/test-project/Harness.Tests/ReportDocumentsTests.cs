using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>P8: FinalReport.pdf/.docx and TestCaseResults.pdf/.docx from the PayorListMini fixture.</summary>
[TestFixture]
public class ReportDocumentsTests
{
    private string _pageDir = "";

    /// <summary>32x18 baseline JPEG (solid light blue), generated once with System.Drawing.</summary>
    internal const string TinyJpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAASACADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD7rooor6M+fCiiigAooooAKKKKAP/Z";

    [SetUp]
    public void SetUp() => _pageDir = ChatRendererTests.CopyFixture();

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(Path.GetDirectoryName(_pageDir)!, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    /// <summary>Tiny JPEG per step and system, referenced from results.json like ModuleTestRunner does.</summary>
    private void AddScreenshots(string module)
    {
        foreach (var sys in new[] { "Reference", "Target" })
        {
            var path = Path.Combine(_pageDir, "runs", $"Module{module}", sys, "results.json");
            var r = JsonNode.Parse(File.ReadAllText(path))!;
            foreach (var step in r["steps"]!.AsArray())
            {
                var name = (string)step!["step"]!;
                var rel = $"runs/Module{module}/{sys}/screenshots/{name}.jpg";
                var abs = Path.Combine(_pageDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                File.WriteAllBytes(abs, Convert.FromBase64String(TinyJpegBase64));
                step["screenshot"] = rel;
            }
            File.WriteAllText(path, r.ToJsonString());
        }
    }

    [Test]
    public void WriteAll_ProducesPdfAndDocx_WithSectionsAndSideBySideScreenshots()
    {
        AddScreenshots("A");
        ModuleReportGenerator.Generate(_pageDir, "ALL");
        var result = ReportDocuments.WriteAll(_pageDir, ChatRendererTests.Now);

        var names = result.Files.Select(Path.GetFileName).ToList();
        Assert.That(names, Is.EqualTo(new[] { "FinalReport.pdf", "FinalReport.docx", "TestCaseResults.pdf", "TestCaseResults.docx" }));
        foreach (var f in result.Files)
        {
            Assert.That(new FileInfo(f).Length, Is.GreaterThan(1000), f);
            if (f.EndsWith(".pdf", StringComparison.Ordinal))
                Assert.That(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(f).Take(4).ToArray()), Is.EqualTo("%PDF"), f);
        }

        using (var final = WordprocessingDocument.Open(result.Files[1], false))
        {
            var text = final.MainDocumentPart!.Document.Body!.InnerText;
            foreach (var h in new[] { "Executive Summary", "Module Results", "Functional Gaps", "CSS Comparison", "Bug Fixes", "Manual Tests", "Limitations" })
                Assert.That(text, Does.Contain(h));
            Assert.That(text, Does.Not.Contain("(field: "), "audit field notes are removed from the documents");
        }

        using var tcr = WordprocessingDocument.Open(result.Files[3], false);
        var main = tcr.MainDocumentPart!;
        var body = main.Document.Body!.InnerText;
        Assert.That(body, Does.Contain("Module A - Load the page and sort the grid"));
        Assert.That(body, Does.Contain("Pager text is '1-10 of 15', expected 'View 1 - 10 of 15' (Reference)."));
        Assert.That(body, Does.Contain("Expected (Reference)"));
        // Module A has 3 steps (NAV, Step1, Step2) on both systems → 2 images per step; Module B has none.
        Assert.That(main.ImageParts.Count(), Is.EqualTo(6));
        var ids = main.Document.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Select(p => p.Id!.Value).ToList();
        Assert.That(ids, Is.Unique);
        Assert.That(body, Does.Contain("(no screenshot)"), "Module B steps without screenshots");
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExpectedVsActual_FailedFirst_ReferenceValueAsExpected()
    {
        var labels = new FieldLabels(new Dictionary<string, string> { ["grid.rowCount"] = "Rows shown" });
        var rs = new RunStep { Assertions = { new RunAssertion { Field = "grid.rowCount", Type = "EQUALS", Expected = "10", Actual = "10", Passed = true },
                                              new RunAssertion { Field = "pager.info", Type = "CHANGES", Expected = "a", Actual = "b", Passed = true } } };
        var ts = new RunStep { Assertions = { new RunAssertion { Field = "pager.info", Type = "CHANGES", Expected = "a", Actual = "b", Passed = true },
                                              new RunAssertion { Field = "grid.rowCount", Type = "EQUALS", Expected = "10", Actual = "5", Passed = false } } };
        var rows = ReportDocuments.ExpectedVsActual(labels, rs, ts);
        Assert.That(rows[0], Is.EqualTo(new[] { "Rows shown value", "10", "5", "FAIL" }));
        Assert.That(rows[1], Is.EqualTo(new[] { "Pager info changes", "'a' -> 'b'", "'a' -> 'b'", "PASS" }));
    }

    [Test]
    public void ExpectedVsActual_SortStatesAndBooleansInPlainWords()
    {
        var labels = new FieldLabels();
        var rs = new RunStep { Assertions = { new RunAssertion { Field = "sort.indicator[Name]", Type = "EQUALS", Expected = "asc", Actual = "asc", Passed = true },
                                              new RunAssertion { Field = "saveButton.enabled", Type = "STAYS SAME", Expected = "false", Actual = "false", Passed = true } } };
        var rows = ReportDocuments.ExpectedVsActual(labels, rs, rs);
        Assert.That(rows[0], Is.EqualTo(new[] { "Sort indicator Name value", "ascending", "ascending", "PASS" }));
        Assert.That(rows[1], Is.EqualTo(new[] { "Save button enabled stays the same", "disabled", "disabled", "PASS" }));
    }
}
