namespace AbilityAnalyzer.Models;

public sealed class NumericEditApplyResult
{
    public string OutputDirectory { get; set; } = "";
    public string AbilitiesFile { get; set; } = "";
    public string TemplatesFile { get; set; } = "";
    public string ReportFile { get; set; } = "";
    public string RecipeFile { get; set; } = "";
    public string? BackupDirectory { get; set; }
    public bool AppliedInPlace { get; set; }
}
