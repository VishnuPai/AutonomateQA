using System.Threading;
using System.Threading.Tasks;

namespace UiTestRunner.AiProviders
{
    public interface IAiModelProvider
    {
        Task<UiActionResponse> GetActionAsync(string gherkinStep, string ariaSnapshot, CancellationToken cancellationToken = default);
        Task<UiVerifyResponse> VerifyAsync(string gherkinStep, string ariaSnapshot, CancellationToken cancellationToken = default);
        Task<string> GenerateGherkinStepAsync(string actionType, string selector, string? value, string snapshot, CancellationToken cancellationToken = default);
    }
}
