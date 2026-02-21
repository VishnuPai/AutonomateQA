namespace UiTestRunner.AiProviders
{
    public record UiActionResponse(
        string ActionType,
        string SelectorType,
        string SelectorValue,
        string InputData,
        string Reasoning
    );
}
