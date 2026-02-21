namespace UiTestRunner.Configuration
{
    /// <summary>
    /// Configuration settings for Playwright browser automation.
    /// </summary>
    public class PlaywrightSettings
    {
        public const string SectionName = "Playwright";

        /// <summary>
        /// Timeout in milliseconds for page navigation.
        /// </summary>
        public int NavigationTimeoutMs { get; set; } = 60000;

        /// <summary>
        /// Timeout in milliseconds for element interactions (click, fill, etc.).
        /// </summary>
        public int InteractionTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Timeout in milliseconds for scrolling elements into view.
        /// </summary>
        public int ScrollTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Video recording width in pixels.
        /// </summary>
        public int VideoWidth { get; set; } = 1280;

        /// <summary>
        /// Video recording height in pixels.
        /// </summary>
        public int VideoHeight { get; set; } = 720;

        /// <summary>
        /// Viewport width in pixels for recorder.
        /// </summary>
        public int ViewportWidth { get; set; } = 1280;

        /// <summary>
        /// Viewport height in pixels for recorder.
        /// </summary>
        public int ViewportHeight { get; set; } = 720;

        /// <summary>
        /// Slow motion delay in milliseconds when running in headed mode.
        /// </summary>
        public int SlowMoMs { get; set; } = 1000;

        /// <summary>
        /// Delay in milliseconds to wait after each action (click, fill, etc.) so the page has time to update
        /// (menus open, SPAs render, navigation completes). Prevents next step from snapshotting stale DOM.
        /// </summary>
        public int PostActionDelayMs { get; set; } = 1500;

        /// <summary>
        /// After a click, wait up to this many milliseconds for load state (e.g. navigation).
        /// Set to 0 to skip waiting for load state.
        /// </summary>
        public int WaitForLoadStateAfterClickMs { get; set; } = 5000;
    }
}
