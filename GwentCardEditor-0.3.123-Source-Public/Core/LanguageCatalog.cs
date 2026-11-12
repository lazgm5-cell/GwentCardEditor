namespace GwentCardEditor;

public static class LanguageCatalog
{
    private static readonly string[] Names = ["Русский", "English", "简体中文", "Polski", "Deutsch", "Português (BR)", "Français", "日本語", "Italiano", "Español", "Español (MX)", "繁體中文", "한국어"];
    public static string Name(string code)
    {
        int index = Array.IndexOf(EditorProject.Languages, code);
        return index >= 0 ? Names[index] : code;
    }
}
