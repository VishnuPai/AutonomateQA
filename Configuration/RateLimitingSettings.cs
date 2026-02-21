namespace UiTestRunner.Configuration
{
    /// <summary>
    /// Configuration settings for rate limiting.
    /// </summary>
    public class RateLimitingSettings
    {
        public const string SectionName = "RateLimiting";

        /// <summary>
        /// Rate limit window in minutes.
        /// </summary>
        public int WindowMinutes { get; set; } = 1;

        /// <summary>
        /// Maximum number of requests allowed per window.
        /// </summary>
        public int PermitLimit { get; set; } = 5;

        /// <summary>
        /// Queue limit for rate limiting (0 = no queuing).
        /// </summary>
        public int QueueLimit { get; set; } = 0;
    }
}
