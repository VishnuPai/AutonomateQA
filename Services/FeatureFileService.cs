using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UiTestRunner.Configuration;

namespace UiTestRunner.Services;

/// <summary>
/// Discovers .feature files under ScenariosPath and parses them into scenarios for batch runs.
/// </summary>
public class FeatureFileService : IFeatureFileService
{
    private readonly string _scenariosRoot;
    private readonly ILogger<FeatureFileService> _logger;

    private static readonly Regex ScenarioStartRegex = new(
        @"^\s*(Scenario|Scenario Outline)\s*:\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public FeatureFileService(IWebHostEnvironment env, IOptions<RunnerSettings> runnerSettings, ILogger<FeatureFileService> logger)
    {
        _logger = logger;
        var basePath = env?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var scenariosPath = runnerSettings?.Value?.ScenariosPath?.Trim().Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
        _scenariosRoot = string.IsNullOrEmpty(scenariosPath)
            ? basePath
            : Path.Combine(basePath, scenariosPath);
    }

    public Task<IReadOnlyList<FeatureFileInfo>> GetFeatureFilesAsync()
    {
        var list = new List<FeatureFileInfo>();
        if (!Directory.Exists(_scenariosRoot))
        {
            _logger.LogWarning("Scenarios root does not exist: {Root}", _scenariosRoot);
            return Task.FromResult<IReadOnlyList<FeatureFileInfo>>(list);
        }

        foreach (var path in Directory.EnumerateFiles(_scenariosRoot, "*.feature", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_scenariosRoot, path);
            list.Add(new FeatureFileInfo
            {
                RelativePath = relative.Replace('\\', '/'),
                Name = Path.GetFileName(path)
            });
        }

        list.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<FeatureFileInfo>>(list);
    }

    public Task<IReadOnlyList<ScenarioItem>> GetScenariosAsync(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.FromResult<IReadOnlyList<ScenarioItem>>(Array.Empty<ScenarioItem>());
        }

        var fullPath = Path.Combine(_scenariosRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(_scenariosRoot), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Requested path is outside Scenarios root: {Path}", relativePath);
            return Task.FromResult<IReadOnlyList<ScenarioItem>>(Array.Empty<ScenarioItem>());
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return Task.FromResult<IReadOnlyList<ScenarioItem>>(Array.Empty<ScenarioItem>());
        }

        var lines = System.IO.File.ReadAllLines(fullPath);
        var scenarios = ParseScenarios(lines);
        return Task.FromResult<IReadOnlyList<ScenarioItem>>(scenarios);
    }

    public Task<string?> GetFeatureFileContentAsync(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Task.FromResult<string?>(null);

        var fullPath = Path.Combine(_scenariosRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(_scenariosRoot), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Requested path is outside Scenarios root: {Path}", relativePath);
            return Task.FromResult<string?>(null);
        }

        if (!System.IO.File.Exists(fullPath))
            return Task.FromResult<string?>(null);

        try
        {
            var content = System.IO.File.ReadAllText(fullPath);
            return Task.FromResult<string?>(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read feature file: {Path}", relativePath);
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Parses Gherkin lines into scenario blocks. Preserves Feature and optional Background before each scenario.
    /// </summary>
    internal static List<ScenarioItem> ParseScenarios(string[] lines)
    {
        var result = new List<ScenarioItem>();
        var featureBlock = new List<string>();
        var backgroundBlock = new List<string>();
        var inFeature = false;
        var inBackground = false;
        List<string>? currentScenario = null;
        string? currentScenarioName = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var lower = trimmed.ToLowerInvariant();

            if (lower.StartsWith("feature:"))
            {
                FlushScenario();
                inFeature = true;
                inBackground = false;
                featureBlock.Clear();
                backgroundBlock.Clear();
                featureBlock.Add(line);
                continue;
            }

            if (inFeature && !inBackground && lower.StartsWith("background:"))
            {
                inBackground = true;
                backgroundBlock.Clear();
                backgroundBlock.Add(line);
                continue;
            }

            var scenarioMatch = ScenarioStartRegex.Match(trimmed);
            if (scenarioMatch.Success)
            {
                FlushScenario();
                currentScenarioName = scenarioMatch.Groups[2].Value.Trim();
                currentScenario = new List<string> { line };
                continue;
            }

            if (currentScenario != null)
                currentScenario.Add(line);
            else if (inBackground)
                backgroundBlock.Add(line);
            else if (inFeature && !inBackground && !lower.StartsWith("background:") && !ScenarioStartRegex.Match(trimmed).Success)
                featureBlock.Add(line);
        }

        FlushScenario();

        void FlushScenario()
        {
            if (currentScenario == null || string.IsNullOrWhiteSpace(currentScenarioName)) return;
            var script = new List<string>();
            if (featureBlock.Count > 0)
            {
                script.AddRange(featureBlock);
                script.Add("");
            }
            if (backgroundBlock.Count > 0)
            {
                script.AddRange(backgroundBlock);
                script.Add("");
            }
            script.AddRange(currentScenario);
            result.Add(new ScenarioItem
            {
                Name = currentScenarioName,
                GherkinScript = string.Join(Environment.NewLine, script)
            });
            currentScenario = null;
            currentScenarioName = null;
        }

        return result;
    }
}
