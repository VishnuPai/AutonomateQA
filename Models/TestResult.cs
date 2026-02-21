using System;
using System.ComponentModel.DataAnnotations;

namespace UiTestRunner.Models
{
    public enum TestStatus
    {
        Pending,
        Running,
        Passed,
        Failed
    }

    public class TestResult
    {
        public int Id { get; set; }
        
        public DateTime RunTime { get; set; } = DateTime.Now;
        
        [Required]
        [MaxLength(2048)]
        public string Url { get; set; } = string.Empty;
        
        [MaxLength(100_000)]
        public string? GherkinScript { get; set; }
        
        public TestStatus Status { get; set; } = TestStatus.Pending;
        
        public TimeSpan? Duration { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public string? ScreenshotPath { get; set; }
        public string? VideoPath { get; set; }
        
        public string? ReasoningLog { get; set; }

        public string? HangfireJobId { get; set; }

        /// <summary>Optional batch run identifier for progress tracking (set when enqueued via BatchRun).</summary>
        [MaxLength(64)]
        public string? BatchRunId { get; set; }

        /// <summary>Feature file path for batch runs (e.g. "Login.feature" or "Auth/Login.feature").</summary>
        [MaxLength(512)]
        public string? FeaturePath { get; set; }

        /// <summary>Scenario name from the feature file (e.g. "Login with valid credentials").</summary>
        [MaxLength(512)]
        public string? ScenarioName { get; set; }

        /// <summary>AI prompt tokens used for this run (OpenAI/Gemini).</summary>
        public int? PromptTokens { get; set; }

        /// <summary>AI completion tokens used for this run.</summary>
        public int? CompletionTokens { get; set; }

        /// <summary>Total tokens (prompt + completion) for this run.</summary>
        public int? TotalTokens { get; set; }
    }
}
