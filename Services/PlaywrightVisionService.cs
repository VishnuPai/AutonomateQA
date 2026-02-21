using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UiTestRunner.Configuration;

namespace UiTestRunner.Services
{
    public class PlaywrightVisionService : IPlaywrightVisionService
    {
        private readonly PlaywrightSettings _playwrightSettings;
        private readonly ILogger<PlaywrightVisionService>? _logger;

        public PlaywrightVisionService(Microsoft.Extensions.Options.IOptions<PlaywrightSettings> playwrightSettings, ILogger<PlaywrightVisionService>? logger = null)
        {
            _playwrightSettings = playwrightSettings?.Value ?? new PlaywrightSettings();
            _logger = logger;
        }

        // Static compiled regexes for better performance
        private static readonly Regex IpRegex = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex EmailRegex = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex PhoneRegex = new Regex(@"\b(?:\+?(\d{1,3}))?[-. (]*(\d{3})[-. )]*(\d{3})[-. ]*(\d{4})\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex CcRegex = new Regex(@"\b(?:\d{4}[- ]?){3}\d{4}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        public async Task<string> GetCleanSnapshotAsync(IPage page, bool forVerification = false, CancellationToken cancellationToken = default)
        {
            // Capture the Aria snapshot of the body
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            cancellationToken.ThrowIfCancellationRequested();

            snapshot = await SanitizeSnapshotAsync(snapshot, cancellationToken);

            // Use smaller limit for verification steps (Then...) to save tokens; verification usually needs only header/top
            var maxLen = _playwrightSettings.MaxAriaSnapshotLength;
            if (forVerification && _playwrightSettings.MaxAriaSnapshotLengthForVerify > 0 && _playwrightSettings.MaxAriaSnapshotLengthForVerify < maxLen)
                maxLen = _playwrightSettings.MaxAriaSnapshotLengthForVerify;
            const int reserveForHints = 800;
            if (maxLen > 0 && snapshot.Length > maxLen - reserveForHints)
            {
                var origLen = snapshot.Length;
                snapshot = snapshot.Substring(0, maxLen - reserveForHints)
                    + "\n\n... (snapshot truncated to reduce token usage)";
                _logger?.LogInformation("Aria snapshot truncated from {Original} to {Max} chars (saves ~{EstTokens} tokens/step)", origLen, maxLen - reserveForHints, (origLen - (maxLen - reserveForHints)) / 4);
            }

            // Enrich with DOM identifiers (id, class) so verification can match elements
            // that have no accessible name (e.g. <div id="mini-cart" class="mini-cart">)
            var domHints = await GetDomIdentifierHintsAsync(page, cancellationToken);
            if (!string.IsNullOrEmpty(domHints))
            {
                snapshot += "\n\n[DOM identifiers present on page – use for verification when Aria has no name]\n" + domHints;
            }

            // Final truncation in case snapshot + hints still exceed limit
            if (maxLen > 0 && snapshot.Length > maxLen)
            {
                snapshot = snapshot.Substring(0, maxLen) + "\n\n... (truncated)";
            }

            return snapshot;
        }

        /// <summary>
        /// Collects element ids and notable class names so the AI can verify presence of elements
        /// that are not clearly named in the Aria tree (e.g. minicart div with only id/class).
        /// </summary>
        private static async Task<string?> GetDomIdentifierHintsAsync(IPage page, CancellationToken cancellationToken)
        {
            try
            {
                var script = """
                    (() => {
                        const keywords = ['cart', 'minicart', 'menu', 'nav', 'header', 'footer', 'basket', 'bag', 'login', 'user', 'product', 'order', 'supplier', 'company', 'language', 'locale', 'search', 'account', 'logo', 'brand'];
                        const ids = new Set();
                        const classes = new Set();
                        document.querySelectorAll('[id]').forEach(el => {
                            if (el.id && el.id.length < 80) ids.add(el.id);
                        });
                        document.querySelectorAll('[class]').forEach(el => {
                            const c = (el.className && typeof el.className === 'string') ? el.className : '';
                            c.split(/\\s+/).filter(Boolean).forEach(cls => {
                                const lower = cls.toLowerCase();
                                if (keywords.some(k => lower.includes(k)) && cls.length < 80) classes.add(cls);
                            });
                        });
                        const idList = Array.from(ids).slice(0, 100).sort();
                        const classList = Array.from(classes).slice(0, 100).sort();
                        return 'ids: ' + idList.join(', ') + '\\nclasses: ' + classList.join(', ');
                    })();
                    """;

                var result = await page.EvaluateAsync<string>(script);
                cancellationToken.ThrowIfCancellationRequested();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> SanitizeSnapshotAsync(string input, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Run on a background thread to prevent starving the web server thread during massive regex parsing
            return await Task.Run(() => 
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var sanitized = IpRegex.Replace(input, "[REDACTED_IP]");
                sanitized = EmailRegex.Replace(sanitized, "[REDACTED_EMAIL]");
                sanitized = PhoneRegex.Replace(sanitized, "[REDACTED_PHONE]");
                sanitized = CcRegex.Replace(sanitized, "[REDACTED_CC]");

                return sanitized;
            }, cancellationToken);
        }
    }
}
