namespace UiTestRunner.Configuration
{
    /// <summary>
    /// Application-level settings for AutonomateQA (default URL, scenario paths).
    /// Override via appsettings.json or environment variables (e.g. Runner__BaseUrl).
    /// </summary>
    public class RunnerSettings
    {
        public const string SectionName = "Runner";

        /// <summary>
        /// Default target URL shown in the runner UI (e.g. your app's login page).
        /// Leave empty for a generic placeholder. Can be overridden per run in the UI.
        /// </summary>
        public string BaseUrl { get; set; } = "";

        /// <summary>
        /// Optional folder path (relative to app content root) where recorded .feature files are saved.
        /// If set, recordings are saved under this folder; otherwise the app directory is used.
        /// Example: "Scenarios"
        /// </summary>
        public string ScenariosPath { get; set; } = "";

        /// <summary>
        /// Maximum number of scenarios to enqueue in a single batch run request (default 100).
        /// Use multiple batch requests or run by feature file to run 900+ scenarios.
        /// </summary>
        public int BatchRunMaxPerRequest { get; set; } = 100;
    }
}
