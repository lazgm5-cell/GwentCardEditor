namespace AbilityAnalyzer.Services;

public static class FilePathResolver
{
    public static string ResolveExistingFile(
        string input,
        string? sourceXmlPath = null,
        params string[] preferredSubdirectories)
    {
        string value = input.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Путь к файлу не указан.");

        var candidates = new List<string>();

        void Add(string candidate)
        {
            string full = Path.GetFullPath(candidate);
            if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                candidates.Add(full);
        }

        if (Path.IsPathRooted(value))
            Add(value);
        else
        {
            if (!string.IsNullOrWhiteSpace(sourceXmlPath))
            {
                string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceXmlPath))
                    ?? Environment.CurrentDirectory;
                Add(Path.Combine(sourceDirectory, value));
                foreach (string subdirectory in preferredSubdirectories)
                    Add(Path.Combine(sourceDirectory, subdirectory, value));
            }

            Add(value);
            Add(Path.Combine(Environment.CurrentDirectory, value));
            Add(Path.Combine(AppContext.BaseDirectory, value));

            foreach (string subdirectory in preferredSubdirectories)
            {
                Add(Path.Combine(AppContext.BaseDirectory, subdirectory, value));
                Add(Path.Combine(Environment.CurrentDirectory, subdirectory, value));
            }
        }

        if (!Path.HasExtension(value))
        {
            foreach (string existing in candidates.ToArray())
                Add(existing + ".json");
        }

        string? match = candidates.FirstOrDefault(File.Exists);
        if (match != null)
            return match;

        throw new FileNotFoundException(
            "Файл не найден. Проверены пути:" + Environment.NewLine +
            string.Join(Environment.NewLine, candidates.Select(path => "  " + path)));
    }

    public static string ResolveSiblingXml(string abilitiesPath, string fileName)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(abilitiesPath))
            ?? AppContext.BaseDirectory;
        string candidate = Path.Combine(directory, fileName);
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(candidate))
            return candidate;

        throw new FileNotFoundException($"Файл {fileName} не найден рядом с Abilities.xml или программой.");
    }
}
