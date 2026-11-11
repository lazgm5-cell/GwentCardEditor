using AbilityAnalyzer.Models;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public sealed class TemplateDocument
{
    public XDocument Document { get; }
    public IReadOnlyDictionary<int, string> RussianCardNames { get; }

    private TemplateDocument(
        XDocument document,
        IReadOnlyDictionary<int, string>? russianCardNames = null)
    {
        Document = document;
        RussianCardNames = russianCardNames ?? new Dictionary<int, string>();
    }

    public static TemplateDocument Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        string besideTemplates = Path.Combine(directory, "ru-ru.csv");
        string besideExecutable = Path.Combine(AppContext.BaseDirectory, "ru-ru.csv");
        string? localizationPath = File.Exists(besideTemplates)
            ? besideTemplates
            : File.Exists(besideExecutable) ? besideExecutable : null;

        IReadOnlyDictionary<int, string> names = localizationPath == null
            ? new Dictionary<int, string>()
            : LocalizationService.ReadCardNames(localizationPath);

        return new TemplateDocument(
            XDocument.Load(fullPath, LoadOptions.PreserveWhitespace),
            names);
    }

    public TemplateDocument DeepClone() => new(
        new XDocument(Document),
        RussianCardNames.ToDictionary(pair => pair.Key, pair => pair.Value));

    public IEnumerable<XElement> CardTemplates =>
        Document.Root?.Elements("Template")
            .Where(element => string.Equals((string?)element.Attribute("Type"), "CardTemplate", StringComparison.Ordinal))
        ?? Enumerable.Empty<XElement>();

    public XElement? FindById(int id) =>
        CardTemplates.FirstOrDefault(element => (int?)element.Attribute("Id") == id);

    public IReadOnlyList<CardRecord> Cards => CardTemplates
        .Select(element =>
        {
            int id = (int?)element.Attribute("Id") ?? 0;
            return new CardRecord
            {
                Id = id,
                DebugName = (string?)element.Attribute("DebugName") ?? "",
                RussianName = RussianCardNames.TryGetValue(id, out string? russianName)
                    ? russianName
                    : ""
            };
        })
        .Where(card => card.Id > 0)
        .ToList();

    public void SaveAs(string path, bool overwrite)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) && !overwrite)
            throw new IOException($"Выходной файл уже существует: {fullPath}");

        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Не удалось определить выходной каталог.");
        Directory.CreateDirectory(directory);

        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Document.Save(temporary, SaveOptions.DisableFormatting);
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
