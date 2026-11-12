using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GwentCardEditor;

internal static class PackageStorage
{
    public static string? BackupRoot { get; set; }
    public static string ProgramRoot => FindProgramRoot(AppContext.BaseDirectory);
    public static string FindProgramRoot(string applicationDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(applicationDirectory));
        return Regex.IsMatch(directory.Name, @"^publish-\d+\.\d+\.\d+$") && directory.Parent is { } parent && File.Exists(Path.Combine(parent.FullName, "CardEditor.cmd"))
            ? parent.FullName : directory.FullName;
    }

    private static string ClientRoot(string source)
    {
        string path = Path.GetFullPath(source);
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        var directory = new DirectoryInfo(path);
        return directory.Name.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase)
            && directory.Parent?.Name.Equals("Gwent_Data", StringComparison.OrdinalIgnoreCase) == true
            ? directory.Parent.Parent!.FullName : directory.FullName;
    }

    internal static string Safe(string value)
    {
        string result = Regex.Replace(value.Normalize(NormalizationForm.FormC), "[<>:\"/\\\\|?*\\x00-\\x1f]", "_");
        result = Regex.Replace(result, @"\s+", "_").Trim(' ', '.', '_');
        return string.IsNullOrEmpty(result) ? "Package" : result;
    }
    private static string Cut(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].TrimEnd(' ', '.', '_', '-');
    }
    internal static string FolderFor(string programRoot, string source)
    {
        return FolderForRoot(Path.Combine(Path.GetFullPath(programRoot), "Backups"), source);
    }
    private static string FolderForRoot(string root, string source)
    {
        root = Path.GetFullPath(root);
        string client = ClientRoot(source);
        string drive = Safe(Path.GetPathRoot(client)!.TrimEnd(Path.DirectorySeparatorChar, ':'));
        string label = Safe(drive + "_" + new DirectoryInfo(client).Name);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(client.ToUpperInvariant())))[..4];
        int budget = Math.Min(48, 122 - root.Length - 1);
        if (budget < 12) throw new InvalidOperationException("BackupPathLong");
        return Path.Combine(root, Cut(label, budget - 5) + "-" + hash);
    }
    internal static string NewPath(string directory, string kind, string? detail, int reservedTail = 104, DateTime? time = null)
    {
        string folder = Path.GetFullPath(directory), stamp = (time ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        int budget = Math.Min(100, 258 - folder.Length - 2 - reservedTail);
        if (budget < stamp.Length + 10) throw new InvalidOperationException("BackupPathLong");
        string label = Safe(kind + (string.IsNullOrWhiteSpace(detail) ? "" : "-" + detail));
        for (int index = 0; index < 10000; index++)
        {
            string suffix = index == 0 ? "" : "-" + (index + 1).ToString("00");
            string leaf = stamp + "__" + Cut(label, budget - stamp.Length - 2 - suffix.Length) + suffix;
            string path = Path.Combine(folder, leaf);
            if (!Directory.Exists(path) && !File.Exists(path)) return path;
        }
        throw new IOException("BackupNameCollision");
    }
    public static string? Prepare(IWin32Window owner, string source, bool writable = true)
    {
        try
        {
            string folder = BackupRoot is { Length: > 0 } configured ? FolderForRoot(configured, source) : FolderFor(ProgramRoot, source);
            EnsureFolder(folder, writable);
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(owner, UiText.Get("BackupLocationError"), UiText.Get("Export"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            string initial = BackupRoot is { Length: > 0 } configured ? configured : ProgramRoot;
            using var chooser = new FolderBrowserDialog { Description = UiText.Get("BackupChooseFolder"), UseDescriptionForTitle = true, InitialDirectory = initial };
            if (chooser.ShowDialog(owner) != DialogResult.OK) return null;
            try
            {
                string selectedRoot = Path.GetFullPath(chooser.SelectedPath);
                string folder = FolderForRoot(selectedRoot, source);
                EnsureFolder(folder, writable);
                BackupRoot = selectedRoot;
                var settings = EditorSettings.Load();
                settings.BackupRoot = selectedRoot;
                settings.Save();
                return folder;
            }
            catch (Exception retry) when (retry is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                MessageBox.Show(owner, UiText.Get(retry.Message == "BackupPathLong" ? "BackupPathLong" : "BackupLocationError"), UiText.Get("Export"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }
    }
    private static void EnsureFolder(string folder, bool writable)
    {
        Directory.CreateDirectory(folder);
        if (!writable) return;
        using var probe = new FileStream(Path.Combine(folder, ".write-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
    public static SaveFileDialog? SaveDialog(IWin32Window owner, string client, string kind, string? detail = null, int reservedTail = 104)
    {
        string? folder = Prepare(owner, client);
        if (folder == null) return null;
        try { return new SaveFileDialog { Title = UiText.Get("ArtExport"), InitialDirectory = folder,
            FileName = NewPath(folder, kind, detail, reservedTail), AddExtension = false, CheckFileExists = false, OverwritePrompt = false, RestoreDirectory = true }; }
        catch (InvalidOperationException e) when (e.Message == "BackupPathLong")
        { MessageBox.Show(owner, UiText.Get("BackupPathLong")); return null; }
    }
    public static OpenFileDialog? RestoreDialog(IWin32Window owner, string client, string title)
    {
        string? folder = Prepare(owner, client, false);
        return folder == null ? null : new OpenFileDialog { Title = title, Filter = "manifest.json|manifest.json", InitialDirectory = folder,
            FileName = Path.Combine(folder, "manifest.json"), RestoreDirectory = true };
    }
    public static void Describe(string package, string source, string kind, string? detail = null)
    {
        try
        {
            string file = Path.Combine(package, "PackageInfo.txt");
            using var writer = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false));
            writer.WriteLine(kind); writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            writer.WriteLine(UiText.Get("BackupClient") + " " + ClientRoot(source));
            if (!string.IsNullOrWhiteSpace(detail)) writer.WriteLine(detail);
            writer.WriteLine(UiText.Get("BackupRestoreHint"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Optional description never invalidates a verified package. */ }
    }
}
