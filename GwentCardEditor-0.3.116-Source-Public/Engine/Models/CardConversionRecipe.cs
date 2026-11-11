namespace AbilityAnalyzer.Models;

public sealed class CardConversionRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Card { get; set; } = "";
    public string TargetType { get; set; } = "artifact";
    public string ConversionMode { get; set; } = "safe";
    public string Activation { get; set; } = "order";
    public int Charges { get; set; } = 1;
    public string Zeal { get; set; } = "auto";
    public bool VisibleCharges { get; set; }
    public bool PreserveExistingEffect { get; set; } = true;
    public string ReplaceTrigger { get; set; } = "deploy";
    public bool UpdateTooltip { get; set; } = true;
    public string Tooltip { get; set; } = "";
    public string OutputDirectoryName { get; set; } = "CardConversion.output";
}
