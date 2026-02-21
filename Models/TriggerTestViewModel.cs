using System.ComponentModel.DataAnnotations;

namespace UiTestRunner.Models
{
    public class TriggerTestViewModel
    {
        [Required]
        [Url]
        [MaxLength(2048)]
        public string Url { get; set; } = string.Empty;
        
        public bool Headed { get; set; } = false;
        
        [MaxLength(100_000)]
        public string? GherkinScript { get; set; }
    }
}
