using AbilityAnalyzer.Models;
using System.Text.Json;

namespace AbilityAnalyzer.Services;

public static class ModPackRecipeService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static ModPackRecipe LoadAbilityRecipe(string path)
    {
        string json = File.ReadAllText(path);
        ModPackRecipe? recipe = JsonSerializer.Deserialize<ModPackRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать Ability mod-pack.");
    }

    public static TemplateModPackRecipe LoadTemplateRecipe(string path)
    {
        string json = File.ReadAllText(path);
        TemplateModPackRecipe? recipe = JsonSerializer.Deserialize<TemplateModPackRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать Template mod-pack.");
    }


    public static SemanticAbilityRecipe LoadSemanticAbilityRecipe(string path)
    {
        string json = File.ReadAllText(path);
        SemanticAbilityRecipe? recipe = JsonSerializer.Deserialize<SemanticAbilityRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать semantic Ability recipe.");
    }

    public static void SaveSemanticAbilityRecipe(string path, SemanticAbilityRecipe recipe)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(recipe, Options));
    }


    public static CardConversionRecipe LoadCardConversionRecipe(string path)
    {
        string json = File.ReadAllText(path);
        CardConversionRecipe? recipe = JsonSerializer.Deserialize<CardConversionRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать card conversion recipe.");
    }

    public static void SaveCardConversionRecipe(string path, CardConversionRecipe recipe)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(recipe, Options));
    }


    public static OrderMechanicsRecipe LoadOrderMechanicsRecipe(string path)
    {
        string json = File.ReadAllText(path);
        OrderMechanicsRecipe? recipe = JsonSerializer.Deserialize<OrderMechanicsRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать order mechanics recipe.");
    }

    public static SemanticCardPackRecipe LoadSemanticCardPackRecipe(string path)
    {
        string json = File.ReadAllText(path);
        SemanticCardPackRecipe? recipe = JsonSerializer.Deserialize<SemanticCardPackRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать semantic card pack recipe.");
    }

    public static ExternalCardImportRecipe LoadExternalCardImportRecipe(string path)
    {
        string json = File.ReadAllText(path);
        ExternalCardImportRecipe? recipe = JsonSerializer.Deserialize<ExternalCardImportRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать рецепт импорта внешних карт.");
    }

    public static UnifiedModPackRecipe LoadUnifiedRecipe(string path)
    {
        string json = File.ReadAllText(path);
        UnifiedModPackRecipe? recipe = JsonSerializer.Deserialize<UnifiedModPackRecipe>(json, Options);
        return recipe ?? throw new InvalidDataException("Не удалось прочитать unified mod-pack.");
    }
}
