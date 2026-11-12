using AbilityAnalyzer.Models;

namespace AbilityAnalyzer.Services;

public sealed class UnifiedModPackResult
{
    public AbilityModPackResult? AbilityResult { get; set; }
    public TemplateModPackResult? TemplateResult { get; set; }
}

public static class UnifiedModPackService
{
    public static UnifiedModPackResult Apply(
        string abilitiesPath,
        string templatesPath,
        string unifiedRecipePath,
        bool overwrite)
    {
        UnifiedModPackRecipe recipe = ModPackRecipeService.LoadUnifiedRecipe(unifiedRecipePath);
        string recipeDirectory = Path.GetDirectoryName(Path.GetFullPath(unifiedRecipePath))
            ?? AppContext.BaseDirectory;
        var result = new UnifiedModPackResult();

        if (!string.IsNullOrWhiteSpace(recipe.AbilityModPackFile))
        {
            string abilityRecipe = FilePathResolver.ResolveExistingFile(
                Path.Combine(recipeDirectory, recipe.AbilityModPackFile),
                abilitiesPath,
                "modpacks");
            result.AbilityResult = AbilityModPackService.ApplyAndSave(
                abilitiesPath,
                abilityRecipe,
                outputOverride: null,
                overwrite);
        }

        if (!string.IsNullOrWhiteSpace(recipe.TemplateModPackFile))
        {
            string templateRecipe = FilePathResolver.ResolveExistingFile(
                Path.Combine(recipeDirectory, recipe.TemplateModPackFile),
                templatesPath,
                "template-modpacks");
            TemplateModPackRecipe templatePack = ModPackRecipeService.LoadTemplateRecipe(templateRecipe);
            result.TemplateResult = TemplateModPackService.ApplyAndSave(
                templatesPath,
                templatePack,
                outputOverride: null,
                overwrite);
        }

        return result;
    }
}
