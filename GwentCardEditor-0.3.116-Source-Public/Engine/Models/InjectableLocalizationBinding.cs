namespace AbilityAnalyzer.Models;

public sealed class InjectableLocalizationBinding
{
    public string VariableName { get; set; } = "";
    public int InjectableAbilityId { get; set; }
    public string LocalizationKey => $"{InjectableAbilityId}_tooltip_ia";
}
