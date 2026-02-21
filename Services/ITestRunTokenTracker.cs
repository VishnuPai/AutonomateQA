namespace UiTestRunner.Services;

/// <summary>
/// Tracks token usage (prompt + completion) for the current test run.
/// AI providers add usage after each API call; the test run saves the total to TestResult.
/// </summary>
public interface ITestRunTokenTracker
{
    void AddUsage(int promptTokens, int completionTokens);
    (int PromptTokens, int CompletionTokens, int TotalTokens) GetTotal();
    void Reset();
}
