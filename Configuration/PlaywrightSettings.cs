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

        /// <summary>
        /// Maximum length (characters) of the Aria snapshot sent to the AI per step. Larger snapshots are truncated to reduce token usage.
        /// Set to 0 to disable truncation (full snapshot sent). Default 8000; reduce to 5000–6000 to save more tokens (may cause failures).
        /// </summary>
        public int MaxAriaSnapshotLength { get; set; } = 8000;

        /// <summary>
        /// Max snapshot length for verification steps only (Then...). Verification often needs only header/top; use a smaller value to save tokens. 0 = use MaxAriaSnapshotLength.
        /// Default 6000 so header elements (e.g. My Profile dropdown) are usually included; increase to 7000–8000 if "Then" steps still fail.
        /// </summary>
        public int MaxAriaSnapshotLengthForVerify { get; set; } = 6000;
    }
}
