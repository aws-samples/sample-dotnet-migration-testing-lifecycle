using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>P2: the remaining-time estimate only uses finished previous lifecycles, caps measured times at the budget,
/// skips phases already done in this lifecycle and never exceeds the total budget.</summary>
[TestFixture]
public class LifecycleTimingsTests
{
    private static string Seg(string start, string? end) =>
        end == null ? $$"""{ "start": "{{start}}", "end": null }""" : $$"""{ "start": "{{start}}", "end": "{{end}}" }""";

    // Shape of PayorList timings.json at the "tests-authored" render: #1 never finished and holds a 3 h 44 min Run
    // segment, #2 never finished, #3 is current (phase 1 running for 28 s, elapsed 32 s).
    private const string ThreeLifecycles = """
    { "lifecycles": [
      { "id": "20261027-120342", "startedAt": "2026-10-27T12:03:42+05:30", "finishedAt": null,
        "phases": [ { "id": 0, "status": "done", "segments": [ { "start": "2026-10-27T12:03:42+05:30", "end": "2026-10-27T12:06:00+05:30" } ] },
                    { "id": 1, "status": "running", "segments": [ { "start": "2026-10-27T12:06:00+05:30", "end": null } ] },
                    { "id": 2, "status": "done", "segments": [], "activeMs": 13432858 },
                    { "id": 3, "status": "skipped", "segments": [] },
                    { "id": 4, "status": "running", "segments": [ { "start": "2026-10-27T15:50:00+05:30", "end": null } ] } ],
        "gates": [] },
      { "id": "20261027-153000", "startedAt": "2026-10-27T15:30:00+05:30", "finishedAt": null,
        "phases": [ { "id": 0, "status": "running", "segments": [ { "start": "2026-10-27T15:30:00+05:30", "end": null } ] } ], "gates": [] },
      { "id": "20261027-155606", "startedAt": "2026-10-27T15:56:06+05:30", "finishedAt": null,
        "phases": [ { "id": 0, "status": "done", "segments": [ { "start": "2026-10-27T15:56:06+05:30", "end": "2026-10-27T15:56:10+05:30" } ] },
                    { "id": 1, "status": "running", "segments": [ { "start": "2026-10-27T15:56:10+05:30", "end": null } ] },
                    { "id": 2, "status": "pending", "segments": [] }, { "id": 3, "status": "pending", "segments": [] }, { "id": 4, "status": "pending", "segments": [] } ],
        "gates": [] } ] }
    """;

    [Test]
    public void Remaining_IgnoresUnfinishedPreviousLifecycle()
    {
        var t = LifecycleTimings.FromNode(JsonNode.Parse(ThreeLifecycles), DateTimeOffset.Parse("2026-10-27T15:56:38+05:30"));
        Assert.That(t.ElapsedMs, Is.EqualTo(32000));
        Assert.That(t.Phases[1].ActiveMs, Is.EqualTo(28000));
        Assert.That(t.RemainingMs, Is.LessThanOrEqualTo(900000));
        // (240 s - 28 s) + Run 240 s + Fix 180 s + Report 60 s: all budgets, the stale 13,432,858 ms is ignored.
        Assert.That(t.RemainingMs, Is.EqualTo(212000 + 240000 + 180000 + 60000));
        Assert.That(t.PhaseLine(), Is.EqualTo("Phase 2 of 5 (Author tests) - elapsed 00:32 - estimated remaining 11:32"));
    }

    [Test]
    public void Estimate_CapsMeasuredAtBudget()
    {
        var json = $$"""
        { "lifecycles": [
          { "id": "old", "startedAt": "2026-10-26T10:00:00+05:30", "finishedAt": "2026-10-26T11:00:00+05:30",
            "phases": [ { "id": 0, "status": "done", "segments": [ {{Seg("2026-10-26T10:00:00+05:30", "2026-10-26T10:01:00+05:30")}} ] },
                        { "id": 1, "status": "done", "segments": [ {{Seg("2026-10-26T10:01:00+05:30", "2026-10-26T10:02:00+05:30")}} ] },
                        { "id": 2, "status": "done", "segments": [ {{Seg("2026-10-26T10:02:00+05:30", "2026-10-26T10:52:00+05:30")}} ] },
                        { "id": 3, "status": "skipped", "segments": [] },
                        { "id": 4, "status": "done", "segments": [ {{Seg("2026-10-26T10:52:00+05:30", "2026-10-26T10:53:00+05:30")}} ] } ], "gates": [] },
          { "id": "new", "startedAt": "2026-10-27T10:00:00+05:30", "finishedAt": null,
            "phases": [ { "id": 0, "status": "running", "segments": [ {{Seg("2026-10-27T10:00:00+05:30", null)}} ] } ], "gates": [] } ] }
        """;
        var t = LifecycleTimings.FromNode(JsonNode.Parse(json), DateTimeOffset.Parse("2026-10-27T10:00:30+05:30"));
        Assert.That(t.Phases[2].EstimateMs, Is.EqualTo(240000), "50 min measured, capped at the 4 min budget");
        Assert.That(t.Phases[3].EstimateMs, Is.EqualTo(0), "skipped");
        Assert.That(t.Phases[1].EstimateMs, Is.EqualTo(60000), "measured below the budget");
        // Plan 60 s - 30 s active + 60 + 240 + 0 + 60.
        Assert.That(t.RemainingMs, Is.EqualTo(30000 + 60000 + 240000 + 0 + 60000));
    }

    [Test]
    public void Remaining_SkipsPhasesDoneInThisLifecycle_AndIsCappedByTotalBudget()
    {
        // Fix (3) already skipped in this lifecycle while Run (2) is running again after a baseline gate.
        var json = $$"""
        { "lifecycles": [
          { "id": "new", "startedAt": "2026-10-27T10:00:00+05:30", "finishedAt": null,
            "phases": [ { "id": 0, "status": "done", "segments": [ {{Seg("2026-10-27T10:00:00+05:30", "2026-10-27T10:02:00+05:30")}} ] },
                        { "id": 1, "status": "done", "segments": [ {{Seg("2026-10-27T10:02:00+05:30", "2026-10-27T10:04:00+05:30")}} ] },
                        { "id": 2, "status": "running", "segments": [ {{Seg("2026-10-27T10:04:00+05:30", null)}} ] },
                        { "id": 3, "status": "skipped", "segments": [] },
                        { "id": 4, "status": "pending", "segments": [] } ], "gates": [] } ] }
        """;
        var t = LifecycleTimings.FromNode(JsonNode.Parse(json), DateTimeOffset.Parse("2026-10-27T10:05:00+05:30"));
        Assert.That(t.RemainingMs, Is.EqualTo(180000 + 0 + 60000), "Run 240-60 s, Fix skipped here, Report 60 s");

        // Elapsed 14:00 of 15:00: capped at the 1 minute left.
        var late = LifecycleTimings.FromNode(JsonNode.Parse(json), DateTimeOffset.Parse("2026-10-27T10:14:00+05:30"));
        Assert.That(late.RemainingMs, Is.EqualTo(60000));
        // Over budget: capped at the total budget, never more.
        var over = LifecycleTimings.FromNode(JsonNode.Parse(json.Replace("\"id\": 2, \"status\": \"running\"", "\"id\": 2, \"status\": \"done\"")
            .Replace("\"id\": 4, \"status\": \"pending\"", "\"id\": 4, \"status\": \"running\"")), DateTimeOffset.Parse("2026-10-27T12:00:00+05:30"));
        Assert.That(over.RemainingMs, Is.LessThanOrEqualTo(900000));
    }
}
