namespace AbilityAnalyzer.Models;

public sealed class AbilitySessionItem
{
    public string SourceFile { get; set; } = "";
    public string Kind { get; set; } = "ability";
    public string DisplayName { get; set; } = "";
    public ModPackRecipe Recipe { get; set; } = new();
}
