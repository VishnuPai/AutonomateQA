using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Text;
using System.Threading;
using UiTestRunner.Configuration;
using UiTestRunner.Constants;
using UiTestRunner.Data;
using UiTestRunner.Models;
using UiTestRunner.AiProviders;

namespace UiTestRunner.Services
{
    public class UiTestService : IUiTestService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UiTestService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly PlaywrightSettings _playwrightSettings;

        private readonly IAiModelProvider _aiProvider;
        private readonly ITestDataManager _testDataManager;
        private readonly IPlaywrightVisionService _visionService;
        private readonly ITestRunTokenTracker _tokenTracker;

        private string? _lastSnapshot; // Store the last snapshot
        private readonly StringBuilder _reasoningBuffer = new(); // Buffer for reasoning log entries

        public UiTestService(IServiceScopeFactory scopeFactory, ILogger<UiTestService> logger, IWebHostEnvironment env, IAiModelProvider aiProvider, ITestDataManager testDataManager, IPlaywrightVisionService visionService, Microsoft.Extensions.Options.IOptions<PlaywrightSettings> playwrightSettings, ITestRunTokenTracker tokenTracker)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _env = env;
            _aiProvider = aiProvider;
            _testDataManager = testDataManager;
            _visionService = visionService;
            _playwrightSettings = playwrightSettings.Value;
            _tokenTracker = tokenTracker;
        }

        public async Task<string> GetPageSnapshotAsync(IPage page, CancellationToken cancellationToken = default)
        {
            // Capture the accessibility tree
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            cancellationToken.ThrowIfCancellationRequested();
            
            // Mask private data
            var sanitized = MaskPrivateData(snapshot);

            // Truncate snapshot to limit prompt tokens (also applied in PlaywrightVisionService for test execution)
            if (_playwrightSettings.MaxAriaSnapshotLength > 0 && sanitized.Length > _playwrightSettings.MaxAriaSnapshotLength)
            {
                sanitized = sanitized.Substring(0, _playwrightSettings.MaxAriaSnapshotLength)
                    + "\n\n... (snapshot truncated to reduce token usage)";
            }

            // Store locally
            _lastSnapshot = sanitized;
            
            return _lastSnapshot;
        }

        private string MaskPrivateData(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            // Mask internal emails (e.g. user@company.local)
            // Simple regex for basic email patterns
            var emailRegex = new System.Text.RegularExpressions.Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
            var result = emailRegex.Replace(input, "***@***.***");

            return result;
        }

        public async Task<TestResult?> RunTestAsync(int testResultId, string url, bool headed = false, string? gherkinScript = null, string? testDataCsvPath = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(testDataCsvPath))
                _testDataManager.LoadCsvForCurrentRun(testDataCsvPath);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var testResult = await db.TestResults.FindAsync(testResultId);

            if (testResult == null)
            {
                _logger.LogError($"TestResult with ID {testResultId} not found.");
                return null;
            }

            testResult.Status = TestStatus.Running;
            testResult.RunTime = DateTime.Now;
            _tokenTracker.Reset();
            await db.SaveChangesAsync();

            var startTime = DateTime.Now;
            var currentStep = "Initializing";

            Microsoft.Playwright.IPage? page = null;
            Microsoft.Playwright.IBrowserContext? context = null;
            Microsoft.Playwright.IBrowser? browser = null;
            Microsoft.Playwright.IPlaywright? playwright = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Initialize browser and navigate to URL
                (playwright, browser, context, page) = await InitializeBrowserAsync(headed, cancellationToken);
                await NavigateToUrlAsync(page, url, cancellationToken);

                // Execute Gherkin script if provided
                if (!string.IsNullOrWhiteSpace(gherkinScript))
                {
                    currentStep = await ExecuteGherkinStepsAsync(page, gherkinScript, cancellationToken);
                }

                // Capture final screenshot and mark as passed
                await CaptureFinalScreenshotAsync(page, testResult, cancellationToken);
                testResult.Status = TestStatus.Passed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test failed at step: {Step}", currentStep);
                testResult.Status = TestStatus.Failed;
                testResult.ErrorMessage = $"Failed at step '{currentStep}': {ex.Message}";
                AppendReasoningLog($"[Error] {ex.Message}");
            }
            finally
            {
                // Flush reasoning log buffer to database
                await FlushReasoningLogAsync(testResult, db);

                var (promptTokens, completionTokens, totalTokens) = _tokenTracker.GetTotal();
                testResult.PromptTokens = promptTokens > 0 ? promptTokens : null;
                testResult.CompletionTokens = completionTokens > 0 ? completionTokens : null;
                testResult.TotalTokens = totalTokens > 0 ? totalTokens : null;
                
                // Cleanup browser resources and save video
                var videoPath = await GetVideoPathAsync(page);
                await CleanupBrowserResourcesAsync(page, context, browser, playwright);
                await SaveVideoFileAsync(videoPath, testResult, cancellationToken);

                testResult.Duration = DateTime.Now - startTime;
                await db.SaveChangesAsync();
            }

            return testResult;
        }

        /// <summary>
        /// Initializes Playwright browser, context, and page.
        /// </summary>
        private async Task<(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)> InitializeBrowserAsync(bool headed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var playwright = await Playwright.CreateAsync();
            cancellationToken.ThrowIfCancellationRequested();
            
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = !headed,
                Channel = "chrome",
                SlowMo = headed ? _playwrightSettings.SlowMoMs : 0,
                Args = new[] { "--ignore-certificate-errors", "--no-sandbox" }
            });
            cancellationToken.ThrowIfCancellationRequested();

            var context = await browser.NewContextAsync(new BrowserNewContextOptions 
            { 
                IgnoreHTTPSErrors = true,
                RecordVideoDir = Path.Combine(_env.WebRootPath, "videos"),
                RecordVideoSize = new RecordVideoSize { Width = _playwrightSettings.VideoWidth, Height = _playwrightSettings.VideoHeight }
            });
            cancellationToken.ThrowIfCancellationRequested();
            
            var page = await context.NewPageAsync();
            
            return (playwright, browser, context, page);
        }

        /// <summary>
        /// Navigates the page to the specified URL.
        /// </summary>
        private async Task NavigateToUrlAsync(IPage page, string url, CancellationToken cancellationToken)
        {
            // Log scheme + host only to avoid leaking query params or tokens
            var safeForLog = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Host}" : "(invalid url)";
            _logger.LogInformation("Navigating to {Url}...", safeForLog);
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = _playwrightSettings.NavigationTimeoutMs });
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Returns true if the line is a Gherkin structure keyword (Feature, Scenario, etc.),
        /// not an executable step. Such lines are skipped when pasting a full .feature file.
        /// </summary>
        private static bool IsGherkinStructureLine(string trimmedLine)
        {
            if (string.IsNullOrWhiteSpace(trimmedLine)) return true;
            var lower = trimmedLine.ToLowerInvariant();
            return lower.StartsWith("feature:") || lower.StartsWith("scenario:") || lower.StartsWith("scenario outline:")
                || lower.StartsWith("background:") || lower.StartsWith("examples:") || lower.StartsWith("rule:");
        }

        /// <summary>
        /// Executes Gherkin script steps and returns the last executed step name.
        /// </summary>
        private async Task<string> ExecuteGherkinStepsAsync(IPage page, string gherkinScript, CancellationToken cancellationToken)
        {
                var lines = gherkinScript.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string currentStep = "Initializing";

            foreach (var line in lines)
            {
                var step = line.Trim();
                if (string.IsNullOrWhiteSpace(step) || step.StartsWith(GherkinKeywords.Comment)) continue;

                // Skip Gherkin structure lines (Feature, Scenario, etc.) — only execute actual steps (When/And/Then/Given)
                if (IsGherkinStructureLine(step)) continue;

                // Active DLP Masking: Replace literal values with placeholders before sending to AI
                step = _testDataManager.MaskLiterals(step);

                cancellationToken.ThrowIfCancellationRequested();
                
                currentStep = $"Executing Step: {step}";
                _logger.LogInformation(currentStep);

                // Verification: Then steps, or any step that asserts visibility (e.g. "And Version is displayed")
                bool isVerification = step.StartsWith(GherkinKeywords.Then, StringComparison.OrdinalIgnoreCase)
                    || step.Contains(" is displayed", StringComparison.OrdinalIgnoreCase)
                    || step.Contains(" is visible", StringComparison.OrdinalIgnoreCase);

                if (isVerification)
                {
                    bool passed = false;
                    string lastReasoning = string.Empty;
                    const int maxAttempts = 2; // 1 initial + 1 retry (fewer attempts = fewer tokens when step fails)

                    for (int i = 0; i < maxAttempts; i++)
                    {
                        if (i > 0)
                        {
                            _logger.LogInformation("Verification failed. Retrying in 2 seconds ({Attempt}/{Max})...", i, maxAttempts);
                            await Task.Delay(2000, cancellationToken);
                        }

                        var snapshot = await _visionService.GetCleanSnapshotAsync(page, forVerification: true, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();

                        var verifyResult = await _aiProvider.VerifyAsync(step, snapshot, cancellationToken);
                        passed = verifyResult.Passed;
                        lastReasoning = verifyResult.Reasoning;

                        if (passed)
                        {
                            AppendReasoningLog($"[Verify] Step: '{step}' - Result: True (Attempt {i+1}) - AI Reasoning: {lastReasoning}");
                            break;
                        }
                    }

                    if (!passed)
                    {
                        AppendReasoningLog($"[Verify] Step: '{step}' - Result: False (Failed after {maxAttempts} attempts) - AI Reasoning: {lastReasoning}");
                        throw new Exception($"AI Verification Failed for step: {step}. Reason: {lastReasoning}");
                    }
                }
                else
                {
                    // For actions, snapshot once (full limit for action steps)
                    var snapshot = await _visionService.GetCleanSnapshotAsync(page, forVerification: false, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    var action = await _aiProvider.GetActionAsync(step, snapshot, cancellationToken);
                    AppendReasoningLog($"[Action] Step: '{step}' - AI Reasoning: {action.Reasoning} - Executing: {action.ActionType} on {action.SelectorValue}");
                    await ExecutePlaywrightAction(page, action, cancellationToken);
                }
            }

            return currentStep;
        }

        /// <summary>
        /// Captures a final screenshot and updates the test result.
        /// </summary>
        private async Task CaptureFinalScreenshotAsync(IPage page, TestResult testResult, CancellationToken cancellationToken)
        {
            var title = await page.TitleAsync();
            var screenshotFileName = $"screenshot_{testResult.Id}_{DateTime.Now.Ticks}.png";
            var screenshotPath = Path.Combine(_env.WebRootPath, "screenshots", screenshotFileName);
            var directory = Path.GetDirectoryName(screenshotPath);
            if (directory != null) Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
            
            testResult.ScreenshotPath = $"/screenshots/{screenshotFileName}";
            testResult.ErrorMessage = $"Completed. Page Title: {title}";
        }

        /// <summary>
        /// Gets the video file path from the page if available.
        /// </summary>
        private async Task<string?> GetVideoPathAsync(IPage? page)
        {
            if (page?.Video == null) return null;
            
            try 
            { 
                return await page.Video.PathAsync(); 
            } 
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get video path.");
                return null;
            }
        }

        /// <summary>
        /// Cleans up browser resources in the correct order.
        /// </summary>
        private async Task CleanupBrowserResourcesAsync(IPage? page, IBrowserContext? context, IBrowser? browser, IPlaywright? playwright)
        {
            // Dispose resources in reverse order: page -> context -> browser -> playwright
            if (page != null)
            {
                try 
                { 
                    await page.CloseAsync(); 
                } 
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to close page.");
                }
            }

            if (context != null)
            {
                try 
                { 
                    await context.CloseAsync(); 
                } 
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to close browser context.");
                }
            }

            if (browser != null)
            {
                try 
                { 
                    await browser.DisposeAsync(); 
                } 
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose browser.");
                }
            }

            if (playwright != null)
            {
                try 
                { 
                    playwright.Dispose(); 
                } 
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose playwright.");
                }
            }
        }

        /// <summary>
        /// Saves the video file to the web root videos directory.
        /// </summary>
        private async Task SaveVideoFileAsync(string? videoPath, TestResult testResult, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                return;

            try
            {
                var destFileName = $"video_{testResult.Id}_{DateTime.Now.Ticks}.webm";
                var destPath = Path.Combine(_env.WebRootPath, "videos", destFileName);
                var destDirectory = Path.GetDirectoryName(destPath);
                if (destDirectory != null && !Directory.Exists(destDirectory))
                {
                    Directory.CreateDirectory(destDirectory);
                }
                File.Move(videoPath, destPath, true);
                testResult.VideoPath = $"/videos/{destFileName}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move video file.");
            }
        }

        /// <summary>
        /// Appends a message to the in-memory reasoning log buffer.
        /// The log will be flushed to the database at the end of the test execution.
        /// </summary>
        private void AppendReasoningLog(string message)
        {
            _reasoningBuffer.AppendLine($"{DateTime.Now:HH:mm:ss}: {message}");
        }

        /// <summary>
        /// Flushes the reasoning log buffer to the database.
        /// This is called once at the end of test execution to optimize database writes.
        /// </summary>
        private async Task FlushReasoningLogAsync(TestResult result, ApplicationDbContext db)
        {
            if (_reasoningBuffer.Length > 0)
            {
                result.ReasoningLog = (_reasoningBuffer.ToString());
                _reasoningBuffer.Clear();
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// After a click or navigation, waits for the page to settle (load state and/or delay)
        /// so the next snapshot captures updated content (menus, SPAs, new page).
        /// </summary>
        private async Task WaitForPageToSettleAfterClickAsync(IPage page, CancellationToken cancellationToken)
        {
            if (_playwrightSettings.WaitForLoadStateAfterClickMs <= 0) return;

            try
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = _playwrightSettings.WaitForLoadStateAfterClickMs });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WaitForLoadState after click timed out or failed (continuing).");
            }

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = Math.Min(3000, _playwrightSettings.WaitForLoadStateAfterClickMs) });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WaitForLoadState NetworkIdle after click timed out (continuing).");
            }
        }

        private async Task ExecutePlaywrightAction(IPage page, UiActionResponse action, CancellationToken cancellationToken = default)
        {
            ILocator locator;
            
            // Try Role first. For Link/Menuitem use non-exact name match so submenu items and labels with extra text (e.g. "Suppliers (5)") still match.
            if (Enum.TryParse<AriaRole>(action.SelectorType, true, out var role))
            {
                var useExact = role != AriaRole.Link && role != AriaRole.Menuitem;
                locator = page.GetByRole(role, new PageGetByRoleOptions { Name = action.SelectorValue, Exact = useExact });
            }
            else if (string.Equals(action.SelectorType, SelectorTypes.Text, StringComparison.OrdinalIgnoreCase))
            {
                locator = page.GetByText(action.SelectorValue);
            }
            else if (string.Equals(action.SelectorType, SelectorTypes.Label, StringComparison.OrdinalIgnoreCase))
            {
                locator = page.GetByLabel(action.SelectorValue);
            }
            else if (string.Equals(action.SelectorType, SelectorTypes.Placeholder, StringComparison.OrdinalIgnoreCase))
            {
                locator = page.GetByPlaceholder(action.SelectorValue, new PageGetByPlaceholderOptions { Exact = true });
            }
            else 
            {
                // Fallback / CSS
                locator = page.Locator(action.SelectorValue);
            }
            
            cancellationToken.ThrowIfCancellationRequested();
            
            try 
            {
                // Attempt to scroll to element
                await locator.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = _playwrightSettings.ScrollTimeoutMs }).ConfigureAwait(false);
            }
            catch { /* Ignore scroll error */ }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Highlight element for debugging (visual only)
                try { await locator.HighlightAsync(); } catch {}

                switch (action.ActionType.ToLowerInvariant())
                {
                    case ActionTypes.Click:
                        await locator.ClickAsync(new LocatorClickOptions { Timeout = _playwrightSettings.InteractionTimeoutMs, Force = true });
                        await WaitForPageToSettleAfterClickAsync(page, cancellationToken);
                        break;
                    case ActionTypes.Fill:
                    case ActionTypes.Type:
                    case ActionTypes.Input:
                        var inputData = _testDataManager.ReplacePlaceholders(action.InputData ?? "");
                        await locator.FillAsync(inputData, new LocatorFillOptions { Timeout = _playwrightSettings.InteractionTimeoutMs });
                        break;
                    case ActionTypes.Check:
                        await locator.CheckAsync();
                        break;
                    case ActionTypes.Uncheck:
                        await locator.UncheckAsync();
                        break;
                    case ActionTypes.Navigate:
                    case ActionTypes.Goto:
                        await page.GotoAsync(action.SelectorValue);
                        await WaitForPageToSettleAfterClickAsync(page, cancellationToken);
                        break;
                    case ActionTypes.Hover:
                        await locator.HoverAsync();
                        break;
                    default:
                         await locator.ClickAsync(new LocatorClickOptions { Timeout = _playwrightSettings.InteractionTimeoutMs, Force = true });
                         await WaitForPageToSettleAfterClickAsync(page, cancellationToken);
                         break;
                }

                // Always wait after any action so DOM/SPA has time to update before next snapshot
                if (_playwrightSettings.PostActionDelayMs > 0)
                {
                    await Task.Delay(_playwrightSettings.PostActionDelayMs, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Capture failure screenshot
                var screenshotPath = Path.Combine(_env.WebRootPath, "screenshots", $"error_action_{DateTime.Now.Ticks}.png");
                var directory = Path.GetDirectoryName(screenshotPath);
                if (directory != null) Directory.CreateDirectory(directory);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
                _logger.LogError(ex, "Action execution failed. Screenshot saved to: {ScreenshotPath}", screenshotPath);
                throw;
            }
        }
    }
}
