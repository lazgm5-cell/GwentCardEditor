namespace AbilityAnalyzer.Models;

public sealed class TemplateOperationRecipe
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<string> Cards { get; set; } = new();
    public string Status { get; set; } = "";
    public long? Mask { get; set; }
    public int? Rarity { get; set; }
    public string RarityName { get; set; } = "";
    public int? Availability { get; set; }
    public string CardSet { get; set; } = "";

    public string CategoryField { get; set; } = "Categories";
    public string Category { get; set; } = "";
    public int? CategorySlot { get; set; }
    public ulong? CategoryMask { get; set; }
    public int? InitialTapCharges { get; set; }
    public bool? Zeal { get; set; }
    public bool? VisibleTapCharges { get; set; }
    public string CardType { get; set; } = "";
    public int? CardTypeValue { get; set; }
}
