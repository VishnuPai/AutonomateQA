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

        /// <summary>Optional environment key (e.g. ST, SIT). When set, test data CSV for this run is loaded from that environment.</summary>
        [MaxLength(64)]
        public string? Environment { get; set; }

        /// <summary>Optional application name (e.g. VTS, Portal). When set, CSV resolution uses TestData/{Feature}.{Environment}.{ApplicationName}.csv. When empty, config TestData:ApplicationName is used.</summary>
        [MaxLength(64)]
        public string? ApplicationName { get; set; }
    }
}
