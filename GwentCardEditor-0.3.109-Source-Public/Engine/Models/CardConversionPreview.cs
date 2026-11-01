namespace AbilityAnalyzer.Models;

public sealed class CardConversionPreview
{
    public required CardRecord Card { get; init; }
    public required string SourceType { get; init; }
    public required string TargetType { get; init; }
    public required bool ZealEnabled { get; init; }
    public required string TooltipKey { get; init; }
    public required string TooltipValue { get; init; }
    public required AbilityAnalyzer.Services.AbilityDocument Abilities { get; init; }
    public required AbilityAnalyzer.Services.TemplateDocument Templates { get; init; }
    public required GraphValidationReport GraphValidation { get; init; }
    public List<string> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
}
