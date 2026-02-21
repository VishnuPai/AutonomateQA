namespace UiTestRunner.Services;

public class TestRunTokenTracker : ITestRunTokenTracker
{
    private int _promptTokens;
    private int _completionTokens;

    public void AddUsage(int promptTokens, int completionTokens)
    {
        _promptTokens += promptTokens;
        _completionTokens += completionTokens;
    }

    public (int PromptTokens, int CompletionTokens, int TotalTokens) GetTotal()
    {
        return (_promptTokens, _completionTokens, _promptTokens + _completionTokens);
    }

    public void Reset()
    {
        _promptTokens = 0;
        _completionTokens = 0;
    }
}
