using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using GwentGameplay.Audio;
using GwentUnity;
using GwentUnity.Audio;
using GwentVisuals;
using HarmonyLib;
using Il2CppInterop.Runtime;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;

namespace GwentCardEditor.CustomAudio;

[BepInPlugin(Id, "GWENT Card Editor - Custom Premium Audio", "0.1.0")]
public sealed class CustomAudioPlugin : BasePlugin
{
    public const string Id = "local.gwent.cardeditor.custom_premium_audio";
    public sealed class Clip { public string Sha256 { get; set; } = ""; public float Gain { get; set; } = 1; public bool Loop { get; set; } }
    public sealed class Configuration { public int Version { get; set; } public Dictionary<int, Clip> Cards { get; set; } = new(); }
    private static CustomAudioPlugin? instance;
    private static string folder = "";
    private static Dictionary<int, Clip> clips = new();
    private static readonly Dictionary<int, Playback> playing = new();

    public override void Load()
    {
        instance = this; folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var harmony = new Harmony(Id);
        try
        {
            string path = Path.Combine(folder, "config.json");
            if (!File.Exists(path)) { Log.LogInfo("No custom audio configuration; no hooks installed."); return; }
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Configuration too large");
            var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing configuration");
            if (config.Version != 1 || config.Cards.Count > 2000) throw new InvalidDataException("Unsupported configuration");
            foreach (var (id, clip) in config.Cards)
            {
                if (id <= 0 || !float.IsFinite(clip.Gain) || clip.Gain < 0 || clip.Gain > 1) throw new InvalidDataException("Invalid clip settings");
                string file = ClipPath(id);
                if (!File.Exists(file) || new FileInfo(file).Length > 50 * 1024 * 1024) throw new InvalidDataException("Missing or oversized clip");
                using var input = File.OpenRead(file);
                using var sha = SHA256.Create();
                if (!Convert.ToHexString(sha.ComputeHash(input)).Equals(clip.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Clip checksum mismatch");
            }
            clips = config.Cards;
            if (clips.Count == 0) return;
            var request = Required(typeof(CardSfxManager), nameof(CardSfxManager.HandleOnRequestAudio));
            var uninitialize = Required(typeof(CardViewSoundHandler), nameof(CardViewSoundHandler.Uninitialize));
            var stop = Required(typeof(CardViewSfxHandler), nameof(CardViewSfxHandler.StopPremiumPreview));
            var update = Required(typeof(GwentApp), nameof(GwentApp.Update));
            var quit = Required(typeof(SoundManager), nameof(SoundManager.OnApplicationQuit));
            harmony.Patch(request, prefix: new HarmonyMethod(typeof(CustomAudioPlugin), nameof(BeforeRequest)) { priority = Priority.First });
            harmony.Patch(uninitialize, prefix: Hook(nameof(BeforeUninitialize)));
            harmony.Patch(stop, prefix: Hook(nameof(BeforeUninitialize)));
            harmony.Patch(update, postfix: Hook(nameof(Tick)));
            harmony.Patch(quit, prefix: Hook(nameof(Shutdown)));
            Log.LogInfo($"Custom premium audio enabled for {clips.Count} cards. No native banks or settings are modified.");
        }
        catch (Exception e) { harmony.UnpatchSelf(); Shutdown(); clips.Clear(); Log.LogError("Custom audio disabled: " + e); }
    }

    private static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    private static HarmonyMethod Hook(string name) => new(typeof(CustomAudioPlugin), name);
    private static string ClipPath(int id) => Path.Combine(folder, "clips", id + ".wav");

    private sealed class LoopStream : WaveStream
    {
        private readonly WaveFileReader source;
        private readonly bool loop;
        public LoopStream(WaveFileReader source, bool loop) { this.source = source; this.loop = loop; }
        public override WaveFormat WaveFormat => source.WaveFormat;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = source.Read(buffer, offset + total, count - total); total += read;
                if (read > 0) continue;
                if (!loop || source.Length == 0) break;
                source.Position = 0;
            }
            return total;
        }
    }
    private sealed class Playback : IDisposable
    {
        internal readonly WaveOutEvent Output = new() { DesiredLatency = 120, NumberOfBuffers = 3 };
        internal readonly WaveFileReader Reader;
        internal readonly VolumeSampleProvider Gain;
        internal readonly CardViewSoundHandler Owner;
        internal readonly CardViewSoundRequestInfo Request;
        internal readonly int CardId;
        internal readonly float Scale;
        internal bool SeenActive;
        internal Playback(CardViewSoundHandler owner, CardViewSoundRequestInfo request, int id, Clip clip)
        {
            Owner = owner; Request = request; CardId = id; Scale = clip.Gain;
            Reader = new WaveFileReader(ClipPath(id));
            try
            {
                if (Reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || Reader.WaveFormat.BitsPerSample != 16 || Reader.WaveFormat.Channels != 2 ||
                    Reader.WaveFormat.SampleRate is not (44100 or 48000) || Reader.TotalTime.TotalSeconds is <= 0 or > 120) throw new InvalidDataException("Unsupported WAV format");
                Gain = new VolumeSampleProvider(new LoopStream(Reader, clip.Loop).ToSampleProvider()) { Volume = Volume() * Scale };
                Output.Init(Gain.ToWaveProvider16());
            }
            catch { Output.Dispose(); Reader.Dispose(); throw; }
        }
        public void Dispose() { try { Output.Stop(); } finally { Output.Dispose(); Reader.Dispose(); } }
    }

    private static bool BeforeRequest(CardSfxManager __instance, CardViewSoundRequestInfo __0)
    {
        try
        {
            var owner = __0.Source;
            if (owner?.CardView?.Card == null || __0.TriggerType != ECardAudioTriggerType.PremiumCardPreview || !owner.CardView.Card.Definition.IsPremium) return true;
            int card = owner.CardView.Card.Definition.TemplateId;
            if (!clips.TryGetValue(card, out var clip)) return true;
            int id = owner.GetInstanceID();
            var action = ReadAction(__0);
            if (action.HasValue)
            {
                if (!playing.TryGetValue(id, out var active)) return true;
                if (action.Value == AkActionOnEventType.AkActionOnEventType_Pause) active.Output.Pause();
                else if (action.Value == AkActionOnEventType.AkActionOnEventType_Resume) active.Output.Play();
                else Stop(id);
                __0.OnFinished(); return false;
            }
            if (__instance.m_Mute) { __0.OnFinished(); return false; }
            Stop(id);
            var playback = new Playback(owner, __0, card, clip);
            try { playback.Output.Play(); } catch { playback.Dispose(); throw; }
            playing[id] = playback;
            instance?.Log.LogInfo($"Custom premium audio started: {card}");
            return false;
        }
        catch (Exception e) { instance?.Log.LogWarning("Using native premium audio: " + e.Message); return true; }
    }
    private static unsafe Il2CppSystem.Nullable<AkActionOnEventType> ReadAction(CardViewSoundRequestInfo request)
    {
        // The generated getter cannot safely box an empty IL2CPP Nullable.
        var result = new Il2CppSystem.Nullable<AkActionOnEventType>();
        var field = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<CardViewSoundRequestInfo>.NativeClassPtr, "<ActionOnRequest>k__BackingField");
        if (field == IntPtr.Zero) throw new MissingFieldException("ActionOnRequest");
        IL2CPP.il2cpp_field_get_value(request.Pointer, field, IL2CPP.il2cpp_object_unbox(result.Pointer).ToPointer());
        return result;
    }
    private static float Volume()
    {
        if (!SoundManager.IsSpawned || SoundManager.Instance.SettingsHandler == null || !SoundManager.Instance.SettingsHandler.IsInitialized) return 0;
        var settings = SoundManager.Instance.SettingsHandler;
        if (settings.GetBoolSettingCurrentValue(SoundSettingType.MuteToggle) || !Application.isFocused && settings.UnfocusMuteType == UnfocusMuteType.All) return 0;
        if (SoundManager.Instance.CardSfxManager?.m_Mute == true) return 0;
        return Mathf.Clamp01(settings.GetFloatSettingCurrentValue(SoundSettingType.SfxVolume));
    }
    private static void BeforeUninitialize(Component __instance) => Stop(__instance.GetInstanceID());
    private static void Stop(int id)
    {
        if (!playing.Remove(id, out var playback)) return;
        try { playback.Dispose(); } catch (Exception e) { instance?.Log.LogWarning(e.Message); }
        try { playback.Request.OnFinished(); } catch (Exception e) { instance?.Log.LogWarning(e.Message); }
    }
    private static void Tick()
    {
        foreach (var (id, playback) in playing.ToArray())
        {
            try
            {
                var owner = playback.Owner;
                if (owner == null || !owner.gameObject.activeInHierarchy || owner.CardView?.Card == null || owner.CardView.Card.Definition.TemplateId != playback.CardId) { Stop(id); continue; }
                var registration = owner.TryCast<RegisterableSoundHandler>();
                if (registration != null)
                {
                    if (registration.IsActive) playback.SeenActive = true;
                    else if (playback.SeenActive) { Stop(id); continue; }
                }
                playback.Gain.Volume = Volume() * playback.Scale;
                if (playback.Output.PlaybackState == PlaybackState.Stopped) Stop(id);
            }
            catch (Exception e) { instance?.Log.LogWarning(e.Message); Stop(id); }
        }
    }
    private static void Shutdown() { foreach (int id in playing.Keys.ToArray()) Stop(id); }
}
