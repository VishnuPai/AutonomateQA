using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Text;
using System.Threading;
using UiTestRunner.AiProviders;
using UiTestRunner.Configuration;
using UiTestRunner.Constants;

namespace UiTestRunner.Services
{
    public class TestRecorderService : ITestRecorderService
    {
        private readonly IAiModelProvider _aiProvider;
        private readonly IPlaywrightVisionService _visionService;
        private readonly ILogger<TestRecorderService> _logger;
        private readonly PlaywrightSettings _playwrightSettings;
        private readonly RunnerSettings _runnerSettings;
        private readonly IWebHostEnvironment _env;
        private readonly List<string> _recordedSteps = new();
        private IPage? _page;

        public TestRecorderService(IAiModelProvider aiProvider, IPlaywrightVisionService visionService, ILogger<TestRecorderService> logger, IOptions<PlaywrightSettings> playwrightSettings, IOptions<RunnerSettings> runnerSettings, IWebHostEnvironment env)
        {
            _aiProvider = aiProvider;
            _visionService = visionService;
            _logger = logger;
            _playwrightSettings = playwrightSettings.Value;
            _runnerSettings = runnerSettings?.Value ?? new RunnerSettings();
            _env = env;
        }

        public async Task StartRecordingAsync(string url, string outputFilename, CancellationToken cancellationToken = default)
        {
            // Validate filename to prevent path traversal attacks
            if (string.IsNullOrWhiteSpace(outputFilename))
            {
                throw new ArgumentException("Filename cannot be null or empty.", nameof(outputFilename));
            }

            // Sanitize filename - only allow filename, not full paths
            var sanitizedFilename = Path.GetFileName(outputFilename);
            if (string.IsNullOrWhiteSpace(sanitizedFilename) || sanitizedFilename != outputFilename)
            {
                throw new ArgumentException($"Invalid filename '{outputFilename}'. Filename cannot contain path separators.", nameof(outputFilename));
            }

            // Ensure filename has .feature extension
            if (!sanitizedFilename.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedFilename += ".feature";
            }

            // Additional validation: check for invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (sanitizedFilename.IndexOfAny(invalidChars) >= 0)
            {
                throw new ArgumentException($"Filename contains invalid characters: {outputFilename}", nameof(outputFilename));
            }

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false, // Must be headed for user interaction
                Channel = "chrome", // Use system installed Chrome
                Args = new[] { "--ignore-certificate-errors", "--no-sandbox" }
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = _playwrightSettings.ViewportWidth, Height = _playwrightSettings.ViewportHeight }
            });

            _page = await context.NewPageAsync();

            // Expose a function to be called from the browser
            await _page.ExposeFunctionAsync("recordEvent", async (string action, string selector, string? value) =>
            {
                try
                {
                    // Do not log Value for input/fill to avoid recording secrets in logs
                    var valueForLog = (string.Equals(action, "input", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "fill", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(value) ? "[REDACTED]" : value;
                    _logger.LogInformation("[RAW JS EVENT] Action: {Action}, Selector: {Selector}, Value: {Value}", action, selector, valueForLog ?? "");

                    if (_page == null)
                    {
                        _logger.LogWarning("Page is null, cannot generate Gherkin step.");
                        return;
                    }

                    // Get current page snapshot for AI context
                    var snapshot = await _visionService.GetCleanSnapshotAsync(_page);

                    // Use AI to generate semantic Gherkin step
                    string step;
                    try
                    {
                        step = await _aiProvider.GenerateGherkinStepAsync(action, selector, value, snapshot);
                        
                        if (string.IsNullOrWhiteSpace(step))
                        {
                            _logger.LogWarning("AI returned empty Gherkin step, using fallback.");
                        // Fallback to simple format if AI fails
                        if (action.Equals(ActionTypes.Input, StringComparison.OrdinalIgnoreCase))
                        {
                            step = $"{GherkinKeywords.When} I type '{value}' into the {selector}";
                        }
                        else if (action.Equals(ActionTypes.Click, StringComparison.OrdinalIgnoreCase))
                        {
                            step = $"{GherkinKeywords.When} I click the {selector}";
                        }
                        else
                        {
                            return; // Unknown action type
                        }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception aiEx)
                    {
                        _logger.LogError(aiEx, "AI failed to generate Gherkin step, using fallback.");
                        // Fallback to simple format if AI fails
                        if (action.Equals(ActionTypes.Input, StringComparison.OrdinalIgnoreCase))
                        {
                            step = $"{GherkinKeywords.When} I type '{value}' into the {selector}";
                        }
                        else if (action.Equals(ActionTypes.Click, StringComparison.OrdinalIgnoreCase))
                        {
                            step = $"{GherkinKeywords.When} I click the {selector}";
                        }
                        else
                        {
                            return; // Unknown action type
                        }
                    }
                    
                    if (string.IsNullOrWhiteSpace(step)) return;

                    // Simple duplicate filtering
                    if (_recordedSteps.Count > 0 && _recordedSteps.Last().Equals(step, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _logger.LogInformation($"Recorded AI-generated step: {step}");
                    _recordedSteps.Add(step);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record event");
                }
            });

            // Navigate
            await _page.GotoAsync(url);

            // Inject Event Listeners
            await _page.AddInitScriptAsync(@"
                function getSelector(el) {
                    let name = el.getAttribute('aria-label') || el.getAttribute('placeholder') || el.name;
                    if (!name && el.id) {
                        const label = document.querySelector(`label[for=""${el.id}""]`);
                        if (label) name = label.innerText.trim();
                    }
                    if (!name && (el.tagName === 'BUTTON' || el.tagName === 'A')) {
                        name = el.innerText ? el.innerText.trim().substring(0, 30) : '';
                    }
                    if (!name && el.getAttribute('value') && (el.tagName === 'INPUT' && (el.type === 'submit' || el.type === 'button'))) {
                        name = el.getAttribute('value');
                    }
                    
                    if (name) {
                        return `'${name}' ${el.tagName.toLowerCase()}`;
                    }

                    if (el.id) return '#' + el.id;

                    // Fallback to simple path
                    let path = [];
                    while (el.nodeType === Node.ELEMENT_NODE) {
                        let selector = el.nodeName.toLowerCase();
                        if (el.id) {
                            selector += '#' + el.id;
                            path.unshift(selector);
                            break;
                        }
                        let sib = el, nth = 1;
                        while (sib = sib.previousElementSibling) {
                            if (sib.nodeName.toLowerCase() == selector) nth++;
                        }
                        if (nth != 1) selector += "":nth-of-type(""+nth+"")"";
                        path.unshift(selector);
                        if (path.length > 2) break; // Don't make it too long
                        el = el.parentNode;
                    }
                    return path.join("" > "");
                }

                let lastInputTarget = null;
                let lastInputValue = '';

                function flushInput() {
                    if (lastInputTarget) {
                        const selector = getSelector(lastInputTarget);
                        window.recordEvent('input', selector, lastInputValue);
                        lastInputTarget = null;
                        lastInputValue = '';
                    }
                }

                document.addEventListener('input', (e) => {
                    if (!e.isTrusted) return; // Ignore programmatic events
                    const tag = e.target.tagName;
                    if (e.target.type === 'hidden' || e.target.style.display === 'none') return;

                    if (tag === 'INPUT' || tag === 'TEXTAREA') {
                        if (lastInputTarget && lastInputTarget !== e.target) {
                            flushInput();
                        }
                        lastInputTarget = e.target;
                        lastInputValue = e.target.value;
                    }
                });

                document.addEventListener('focusout', (e) => {
                    if (!e.isTrusted) return;
                    if (e.target === lastInputTarget) {
                        flushInput();
                    }
                });

                document.addEventListener('keydown', (e) => {
                    if (!e.isTrusted) return;
                    if (e.key === 'Enter') {
                        if (lastInputTarget === e.target) {
                            flushInput();
                        }
                    }
                });

                document.addEventListener('click', (e) => {
                    if (!e.isTrusted) return; // Prevent clicking by background scripts
                    if (lastInputTarget && lastInputTarget !== e.target) {
                        flushInput();
                    }
                    const target = e.target;
                    const selector = getSelector(target);
                    if (selector === 'body' || selector === 'html') return;
                    window.recordEvent('click', selector, null);
                });
            ");

            _logger.LogInformation("Recording started. Close the browser to save.");

            // Wait for the browser to be closed by the user
            // We monitor the context close event or loop until closed
            var tcs = new TaskCompletionSource();
            context.Close += (_, _) => tcs.TrySetResult();
            
            // Also handle page close
            _page.Close += (_, _) => tcs.TrySetResult();

            await tcs.Task;

            // Save to file using sanitized filename
            await SaveFeatureFileAsync(sanitizedFilename);
        }

        private async Task SaveFeatureFileAsync(string filename)
        {
            // Additional safety check - ensure filename is still safe
            var safeFilename = Path.GetFileName(filename);
            if (string.IsNullOrWhiteSpace(safeFilename) || safeFilename != filename)
            {
                throw new InvalidOperationException($"Invalid filename detected: {filename}");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Feature: Recorded Test {DateTime.Now:yyyy-MM-dd HH-mm}");
            sb.AppendLine();
            sb.AppendLine("  Scenario: User Journey");
            
            foreach (var step in _recordedSteps)
            {
                sb.AppendLine($"    {step}");
            }

            // Use Runner:ScenariosPath under content root if set; otherwise current directory
            var baseDir = !string.IsNullOrWhiteSpace(_runnerSettings.ScenariosPath)
                ? Path.Combine(_env.ContentRootPath, _runnerSettings.ScenariosPath.Trim().Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Directory.GetCurrentDirectory();
            try
            {
                if (!Directory.Exists(baseDir))
                    Directory.CreateDirectory(baseDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create Scenarios path {Path}, using current directory.", baseDir);
                baseDir = Directory.GetCurrentDirectory();
            }
            var path = Path.Combine(baseDir, safeFilename);
            var fullPath = Path.GetFullPath(path);
            var fullBaseDir = Path.GetFullPath(baseDir);
            if (!fullPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Path traversal detected. Attempted path: {fullPath}");
            }

            await File.WriteAllTextAsync(fullPath, sb.ToString());
            _logger.LogInformation($"Saved feature file to {fullPath}");
        }
    }
}
