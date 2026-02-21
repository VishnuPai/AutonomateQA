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
    }
}
