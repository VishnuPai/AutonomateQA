using System;
using System.ComponentModel.DataAnnotations;

namespace UiTestRunner.Models
{
    public class TestCase
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(2048)]
        public string Url { get; set; } = string.Empty;
        
        [MaxLength(100_000)]
        public string? GherkinScript { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
