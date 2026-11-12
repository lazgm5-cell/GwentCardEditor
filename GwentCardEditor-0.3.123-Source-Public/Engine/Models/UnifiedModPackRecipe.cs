namespace AbilityAnalyzer.Models;

public sealed class UnifiedModPackRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string AbilityModPackFile { get; set; } = "";
    public string TemplateModPackFile { get; set; } = "";
}
