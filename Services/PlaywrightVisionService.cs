using Microsoft.Playwright;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UiTestRunner.Services
{
    public class PlaywrightVisionService : IPlaywrightVisionService
    {
        // Static compiled regexes for better performance
        private static readonly Regex IpRegex = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex EmailRegex = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex PhoneRegex = new Regex(@"\b(?:\+?(\d{1,3}))?[-. (]*(\d{3})[-. )]*(\d{3})[-. ]*(\d{4})\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex CcRegex = new Regex(@"\b(?:\d{4}[- ]?){3}\d{4}\b", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        public async Task<string> GetCleanSnapshotAsync(IPage page, CancellationToken cancellationToken = default)
        {
            // Capture the Aria snapshot of the body
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            cancellationToken.ThrowIfCancellationRequested();

            snapshot = await SanitizeSnapshotAsync(snapshot, cancellationToken);

            // Enrich with DOM identifiers (id, class) so verification can match elements
            // that have no accessible name (e.g. <div id="mini-cart" class="mini-cart">)
            var domHints = await GetDomIdentifierHintsAsync(page, cancellationToken);
            if (!string.IsNullOrEmpty(domHints))
            {
                snapshot += "\n\n[DOM identifiers present on page – use for verification when Aria has no name]\n" + domHints;
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
