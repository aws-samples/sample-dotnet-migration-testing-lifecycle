namespace MigrationTests.Playwright;

/// <summary>
/// Resolves paths relative to the test-project root (the folder holding lifecycle.ps1),
/// independent of which project's bin the code runs from.
/// </summary>
public static class HarnessPaths
{
    private static readonly Lazy<string> _testRoot = new(FindTestRoot);

    /// <summary>
    /// The test-project root: env LIFECYCLE_TEST_ROOT when set, else the first ancestor of
    /// AppContext.BaseDirectory containing lifecycle.ps1, else AppContext.BaseDirectory.
    /// </summary>
    public static string TestRoot => _testRoot.Value;

    /// <summary>Rooted paths are returned as-is; relative paths are resolved against <see cref="TestRoot"/>.</summary>
    public static string Resolve(string relative)
    {
        if (string.IsNullOrEmpty(relative)) return TestRoot;
        return Path.IsPathRooted(relative) ? relative : Path.GetFullPath(Path.Combine(TestRoot, relative));
    }

    private static string FindTestRoot()
    {
        var env = Environment.GetEnvironmentVariable("LIFECYCLE_TEST_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lifecycle.ps1"))) return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
