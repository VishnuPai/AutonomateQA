using Microsoft.Playwright;
using System.Threading;
using System.Threading.Tasks;

namespace UiTestRunner.Services
{
    public interface IPlaywrightVisionService
    {
        /// <param name="forVerification">When true, a smaller snapshot limit may be used (see Playwright:MaxAriaSnapshotLengthForVerify) to save tokens.</param>
        Task<string> GetCleanSnapshotAsync(IPage page, bool forVerification = false, CancellationToken cancellationToken = default);
    }
}
