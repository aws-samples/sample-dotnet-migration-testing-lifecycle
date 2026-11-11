using System.Text.Json.Nodes;
using Microsoft.Playwright;
using MigrationTests.Playwright;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// Runs the in-page inventory script on a static page in headless Chromium (no app needed): roles, accessible
/// names, grid row cap, containers/order, health facts and the font/background helpers. Ignored when Chromium
/// cannot be launched (doctor checks the browsers).
/// </summary>
[TestFixture]
public class ElementInventoryBrowserTests
{
    private IPlaywright? _pw;
    private IBrowser? _browser;
    private IPage? _page;

    private const string Html = @"<!doctype html><html><body style='margin:0'>
<form id='f' style='background: rgb(240, 240, 240)'>
  <label for='name'>Payor Name:</label> <input id='name' placeholder='Search by name...'>
  <button type='button' id='go'>Search</button>
  <button type='button' id='icon'></button>
  <input type='submit' value='Save' disabled>
</form>
<h2>Totals</h2>
<h2>Payors</h2>
<table><thead><tr><th><div role='columnheader'>ID</div></th><th>Name</th></tr></thead><tbody>
  <tr><td>1</td><td>Acme</td></tr><tr><td>2</td><td>Beta</td></tr><tr><td>3</td><td>Core</td></tr>
  <tr><td>4</td><td>Delta</td></tr><tr><td>5</td><td>Echo</td></tr>
</tbody></table>
<img src='does-not-exist.png' width='20' height='20'>
<div class='validation-message'>Name is required</div>
</body></html>";

    [OneTimeSetUp]
    public async Task Launch()
    {
        try
        {
            _pw = await Microsoft.Playwright.Playwright.CreateAsync();
            _browser = await _pw.Chromium.LaunchAsync(new() { Headless = true });
        }
        catch (Exception e)
        {
            Assert.Ignore("Chromium not available: " + e.Message.Split('\n')[0]);
        }
    }

    [OneTimeTearDown]
    public async Task Close()
    {
        if (_browser != null) await _browser.CloseAsync();
        _pw?.Dispose();
    }

    [SetUp]
    public async Task NewPage()
    {
        _page = await _browser!.NewPageAsync(new() { ViewportSize = new() { Width = 1366, Height = 800 } });
        await _page.SetContentAsync(Html);
        try { await _page.WaitForFunctionAsync("() => document.images[0].complete", null, new() { Timeout = 3000 }); } catch { }
    }

    [TearDown]
    public async Task ClosePage() { if (_page != null) await _page.CloseAsync(); }

    private async Task<JsonObject> Inventory(int maxRows = 3) =>
        (JsonNode.Parse(await _page!.EvaluateAsync<string>(ElementInventory.InventoryScript, new Dictionary<string, object>
        {
            ["maxRows"] = maxRows, ["maxElements"] = 300, ["health"] = true, ["brokenImages"] = true, ["emptySections"] = true, ["a11y"] = true
        })) as JsonObject)!;

    [Test]
    public async Task Inventory_RolesNamesRowsAndHealth()
    {
        var root = await Inventory();
        var els = root["elements"]!.AsArray().Select(e => e!.AsObject()).ToList();
        string Name(JsonObject e) => e["name"]!.GetValue<string>();
        string Role(JsonObject e) => e["role"]!.GetValue<string>();

        var input = els.Single(e => Role(e) == "textbox");
        Assert.That((Name(input), input["label"]!.GetValue<string>(), input["placeholder"]!.GetValue<string>()),
            Is.EqualTo(("Payor Name:", "Payor Name:", "Search by name...")));
        Assert.That(els.Any(e => Role(e) == "button" && Name(e) == "Search" && e["interactive"]!.GetValue<bool>()));
        var save = els.Single(e => Role(e) == "button" && Name(e) == "Save");
        Assert.That(save["disabled"]!.GetValue<bool>(), Is.True);
        Assert.That(save["states"]!["disabled.cursor"], Is.Not.Null);
        Assert.That(els.Count(e => Role(e) == "columnheader"), Is.EqualTo(2), "nested same-role/same-name div collapsed into its th");
        var cells = els.Where(e => Role(e) == "cell").ToList();
        Assert.That(cells.Count, Is.EqualTo(6), "first 3 data rows x 2 columns");
        Assert.That(cells.Select(c => c["rowIndex"]!.GetValue<int>()).Distinct(), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(cells.Last()["colIndex"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(els.Any(e => Name(e) == "Name is required"), "validation message inventoried");
        var label = els.Single(e => Role(e) == "label");
        Assert.That(label["labelFor"]!.GetValue<string>(), Is.EqualTo(input["domPath"]!.GetValue<string>()));
        Assert.That(input["styles"]!["background-color"]!.GetValue<string>(), Is.EqualTo("rgb(255, 255, 255)"));
        Assert.That(label["styles"]!["background-color"]!.GetValue<string>(), Is.EqualTo("rgb(240, 240, 240)"), "effective background from the form");

        var health = root["health"]!;
        Assert.That(health["brokenImages"]!.AsArray().Single()!.GetValue<string>(), Does.Contain("does-not-exist.png"));
        Assert.That(health["emptySections"]!.AsArray().Select(x => x!.GetValue<string>()), Is.EqualTo(new[] { "heading \"Totals\" has no content below it" }));
        var a11y = health["a11y"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.That(a11y, Has.Some.StartsWith("button without an accessible name: button#icon"));
        Assert.That(a11y, Has.Some.StartsWith("image without alt text"));

        // Selector of the inventory element relocates it (used for hover/focus).
        Assert.That(await _page!.Locator("css=" + input["domPath"]!.GetValue<string>()).GetAttributeAsync("id"), Is.EqualTo("name"));
        var issues = ElementInventory.HealthIssues(health);
        Assert.That(issues.Select(i => i.Kind).Distinct(), Is.EquivalentTo(new[] { ContentIssue.BrokenImage, ContentIssue.EmptySection, ContentIssue.Accessibility }));
    }

    [Test]
    public async Task StateScript_CapturesHoverStyles()
    {
        await _page!.AddStyleTagAsync(new() { Content = "#go:hover { background-color: rgb(0, 0, 200); color: rgb(255, 255, 255); }" });
        var go = _page.Locator("#go");
        await go.HoverAsync();
        var s = JsonNode.Parse(await go.EvaluateAsync<string>(ElementInventory.StateScript))!;
        Assert.That(s["background-color"]!.GetValue<string>(), Is.EqualTo("rgb(0, 0, 200)"));
        Assert.That(s["color"]!.GetValue<string>(), Is.EqualTo("rgb(255, 255, 255)"));
        Assert.That(s["outline"], Is.Not.Null);
        Assert.That(s["border-top"]!.GetValue<string>(), Does.Contain("px"));
    }

    [Test]
    public async Task InteractiveStates_ForcedViaCdp_NoEventsReachThePage()
    {
        await _page!.AddStyleTagAsync(new() { Content =
            "#go:hover { background-color: rgb(0, 0, 200); } #name:focus { outline: 3px solid rgb(200, 0, 0); }" });
        await _page.EvaluateAsync(@"() => { window.__ev = [];
            for (const t of ['mouseover', 'mouseenter', 'focus', 'focusin', 'blur', 'focusout'])
                document.addEventListener(t, e => window.__ev.push(t + ':' + (e.target.id || e.target.localName)), true); }");
        var root = await Inventory();
        var elements = root["elements"]!.AsArray();
        var log = new List<string>();
        await ElementInventory.CaptureInteractiveStates(_page, elements, 12, log.Add);

        var els = elements.Select(e => e!.AsObject()).ToList();
        var go = els.Single(e => e["name"]!.GetValue<string>() == "Search");
        var input = els.Single(e => e["role"]!.GetValue<string>() == "textbox");
        Assert.That(go["states"]!["hover.background-color"]!.GetValue<string>(), Is.EqualTo("rgb(0, 0, 200)"));
        Assert.That(input["states"]!["focus.outline"]!.GetValue<string>(), Is.EqualTo("3px solid rgb(200, 0, 0)"));
        Assert.That(log, Is.Empty, string.Join("\n", log));
        Assert.That(await _page.EvaluateAsync<string[]>("() => window.__ev"), Is.Empty, "no mouse/focus/blur events dispatched");
        Assert.That(await _page.EvaluateAsync<string>("() => document.activeElement.localName"), Is.EqualTo("body"));
        // Forced states are cleared afterwards.
        Assert.That(await _page.EvalOnSelectorAsync<string>("#go", "e => getComputedStyle(e).backgroundColor"), Is.Not.EqualTo("rgb(0, 0, 200)"));
        Assert.That(await _page.EvaluateAsync<bool>("() => !document.getElementById('__mt_notrans')"), Is.True);
    }

    [Test]
    public async Task Inventory_Overflow_OnlyRealContent_NotClippedDecoration()
    {
        await _page!.SetContentAsync(@"<!doctype html><html><head><style>
  button { font: 14px sans-serif; white-space: nowrap; overflow: hidden; position: relative; }
  #ripple::after { content: ''; position: absolute; left: 0; top: 0; width: 100%; height: 100%; transform: scale(10); }
</style></head><body>
  <button id='ripple'>Search</button>
  <button id='cut' style='width: 40px'>A much longer caption</button>
</body></html>");
        var els = (await Inventory())["elements"]!.AsArray().Select(e => e!.AsObject()).ToList();
        bool Overflow(string name) => els.Single(e => e["name"]!.GetValue<string>() == name)["overflowX"]!.GetValue<bool>();
        Assert.That(Overflow("Search"), Is.False, "a clipped, transformed pseudo-element is not content overflow");
        Assert.That(Overflow("A much longer caption"), Is.True, "text wider than the button is cut off");
    }

    [TestCase("input", "textbox", "text", false)]
    [TestCase("input", "searchbox", "search", false)]
    [TestCase("input", "textbox", "date", false)]
    [TestCase("select", "combobox", "", false)]
    [TestCase("textarea", "textbox", "", false)]
    [TestCase("div", "combobox", "", false)]
    [TestCase("button", "button", "", true)]
    [TestCase("a", "link", "", true)]
    [TestCase("input", "checkbox", "checkbox", true)]
    [TestCase("input", "button", "submit", true)]
    public void FocusSafeWithoutCdp_SkipsTextEntryAndListControls(string tag, string role, string type, bool safe) =>
        Assert.That(ElementInventory.FocusSafeWithoutCdp(new JsonObject { ["tag"] = tag, ["role"] = role, ["inputType"] = type }), Is.EqualTo(safe));

    [Test]
    public async Task Inventory_ParsesIntoModel_AndRenderedFontHelper()
    {
        var json = await _page!.EvaluateAsync<string>(ElementInventory.InventoryScript, new Dictionary<string, object>
        {
            ["maxRows"] = 3, ["maxElements"] = 300, ["health"] = false, ["brokenImages"] = true, ["emptySections"] = true, ["a11y"] = true
        });
        var inv = System.Text.Json.JsonSerializer.Deserialize<InventoryFile>(json, ReportJson.Options)!;
        Assert.That(inv.Elements.Count, Is.GreaterThan(10));
        Assert.That(inv.Viewport.Width, Is.EqualTo(1366));
        Assert.That(inv.Elements.All(e => e.Box.W > 0 && e.DomPath.StartsWith("html > ")));

        var font = await _page.EvaluateAsync<string>("() => { " + CssCompareHelper.JsHelpers + " return __renderedFont('\"No Such Font 123\", sans-serif'); }");
        Assert.That(font, Is.EqualTo("sans-serif"), "an unavailable family is skipped");
    }
}
