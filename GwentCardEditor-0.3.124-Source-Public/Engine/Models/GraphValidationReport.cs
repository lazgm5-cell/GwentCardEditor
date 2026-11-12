namespace AbilityAnalyzer.Models;

public sealed class GraphValidationReport
{
    public List<ValidationIssue> Issues { get; } = new();
    public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
    public int ErrorCount => Issues.Count(issue => issue.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
}
