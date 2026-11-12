using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GwentCardEditor;

public sealed record ClientSource(string ArchivePath, string ArchiveHash, Dictionary<string, string> Entries,
    Dictionary<string, string>? FileHashes = null, string? DefaultArchivePath = null)
{
    public const string MetadataFile = "card-editor-source.json";
    private sealed record DefaultArchiveMetadata(int Version, string ClientArchivePath, string ArchiveHash, DateTime CreatedUtc);
    public static string ResolveArchive(string path)
    {
        string full = Path.GetFullPath(path);
        return new[] { full, Path.Combine(full, "data_definitions"),
            Path.Combine(full, "StreamingAssets", "data_definitions"),
            Path.Combine(full, "Gwent_Data", "StreamingAssets", "data_definitions") }
            .FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("ClientNotFound");
    }
    public static EditorProject Open(string path, string cacheRoot)
    {
        if (File.Exists(Path.Combine(path, "Templates.xml"))) return new EditorProject(path);
        string archive = ResolveArchive(path);
        string hash = Hash(archive);
        string defaultArchive = SaveDefaultArchive(archive, hash);
        Directory.CreateDirectory(cacheRoot);
        string key = hash + "_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(archive)))[..8];
        string directory = Path.Combine(Path.GetFullPath(cacheRoot), "Client_" + key);
        if (Directory.Exists(directory))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<ClientSource>(File.ReadAllText(Path.Combine(directory, MetadataFile)));
                if (existing?.ArchiveHash == hash && existing.ArchivePath == archive && existing.FileHashes != null
                    && existing.FileHashes.All(p => File.Exists(Path.Combine(directory, p.Key)) && Hash(Path.Combine(directory, p.Key)) == p.Value)
                    && Directory.GetFiles(directory).Count(p => Path.GetExtension(p) is ".xml" or ".csv") == existing.FileHashes.Count)
                {
                    if (!string.Equals(existing.DefaultArchivePath, defaultArchive, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = existing with { DefaultArchivePath = defaultArchive };
                        File.WriteAllText(Path.Combine(directory, MetadataFile), JsonSerializer.Serialize(existing));
                    }
                    return new EditorProject(directory);
                }
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException) { }
            directory += "_" + Guid.NewGuid().ToString("N");
        }
        Directory.CreateDirectory(directory);
        try
        {
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new InvalidDataException("Duplicate archive entries.");
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    bool xml = !name.Contains('/') && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
                    bool csv = name.StartsWith("Localization/", StringComparison.OrdinalIgnoreCase)
                        && name.Count(c => c == '/') == 1 && name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                    if (!xml && !csv) continue;
                    string flat = Path.GetFileName(name);
                    if (flat is "." or ".." || flat.Contains(':') || !entries.TryAdd(flat, entry.FullName))
                        throw new InvalidDataException("Invalid archive entry.");
                    if (entry.Length > 128 * 1024 * 1024) throw new InvalidDataException("Archive entry too large.");
                    entry.ExtractToFile(Path.Combine(directory, flat));
                }
            }
            if (Hash(archive) != hash) throw new InvalidOperationException("SourceChanged");
            var fileHashes = entries.Keys.ToDictionary(name => name, name => Hash(Path.Combine(directory, name)));
            File.WriteAllText(Path.Combine(directory, MetadataFile), JsonSerializer.Serialize(new ClientSource(archive, hash, entries, fileHashes, defaultArchive)));
            return new EditorProject(directory);
        }
        catch
        {
            // Only this method's newly-created, absolute cache directory is removed.
            if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(cacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
            throw;
        }
    }
    private static string SaveDefaultArchive(string archive, string sourceHash)
    {
        string clientKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(archive).ToUpperInvariant())));
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GwentCardEditor", "DefaultDataDefinitions", clientKey);
        string saved = Path.Combine(directory, "data_definitions");
        string metadataPath = Path.Combine(directory, "default-data-definitions.json");
        Directory.CreateDirectory(directory);

        if (File.Exists(saved) || File.Exists(metadataPath))
        {
            if (!File.Exists(saved) || !File.Exists(metadataPath)) throw new InvalidOperationException("DefaultBaselineInvalid");
            DefaultArchiveMetadata? metadata;
            try { metadata = JsonSerializer.Deserialize<DefaultArchiveMetadata>(File.ReadAllText(metadataPath)); }
            catch (JsonException) { throw new InvalidOperationException("DefaultBaselineInvalid"); }
            if (metadata?.Version != 1 || !string.Equals(metadata.ClientArchivePath, Path.GetFullPath(archive), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(metadata.ArchiveHash, Hash(saved), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DefaultBaselineInvalid");
            return saved;
        }

        string temporaryArchive = saved + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string temporaryMetadata = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool publishedArchive = false;
        try
        {
            File.Copy(archive, temporaryArchive);
            if (!string.Equals(Hash(temporaryArchive), sourceHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Hash(archive), sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SourceChanged");
            File.Move(temporaryArchive, saved);
            publishedArchive = true;
            var metadata = new DefaultArchiveMetadata(1, Path.GetFullPath(archive), sourceHash, DateTime.UtcNow);
            File.WriteAllText(temporaryMetadata, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporaryMetadata, metadataPath);
            return saved;
        }
        catch
        {
            if (publishedArchive && !File.Exists(metadataPath)) File.Delete(saved);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
            if (File.Exists(temporaryMetadata)) File.Delete(temporaryMetadata);
        }
    }
    public void Verify()
    {
        if (!File.Exists(ArchivePath) || Hash(ArchivePath) != ArchiveHash) throw new InvalidOperationException("SourceChanged");
    }
    internal string CurrentArchiveHash() => File.Exists(ArchivePath) ? Hash(ArchivePath) : throw new InvalidOperationException("SourceChanged");
    internal void VerifyExportSource(string baseline, string liveHash)
    {
        if (CurrentArchiveHash() != liveHash || !File.Exists(baseline) || Hash(baseline) != ArchiveHash)
            throw new InvalidOperationException("SourceChanged");
    }
    public void ExportArchive(string directory, IReadOnlySet<string> modifiedFiles, string? outputDirectory = null)
        => ExportArchive(directory, modifiedFiles, outputDirectory, ArchivePath, ArchiveHash);

    internal void ExportArchive(string directory, IReadOnlySet<string> modifiedFiles, string? outputDirectory, string baseline, string liveHash)
    {
        VerifyExportSource(baseline, liveHash);
        string output = Path.Combine(outputDirectory ?? directory, "data_definitions");
        File.Copy(baseline, output);
        if (Hash(output) != ArchiveHash) throw new InvalidOperationException("SourceChanged");
        using (var zip = ZipFile.Open(output, ZipArchiveMode.Update))
        {
            foreach (string flat in modifiedFiles)
            {
                if (!Entries.TryGetValue(flat, out string? entryName)) throw new InvalidDataException("Unmapped source file.");
                var previous = zip.GetEntry(entryName) ?? throw new InvalidDataException("Missing archive entry.");
                DateTimeOffset timestamp = previous.LastWriteTime;
                previous.Delete();
                zip.CreateEntryFromFile(Path.Combine(directory, flat), entryName, CompressionLevel.Optimal).LastWriteTime = timestamp;
            }
        }
        using var original = ZipFile.OpenRead(baseline);
        using var written = ZipFile.OpenRead(output);
        if (original.Entries.Count != written.Entries.Count) throw new InvalidDataException("Archive entry count changed.");
        foreach (var entry in original.Entries)
        {
            var other = written.GetEntry(entry.FullName) ?? throw new InvalidDataException("Archive entry lost.");
            string? flat = Entries.FirstOrDefault(p => p.Value == entry.FullName).Key;
            if (flat != null && modifiedFiles.Contains(flat))
            {
                using var contents = other.Open();
                if (Convert.ToHexString(SHA256.HashData(contents)) != Hash(Path.Combine(directory, flat)))
                    throw new InvalidDataException("Archive replacement verification failed.");
            }
            else
            {
                using var a = entry.Open();
                using var b = other.Open();
                if (!SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b)))
                    throw new InvalidDataException("Unmodified archive entry changed.");
            }
        }
        VerifyExportSource(baseline, liveHash);
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
