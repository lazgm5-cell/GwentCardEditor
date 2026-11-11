namespace AbilityAnalyzer.Models;

/// <summary>
/// Изменяет поле внутри конкретного импортированного фрагмента до перенумерации Id.
/// Благодаря этому рецепт не зависит от новых Id в итоговом графе.
/// </summary>
public sealed class FragmentPatchRecipe
{
    public string Fragment { get; set; } = "";
    public int SourceNodeId { get; set; }
    public string FieldName { get; set; } = "";
    public string AttributeName { get; set; } = "V";
    public string Value { get; set; } = "";
}
