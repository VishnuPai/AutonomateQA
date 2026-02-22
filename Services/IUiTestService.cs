using System.Threading;
using System.Threading.Tasks;
using UiTestRunner.Models;

namespace UiTestRunner.Services
{
    public interface IUiTestService
    {
        Task<TestResult?> RunTestAsync(int testResultId, string url, bool headed = false, string? gherkinScript = null, string? testDataCsvPath = null, CancellationToken cancellationToken = default);
    }
}
