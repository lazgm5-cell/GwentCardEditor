namespace AbilityAnalyzer.Models;

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
