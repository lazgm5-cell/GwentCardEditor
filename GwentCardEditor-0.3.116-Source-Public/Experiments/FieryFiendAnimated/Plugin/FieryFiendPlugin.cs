using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using GwentUnity;
using GwentVisuals;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;
#if CARD_EDITOR_ANIMATED_FRAMES
using GwentGameplay;
#endif

namespace GwentCardEditor.FieryFiend;

#if CARD_EDITOR_ANIMATED_FRAMES
[BepInPlugin(Id, "GWENT Card Editor - Animated Fiery Fiend", "0.2.0")]
#else
[BepInPlugin(Id, "Fiery Fiend Animated Frame - Experimental", "0.1.1")]
#endif
public sealed class FieryFiendPlugin : BasePlugin
{
    public const string Id = "local.gwent.cardeditor.fieryfiend.experimental";
    private static FieryFiendPlugin? plugin;
    private static string folder = "";
    private static HashSet<int> targets = new();
#if CARD_EDITOR_ANIMATED_FRAMES
    private static bool allGold;
#endif
    private static readonly Dictionary<int, (CardMeshController View, Texture? Original)> views = new();
    private static readonly HashSet<int> releasing = new();
    private static readonly List<Object> owned = new();
    private static Camera? camera;
    private static RenderTexture? frame;
    private static bool assigning, ready, disabled;
    private static float nextRender, started;
    private static string? initialHash;
    private static int rendered;
    private const string ShaderName = "ShaderLibrary/Generic/GwentStandard";
    private const string SceneSha256 = "16E95BB182B56F4B188EEB024CD35B739DF81B76FD125705A448FC38B51EB799";

    public override void Load()
    {
        plugin = this; folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var harmony = new Harmony(Id);
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "config.json")));
            if (!config.RootElement.GetProperty("Enabled").GetBoolean()) return;
            targets = config.RootElement.GetProperty("TargetCards").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
#if CARD_EDITOR_ANIMATED_FRAMES
            allGold = config.RootElement.TryGetProperty("AllGold", out var scope) && scope.GetBoolean();
            if (config.RootElement.TryGetProperty("Version", out var version) && version.GetInt32() != 1) throw new InvalidDataException("Unsupported frame configuration");
            if (allGold ? targets.Count != 0 : targets.Count is < 1 or > 20) throw new InvalidDataException("Invalid frame scope");
            if (targets.Any(id => id <= 0)) throw new InvalidDataException("Invalid frame targets");
#else
            if (targets.Count is < 1 or > 20 || targets.Any(id => id <= 0)) throw new InvalidDataException("Invalid experimental targets");
#endif
            Patch(harmony, typeof(CardMeshController), "SetFrameTextureInternal", nameof(AfterFrame), false);
            Patch(harmony, typeof(CardMeshController), "Uninitialize", nameof(Removing), true);
            Patch(harmony, typeof(CardMeshController), "Uninitialize", nameof(AfterRemoving), false);
            Patch(harmony, typeof(CardMeshController), "OnDestroy", nameof(Removing), true);
            Patch(harmony, typeof(CardMeshController), "OnDestroy", nameof(AfterRemoving), false);
            Patch(harmony, typeof(GwentApp), "Update", nameof(Tick), false);
            if (AccessTools.Method(typeof(GwentApp), "OnApplicationQuit") is { } quit)
                harmony.Patch(quit, prefix: new HarmonyMethod(typeof(FieryFiendPlugin), nameof(Shutdown)));
#if CARD_EDITOR_ANIMATED_FRAMES
            Log.LogInfo("Animated Fiery Fiend scope: " + (allGold ? "all gold cards; leaders excluded" : string.Join(",", targets)) + ". Shared renderer, up to 20 fps. No game resource files modified by the plugin.");
#else
            Log.LogInfo("Experimental Fiery Fiend enabled for: " + string.Join(",", targets) + ". No game resource files are modified.");
#endif
        }
        catch (Exception e) { harmony.UnpatchSelf(); Fail(e); }
    }

    private static void Patch(Harmony h, Type type, string method, string callback, bool prefix)
    {
        var original = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
        var hook = new HarmonyMethod(typeof(FieryFiendPlugin), callback);
        h.Patch(original, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
    }
    private static bool Target(CardMeshController c)
    {
        if (c == null || c.ParentCardView?.Card == null) return false;
#if CARD_EDITOR_ANIMATED_FRAMES
        var card = c.ParentCardView.Card;
        if (allGold) return card.Template != null && FrameEligibility.Accept(card.Template.Tier == ECardTier.Gold, card.IsLeader || card.Template.Type == ECardType.Leader);
#endif
        return targets.Contains(c.ParentCardView.Card.Definition.TemplateId);
    }
    private static void AfterFrame(CardMeshController __instance)
    {
        if (assigning || disabled) return;
        try
        {
            int id = __instance.GetInstanceID();
            if (releasing.Contains(id)) return;
            if (!Target(__instance)) { Remove(__instance, true); return; }
            Texture? original = __instance.FrontMaterial?.GetTexture("_FrameTex");
            if (original == frame && views.TryGetValue(id, out var previous)) original = previous.Original;
            views[id] = (__instance, original);
            if (ready) Assign(__instance, frame!);
        }
        catch (Exception e) { Fail(e); }
    }
    private static void Assign(CardMeshController view, Texture value)
    {
        assigning = true;
        try { view.SetFrameTexture(value); }
        finally { assigning = false; }
    }
    private static void Removing(CardMeshController __instance)
    {
        if (__instance == null) return;
        releasing.Add(__instance.GetInstanceID());
        if (!assigning) Remove(__instance, true);
    }
    private static void AfterRemoving(CardMeshController __instance) { if (__instance != null) releasing.Remove(__instance.GetInstanceID()); }
    private static void Remove(CardMeshController c, bool restore)
    {
        if (c == null || !views.Remove(c.GetInstanceID(), out var entry)) return;
        if (restore && c.FrontMaterial != null && c.FrontMaterial.GetTexture("_FrameTex") == frame)
        {
            assigning = true;
            try { if (entry.Original != null) c.SetFrameTexture(entry.Original); else c.SetFrameTexture(); }
            catch (Exception e) { plugin?.Log.LogWarning("Frame restore: " + e.Message); }
            finally { assigning = false; }
        }
    }
    private static void Tick()
    {
        if (disabled || views.Count == 0) return;
        if (Time.realtimeSinceStartup < nextRender) return;
        nextRender = Time.realtimeSinceStartup + .05f;
        try
        {
            foreach (var pair in views.ToArray())
                if (pair.Value.View == null) views.Remove(pair.Key);
                else if (!Target(pair.Value.View)) Remove(pair.Value.View, true);
#if CARD_EDITOR_ANIMATED_FRAMES
            if (!views.Values.Any(v => v.View != null && FrameEligibility.ShouldRender(v.View.gameObject.activeInHierarchy,
                v.View.ParentCardView.Culling == ECardCulling.FULL, v.View.Mesh != null && v.View.Mesh.enabled, Time.timeScale <= 0))) return;
#else
            if (!views.Values.Any(v => v.View != null && v.View.gameObject.activeInHierarchy && !v.View.ParentCardView.Paused)) return;
#endif
            if (camera == null)
            {
                if (Shader.Find(ShaderName) == null) return;
                CreateScene(); started = Time.realtimeSinceStartup;
            }
            camera!.Render(); rendered++;
            if (initialHash == null) initialHash = Capture("frame-0.png");
            else if (!ready && Time.realtimeSinceStartup - started >= 1.2f)
            {
                string next = Capture("frame-1.png");
                if (next == initialHash) throw new InvalidDataException("Offscreen frames are identical; animated shader not confirmed. Native frames retained.");
                ready = true;
                foreach (var entry in views.Values) Assign(entry.View, frame!);
                plugin?.Log.LogInfo($"Fiery Fiend native render has non-empty, changing pixels ({rendered} frames). Applied to {views.Count} views. Inspect frame-0.png/frame-1.png.");
            }
        }
        catch (Exception e) { Fail(e); }
    }
    private static Vector3 V(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
    private static Vector2 V2(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle());
    private static float[] Values(JsonElement e) => e.EnumerateArray().Select(v => v.GetSingle()).ToArray();

    private static void CreateScene()
    {
        byte[] sceneBytes = File.ReadAllBytes(Path.Combine(folder, "assets", "scene.json"));
        if (!Convert.ToHexString(SHA256.HashData(sceneBytes)).Equals(SceneSha256, StringComparison.Ordinal)) throw new InvalidDataException("Scene checksum");
        using var document = JsonDocument.Parse(sceneBytes);
        var scene = document.RootElement;
        if (scene.GetProperty("Version").GetInt32() != 1 || scene.GetProperty("Shader").GetString() != ShaderName) throw new InvalidDataException("Unsupported scene");
        var shader = Shader.Find(ShaderName) ?? throw new InvalidDataException("Native shader not loaded");
        var textures = new Dictionary<string, Texture2D>();
        foreach (var p in scene.GetProperty("Textures").EnumerateObject())
        {
            string relative = p.Value.GetProperty("File").GetString()!;
            if (relative != "textures/" + p.Name + ".png" || !long.TryParse(p.Name, out _)) throw new InvalidDataException("Texture path");
            byte[] raw = File.ReadAllBytes(Path.Combine(folder, "assets", relative));
            if (!Convert.ToHexString(SHA256.HashData(raw)).Equals(p.Value.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Texture checksum");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); owned.Add(texture);
            if (!ImageConversion.LoadImage(texture, raw) || texture.width != p.Value.GetProperty("Width").GetInt32() || texture.height != p.Value.GetProperty("Height").GetInt32()) throw new InvalidDataException("Texture decode");
            var sampling = p.Value.GetProperty("Sampling");
            int u = sampling.GetProperty("WrapU").GetInt32(), v = sampling.GetProperty("WrapV").GetInt32(), w = sampling.GetProperty("WrapW").GetInt32();
            int filter = sampling.GetProperty("Filter").GetInt32(), aniso = sampling.GetProperty("Aniso").GetInt32();
            float bias = sampling.GetProperty("MipBias").GetSingle();
            if (u is < 0 or > 3 || v is < 0 or > 3 || w is < 0 or > 3 || filter is < 0 or > 2 || aniso is < 0 or > 16 || !float.IsFinite(bias)) throw new InvalidDataException("Texture sampling settings");
            texture.wrapModeU = (TextureWrapMode)u; texture.wrapModeV = (TextureWrapMode)v; texture.wrapModeW = (TextureWrapMode)w;
            texture.filterMode = (FilterMode)filter; texture.anisoLevel = aniso; texture.mipMapBias = bias;
            textures.Add(p.Name, texture);
        }
        var materials = new List<Material>();
        foreach (var value in scene.GetProperty("Materials").EnumerateArray())
        {
            var material = new Material(shader); owned.Add(material); materials.Add(material);
            material.name = "FieryExperimental/" + value.GetProperty("Name").GetString();
            foreach (var p in value.GetProperty("Floats").EnumerateObject()) if (material.HasProperty(p.Name)) material.SetFloat(p.Name, p.Value.GetSingle());
            foreach (var p in value.GetProperty("Colors").EnumerateObject()) if (material.HasProperty(p.Name)) material.SetVector(p.Name, new Vector4(p.Value.GetProperty("r").GetSingle(), p.Value.GetProperty("g").GetSingle(), p.Value.GetProperty("b").GetSingle(), p.Value.GetProperty("a").GetSingle()));
            foreach (var p in value.GetProperty("Textures").EnumerateArray())
            {
                string name = p.GetProperty("Name").GetString()!;
                material.SetTexture(name, textures[p.GetProperty("Texture").GetString()!]);
                material.SetTextureScale(name, V2(p.GetProperty("Scale"))); material.SetTextureOffset(name, V2(p.GetProperty("Offset")));
            }
            foreach (var keyword in value.GetProperty("Keywords").EnumerateArray()) material.EnableKeyword(keyword.GetString());
            material.renderQueue = value.GetProperty("Queue").GetInt32();
        }
        var meshes = new List<Mesh>();
        foreach (var value in scene.GetProperty("Meshes").EnumerateArray())
        {
            var mesh = new Mesh(); owned.Add(mesh); meshes.Add(mesh);
            mesh.vertices = new Il2CppStructArray<Vector3>(value.GetProperty("Vertices").EnumerateArray().Select(e => { var v = Values(e); return new Vector3(v[0], v[1], v[2]); }).ToArray());
            mesh.uv = new Il2CppStructArray<Vector2>(value.GetProperty("Uv").EnumerateArray().Select(e => { var v = Values(e); return new Vector2(v[0], v[1]); }).ToArray());
            mesh.normals = new Il2CppStructArray<Vector3>(value.GetProperty("Normals").EnumerateArray().Select(e => { var v = Values(e); return new Vector3(v[0], v[1], v[2]); }).ToArray());
            mesh.colors = new Il2CppStructArray<Color>(value.GetProperty("Colors").EnumerateArray().Select(e => { var v = Values(e); return new Color(v[0], v[1], v[2], v[3]); }).ToArray());
            mesh.triangles = new Il2CppStructArray<int>(value.GetProperty("Triangles").EnumerateArray().Select(v => v.GetInt32()).ToArray()); mesh.RecalculateBounds();
        }
        var root = new GameObject("FieryFiendExperimentalScene"); owned.Add(root); Object.DontDestroyOnLoad(root); root.transform.position = new Vector3(0, 5000, 0);
        foreach (var node in scene.GetProperty("Nodes").EnumerateArray())
        {
            var go = new GameObject(node.GetProperty("Name").GetString()); go.layer = 31; go.transform.SetParent(root.transform, false);
            go.transform.localPosition = V(node.GetProperty("Position")); go.transform.localScale = V(node.GetProperty("Scale"));
            var q = node.GetProperty("Rotation"); go.transform.localRotation = new Quaternion(q.GetProperty("x").GetSingle(), q.GetProperty("y").GetSingle(), q.GetProperty("z").GetSingle(), q.GetProperty("w").GetSingle());
            go.AddComponent<MeshFilter>().sharedMesh = meshes[node.GetProperty("Mesh").GetInt32()]; go.AddComponent<MeshRenderer>().sharedMaterial = materials[node.GetProperty("Material").GetInt32()];
        }
        var eye = new GameObject("FieryFiendExperimentalCamera"); owned.Add(eye); Object.DontDestroyOnLoad(eye);
        camera = eye.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .7876f; camera.aspect = 1.05f / 1.5752f;
        eye.transform.position = new Vector3(0, 5002, 0); eye.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.nearClipPlane = .1f; camera.farClipPlane = 4; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
        frame = new RenderTexture(512, 1024, 24, RenderTextureFormat.ARGB32); owned.Add(frame); frame.Create(); camera.targetTexture = frame;
        camera.aspect = 1.05f / 1.5752f; camera.allowHDR = false; camera.allowMSAA = false;
        plugin?.Log.LogInfo("Fiery Fiend scene recreated: 6 nodes, native 2022 shader. Waiting for pixel animation check.");
    }
    private static string Capture(string name)
    {
        var previous = RenderTexture.active; var image = new Texture2D(512, 1024, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = frame; image.ReadPixels(new Rect(0, 0, 512, 1024), 0, 0); image.Apply();
            byte[] data = ImageConversion.EncodeToPNG(image); File.WriteAllBytes(Path.Combine(folder, name), data);
            var pixels = image.GetPixels32(); int visible = 0;
            for (int index = 0; index < pixels.Length && visible < 500; index++)
            {
                var pixel = pixels[index];
                if (pixel.a > 30 && (pixel.r > 5 || pixel.g > 5 || pixel.b > 5)) visible++;
            }
            if (visible < 500) throw new InvalidDataException("Offscreen frame is blank; native frame retained.");
            return Convert.ToHexString(SHA256.HashData(data));
        }
        finally { RenderTexture.active = previous; Object.Destroy(image); }
    }
    private static void Fail(Exception error) { disabled = true; plugin?.Log.LogError("Fiery Fiend disabled; restoring native frames: " + error); Shutdown(); }
    private static void Shutdown()
    {
        foreach (var entry in views.Values.ToArray()) Remove(entry.View, true);
        views.Clear();
        releasing.Clear();
        if (camera != null) camera.targetTexture = null;
        if (frame != null) frame.Release();
        foreach (var value in owned.AsEnumerable().Reverse()) if (value != null) Object.Destroy(value);
        owned.Clear(); frame = null; camera = null; ready = false;
    }
}
