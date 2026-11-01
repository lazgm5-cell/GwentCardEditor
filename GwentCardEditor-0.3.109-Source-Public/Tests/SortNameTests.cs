using System.Globalization;
using System.Security.Cryptography;
using GwentCardEditor;

internal static class SortNameTests
{
    public static IReadOnlyList<string> Run(string source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        foreach (var (left, right) in new[] { ("\"", "\""), ("'", "'"), ("«", "»"), ("“", "”"), ("„", "“"), ("‘", "’"), ("「", "」"), ("『", "』") })
            Check(CardCatalog.NameSortKey(" " + left + " Рыхлый Трюфель " + right + " ") == "Рыхлый Трюфель", "sort strips external quotes " + left + right);
        Check(CardCatalog.NameSortKey("D'ao") == "D'ao", "sort preserves internal apostrophes");
        Check(CardCatalog.NameSortKey("\"' \"") == "", "quote-only sort key is safe");
        string directory = Path.Combine(scratch, "sort-names-source");
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        string locale = Path.Combine(directory, "ru-ru.csv");
        File.WriteAllText(locale, "11_name;Редька\r\n12_name;Рыба\r\n13_name;«Рыхлый Трюфель»\r\n14_name;Сабля\r\n15_name;Рябина\r\n");
        byte[] original = SHA256.HashData(File.ReadAllBytes(locale));
        var project = new EditorProject(directory);
        var up = CardCatalog.Query(project, 1, "ru-ru", group: CardGroup.All, showHidden: true, sort: CardSort.Name).Where(c => !c.IsToken);
        var down = CardCatalog.Query(project, 1, "ru-ru", group: CardGroup.All, showHidden: true, sort: CardSort.Name, descending: true).Where(c => !c.IsToken);
        Check(up.Select(c => c.Id).SequenceEqual(new[] { 11, 12, 13, 15, 14 }), "quoted truffle sorts between Russian neighbours");
        Check(down.Select(c => c.Id).SequenceEqual(new[] { 14, 15, 13, 12, 11 }), "quoted truffle reverse sorting");
        Check(project.Name(13, "ru-ru") == "«Рыхлый Трюфель»", "display name preserves quotes");
        Check(CardCatalog.Query(project, 1, "ru-ru", "Рыхлый", showHidden: true).Single().Id == 13, "search still finds quoted name");
        Check(!project.HasChanges && original.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(locale))), "sorting does not edit localization");
        return passed;
    }
}
