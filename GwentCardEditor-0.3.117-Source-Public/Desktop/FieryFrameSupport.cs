namespace GwentCardEditor;

internal static class FieryFrameSupport
{
    public static string Runtime => Path.Combine(AppContext.BaseDirectory, "OptionalPlugins", "FieryFiend");
    public static bool Available => ArtWorker.Available && File.Exists(Path.Combine(AppContext.BaseDirectory, "ArtTools", "fiery_frames.py"))
        && File.Exists(Path.Combine(Runtime, "known-runtimes.json")) && File.Exists(Path.Combine(Runtime, "GwentCardEditor.FieryFiend.Experimental.dll"));
    private static string? Root(string assets) => Path.GetFileName(assets).Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase)
        && Directory.GetParent(assets)?.Name.Equals("Gwent_Data", StringComparison.OrdinalIgnoreCase) == true ? Directory.GetParent(assets)?.Parent?.FullName : null;
    public static bool Supported(string assets) => Root(assets) is string root && Available && File.Exists(Path.Combine(root, "Gwent.exe"))
        && File.Exists(Path.Combine(root, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")) && File.Exists(Path.Combine(root, "BepInEx", "interop", "Assembly-CSharp.dll"));
    public static bool Installed(string assets) => Root(assets) is string root && File.Exists(Path.Combine(root, "BepInEx", "plugins", "GwentCardEditor.FieryFiend.Experimental", "GwentCardEditor.FieryFiend.Experimental.dll"));
}
