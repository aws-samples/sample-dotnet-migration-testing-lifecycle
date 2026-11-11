namespace MigrationTests.Playwright;

public class TestCaseResult
{
    public string TestName { get; set; } = "";
    public int TestNumber { get; set; }
    public string Description { get; set; } = "";
    public string TestData { get; set; } = "";
    public string Status { get; set; } = "NeedsReview"; // Verified | NeedsReview | IssueFound
    public string StatusNote { get; set; } = "";
    public DateTime ExecutedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public List<ScreenshotInfo> Screenshots { get; set; } = new();
}
