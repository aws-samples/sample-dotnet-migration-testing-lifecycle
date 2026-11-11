using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// P1 contract for .kiro/workflows/test-page.workflow.json: gates pause through send_message warning
/// (no need_input completion signal), and author slots pause instead of ending when the plan is not signed off.
/// </summary>
[TestFixture]
public class WorkflowContractTests
{
    private static JsonNode LoadWorkflow()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".kiro", "workflows")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root with .kiro/workflows not found above the test directory");
        var path = Path.Combine(dir!.FullName, ".kiro", "workflows", "test-page.workflow.json");
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static IEnumerable<JsonObject> AllSteps(JsonNode wf)
    {
        static IEnumerable<JsonObject> Walk(JsonArray? arr)
        {
            if (arr == null) yield break;
            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                yield return o;
                foreach (var c in Walk(o["branches"] as JsonArray)) yield return c;
                foreach (var c in Walk(o["steps"] as JsonArray)) yield return c;
            }
        }
        return Walk(wf["steps"] as JsonArray);
    }

    private static JsonObject Step(JsonNode wf, string id) => AllSteps(wf).Single(s => (string?)s["id"] == id);

    [Test]
    public void Workflow_Parses_AndNoStepUsesNeedInputCompletion()
    {
        var wf = LoadWorkflow();
        foreach (var s in AllSteps(wf))
            Assert.That((string?)s["completion"]?["completionSignal"], Is.Not.EqualTo("need_input"), $"step {s["id"]}");
    }

    [TestCase("plan", "plan_signoff")]
    [TestCase("final-report", "final_signoff")]
    public void GateSteps_PauseUntilConfirmed(string id, string gate)
    {
        var prompt = (string)Step(LoadWorkflow(), id)["prompt"]!;
        Assert.That(prompt, Does.Not.Contain("need_input"));
        Assert.That(prompt, Does.Contain("the step stays paused until the human replies"));
        Assert.That(prompt, Does.Contain($"Never send success before `.\\.kiro\\test-project\\lifecycle.ps1 -Action gate -Page {{{{page}}}} -Gate {gate} -State confirmed` exits 0"));
    }

    [Test]
    public void AuthorSlots_WaitForPlanSignOff_WithWarning()
    {
        var wf = LoadWorkflow();
        for (var k = 1; k <= 6; k++)
        {
            var prompt = (string)Step(wf, $"author-{k}")["prompt"]!;
            Assert.That(prompt, Does.Contain("send_message warning 'waiting for plan sign-off"), $"author-{k}");
            Assert.That(prompt, Does.Not.Contain("send_message error 'plan not signed off'"), $"author-{k}");
        }
    }
    [Test]
    public void RunStep_SkippedModules_StillRenderModuleResultsBeforeRunSummary()
    {
        var prompt = (string)Step(LoadWorkflow(), "run")["prompt"]!;
        var skip = prompt.IndexOf("for EVERY skipped module X, in plan order", StringComparison.Ordinal);
        Assert.That(skip, Is.GreaterThan(0));
        Assert.That(prompt.IndexOf("-View module-result -Module X", skip, StringComparison.Ordinal), Is.GreaterThan(skip));
        Assert.That(prompt.IndexOf("-View run-summary", skip, StringComparison.Ordinal), Is.GreaterThan(skip), "run-summary after the module results");
    }
    [Test]
    public void Lifecycle_ProgressPhase0_StartsNewSessionUnlessInterrupted()
    {
        // BUG 1: a new workflow run must not reuse results of earlier runs through the resume-skip rule.
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".kiro", "workflows"))) dir = dir.Parent;
        var ps = File.ReadAllText(Path.Combine(dir!.FullName, ".kiro", "test-project", "lifecycle.ps1"));
        var body = ps[ps.IndexOf("function Invoke-ProgressAction", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\nfunction ", 10, StringComparison.Ordinal)];
        Assert.That(body, Does.Contain("if ($Phase -eq 0)"));
        Assert.That(body, Does.Contain("Test-SessionResumable $s"));
        Assert.That(body, Does.Contain("Start-NewSessionForRun $s"));
        var rule = ps[ps.IndexOf("function Test-SessionResumable", StringComparison.Ordinal)..];
        rule = rule[..rule.IndexOf("\nfunction ", 10, StringComparison.Ordinal)];
        Assert.That(rule, Does.Contain("finalReport.signedOff -eq $true"));
        Assert.That(rule, Does.Contain("'report_pending', 'complete'"));
        Assert.That(rule, Does.Contain("TotalHours -lt $MaxIdleHours"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".kiro", "workflows"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root with .kiro/workflows not found above the test directory");
        return dir!.FullName;
    }

    private static string LifecycleFunction(string name)
    {
        var ps = File.ReadAllText(Path.Combine(RepoRoot(), ".kiro", "test-project", "lifecycle.ps1"));
        var body = ps[ps.IndexOf("function " + name, StringComparison.Ordinal)..];
        return body[..body.IndexOf("\nfunction ", 10, StringComparison.Ordinal)];
    }

    [Test]
    public void NoSampleSourceLiterals_InWorkflowAndAgents()
    {
        // The source paths come from appsettings (ReferenceSourcePath / TargetSourcePath) via status.json paths.*.
        var root = RepoRoot();
        var files = new List<string> { Path.Combine(root, ".kiro", "workflows", "test-page.workflow.json") };
        files.AddRange(Directory.GetFiles(Path.Combine(root, ".kiro", "agents"), "*.md"));
        Assert.That(files, Has.Count.GreaterThan(1));
        // Generic: any sample/<Name>.Legacy|Web|BlazorWeb|Host path, whatever the sample is called.
        var samplePath = new System.Text.RegularExpressions.Regex(@"sample[/\\][A-Za-z0-9_]+\.(Legacy|Web|BlazorWeb|Host)\b");
        // Plus the configured source paths themselves (both slash forms).
        var settings = LoadAppSettings();
        var literals = new[] { (string)settings["ReferenceSourcePath"]!, (string)settings["TargetSourcePath"]! }
            .SelectMany(p => new[] { p.Replace('\\', '/'), p.Replace('/', '\\') }).ToList();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            Assert.That(samplePath.IsMatch(text), Is.False, $"{Path.GetFileName(f)}: {samplePath.Match(text).Value}");
            foreach (var lit in literals)
                Assert.That(text, Does.Not.Contain(lit), Path.GetFileName(f));
        }
    }

    private static JsonNode LoadAppSettings() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ".kiro", "test-project", "appsettings.test.json")))!;

    [Test]
    public void AppSettingsSourcePaths_Exist()
    {
        // A sample rename must update appsettings; otherwise this fails loudly instead of the guard above going quiet.
        var root = RepoRoot();
        var settings = LoadAppSettings();
        foreach (var key in new[] { "ReferenceSourcePath", "TargetSourcePath" })
        {
            var rel = (string?)settings[key];
            Assert.That(rel, Is.Not.Null.And.Not.Empty, key);
            Assert.That(Directory.Exists(Path.Combine(root, rel!)), Is.True, $"{key} '{rel}' does not exist");
        }
        var apps = settings["Apps"] as JsonArray;
        Assert.That(apps, Is.Not.Null.And.Not.Empty, "Apps");
        var testProject = Path.Combine(root, ".kiro", "test-project");
        foreach (var app in apps!)
        {
            var project = (string?)app!["project"];
            Assert.That(project, Is.Not.Null.And.Not.Empty, (string?)app["name"]);
            Assert.That(File.Exists(Path.GetFullPath(Path.Combine(testProject, project!))), Is.True,
                $"Apps[{(string?)app["name"]}].project '{project}' does not exist");
        }
    }

    [Test]
    public void ZeroPromptMarker_MatchesPermissionsTemplateLine1()
    {
        // Doctor looks for this marker in the installed permissions file; template and runner must agree.
        const string marker = "# testing-lifecycle zero-prompt rules v1";
        var root = RepoRoot();
        var line1 = File.ReadLines(Path.Combine(root, ".kiro", "settings", "permissions.yaml")).First();
        Assert.That(line1, Is.EqualTo(marker));
        var ps = File.ReadAllText(Path.Combine(root, ".kiro", "test-project", "lifecycle.ps1"));
        Assert.That(ps, Does.Contain("$ZeroPromptMarker = '" + marker + "'"));
    }

    [Test]
    public void Lifecycle_ProgressPhase0_WritesSourcePaths()
    {
        Assert.That(LifecycleFunction("Invoke-ProgressAction"), Does.Contain("Get-SourcePaths"));
    }

    [Test]
    public void Doctor_HasExecutionPolicyAndSourcePathChecks()
    {
        var body = LifecycleFunction("Invoke-Doctor");
        Assert.That(body, Does.Contain("Get-FreshShellExecutionPolicy"));
        Assert.That(body, Does.Contain("Set-ExecutionPolicy -Scope CurrentUser RemoteSigned"));
        Assert.That(body, Does.Contain("Get-SourcePaths"));
        // Policy of a new shell: the scope list, not this process (which may run under -ExecutionPolicy Bypass).
        var ep = LifecycleFunction("Get-FreshShellExecutionPolicy");
        Assert.That(ep, Does.Contain("Get-ExecutionPolicy -List"));
        Assert.That(ep, Does.Contain("'MachinePolicy', 'UserPolicy', 'CurrentUser', 'LocalMachine'"));
        // All scopes Undefined: host default by edition (PowerShell 7 = RemoteSigned, Windows PowerShell 5.1 = Restricted).
        Assert.That(ep, Does.Contain("$PSVersionTable.PSEdition -eq 'Core'"));
        Assert.That(ep, Does.Match(@"PSEdition -eq 'Core'\)\s*\{\s*\$effective = 'RemoteSigned'"));
        Assert.That(ep, Does.Match(@"else\s*\{\s*\$effective = 'Restricted'"));
    }

    [Test]
    public void FixPrompt_AndTargetFixer_WorkOnPageLevelCssGroups()
    {
        var prompt = (string)Step(LoadWorkflow(), "fix")["prompt"]!;
        var fixer = File.ReadAllText(Path.Combine(RepoRoot(), ".kiro", "agents", "target-fixer.md"));
        foreach (var (name, text) in new[] { ("fix prompt", prompt), ("target-fixer.md", fixer) })
        {
            Assert.That(text, Does.Contain("css_groups.json"), name);
            Assert.That(text, Does.Contain("css:group:"), name);
            Assert.That(text, Does.Contain("ONE CSS rule or theme token change"), name);
            Assert.That(text, Does.Contain("Reference larger"), name);
        }
    }

    [TestCase("Complete-ModuleRun")]
    [TestCase("Complete-ModuleRerun")]
    [TestCase("Invoke-ReportAction")]
    public void Lifecycle_SyncsCssGroupCountsAfterReports(string function)
    {
        // Group ownership can move to another module when a module reports: every module's counts are re-read.
        var body = LifecycleFunction(function);
        Assert.That(body.IndexOf("Sync-CssGroupCounts", StringComparison.Ordinal),
            Is.GreaterThan(body.IndexOf("Merge-Summary", StringComparison.Ordinal)), function);
        Assert.That(LifecycleFunction("Merge-Summary"), Does.Contain("$m.actionableCssRows = Get-Int $sum.actionableCssRows"));
    }

    [Test]
    public void Doctor_ChecksStackProfileChain()
    {
        var body = LifecycleFunction("Invoke-Doctor");
        Assert.That(body, Does.Contain("'Stack profile'"));
        Assert.That(body, Does.Contain("$settings['StackProfile']"));
        Assert.That(body, Does.Contain("$pj.extends"));
    }
}
