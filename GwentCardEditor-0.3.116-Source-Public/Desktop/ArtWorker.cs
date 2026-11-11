using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GwentCardEditor;

internal static class ArtWorker
{
    public static bool CardbacksAvailable => Available && File.Exists(Path.Combine(AppContext.BaseDirectory, "ArtTools", "cardback_swap.py"));
    public static bool MediaCheckAvailable => CustomAudioAvailable && File.Exists(Path.Combine(AppContext.BaseDirectory, "ArtTools", "media_smoke.py"));
    public static string CustomAudioRuntime => Path.Combine(AppContext.BaseDirectory, "OptionalPlugins", "CustomAudio");
    public static bool Mp3Available => File.Exists(Path.Combine(AppContext.BaseDirectory, "AudioDecode", "GwentAudioDecode.exe"));
    public static bool CustomAudioAvailable => Available && File.Exists(Path.Combine(AppContext.BaseDirectory, "ArtTools", "custom_audio.py"))
        && new[] { "GwentCardEditor.CustomAudio.dll", "NAudio.Core.dll", "NAudio.WinMM.dll", "NAudio-LICENSE.txt" }.All(n => File.Exists(Path.Combine(CustomAudioRuntime, n)));
    public static bool Available => File.Exists(Path.Combine(AppContext.BaseDirectory, "ArtRuntime", "python.exe"))
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "ArtTools", "art_swap.py"));

    public static async Task<JsonDocument> Run(params string[] arguments)
    {
        if (!Available) throw new InvalidOperationException("ArtRuntimeMissing");
        bool mp3 = arguments.FirstOrDefault() is "mp3-normalize" or "mp3-probe";
        if (mp3 && !Mp3Available) throw new InvalidOperationException("CustomMp3Missing");
        string executable = mp3 ? Path.Combine(AppContext.BaseDirectory, "AudioDecode", "GwentAudioDecode.exe")
            : Path.Combine(AppContext.BaseDirectory, "ArtRuntime", "python.exe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        var command = mp3 ? (arguments[0] == "mp3-probe" ? new[] { "--probe" }.Concat(arguments.Skip(1)) : arguments.Skip(1))
            : new[] { "-I", "-X", "utf8", Path.Combine(AppContext.BaseDirectory, "ArtTools", "art_swap.py") }.Concat(arguments);
        foreach (string argument in command)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ArtOperationFailed");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(mp3 ? 1 : 5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new InvalidOperationException(mp3 ? "CustomMp3Failed" : "ArtOperationFailed");
        }
        string stdout = await output, stderr = await errors;
        if (process.ExitCode != 0)
        {
            Directory.CreateDirectory(EditorSettings.Home);
            File.WriteAllText(Path.Combine(EditorSettings.Home, "art-worker.log"), stderr + Environment.NewLine + stdout);
            string? code = new[] { "ArtImageTooLarge", "ArtImageUnsupported", "ArtCropRatio", "ArtTextureLayout", "AudioUnsupported", "AudioSameSource", "CustomAudioRange", "CustomAudioFormat", "CustomAudioClient", "CustomAudioLoader", "CustomAudioConflict", "CustomMp3Codec", "CustomMp3Limit", "CustomMp3Output", "CustomMp3Failed" }
                .FirstOrDefault(stderr.Contains);
            code ??= new[] { "FieryLoader", "FieryConflict", "FieryRuntime", "FieryOutput" }.FirstOrDefault(stderr.Contains);
            code ??= new[] { "CardbackUnsupported", "CardbackGeometry", "CardbackCatalogMismatch", "CardbackCropRatio" }.FirstOrDefault(stderr.Contains);
            code ??= stderr.Contains("MediaTestReport") ? "MediaTestReport"
                : stderr.Contains("VisualFrameAssets") ? "VisualFrameAssets"
                : stderr.Contains("Target art is shared") ? "ArtShared"
                : stderr.Contains("Close Gwent.exe") ? "ArtGameRunning"
                : stderr.Contains("Client changed; refusing overwrite:") ? "ArtFileConflict"
                : stderr.Contains("Client card data differs") ? "ArtDataConflict"
                : stderr.Contains("Client changed") || stderr.Contains("Client card data differs") ? "ArtConflict"
                : stderr.Contains("UnidentifiedImageError") ? "ArtImageUnsupported"
                : stderr.Contains("DecompressionBomb") ? "ArtImageTooLarge"
                : stderr.Contains("Unverified target Unity version") ? "ArtTextureLayout" : null;
            throw new InvalidOperationException(code ?? (mp3 ? "CustomMp3Failed" : "ArtOperationFailed"));
        }
        return JsonDocument.Parse(stdout);
    }
}
