using System.Threading;
using Hangfire;
using UiTestRunner.Services;

namespace UiTestRunner.Background
{
    public class TestRunnerJob
    {
        private readonly IUiTestService _uiTestService;

        public TestRunnerJob(IUiTestService uiTestService)
        {
            _uiTestService = uiTestService;
        }

        [AutomaticRetry(Attempts = 0)]
        public async Task Execute(int testResultId, string url, bool headed, string? gherkinScript, CancellationToken cancellationToken = default)
        {
            await _uiTestService.RunTestAsync(testResultId, url, headed, gherkinScript, cancellationToken);
        }
    }
}
