using System;
using System.Linq;
using System.Net;

namespace UiTestRunner.Helpers
{
    public static class UrlValidator
    {
        public static bool IsSafeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            // Only allow HTTP and HTTPS
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            // Block explicit localhost strings
            var blockedHosts = new[] { "localhost", "127.0.0.1", "::1", "0.0.0.0" };
            if (blockedHosts.Contains(uri.Host.ToLowerInvariant()))
            {
                return false;
            }

            // IP Validation
            if (IPAddress.TryParse(uri.Host, out var ip))
            {
                var bytes = ip.GetAddressBytes();

                // Block loopback (127.x.x.x)
                if (IPAddress.IsLoopback(ip) || (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && bytes[0] == 127))
                {
                    return false;
                }

                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // Block AWS / Cloud Metadata IP (169.254.169.254)
                    if (bytes[0] == 169 && bytes[1] == 254) return false;

                    // Block Private Subnets (RFC 1918) - 10.x.x.x, 172.16.x.x-172.31.x.x, 192.168.x.x
                    if (bytes[0] == 10) return false;
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                    if (bytes[0] == 192 && bytes[1] == 168) return false;
                }
            }

            return true;
        }
    }
}
