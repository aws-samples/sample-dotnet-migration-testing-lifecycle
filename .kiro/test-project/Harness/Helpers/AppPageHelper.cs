using Microsoft.Playwright;

namespace MigrationTests.Playwright.Helpers;

/// <summary>
/// App-specific page interaction helper.
/// 
/// This file contains interaction patterns specific to YOUR application's
/// UI framework and components. Override/extend these methods as your app's
/// patterns emerge during migration and testing.
/// 
/// The default implementations handle common web app patterns.
/// Customize selectors and behavior to match your app.
/// </summary>
public static class AppPageHelper
{
    /// <summary>
    /// Dismisses notification/toast/snackbar messages that may block interactions.
    /// 
    /// CUSTOMIZE: Change the selector to match your app's notification component.
    /// Common patterns:
    ///   - MudBlazor: ".mud-snackbar"
    ///   - Bootstrap: ".toast.show"
    ///   - Custom: ".notification", ".alert"
    /// </summary>
    public static async Task DismissNotifications(IPage page)
    {
        var notificationSelector = ".notification, .toast, .snackbar, .alert";
        for (int i = 0; i < 5; i++)
        {
            var notification = page.Locator(notificationSelector).First;
            if (!await notification.IsVisibleAsync())
                break;
            var closeBtn = notification.Locator("button, .close").First;
            if (await closeBtn.IsVisibleAsync())
            {
                try { await closeBtn.ClickAsync(); await page.WaitForTimeoutAsync(300); }
                catch { break; }
            }
            else break;
        }
    }

    /// <summary>
    /// Dismisses an open dialog/modal by clicking OK or Close.
    /// 
    /// CUSTOMIZE: Change selectors to match your app's dialog component.
    /// </summary>
    public static async Task DismissDialog(IPage page)
    {
        var dialogSelector = "[role='dialog'], .modal.show, .dialog";
        var dialog = page.Locator(dialogSelector).First;
        if (!await dialog.IsVisibleAsync())
            return;

        // Try OK button first, then close button
        var okBtn = dialog.Locator("button:has-text('OK'), button:has-text('Close')").First;
        if (await okBtn.IsVisibleAsync())
            await okBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>
    /// Clicks a sortable column header to trigger sort.
    /// 
    /// CUSTOMIZE: Change selector to match your app's grid header pattern.
    /// Common patterns:
    ///   - MudBlazor: "span.sortable-column-header:has-text('{title}')"
    ///   - jqGrid: "th:has-text('{title}')"
    ///   - HTML table: "th[data-sortable]:has-text('{title}')"
    /// </summary>
    public static async Task ClickSortColumn(IPage page, string columnTitle, int waitMs = 3000)
    {
        var header = page.Locator($"th:has-text('{columnTitle}')").First;
        if (await header.IsVisibleAsync())
        {
            await header.ClickAsync();
            await page.WaitForTimeoutAsync(waitMs);
        }
    }

    /// <summary>
    /// Gets pagination info text (e.g., "View 1-10 of 200", "Showing 1 to 10 of 200").
    /// 
    /// CUSTOMIZE: Change selector to match your app's pager component.
    /// </summary>
    public static async Task<string> GetPagerInfo(IPage page)
    {
        var pagerSelector = ".pager-info, .pagination-info, [class*='pager']";
        var pager = page.Locator(pagerSelector).First;
        if (await pager.IsVisibleAsync())
            return (await pager.TextContentAsync())?.Trim() ?? "";
        return "";
    }

    /// <summary>
    /// Clicks the Next Page button in pagination.
    /// 
    /// CUSTOMIZE: Change selector for your pager's next button.
    /// </summary>
    public static async Task ClickNextPage(IPage page, int waitMs = 3000)
    {
        var nextSelector = "[title='Next Page'], .page-next, [aria-label='Next']";
        var next = page.Locator(nextSelector).First;
        if (await next.IsVisibleAsync())
        {
            await next.ClickAsync();
            await page.WaitForTimeoutAsync(waitMs);
        }
    }

    /// <summary>
    /// Navigates to a page in the target system.
    /// 
    /// CUSTOMIZE: Add your app's navigation flow:
    ///   - SPA routing may need hydration waits
    ///   - Menu-based navigation may need multiple clicks
    ///   - Auth redirects may need handling
    /// </summary>
    public static async Task NavigateToPage(IPage page, string url, int waitMs = 5000)
    {
        await page.GotoAsync(url);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(waitMs);
    }
}
