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

        public TestRunnerController(ApplicationDbContext context, IBackgroundJobClient backgroundJobClient, ITestRecorderService recorderService, Microsoft.Extensions.Options.IOptions<UiTestRunner.Configuration.RunnerSettings> runnerSettings, ILogger<TestRunnerController> logger, IWebHostEnvironment env)
        {
            _context = context;
            _backgroundJobClient = backgroundJobClient;
            _recorderService = recorderService;
            _runnerSettings = runnerSettings?.Value ?? new UiTestRunner.Configuration.RunnerSettings();
            _logger = logger;
            _env = env;
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
        public async Task<IActionResult> GetResults([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            page = Math.Max(1, page);
            if (pageSize != 10 && pageSize != 25 && pageSize != 50) pageSize = 10;

            var totalCount = await _context.TestResults.CountAsync();
            var results = await _context.TestResults
                .OrderByDescending(t => t.RunTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Json(new { results, totalCount, page, pageSize });
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
    }
}
