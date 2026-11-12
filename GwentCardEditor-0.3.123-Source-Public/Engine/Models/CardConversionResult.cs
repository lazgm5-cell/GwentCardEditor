namespace AbilityAnalyzer.Models;

public sealed class CardConversionResult
{
    public required string OutputDirectory { get; init; }
    public required string AbilitiesFile { get; init; }
    public required string TemplatesFile { get; init; }
    public required string LocalizationFile { get; init; }
    public required string ReportFile { get; init; }
    public required CardConversionPreview Preview { get; init; }
}
