using Hangfire;
using Microsoft.EntityFrameworkCore;
using UiTestRunner.Data;
using UiTestRunner.Models;
using UiTestRunner.Services;

namespace UiTestRunner.Background;

/// <summary>
/// Runs a list of scenarios one by one in a single job. Each scenario gets its own TestResult and runs sequentially.
/// </summary>
public class SequentialBatchJob
{
    private readonly IServiceProvider _serviceProvider;

    public SequentialBatchJob(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task Execute(string baseUrl, bool headed, List<ScenarioRunItem> scenarios, string? batchRunId, string? testDataCsvPath = null, string? environment = null, string? applicationName = null, CancellationToken cancellationToken = default)
    {
        if (scenarios == null || scenarios.Count == 0)
            return;

        List<int> resultIds;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var results = new List<TestResult>();
            foreach (var item in scenarios)
            {
                results.Add(new TestResult
                {
                    Url = baseUrl,
                    GherkinScript = item.GherkinScript,
                    Status = TestStatus.Pending,
                    RunTime = DateTime.Now,
                    BatchRunId = batchRunId,
                    FeaturePath = item.FeaturePath,
                    ScenarioName = item.Name,
                    Environment = environment,
                    ApplicationName = applicationName
                });
            }
            db.TestResults.AddRange(results);
            await db.SaveChangesAsync(cancellationToken);
            resultIds = results.Select(r => r.Id).ToList();
        }

        for (var i = 0; i < resultIds.Count && !cancellationToken.IsCancellationRequested; i++)
        {
            using var scope = _serviceProvider.CreateScope();
            var uiTestService = scope.ServiceProvider.GetRequiredService<IUiTestService>();
            var item = scenarios[i];
            var csvPath = !string.IsNullOrEmpty(item.TestDataCsvPath) ? item.TestDataCsvPath : testDataCsvPath;
            // When per-scenario path is null we fall back to batch default; if that default is a Production path but the run is for another env (e.g. SIT), do not use it — use the expected path for this feature+env so we never load Production data.
            if (string.IsNullOrEmpty(csvPath) == false && string.IsNullOrEmpty(environment) == false &&
                !environment.Equals("Production", StringComparison.OrdinalIgnoreCase) &&
                csvPath.Contains("Production", StringComparison.OrdinalIgnoreCase))
            {
                var featureName = string.IsNullOrWhiteSpace(item.FeaturePath) ? null : Path.GetFileNameWithoutExtension(item.FeaturePath.Trim());
                if (!string.IsNullOrEmpty(featureName))
                    csvPath = !string.IsNullOrWhiteSpace(applicationName)
                        ? $"TestData/{featureName}.{environment.Trim()}.{applicationName}.csv"
                        : $"TestData/{featureName}.{environment.Trim()}.csv";
            }
            await uiTestService.RunTestAsync(resultIds[i], baseUrl, headed, item.GherkinScript, csvPath, cancellationToken);
        }
    }
}
