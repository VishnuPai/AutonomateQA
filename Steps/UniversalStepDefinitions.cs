using System;
using System.Threading.Tasks;
using TechTalk.SpecFlow;
using Microsoft.Playwright;
using UiTestRunner.Services;
using UiTestRunner.AiProviders;
using UiTestRunner.Data;
using Microsoft.Extensions.DependencyInjection;
using TechTalk.SpecFlow.Bindings;

namespace UiTestRunner.Steps
{
    [Binding]
    public class UniversalStepDefinitions
    {
        private readonly IPage _page;
        private readonly IPlaywrightVisionService _visionService;
        private readonly IAiModelProvider _aiProvider;
        private readonly ScenarioContext _scenarioContext;
        private readonly ITestDataManager _testDataManager;
        private readonly ApplicationDbContext _dbContext;

        public UniversalStepDefinitions(IPage page, IPlaywrightVisionService visionService, IAiModelProvider aiProvider, ScenarioContext scenarioContext, ITestDataManager testDataManager, ApplicationDbContext dbContext)
        {
            _page = page;
            _visionService = visionService;
            _aiProvider = aiProvider;
            _scenarioContext = scenarioContext;
            _testDataManager = testDataManager;
            _dbContext = dbContext;
        }

        [When(@"(.*)")]
        [Then(@"(.*)")]
        public async Task ExecuteUniversalStep(string stepText)
        {
            var stepType = _scenarioContext.StepContext.StepInfo.StepDefinitionType;
            Console.WriteLine($"[UniversalStep] {stepType}: {stepText}");

            // 1. Get Visual State
            var snapshot = await _visionService.GetCleanSnapshotAsync(_page);

            if (stepType == StepDefinitionType.Then)
            {
                // Verification Mode
                Console.WriteLine("Verifying state with AI...");
                var result = await _aiProvider.VerifyAsync(stepText, snapshot);
                
                await LogReasoningAsync($"[Verify] Step: '{stepText}' - Result: {result.Passed}");

                if (!result.Passed)
                {
                    throw new Exception($"AI Verification Failed for step: {stepText}");
                }
            }
            else
            {
                // Action Mode
                Console.WriteLine("Asking AI for action...");
                var action = await _aiProvider.GetActionAsync(stepText, snapshot);
                
                await LogReasoningAsync($"[Action] Step: '{stepText}' - AI Reasoning: {action.Reasoning} - Executing: {action.ActionType} on {action.SelectorValue}");

                await ExecutePlaywrightAction(action);
            }
        }

        private async Task LogReasoningAsync(string message)
        {
            Console.WriteLine(message);
            
            // Try to get TestResultId from ScenarioContext if it exists (assuming it was set by a Hook)
            if (_scenarioContext.TryGetValue("TestResultId", out int testResultId))
            {
                var result = await _dbContext.TestResults.FindAsync(testResultId);
                if (result != null)
                {
                    result.ReasoningLog += $"{DateTime.Now}: {message}\n";
                    await _dbContext.SaveChangesAsync();
                }
            }
        }

        private async Task ExecutePlaywrightAction(UiActionResponse action)
        {
            // Map String to AriaRole enum roughly
            AriaRole? role = null;
            if (Enum.TryParse<AriaRole>(action.SelectorType, true, out var parsedRole))
            {
                role = parsedRole;
            }

            ILocator locator;
            if (role.HasValue)
            {
                locator = _page.GetByRole(role.Value, new PageGetByRoleOptions { Name = action.SelectorValue });
            }
            else
            {
                // Fallback if AI didn't return a valid Role string, treat SelectorType as generic or just use SelectorValue
                // If SelectorType is "Text", use GetByText
                if (string.Equals(action.SelectorType, "Text", StringComparison.OrdinalIgnoreCase))
                {
                    locator = _page.GetByText(action.SelectorValue);
                }
                else if (string.Equals(action.SelectorType, "Label", StringComparison.OrdinalIgnoreCase))
                {
                    locator = _page.GetByLabel(action.SelectorValue);
                }
                else 
                {
                    // Fallback to Locator if it looks like CSS
                    locator = _page.Locator(action.SelectorValue);
                }
            }

            try 
            {
                // Scroll into view if possible
                await locator.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 5000 }).ConfigureAwait(false);
            }
            catch 
            {
                // Ignore scroll errors, element might be obscured or not scrollable
            }

            try 
            {
                switch (action.ActionType.ToLowerInvariant())
                {
                    case "click":
                        await locator.ClickAsync();
                        break;
                    case "fill":
                    case "type":
                    case "input":
                        var inputData = _testDataManager.ReplacePlaceholders(action.InputData ?? "");
                        await locator.FillAsync(inputData);
                        break;
                    case "check":
                        await locator.CheckAsync();
                        break;
                    case "uncheck":
                        await locator.UncheckAsync();
                        break;
                    case "navigate":
                    case "goto":
                        await _page.GotoAsync(action.SelectorValue);
                        break;
                    case "assert":
                    case "verify":
                        await Microsoft.Playwright.Assertions.Expect(locator).ToBeVisibleAsync();
                        break;
                    default:
                         await locator.ClickAsync(); // Default to click?
                         break;
                }
            }
            catch (Exception ex)
            {
                var screenshotPath = $"error_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                await _page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
                Console.WriteLine($"[Error] Action failed: {ex.Message}. Screenshot saved to {screenshotPath}");
                throw;
            }
        }
    }
}
