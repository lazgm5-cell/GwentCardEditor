namespace AbilityAnalyzer.Models;

public sealed class TemplateModPackRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string OutputFileName { get; set; } = "Templates.modpack.xml";
    public List<TemplateOperationRecipe> Operations { get; set; } = new();
}
