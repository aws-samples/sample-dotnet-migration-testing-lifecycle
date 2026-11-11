using System.Diagnostics;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// lifecycle.ps1 gate plan_signoff: the runner fingerprints plan.json (module ids, plannedSteps, test names,
/// cssStates, step ids) and bumps plan.json revision itself when the structure changed but the revision did not.
/// Runs the real script against a throw-away page under .kiro/app-testing/ (git-ignored, deleted in TearDown).
/// </summary>
[TestFixture]
[NonParallelizable]
public class PlanRevisionGateTests
{
    private string _root = "";
    private string _page = "";
    private string _pageDir = "";

    private string PlanPath => Path.Combine(_pageDir, "state-map", "plan.json");
    private string StatusPath => Path.Combine(_pageDir, "status.json");

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("lifecycle.ps1 runs on Windows PowerShell only");
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".kiro", "workflows"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root with .kiro/workflows not found above the test directory");
        _root = dir!.FullName;
        _page = "ZzPlanRev" + Guid.NewGuid().ToString("N")[..12];
        _pageDir = Path.Combine(_root, ".kiro", "app-testing", _page);
        Directory.CreateDirectory(Path.Combine(_pageDir, "state-map"));
    }

    [TearDown]
    public void TearDown()
    {
        if (_pageDir.Length > 0 && Directory.Exists(_pageDir)) Directory.Delete(_pageDir, recursive: true);
    }

    private static JsonObject Module(string id, int plannedSteps, string name = "Edit and save")
    {
        var steps = new JsonArray();
        for (var i = 1; i <= plannedSteps; i++) steps.Add(new JsonObject { ["id"] = $"Step{i}", ["action"] = $"Action {i}" });
        return new JsonObject
        {
            ["id"] = id, ["name"] = name, ["dataEffect"] = "mutating", ["plannedSteps"] = plannedSteps,
            ["referenceTest"] = $"ReferenceZz_Mod{id}", ["targetTest"] = $"Zz_Mod{id}",
            ["cssStates"] = new JsonArray("cell_edit_open", "dirty"), ["steps"] = steps,
            ["notTested"] = new JsonArray("Keyboard-only navigation"),
        };
    }

    private void WritePlan(int revision, params JsonObject[] modules)
    {
        var plan = new JsonObject { ["page"] = _page, ["revision"] = revision, ["modules"] = new JsonArray(modules) };
        File.WriteAllText(PlanPath, plan.ToJsonString());
    }

    private JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!;
    private int PlanRevision() => (int)ReadJson(PlanPath)["revision"]!;
    private JsonNode StatusPlan() => ReadJson(StatusPath)["plan"]!;

    private string Gate(string state)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(_root, ".kiro", "test-project", "lifecycle.ps1"),
                                  "-Action", "gate", "-Page", _page, "-Gate", "plan_signoff", "-State", state })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.That(p.WaitForExit(120_000), Is.True, "gate timed out");
        var output = stdout.Result + stderr.Result;
        Assert.That(p.ExitCode, Is.EqualTo(0), output);
        return output;
    }

    [Test]
    public void FirstGate_KeepsRevision1_AndRecordsFingerprint()
    {
        WritePlan(1, Module("A", 5), Module("C", 8));
        Gate("pending");

        Assert.That(PlanRevision(), Is.EqualTo(1));
        var sp = StatusPlan();
        Assert.That((int?)sp["fingerprintRevision"], Is.EqualTo(1));
        Assert.That((string?)sp["fingerprint"], Has.Length.EqualTo(64));
    }

    [Test]
    public void PlannedStepsChange_WithSameRevision_RunnerBumpsRevision()
    {
        WritePlan(1, Module("A", 5), Module("C", 8));
        Gate("pending");
        var fp1 = (string?)StatusPlan()["fingerprint"];

        WritePlan(1, Module("A", 5), Module("C", 9));   // planner changed plannedSteps but kept revision 1
        Gate("pending");

        Assert.That(PlanRevision(), Is.EqualTo(2), "plan.json revision rewritten by the runner");
        var sp = StatusPlan();
        Assert.That((int?)sp["fingerprintRevision"], Is.EqualTo(2));
        Assert.That((string?)sp["fingerprint"], Is.Not.EqualTo(fp1));
        var plan = ReadJson(PlanPath);
        Assert.That((int?)plan["modules"]![1]!["plannedSteps"], Is.EqualTo(9), "rest of plan.json kept");
        Assert.That(plan["modules"]![1]!["steps"]!.AsArray(), Has.Count.EqualTo(9));
        Assert.That(File.ReadAllText(StatusPath), Does.Contain("plan revision bumped to 2 by runner (fingerprint changed)"));
    }

    [Test]
    public void WordingOnlyChange_DoesNotBump()
    {
        WritePlan(1, Module("A", 5), Module("C", 8));
        Gate("pending");
        Gate("confirmed");

        var c = Module("C", 8, name: "Edit, then save all");
        c["notTested"] = new JsonArray("Keyboard-only navigation", "Screen reader output");
        WritePlan(1, Module("A", 5), c);
        Gate("pending");
        Gate("confirmed");

        Assert.That(PlanRevision(), Is.EqualTo(1));
        var sp = StatusPlan();
        Assert.That((int?)sp["fingerprintRevision"], Is.EqualTo(1));
        Assert.That((int?)sp["signedOffRevision"], Is.EqualTo(1));
        Assert.That(File.ReadAllText(StatusPath), Does.Contain("plan revision 1 re-confirmed"));
    }

    [Test]
    public void ConfirmedAfterFingerprintChange_SignsOffAgain()
    {
        WritePlan(1, Module("A", 5), Module("C", 8));
        Gate("pending");
        Gate("confirmed");
        Assert.That((int?)StatusPlan()["signedOffRevision"], Is.EqualTo(1));

        // Structure changes after sign-off, revision kept; confirmed without a new pending call.
        WritePlan(1, Module("A", 5), Module("C", 9));
        Gate("confirmed");

        Assert.That(PlanRevision(), Is.EqualTo(2));
        var sp = StatusPlan();
        Assert.That((int?)sp["signedOffRevision"], Is.EqualTo(2));
        Assert.That((string?)sp["signedOffFingerprint"], Is.EqualTo((string?)sp["fingerprint"]));
        Assert.That((int?)ReadJson(StatusPath)["modules"]!["C"]!["plannedSteps"], Is.EqualTo(9));
        Assert.That(File.ReadAllText(StatusPath), Does.Contain("plan revision 2 signed off"));
    }
}
