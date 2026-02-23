using System.ComponentModel.DataAnnotations;

namespace UiTestRunner.Models;

/// <summary>
/// Request to enqueue multiple scenarios from .feature file(s) for batch run.
/// </summary>
public class BatchRunRequest
{
    [Required]
    [Url]
    [MaxLength(2048)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Single .feature path (relative to Scenarios folder). Use this or FeaturePaths.</summary>
    [MaxLength(512)]
    public string? FeaturePath { get; set; }

    /// <summary>Multiple .feature paths. If set, FeaturePath is ignored.</summary>
    public List<string>? FeaturePaths { get; set; }

    /// <summary>Cap number of scenarios to enqueue (e.g. 20 for a smoke run). Omit to run all.</summary>
    [Range(1, 10_000)]
    public int? MaxScenarios { get; set; }

    public bool Headed { get; set; }

    /// <summary>When true, run all scenarios one by one in a single job (sequential). When false, enqueue one job per scenario (parallel when workers allow).</summary>
    public bool Sequential { get; set; }

    /// <summary>Optional environment key (e.g. ST, SIT). When set, test data CSV for the run is loaded from that environment.</summary>
    [MaxLength(64)]
    public string? Environment { get; set; }

    /// <summary>Optional application name (e.g. VTS, Portal). When set, CSV resolution uses TestData/{Feature}.{Environment}.{ApplicationName}.csv. When empty, config TestData:ApplicationName is used.</summary>
    [MaxLength(64)]
    public string? ApplicationName { get; set; }
}
