using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public sealed class AbilityDocument
{
    public XDocument Document { get; }

    private AbilityDocument(XDocument document)
    {
        Document = document;
    }

    public static AbilityDocument Load(string path)
    {
        return new AbilityDocument(XDocument.Load(path, LoadOptions.PreserveWhitespace));
    }

    public AbilityDocument DeepClone()
    {
        return new AbilityDocument(new XDocument(Document));
    }

    public XElement? FindCardAbility(int id)
    {
        return Document.Root?
            .Elements("Ability")
            .FirstOrDefault(element =>
                string.Equals((string?)element.Attribute("Type"), "CardAbility", StringComparison.Ordinal)
                && (int?)element.Attribute("Id") == id);
    }

    public XElement FindRequiredCardAbility(int id)
    {
        return FindCardAbility(id)
            ?? throw new InvalidOperationException($"CardAbility Id={id} не найдена.");
    }

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
