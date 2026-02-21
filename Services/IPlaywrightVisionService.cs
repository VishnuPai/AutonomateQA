using Microsoft.Playwright;
using System.Threading;
using System.Threading.Tasks;

namespace UiTestRunner.Services
{
    public interface IPlaywrightVisionService
    {
        Task<string> GetCleanSnapshotAsync(IPage page, CancellationToken cancellationToken = default);
    }
}
