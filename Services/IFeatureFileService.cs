namespace UiTestRunner.Services;

/// <summary>
/// Discovers .feature files and parses scenarios for batch runs.
/// </summary>
public interface IFeatureFileService
{
    /// <summary>
    /// Gets relative paths of all .feature files under the configured Scenarios path (recursive).
    /// </summary>
    Task<IReadOnlyList<FeatureFileInfo>> GetFeatureFilesAsync();

    /// <summary>
    /// Parses a .feature file and returns each scenario (Scenario / Scenario Outline) as a runnable Gherkin script.
    /// </summary>
    /// <param name="relativePath">Path relative to Scenarios folder (e.g. "Login.feature" or "Auth/Login.feature").</param>
    Task<IReadOnlyList<ScenarioItem>> GetScenariosAsync(string relativePath);

    /// <summary>
    /// Returns the raw file content of a .feature file for viewing.
    /// </summary>
    Task<string?> GetFeatureFileContentAsync(string relativePath);
}

public class FeatureFileInfo
{
    public string RelativePath { get; set; } = "";
    public string Name { get; set; } = "";
}

public class ScenarioItem
{
    public string Name { get; set; } = "";
    public string GherkinScript { get; set; } = "";
    /// <summary>Set by caller when building list from a feature path.</summary>
    public string? FeaturePath { get; set; }
    /// <summary>Scenario tags (e.g. ignore, manual) without @. Used to hide from execution while still showing in the UI.</summary>
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
}
