using System.Globalization;
using System.Text.Json.Nodes;

namespace MigrationTests.Reporting;

/// <summary>One phase of the latest lifecycle (design §4), with values recomputed at <see cref="LifecycleTimings.Now"/>.</summary>
public sealed class PhaseTiming
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public long BudgetMs { get; init; }
    /// <summary>pending | running | done | skipped | not-measured.</summary>
    public string Status { get; init; } = "pending";
    /// <summary>Sum of closed segments plus (now - start) of an open segment.</summary>
    public long ActiveMs { get; init; }
    /// <summary>min(activeMs, budget) of the most recent finished previous lifecycle where this phase was done (0 if skipped), else budgetMs.</summary>
    public long EstimateMs { get; init; }
    /// <summary>Start of the first segment (null when the phase never ran).</summary>
    public DateTimeOffset? FirstStart { get; init; }
    public bool Over => ActiveMs > BudgetMs;
}

/// <summary>
/// Reads timings.json "lifecycles[]" (schemaVersion 2) and computes the values behind the progress
/// line and the final-report phase table (design §4). Same formulas as lifecycle.ps1 Get-PhaseLine.
/// Pure: every value is derived from the file and <see cref="Now"/>.
/// </summary>
public sealed class LifecycleTimings
{
    public static readonly string[] PhaseNames = { "Plan", "Author tests", "Run Reference + Target", "Fix", "Report" };
    public static readonly long[] PhaseBudgetMs = { 240000, 240000, 240000, 180000, 60000 };
    public const long DefaultTotalBudgetMs = 900000;
    public const string NoLifecycleLine = "Phase timing not measured (no lifecycle recorded)";
    public const string NoLifecycleTotal = "Total lifecycle time: not measured (no lifecycle recorded)";

    public DateTimeOffset Now { get; }
    public bool HasLifecycle { get; }
    public string? LifecycleId { get; }
    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; }
    public long TotalBudgetMs { get; } = DefaultTotalBudgetMs;
    public IReadOnlyList<PhaseTiming> Phases { get; } = Array.Empty<PhaseTiming>();
    /// <summary>Gate wait clipped to [startedAt, finishedAt ?? now], summed over every gate entry.</summary>
    public long GateWaitMs { get; }
    /// <summary>end - startedAt - gate wait.</summary>
    public long ElapsedMs { get; }
    /// <summary>Elapsed - sum(activeMs).</summary>
    public long OrchestrationMs { get; }
    /// <summary>Time recorded after finishedAt (postGate[]); shown separately, never counted.</summary>
    public long PostGateMs { get; }
    public bool GatePending { get; }
    /// <summary>Highest running phase, else highest done (-1 when none).</summary>
    public int CurrentPhase { get; } = -1;
    /// <summary>Raw latest lifecycle node (null without a lifecycle).</summary>
    public JsonObject? Lifecycle { get; }

    private LifecycleTimings(DateTimeOffset now) { Now = now; }

    private LifecycleTimings(DateTimeOffset now, JsonObject latest, IReadOnlyList<JsonObject> previous)
    {
        Now = now;
        HasLifecycle = true;
        Lifecycle = latest;
        LifecycleId = Str(latest["id"]);
        StartedAt = Date(latest["startedAt"]);
        FinishedAt = Date(latest["finishedAt"]);
        var budget = Long(latest["totalBudgetMs"]);
        if (budget > 0) TotalBudgetMs = budget;
        var end = FinishedAt ?? now;

        var phaseNodes = (latest["phases"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var phases = new List<PhaseTiming>();
        for (var k = 0; k < PhaseNames.Length; k++)
        {
            var p = phaseNodes.FirstOrDefault(n => Long(n["id"], -1) == k);
            var segs = Segments(p?["segments"], end);
            var phaseBudget = p != null && Long(p["budgetMs"]) > 0 ? Long(p["budgetMs"]) : PhaseBudgetMs[k];
            phases.Add(new PhaseTiming
            {
                Id = k,
                Name = Str(p?["name"]) is { Length: > 0 } n ? n : PhaseNames[k],
                BudgetMs = phaseBudget,
                Status = Str(p?["status"]) is { Length: > 0 } s ? s : "pending",
                ActiveMs = segs.Sum(x => Math.Max(0, (long)(x.End - x.Start).TotalMilliseconds)),
                EstimateMs = Estimate(previous, k, phaseBudget, now),
                FirstStart = segs.Count > 0 ? segs.Min(x => x.Start) : null
            });
        }
        Phases = phases;

        var start = StartedAt ?? end;
        long gate = 0;
        foreach (var g in (latest["gates"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var pendingAt = Date(g["pendingAt"]);
            if (pendingAt == null) continue;
            var confirmedAt = Date(g["confirmedAt"]);
            if (confirmedAt == null) GatePending = true;
            gate += GateMs(pendingAt.Value, confirmedAt, start, end, now);
        }
        GateWaitMs = gate;
        ElapsedMs = Math.Max(0, (long)(end - start).TotalMilliseconds - gate);
        OrchestrationMs = Math.Max(0, ElapsedMs - phases.Sum(p => p.ActiveMs));
        PostGateMs = Segments(latest["postGate"], now).Sum(x => Math.Max(0, (long)(x.End - x.Start).TotalMilliseconds));

        var running = phases.Where(p => p.Status == "running").Select(p => p.Id).DefaultIfEmpty(-1).Max();
        CurrentPhase = running >= 0 ? running : phases.Where(p => p.Status == "done").Select(p => p.Id).DefaultIfEmpty(-1).Max();
    }

    /// <summary>Gate wait clipped to the lifecycle window: max(0, min(confirmedAt ?? now, end) - max(pendingAt, startedAt)).</summary>
    public static long GateMs(DateTimeOffset pendingAt, DateTimeOffset? confirmedAt, DateTimeOffset startedAt, DateTimeOffset end, DateTimeOffset now)
    {
        var to = confirmedAt ?? now;
        if (to > end) to = end;
        var from = pendingAt > startedAt ? pendingAt : startedAt;
        return Math.Max(0, (long)(to - from).TotalMilliseconds);
    }

    /// <summary>Loads &lt;pageDir&gt;/timings.json. Missing file or empty lifecycles[] → "No lifecycle".</summary>
    public static LifecycleTimings Load(string pageDir, DateTimeOffset? now = null) =>
        FromNode(ReportJson.ReadNode(Path.Combine(pageDir, "timings.json")), now ?? DateTimeOffset.Now);

    public static LifecycleTimings FromNode(JsonNode? timings, DateTimeOffset now)
    {
        var list = (timings?["lifecycles"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        if (list.Count == 0) return new LifecycleTimings(now);
        var previous = list.Take(list.Count - 1).Reverse().ToList(); // most recent first
        return new LifecycleTimings(now, list[^1], previous);
    }

    /// <summary>Remaining estimate: current phase max(0, est - activeMs) plus est of every later phase; 0 once finished.</summary>
    public long RemainingMs
    {
        get
        {
            if (!HasLifecycle || FinishedAt != null) return 0;
            var k = Math.Max(0, CurrentPhase);
            long rem = Math.Max(0, Phases[k].EstimateMs - Phases[k].ActiveMs);
            for (var j = k + 1; j < Phases.Count; j++)
                if (Phases[j].Status is not ("done" or "skipped")) rem += Phases[j].EstimateMs;
            // Cap: what is left of the total budget, or the total budget once it is exceeded.
            var cap = TotalBudgetMs - ElapsedMs;
            if (cap <= 0) cap = TotalBudgetMs;
            return Math.Min(rem, cap);
        }
    }

    /// <summary>"Phase {k+1} of 5 ({name}) - elapsed mm:ss - estimated remaining mm:ss[ - waiting for you]".</summary>
    public string PhaseLine()
    {
        if (!HasLifecycle) return NoLifecycleLine;
        var k = Math.Max(0, CurrentPhase);
        var line = $"Phase {k + 1} of {PhaseNames.Length} ({Phases[k].Name}) - elapsed {Mmss(ElapsedMs)} - estimated remaining {Mmss(RemainingMs)}";
        return GatePending ? line + " - waiting for you" : line;
    }

    /// <summary>Total minutes and seconds, e.g. 965000 → "16:05".</summary>
    public static string Mmss(long ms)
    {
        if (ms < 0) ms = 0;
        var totalSec = ms / 1000;
        return $"{totalSec / 60:00}:{totalSec % 60:00}";
    }

    /// <summary>
    /// Only FINISHED previous lifecycles count (an unfinished one may hold a stale multi-hour segment).
    /// Most recent finished one where phase k is done: min(measured, budget); skipped: 0; else the budget.
    /// Same formula as lifecycle.ps1 Get-PhaseEstimateMs.
    /// </summary>
    internal static long Estimate(IReadOnlyList<JsonObject> previous, int k, long budget, DateTimeOffset now)
    {
        foreach (var lc in previous)
        {
            var finished = Date(lc["finishedAt"]);
            if (finished == null) continue;
            var p = (lc["phases"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(n => Long(n["id"], -1) == k);
            var st = Str(p?["status"]);
            if (st == "skipped") return 0;
            if (st == "done")
            {
                var segs = Segments(p!["segments"], finished.Value);
                var measured = segs.Count > 0 ? segs.Sum(x => Math.Max(0, (long)(x.End - x.Start).TotalMilliseconds)) : Long(p["activeMs"]);
                return Math.Min(measured, budget);
            }
        }
        return budget;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Segments(JsonNode? node, DateTimeOffset openEnd)
    {
        var list = new List<(DateTimeOffset, DateTimeOffset)>();
        foreach (var s in (node as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var start = Date(s["start"]);
            if (start == null) continue;
            var end = Date(s["end"]) ?? openEnd;
            list.Add((start.Value, end < start.Value ? start.Value : end));
        }
        return list;
    }

    internal static string Str(JsonNode? n)
    {
        if (n is not JsonValue v) return "";
        return v.TryGetValue<string>(out var s) ? s : v.ToJsonString();
    }

    internal static long Long(JsonNode? n, long fallback = 0)
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (v.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
        return fallback;
    }

    internal static DateTimeOffset? Date(JsonNode? n)
    {
        var s = Str(n);
        if (s.Length == 0) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
    }
}
