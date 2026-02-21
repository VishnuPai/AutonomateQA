using System.Diagnostics;
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

        public TestRunnerController(ApplicationDbContext context, IBackgroundJobClient backgroundJobClient, ITestRecorderService recorderService, Microsoft.Extensions.Options.IOptions<UiTestRunner.Configuration.RunnerSettings> runnerSettings, ILogger<TestRunnerController> logger, IWebHostEnvironment env, IFeatureFileService featureFileService)
        {
            _context = context;
            _backgroundJobClient = backgroundJobClient;
            _recorderService = recorderService;
            _runnerSettings = runnerSettings?.Value ?? new UiTestRunner.Configuration.RunnerSettings();
            _logger = logger;
            _env = env;
            _featureFileService = featureFileService;
        }

        public IActionResult Index()
        {
            ViewData["BaseUrl"] = _runnerSettings.BaseUrl ?? "";
            return View();
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

            var testResult = new TestResult
            {
                Url = model.Url,
                GherkinScript = model.GherkinScript,
                Status = TestStatus.Pending,
                RunTime = DateTime.Now
            };

            _context.TestResults.Add(testResult);
            await _context.SaveChangesAsync();

            // Enqueue Hangfire Job
            var hangfireId = _backgroundJobClient.Enqueue<TestRunnerJob>(job => job.Execute(testResult.Id, model.Url, model.Headed, model.GherkinScript));

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
                RunTime = DateTime.Now
            };
            _context.TestResults.Add(testResult);
            await _context.SaveChangesAsync();

            var hangfireId = _backgroundJobClient.Enqueue<TestRunnerJob>(job => job.Execute(testResult.Id, testResult.Url, false, testResult.GherkinScript));
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
                    allScenarios.Add(new ScenarioItem { Name = s.Name, GherkinScript = s.GherkinScript, FeaturePath = p });
                }
            }

            var maxToRun = _runnerSettings.BatchRunMaxPerRequest;
            if (request.MaxScenarios.HasValue && request.MaxScenarios.Value > 0)
                maxToRun = Math.Min(maxToRun, request.MaxScenarios.Value);
            var toRun = allScenarios.Take(maxToRun).ToList();
            var batchRunId = Guid.NewGuid().ToString("N")[..16];

            if (request.Sequential)
            {
                var scenarioPayload = toRun.Select(s => new ScenarioRunItem { Name = s.Name, GherkinScript = s.GherkinScript, FeaturePath = s.FeaturePath }).ToList();
                _backgroundJobClient.Enqueue<SequentialBatchJob>(job => job.Execute(request.BaseUrl, request.Headed, scenarioPayload, batchRunId));
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
                    ScenarioName = item.Name
                };
                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();
                var hangfireId = _backgroundJobClient.Enqueue<TestRunnerJob>(job => job.Execute(testResult.Id, request.BaseUrl, request.Headed, item.GherkinScript));
                testResult.HangfireJobId = hangfireId;
                await _context.SaveChangesAsync();
                enqueued++;
            }

            _logger.LogInformation("BatchRun enqueued {Count} scenarios for {Url}, batchRunId={BatchRunId}", enqueued, request.BaseUrl, batchRunId);
            return Ok(new { enqueued, totalInFiles = allScenarios.Count, batchRunId, message = $"Enqueued {enqueued} scenario(s). Track progress below." });
        }
    }
}
