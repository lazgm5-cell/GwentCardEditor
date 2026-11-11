namespace AbilityAnalyzer.Models;

public sealed class BoundaryIssue
{
    public string Fragment { get; set; } = "";
    public int SourceEndpointId { get; set; }
    public int TargetEndpointId { get; set; }
    public string TargetField { get; set; } = "";
    public bool Resolved { get; set; }
    public string Resolution { get; set; } = "";
}
