using System.Diagnostics;
using System.Security.Cryptography;

namespace GwentCardEditor;

internal static class DeckVisibilityInstaller
{
    private const string Name = "GwentCardEditor.DeckVisibility.dll";
    private static string Source => Path.Combine(AppContext.BaseDirectory, "OptionalPlugins", Name);
    private static string? Root(string assets) => Path.GetFileName(assets).Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase)
        && Directory.GetParent(assets)?.Name.Equals("Gwent_Data", StringComparison.OrdinalIgnoreCase) == true
        ? Directory.GetParent(assets)?.Parent?.FullName : null;
    private static string Destination(string assets) => Path.Combine(Root(assets) ?? throw new InvalidOperationException("VisualPluginUnsupported"), "BepInEx", "plugins", "GwentCardEditor.DeckVisibility", Name);
    public static bool Installed(string assets) => Root(assets) != null && File.Exists(Destination(assets));
    public static bool Supported(string assets) => Root(assets) is string root && File.Exists(Source) && File.Exists(Path.Combine(root, "Gwent.exe"))
        && File.Exists(Path.Combine(root, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"))
        && File.Exists(Path.Combine(root, "BepInEx", "interop", "Assembly-CSharp.dll"));
    private static byte[] Hash(string path) { using var file = File.OpenRead(path); return SHA256.HashData(file); }
    public static void SetHidden(string assets, bool hidden)
    {
        if (!Supported(assets)) throw new InvalidOperationException("VisualPluginUnsupported");
        var processes = Process.GetProcessesByName("Gwent");
        try { if (processes.Length != 0) throw new InvalidOperationException("ArtGameRunning"); }
        finally { foreach (var process in processes) process.Dispose(); }
        string destination = Destination(assets);
        if (File.Exists(destination))
        {
            if (!Hash(destination).SequenceEqual(Hash(Source))) throw new InvalidOperationException("ArtConflict");
            if (!hidden) File.Delete(destination);
            return;
        }
        if (!hidden) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(Source, temporary);
            if (!Hash(temporary).SequenceEqual(Hash(Source))) throw new IOException("Plugin checksum mismatch");
            File.Move(temporary, destination);
        }
        finally { File.Delete(temporary); }
    }
}
