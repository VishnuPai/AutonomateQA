using System.Diagnostics;
using System.IO;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UiTestRunner.Background;
using UiTestRunner.Data;
using UiTestRunner.Models;
using UiTestRunner.Services;
using UiTestRunner.Helpers;

namespace UiTestRunner.Controllers
{
    public class TestRunnerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ITestRecorderService _recorderService;
        private readonly UiTestRunner.Configuration.RunnerSettings _runnerSettings;
        private readonly ILogger<TestRunnerController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IFeatureFileService _featureFileService;
        private readonly IConfiguration _configuration;

        public TestRunnerController(ApplicationDbContext context, IBackgroundJobClient backgroundJobClient, ITestRecorderService recorderService, Microsoft.Extensions.Options.IOptions<UiTestRunner.Configuration.RunnerSettings> runnerSettings, ILogger<TestRunnerController> logger, IWebHostEnvironment env, IFeatureFileService featureFileService, IConfiguration configuration)
        {
            _context = context;
            _backgroundJobClient = backgroundJobClient;
            _recorderService = recorderService;
            _runnerSettings = runnerSettings?.Value ?? new UiTestRunner.Configuration.RunnerSettings();
            _logger = logger;
            _env = env;
            _featureFileService = featureFileService;
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            ViewData["BaseUrl"] = _runnerSettings.BaseUrl ?? "";
            return View();
        }

        /// <summary>Returns configured environments for the UI dropdown (name, baseUrl, csvPath). Display name "PROD" for Production.</summary>
        [HttpGet]
        public IActionResult GetEnvironments()
        {
            var list = new List<object>();
            var section = _configuration.GetSection("Environments");
            if (!section.Exists()) return Json(list);
            foreach (var child in section.GetChildren())
            {
                var key = child.Key;
                var baseUrl = child["BaseUrl"]?.Trim() ?? "";
                var csvPath = child["CsvPath"]?.Trim() ?? "";
                var displayName = string.Equals(key, "Production", StringComparison.OrdinalIgnoreCase) ? "PROD" : key;
                list.Add(new { name = key, displayName, baseUrl, csvPath });
            }
            return Json(list);
        }

        /// <summary>Returns configured application names for the UI dropdown. From TestData:Applications (array) plus TestData:ApplicationName as default when present.</summary>
        [HttpGet]
        public IActionResult GetApplications()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<object>();
            var defaultApp = _configuration["TestData:ApplicationName"]?.Trim();
            if (!string.IsNullOrEmpty(defaultApp))
            {
                list.Add(new { name = defaultApp, isDefault = true });
                seen.Add(defaultApp);
            }
            var apps = _configuration.GetSection("TestData:Applications").Get<string[]>();
            if (apps != null)
            {
                foreach (var name in apps)
                {
                    var n = name?.Trim();
                    if (string.IsNullOrEmpty(n) || seen.Contains(n)) continue;
                    seen.Add(n);
                    list.Add(new { name = n, isDefault = false });
                }
            }
            return Json(list);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("TriggerTestPolicy")]
        public async Task<IActionResult> TriggerTest([FromBody] TriggerTestViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!UrlValidator.IsSafeUrl(model.Url))
            {
                return BadRequest("Invalid or blocked URL. Internal network targets are not permitted for security reasons.");
            }

            if (FeatureFileService.ScriptHasIgnoreOrManualTag(model.GherkinScript))
            {
                return BadRequest("This scenario is tagged @ignore or @manual and cannot be run.");
            }

            var testResult = new TestResult
            {
                Url = model.Url,
                GherkinScript = model.GherkinScript,
                Status = TestStatus.Pending,
                RunTime = DateTime.Now,
                Environment = !string.IsNullOrWhiteSpace(model.Environment) ? model.Environment!.Trim() : null,
                ApplicationName = !string.IsNullOrWhiteSpace(model.ApplicationName) ? model.ApplicationName!.Trim() : null
            };

            _context.TestResults.Add(testResult);
            await _context.SaveChangesAsync();

            string? testDataCsvPath = null;
            if (!string.IsNullOrWhiteSpace(model.Environment))
            {
                var envKey = model.Environment.Trim();
                testDataCsvPath = _configuration[$"Environments:{envKey}:CsvPath"]?.Trim();
                if (string.IsNullOrEmpty(testDataCsvPath))
                    _logger.LogWarning("Environment '{Env}' has no CsvPath configured; run will use default test data.", envKey);
                else
                {
                    // Never pass a Production path when the selected environment is not Production (guards against config override).
                    var appName = !string.IsNullOrWhiteSpace(model.ApplicationName) ? model.ApplicationName.Trim() : _configuration["TestData:ApplicationName"]?.Trim();
                    if (testDataCsvPath.Contains("Production", StringComparison.OrdinalIgnoreCase) && !envKey.Equals("Production", StringComparison.OrdinalIgnoreCase))
                    {
                        testDataCsvPath = !string.IsNullOrWhiteSpace(appName)
                            ? $"TestData/Header.{envKey}.{appName}.csv"
                            : $"TestData/Header.{envKey}.csv";
                        _logger.LogWarning("TriggerTest: config had Production CSV for env '{Env}'; using expected path instead: {CsvPath}", envKey, testDataCsvPath);
                    }
                    else
                        _logger.LogInformation("TriggerTest: using test data CSV for environment {Env}: {CsvPath}", envKey, testDataCsvPath);
                }
            }

            // Enqueue Hangfire Job
            var hangfireId = _backgroundJobClient.Enqueue<TestRunnerJob>(job => job.Execute(testResult.Id, model.Url, model.Headed, model.GherkinScript, testDataCsvPath));

            testResult.HangfireJobId = hangfireId;
            await _context.SaveChangesAsync();

            return Ok(new { jobId = testResult.Id, hangfireId = hangfireId });
        }

        [HttpGet]
        public async Task<IActionResult> GetResults([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] bool singleOnly = false)
        {
            page = Math.Max(1, page);
            if (pageSize != 10 && pageSize != 25 && pageSize != 50) pageSize = 10;

            var query = _context.TestResults.AsQueryable();
            if (singleOnly)
                query = query.Where(t => t.BatchRunId == null);

            var totalCount = await query.CountAsync();
            var totalTokensAllTime = await _context.TestResults.SumAsync(t => t.TotalTokens ?? 0);
            var results = await query
                .OrderByDescending(t => t.RunTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Json(new { results, totalCount, page, pageSize, totalTokensAllTime });
        }

        /// <summary>Returns batch runs only, paginated by batch run (one page = N batch runs, each with all its scenario results).</summary>
        [HttpGet]
        public async Task<IActionResult> GetBatchRuns([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            page = Math.Max(1, page);
            if (pageSize != 10 && pageSize != 25 && pageSize != 50) pageSize = 10;

            var totalBatchCount = await _context.TestResults
                .Where(t => t.BatchRunId != null)
                .Select(t => t.BatchRunId)
                .Distinct()
                .CountAsync();

            var batchIdsOrdered = await _context.TestResults
                .Where(t => t.BatchRunId != null)
                .GroupBy(t => t.BatchRunId!)
                .Select(g => new { BatchRunId = g.Key, RunTime = g.Max(t => t.RunTime) })
                .OrderByDescending(x => x.RunTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var batchRuns = new List<object>();
            if (batchIdsOrdered.Count == 0)
            {
                return Json(new { batchRuns, totalBatchCount, page, pageSize });
            }

            var idsOnPage = batchIdsOrdered.Select(b => b.BatchRunId).ToList();
            var allItems = await _context.TestResults
                .Where(t => idsOnPage.Contains(t.BatchRunId!))
                .OrderByDescending(t => t.RunTime)
                .ToListAsync();

            var itemsByBatch = allItems
                .GroupBy(t => t.BatchRunId!)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.RunTime).ToList());

            foreach (var b in batchIdsOrdered)
            {
                var items = itemsByBatch.TryGetValue(b.BatchRunId, out var list) ? list : new List<TestResult>();
                batchRuns.Add(new { batchRunId = b.BatchRunId, runTime = b.RunTime, items });
            }

            return Json(new { batchRuns, totalBatchCount, page, pageSize });
        }

        /// <summary>Returns a single test result by id (for progress polling).</summary>
        [HttpGet]
        public async Task<IActionResult> GetResult([FromQuery] int id)
        {
            var result = await _context.TestResults.FindAsync(id);
            if (result == null) return NotFound();
            return Json(new { id = result.Id, status = (int)result.Status, statusText = result.Status.ToString(), duration = result.Duration?.ToString(), runTime = result.RunTime });
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteResult(int id)
        {
            var result = await _context.TestResults.FindAsync(id);
            if (result == null) return NotFound();

            var webRoot = _env.WebRootPath;
            if (!string.IsNullOrEmpty(webRoot))
            {
                if (!string.IsNullOrEmpty(result.ScreenshotPath))
                {
                    var screenshotFull = Path.Combine(webRoot, result.ScreenshotPath.TrimStart('/'));
                    if (System.IO.File.Exists(screenshotFull)) try { System.IO.File.Delete(screenshotFull); } catch (Exception ex) { _logger.LogWarning(ex, "Could not delete screenshot {Path}", screenshotFull); }
                }
                if (!string.IsNullOrEmpty(result.VideoPath))
                {
                    var videoFull = Path.Combine(webRoot, result.VideoPath.TrimStart('/'));
                    if (System.IO.File.Exists(videoFull)) try { System.IO.File.Delete(videoFull); } catch (Exception ex) { _logger.LogWarning(ex, "Could not delete video {Path}", videoFull); }
                }
            }

            _context.TestResults.Remove(result);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("TriggerTestPolicy")]
        public async Task<IActionResult> RerunResult([FromQuery] int id)
        {
            var existing = await _context.TestResults.FindAsync(id);
            if (existing == null) return NotFound();
            if (!UrlValidator.IsSafeUrl(existing.Url)) return BadRequest(new { error = "Cannot re-run: URL is no longer allowed (internal or blocked)." });

            var testResult = new TestResult
            {
                Url = existing.Url,
                GherkinScript = existing.GherkinScript,
                Status = TestStatus.Pending,
                RunTime = DateTime.Now,
                Environment = existing.Environment,
                ApplicationName = existing.ApplicationName
            };
            _context.TestResults.Add(testResult);
            await _context.SaveChangesAsync();

            string? testDataCsvPath = null;
            if (!string.IsNullOrWhiteSpace(existing.Environment))
            {
                var envKey = existing.Environment.Trim();
                testDataCsvPath = _configuration[$"Environments:{envKey}:CsvPath"]?.Trim();
            }
            var hangfireId = _backgroundJobClient.Enqueue<TestRunnerJob>(job => job.Execute(testResult.Id, testResult.Url, false, testResult.GherkinScript, testDataCsvPath));
            testResult.HangfireJobId = hangfireId;
            await _context.SaveChangesAsync();

            return Ok(new { jobId = testResult.Id, hangfireId = hangfireId });
        }

        [HttpPost]
        public async Task<IActionResult> StartRecording([FromBody] string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return BadRequest("URL is required");

            if (!UrlValidator.IsSafeUrl(url))
            {
                return BadRequest("Invalid or blocked URL. Internal network targets are not permitted for security reasons.");
            }

            try 
            {
                var filename = $"recorded_test_{DateTime.Now:yyyyMMdd_HHmmss}.feature";
                // Await specifically to catch launch errors and report them to the user
                await _recorderService.StartRecordingAsync(url, filename);
                return Ok(new { Message = $"Recording saved to {filename}", Filename = filename });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recorder failed to start.");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTestCases()
        {
            var cases = await _context.TestCases
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return Json(cases);
        }

        [HttpPost]
        public async Task<IActionResult> SaveTestCase([FromBody] TestCase model)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Url))
            {
                return BadRequest("Name and URL are required.");
            }

            if (!UrlValidator.IsSafeUrl(model.Url))
            {
                return BadRequest("Invalid or blocked URL. Internal network targets are not permitted.");
            }

            model.CreatedAt = DateTime.Now;
            _context.TestCases.Add(model);
            await _context.SaveChangesAsync();

            return Ok(model);
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteTestCase(int id)
        {
            var testCase = await _context.TestCases.FindAsync(id);
            if (testCase == null) return NotFound();

            _context.TestCases.Remove(testCase);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>Returns progress for a batch run (completed, remaining, estimated time).</summary>
        [HttpGet]
        public async Task<IActionResult> GetBatchProgress([FromQuery] string batchRunId)
        {
            if (string.IsNullOrWhiteSpace(batchRunId))
                return BadRequest("batchRunId is required.");
            var batch = await _context.TestResults
                .Where(t => t.BatchRunId == batchRunId)
                .Select(t => new { t.Status, t.Duration })
                .ToListAsync();
            var total = batch.Count;
            if (total == 0)
                return Json(new { total = 0, completed = 0, passed = 0, failed = 0, running = 0, pending = 0, estimatedSecondsRemaining = (double?)null });
            var passed = batch.Count(t => t.Status == TestStatus.Passed);
            var failed = batch.Count(t => t.Status == TestStatus.Failed);
            var running = batch.Count(t => t.Status == TestStatus.Running);
            var pending = batch.Count(t => t.Status == TestStatus.Pending);
            var completed = passed + failed;
            double? estimatedSecondsRemaining = null;
            if (completed > 0 && (pending > 0 || running > 0))
            {
                var completedWithDuration = batch.Where(t => (t.Status == TestStatus.Passed || t.Status == TestStatus.Failed) && t.Duration.HasValue).ToList();
                if (completedWithDuration.Count > 0)
                {
                    var avgSeconds = completedWithDuration.Average(t => t.Duration!.Value.TotalSeconds);
                    estimatedSecondsRemaining = Math.Round(avgSeconds * (pending + running), 0);
                }
            }
            return Json(new { total, completed, passed, failed, running, pending, estimatedSecondsRemaining });
        }

        /// <summary>Returns list of .feature files under the configured Scenarios path.</summary>
        [HttpGet]
        public async Task<IActionResult> GetFeatureFiles()
        {
            var files = await _featureFileService.GetFeatureFilesAsync();
            return Json(files);
        }

        /// <summary>Parses a .feature file and returns scenario names and runnable Gherkin scripts.</summary>
        [HttpGet]
        public async Task<IActionResult> GetScenarios([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path is required.");
            var scenarios = await _featureFileService.GetScenariosAsync(path);
            return Json(scenarios);
        }

        /// <summary>Returns raw content of a .feature file for viewing in the UI.</summary>
        [HttpGet]
        public async Task<IActionResult> GetFeatureFileContent([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path is required.");
            var content = await _featureFileService.GetFeatureFileContentAsync(path);
            if (content == null)
                return NotFound();
            return Json(new { content });
        }

        /// <summary>Enqueues scenarios from one or more .feature files for batch run. Capped by BatchRunMaxPerRequest.</summary>
        [HttpPost]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("TriggerTestPolicy")]
        public async Task<IActionResult> BatchRun([FromBody] BatchRunRequest request)
        {
            if (request == null || !ModelState.IsValid)
                return BadRequest(ModelState);
            if (!UrlValidator.IsSafeUrl(request.BaseUrl))
                return BadRequest("Invalid or blocked URL. Internal network targets are not permitted.");

            var paths = request.FeaturePaths?.Count > 0
                ? request.FeaturePaths
                : (!string.IsNullOrWhiteSpace(request.FeaturePath) ? new List<string> { request.FeaturePath! } : null);
            if (paths == null || paths.Count == 0)
                return BadRequest("Provide FeaturePath or FeaturePaths.");

            var allScenarios = new List<ScenarioItem>();
            foreach (var p in paths)
            {
                var scenarios = await _featureFileService.GetScenariosAsync(p);
                foreach (var s in scenarios)
                {
                    allScenarios.Add(new ScenarioItem { Name = s.Name, GherkinScript = s.GherkinScript, FeaturePath = p, Tags = s.Tags });
                }
            }

            // Exclude @ignore and @manual from execution (they remain visible in Feature files & scenarios tab)
            var runnable = allScenarios.Where(s => !HasIgnoreOrManualTag(s) && !FeatureFileService.ScriptHasIgnoreOrManualTag(s.GherkinScript)).ToList();
            var maxToRun = _runnerSettings.BatchRunMaxPerRequest;
            if (request.MaxScenarios.HasValue && request.MaxScenarios.Value > 0)
                maxToRun = Math.Min(maxToRun, request.MaxScenarios.Value);
            var toRun = runnable.Take(maxToRun).ToList();
            var batchRunId = Guid.NewGuid().ToString("N")[..16];

            string? testDataCsvPath = null;
            var environmentKey = !string.IsNullOrWhiteSpace(request.Environment) ? request.Environment.Trim() : null;
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                testDataCsvPath = _configuration[$"Environments:{environmentKey}:CsvPath"]?.Trim();
                if (string.IsNullOrEmpty(testDataCsvPath) && toRun.Count > 0)
                {
                    // No CsvPath configured for this environment: use an environment-specific default so we don't fall back to another env's file (e.g. Production).
                    var appName = _configuration["TestData:ApplicationName"]?.Trim();
                    var firstFeatureName = Path.GetFileNameWithoutExtension(toRun[0].FeaturePath?.Trim() ?? "");
                    if (!string.IsNullOrEmpty(firstFeatureName))
                    {
                        testDataCsvPath = !string.IsNullOrWhiteSpace(appName)
                            ? $"TestData/{firstFeatureName}.{environmentKey}.{appName}.csv"
                            : $"TestData/{firstFeatureName}.{environmentKey}.csv";
                        _logger.LogInformation("BatchRun: Environment '{Env}' has no CsvPath in config; using environment-specific default: {CsvPath}", environmentKey, testDataCsvPath);
                    }
                }
                else if (!string.IsNullOrEmpty(testDataCsvPath))
                {
                    // Never use a Production path when the selected environment is not Production (guards against config override).
                    var appForGuard = !string.IsNullOrWhiteSpace(request.ApplicationName) ? request.ApplicationName.Trim() : _configuration["TestData:ApplicationName"]?.Trim();
                    if (testDataCsvPath.Contains("Production", StringComparison.OrdinalIgnoreCase) && !environmentKey.Equals("Production", StringComparison.OrdinalIgnoreCase))
                    {
                        testDataCsvPath = !string.IsNullOrWhiteSpace(appForGuard)
                            ? $"TestData/Header.{environmentKey}.{appForGuard}.csv"
                            : $"TestData/Header.{environmentKey}.csv";
                        _logger.LogWarning("BatchRun: config had Production CSV for env '{Env}'; using expected path instead: {CsvPath}", environmentKey, testDataCsvPath);
                    }
                    else
                        _logger.LogInformation("BatchRun: using test data CSV for environment {Env}: {CsvPath}", environmentKey, testDataCsvPath);
                }
            }

            var applicationName = !string.IsNullOrWhiteSpace(request.ApplicationName) ? request.ApplicationName!.Trim() : _configuration["TestData:ApplicationName"]?.Trim();
            if (request.Sequential)
            {
                var scenarioPayload = toRun.Select(s =>
                {
                    var resolved = ResolveCsvPath(environmentKey, s.FeaturePath, applicationName, testDataCsvPath);
                    // When Environment is set but resolution returned null, use expected path so we never fall back to batch default (e.g. Production) in the job.
                    if (string.IsNullOrEmpty(resolved) && !string.IsNullOrWhiteSpace(environmentKey) && !string.IsNullOrWhiteSpace(s.FeaturePath))
                    {
                        var fn = Path.GetFileNameWithoutExtension(s.FeaturePath.Trim());
                        if (!string.IsNullOrEmpty(fn))
                            resolved = !string.IsNullOrWhiteSpace(applicationName)
                                ? $"TestData/{fn}.{environmentKey.Trim()}.{applicationName}.csv"
                                : $"TestData/{fn}.{environmentKey.Trim()}.csv";
                    }
                    return new ScenarioRunItem
                    {
                        Name = s.Name,
                        GherkinScript = s.GherkinScript,
                        FeaturePath = s.FeaturePath,
                        TestDataCsvPath = resolved
                    };
                }).ToList();
                _backgroundJobClient.Enqueue<SequentialBatchJob>(job => job.Execute(request.BaseUrl, request.Headed, scenarioPayload, batchRunId, testDataCsvPath, environmentKey, applicationName));
                _logger.LogInformation("BatchRun enqueued sequential job with {Count} scenarios for {Url}, batchRunId={BatchRunId}", toRun.Count, request.BaseUrl, batchRunId);
                return Ok(new { enqueued = toRun.Count, sequential = true, totalInFiles = allScenarios.Count, batchRunId, message = $"Enqueued 1 sequential batch ({toRun.Count} scenario(s)). Track progress below." });
            }

            var enqueued = 0;
            foreach (var item in toRun)
            {
                var testResult = new TestResult
                {
                    Url = request.BaseUrl,
                    GherkinScript = item.GherkinScript,
                    Status = TestStatus.Pending,
                    RunTime = DateTime.Now,
                    BatchRunId = batchRunId,
                    FeaturePath = item.FeaturePath,
                    ScenarioName = item.Name,
                    Environment = environmentKey,
                    ApplicationName = applicationName
                };
                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();
                var resolvedCsvPath = ResolveCsvPath(environmentKey, item.FeaturePath, applicationName, testDataCsvPath);
                if (string.IsNullOrEmpty(resolvedCsvPath) && !string.IsNullOrWhiteSpace(environmentKey) && !string.IsNullOrWhiteSpace(item.FeaturePath))
                {
                    var fn = Path.GetFileNameWithoutExtension(item.FeaturePath.Trim());
                    if (!string.IsNullOrEmpty(fn))
                        resolvedCsvPath = !string.IsNullOrWhiteSpace(applicationName)
                            ? $"TestData/{fn}.{environmentKey.Trim()}.{applicationName}.csv"
                            : $"TestData/{fn}.{environmentKey.Trim()}.csv";
                }
                var hangfireId = _backgroundJobClient.Enqueue<TestRunnerJob>(job => job.Execute(testResult.Id, request.BaseUrl, request.Headed, item.GherkinScript, resolvedCsvPath));
                testResult.HangfireJobId = hangfireId;
                await _context.SaveChangesAsync();
                enqueued++;
            }

            _logger.LogInformation("BatchRun enqueued {Count} scenarios for {Url}, batchRunId={BatchRunId}", enqueued, request.BaseUrl, batchRunId);
            return Ok(new { enqueued, totalInFiles = allScenarios.Count, batchRunId, message = $"Enqueued {enqueued} scenario(s). Track progress below." });
        }

        /// <summary>
        /// If environment and feature path are set, tries TestData/{FeatureName}.{Environment}.{ApplicationName}.csv
        /// (when TestData:ApplicationName is set or derived from default CsvPath), then TestData/{FeatureName}.{Environment}.csv.
        /// Returns that path when the file exists; otherwise returns the environment default.
        /// </summary>
        private string? ResolveCsvPath(string? environmentKey, string? featurePath, string? applicationName, string? defaultCsvPath)
        {
            if (string.IsNullOrWhiteSpace(environmentKey) || string.IsNullOrWhiteSpace(featurePath))
                return defaultCsvPath;
            var featureName = Path.GetFileNameWithoutExtension(featurePath.Trim());
            if (string.IsNullOrEmpty(featureName)) return defaultCsvPath;
            var env = environmentKey.Trim();
            var appName = applicationName;
            if (string.IsNullOrWhiteSpace(appName) && !string.IsNullOrWhiteSpace(defaultCsvPath))
            {
                var derived = DeriveApplicationNameFromCsvPath(defaultCsvPath, env);
                if (!string.IsNullOrEmpty(derived)) appName = derived;
                // When default is for another env (e.g. Header.Production.VTS), derivation returns null; still try current env with same app segment so Header.SIT.VTS.csv is tried.
                if (string.IsNullOrWhiteSpace(appName))
                    appName = GetApplicationNameSegmentFromCsvPath(defaultCsvPath);
            }
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(appName))
                candidates.Add($"TestData/{featureName}.{env}.{appName!.Trim()}.csv");
            candidates.Add($"TestData/{featureName}.{env}.csv");
            foreach (var candidatePath in candidates)
            {
                var fullPath = Path.Combine(_env.ContentRootPath ?? "", candidatePath);
                if (System.IO.File.Exists(fullPath))
                {
                    _logger.LogInformation("Using feature-specific test data CSV: {CsvPath}", candidatePath);
                    return candidatePath;
                }
            }
            // Do not use environment default when it is for a different environment (e.g. config has SIT pointing at Header.Production.VTS.csv).
            if (!string.IsNullOrWhiteSpace(defaultCsvPath) && DefaultCsvPathIsForDifferentEnvironment(defaultCsvPath, env))
            {
                _logger.LogWarning("Environment default CSV appears to be for a different environment: {Default}. Using expected path for {Env}: {Candidate}", defaultCsvPath, env, candidates.Count > 0 ? candidates[0] : "(none)");
                return candidates.Count > 0 ? candidates[0] : null;
            }
            // Backup: do not use default path that clearly contains another environment name (e.g. .Production.) when current env is not that.
            if (!string.IsNullOrWhiteSpace(defaultCsvPath) && env.Length > 0 && defaultCsvPath.Contains($".Production.", StringComparison.OrdinalIgnoreCase) && !string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Environment default CSV path contains '.Production.' but selected environment is '{Env}'. Using expected path: {Candidate}", env, candidates.Count > 0 ? candidates[0] : "(none)");
                return candidates.Count > 0 ? candidates[0] : null;
            }
            // Final guard: never return a default path that contains "Production" when the selected environment is not Production (e.g. SIT).
            if (!string.IsNullOrEmpty(defaultCsvPath) && candidates.Count > 0 &&
                !string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase) &&
                defaultCsvPath.Contains("Production", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejecting default CSV path containing 'Production' for environment '{Env}'. Using: {Candidate}", env, candidates[0]);
                return candidates[0];
            }
            _logger.LogDebug("Feature-specific CSV not found (tried: {Candidates}). Using environment default: {Default}", string.Join(", ", candidates), defaultCsvPath ?? "(none)");
            return defaultCsvPath;
        }

        /// <summary>Returns the last dot-segment of the CSV filename (e.g. VTS from Header.Production.VTS.csv) so we can try FeatureName.CurrentEnv.VTS.csv when the default path is for another env.</summary>
        private static string? GetApplicationNameSegmentFromCsvPath(string defaultCsvPath)
        {
            var fileName = Path.GetFileName(defaultCsvPath.Trim());
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return null;
            var nameWithoutExt = fileName.AsSpan(0, fileName.Length - 4);
            var parts = new List<string>();
            foreach (var part in nameWithoutExt.ToString().Split('.'))
            {
                if (!string.IsNullOrEmpty(part)) parts.Add(part);
            }
            return parts.Count >= 3 ? parts[parts.Count - 1] : null;
        }

        /// <summary>Returns true when the default CSV path filename indicates a different environment (e.g. Header.Production.VTS.csv when env is SIT).</summary>
        private static bool DefaultCsvPathIsForDifferentEnvironment(string defaultCsvPath, string environmentKey)
        {
            var fileName = Path.GetFileName(defaultCsvPath.Trim());
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
            var nameWithoutExt = fileName.AsSpan(0, fileName.Length - 4);
            var parts = new List<string>();
            foreach (var part in nameWithoutExt.ToString().Split('.'))
            {
                if (!string.IsNullOrEmpty(part)) parts.Add(part);
            }
            // Pattern: FeatureName.Environment.ApplicationName or FeatureName.Environment; second-to-last or single middle part is env.
            if (parts.Count >= 2)
            {
                var envInPath = parts.Count >= 3 ? parts[parts.Count - 2] : parts[1];
                if (!string.Equals(envInPath, environmentKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Derives ApplicationName from a default CsvPath like TestData/Header.SIT.VTS.csv when Environment matches.</summary>
        private static string? DeriveApplicationNameFromCsvPath(string defaultCsvPath, string environmentKey)
        {
            var fileName = Path.GetFileName(defaultCsvPath.Trim());
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return null;
            var nameWithoutExt = fileName.AsSpan(0, fileName.Length - 4);
            var parts = new List<string>();
            foreach (var part in nameWithoutExt.ToString().Split('.'))
            {
                if (!string.IsNullOrEmpty(part)) parts.Add(part);
            }
            if (parts.Count >= 2 && string.Equals(parts[parts.Count - 2], environmentKey, StringComparison.OrdinalIgnoreCase))
                return parts[parts.Count - 1];
            return null;
        }

        private static bool HasIgnoreOrManualTag(ScenarioItem s)
        {
            if (s.Tags == null || s.Tags.Count == 0) return false;
            foreach (var t in s.Tags)
            {
                if (string.Equals(t, "ignore", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "manual", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
