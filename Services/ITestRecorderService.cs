using System.Threading;
using System.Threading.Tasks;

namespace UiTestRunner.Services
{
    public interface ITestRecorderService
    {
        /// <summary>
        /// Launches a headed browser, records user interactions, converts them to Gherkin steps via AI,
        /// and saves the resulting feature file when the browser is closed.
        /// </summary>
        /// <param name="url">The starting URL.</param>
        /// <param name="outputFilename">The name of the feature file to save (e.g., "login.feature").</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task StartRecordingAsync(string url, string outputFilename, CancellationToken cancellationToken = default);
    }
}
