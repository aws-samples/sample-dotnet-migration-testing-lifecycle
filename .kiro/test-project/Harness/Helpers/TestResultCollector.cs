namespace MigrationTests.Playwright;

/// <summary>
/// Thread-safe collector for test results across all modules in a run.
/// </summary>
public static class TestResultCollector
{
    private static readonly List<TestCaseResult> _results = new();
    private static readonly object _lock = new();

    public static void AddResult(TestCaseResult result)
    {
        lock (_lock) { _results.Add(result); }
    }

    public static IReadOnlyList<TestCaseResult> GetResults()
    {
        lock (_lock) { return _results.ToList(); }
    }

    public static void Clear()
    {
        lock (_lock) { _results.Clear(); }
    }
}
