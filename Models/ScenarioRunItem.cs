namespace UiTestRunner.Models;

/// <summary>
/// Serializable scenario item for batch/sequential run (e.g. Hangfire job argument).
/// </summary>
public class ScenarioRunItem
{
    public string Name { get; set; } = "";
    public string GherkinScript { get; set; } = "";
    /// <summary>Feature file path (e.g. "Login.feature").</summary>
    public string? FeaturePath { get; set; }
    /// <summary>Resolved test data CSV path for this scenario (e.g. TestData/FeatureName.Environment.ApplicationName.csv). When set, overrides env default.</summary>
    public string? TestDataCsvPath { get; set; }
}
