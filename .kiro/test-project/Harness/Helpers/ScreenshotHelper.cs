using Microsoft.Playwright;

namespace MigrationTests.Playwright;

/// <summary>
/// Manages screenshot capture with standardized naming and directory structure.
/// Creates a timestamped subdirectory per scenario.
/// </summary>
public class ScreenshotHelper
{
    private readonly string _baseDir;
    private readonly string _scenarioName;
    private readonly string _outputDirectory;
    private int _stepCounter;

    public bool SkipCapture { get; set; }
    public string OutputDirectory => _outputDirectory;
    public int StepCount => _stepCounter;

    public ScreenshotHelper(string scenarioName, string baseDirectory = "screenshots")
    {
        _scenarioName = SanitizeForPath(scenarioName);
        _baseDir = baseDirectory;
        _stepCounter = 0;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _outputDirectory = Path.Combine(_baseDir, _scenarioName, timestamp);
    }

    public async Task<ScreenshotInfo> CaptureAsync(IPage page, string stepDescription)
    {
        _stepCounter++;
        if (SkipCapture)
            return new ScreenshotInfo(_scenarioName, _stepCounter, stepDescription, "", DateTime.Now);

        Directory.CreateDirectory(_outputDirectory);
        var fileName = $"{_stepCounter:D2}_{SanitizeForPath(stepDescription)}.png";
        var filePath = Path.Combine(_outputDirectory, fileName);

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = filePath, FullPage = true });
        return new ScreenshotInfo(_scenarioName, _stepCounter, stepDescription, filePath, DateTime.Now);
    }

    public async Task<ScreenshotInfo> CaptureElementAsync(ILocator element, string stepDescription)
    {
        _stepCounter++;
        if (SkipCapture)
            return new ScreenshotInfo(_scenarioName, _stepCounter, stepDescription, "", DateTime.Now);

        Directory.CreateDirectory(_outputDirectory);
        var fileName = $"{_stepCounter:D2}_{SanitizeForPath(stepDescription)}.png";
        var filePath = Path.Combine(_outputDirectory, fileName);

        await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = filePath });
        return new ScreenshotInfo(_scenarioName, _stepCounter, stepDescription, filePath, DateTime.Now);
    }

    public async Task<ScreenshotInfo> HighlightAndCaptureAsync(IPage page, string selector, string stepDescription, string color = "green")
    {
        _stepCounter++;
        if (SkipCapture)
            return new ScreenshotInfo(_scenarioName, _stepCounter, stepDescription, "", DateTime.Now);

        await page.EvaluateAsync(@"([selector, color, label]) => {
            const el = document.querySelector(selector);
            if (!el) return;
            el.style.outline = `3px solid ${color}`;
            el.style.outlineOffset = '2px';
            const annotation = document.createElement('div');
            annotation.id = 'test-annotation';
            annotation.textContent = label;
            annotation.style.cssText = `position:absolute;top:-32px;left:0;z-index:99999;color:${color};font-size:16px;font-weight:bold;font-family:Arial;`;
            el.style.position = 'relative';
            el.appendChild(annotation);
        }", new object[] { selector, color, stepDescription });

        await Task.Delay(200);
        var result = await CaptureAsync(page, stepDescription);

        await page.EvaluateAsync(@"([selector]) => {
            const el = document.querySelector(selector);
            if (!el) return;
            el.style.outline = '';
            el.style.outlineOffset = '';
            document.getElementById('test-annotation')?.remove();
        }", new object[] { selector });

        return result;
    }

    internal static string SanitizeForPath(string input)
    {
        var sanitized = input;
        foreach (var c in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(c, '_');
        return sanitized.Replace(' ', '_');
    }
}

public record ScreenshotInfo(string ScenarioName, int StepNumber, string StepDescription, string FilePath, DateTime CapturedAt);
