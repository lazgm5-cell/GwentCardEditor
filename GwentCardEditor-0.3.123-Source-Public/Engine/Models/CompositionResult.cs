namespace AbilityAnalyzer.Models;

public sealed class CompositionResult
{
    public int TargetCardAbilityId { get; set; }
    public int AddedNodeCount { get; set; }
    public int AddedConnectionCount { get; set; }
    public List<BoundaryIssue> BoundaryIssues { get; } = new();
    public List<string> AppliedPatches { get; } = new();
    public GraphValidationReport Validation { get; set; } = new();
}
