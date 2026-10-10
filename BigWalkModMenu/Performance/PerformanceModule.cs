using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BigWalk.ModMenu.Performance
{
    [BepInPlugin("com.bigwalk.modmenu.performance", "Big Walk Mod Menu - Performance", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new PerformanceModule());
        }
    }

    public sealed class PerformanceModule : MenuModule
    {
        public override string Id => "performance";
        public override string Title => "Performance";
        public override int Order => 40;

        private readonly Hotkey _overlayKey = new Hotkey("Overlay", "Show/hide the counter", KeyCode.F3);
        private readonly List<Knob> _knobs = new List<Knob>();
        private bool _overlay, _graph = true;

        // Frame statistics.
        private const int Samples = 240;
        private readonly float[] _frames = new float[Samples];
        private readonly float[] _sorted = new float[Samples];
        private int _frameIndex, _frameCount;
        private float _fps, _low, _avgMs, _cpuMs, _gpuMs;
        private float _nextStats;
        private Il2CppStructArray<FrameTiming> _timings;
        private bool _timingsBroken;

        // Per-system quality (the game's own 0-5 levels, applied to one system at a time).
        private static readonly string[] SystemNames = { "Vegetation and terrain", "Ocean", "Sky and sun", "Lights", "Screen effects", "Distant detail" };
        private readonly int[] _systemLevel = { -1, -1, -1, -1, -1, -1 }; // -1 = follow the game
        private readonly int[] _systemApplied = { -1, -1, -1, -1, -1, -1 };

        // Post-processing effects and renderer features found at run time: name -> forced state.
        private readonly Dictionary<string, bool> _effectForced = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _effectOriginal = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _featureForced = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _featureOriginal = new Dictionary<string, bool>();
        private readonly HashSet<string> _touched = new HashSet<string>();
        private bool _showAdvanced;

        private static UniversalRenderPipelineAsset Urp
        {
            get
            {
                RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
                return asset != null ? asset.TryCast<UniversalRenderPipelineAsset>() : null;
            }
        }

        private static UniversalAdditionalCameraData _cameraData;

        // The player camera's URP settings (antialiasing lives there, not in the pipeline asset).
        private static UniversalAdditionalCameraData CameraData
        {
            get
            {
                if (GraphicsShare.AntialiasingOwner != null) return null; // another tab is driving it
                Camera cam = Game.PlayerCamera;
                if (cam == null) return null;
                if (!Game.Alive(_cameraData) || _cameraData == null || _cameraData.gameObject != cam.gameObject)
                {
                    _cameraData = cam.GetComponent<UniversalAdditionalCameraData>();
                }
                return _cameraData;
            }
        }

        public PerformanceModule()
        {
            string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";

            _knobs.Add(new Knob("RenderScale", "Render scale", 0.1f, 2f, 0.05f, 1f, false,
                () => { var u = Urp; return u != null ? u.renderScale : float.NaN; },
                v => Urp.renderScale = v,
                (v, e) => $"{Pct(v)} ({Mathf.RoundToInt(Screen.width * v)}x{Mathf.RoundToInt(Screen.height * v)})"));
            _knobs.Add(new Knob("ShadowDistance", "Shadow distance", 0f, 3f, 0.05f, 1f, true,
                () => { var u = Urp; return u != null ? u.shadowDistance : float.NaN; },
                v => Urp.shadowDistance = v,
                (v, e) => v < 0.01f ? "off" : $"{Pct(v)} ({e:0} m)"));
            _knobs.Add(new Knob("LodBias", "Detail of distant objects (LOD)", 0.1f, 10f, 0.05f, 1f, true,
                () => QualitySettings.lodBias,
                v => QualitySettings.lodBias = v,
                (v, e) => Pct(v)));
            _knobs.Add(new Knob("FarClip", "View distance", 0.1f, 3f, 0.05f, 1f, true,
                () => { var c = Game.PlayerCamera; return c != null ? c.farClipPlane : float.NaN; },
                v => { var c = Game.PlayerCamera; if (c != null) c.farClipPlane = v; },
                (v, e) => $"{Pct(v)} ({e:0} m)"));
            _knobs.Add(new Knob("Msaa", "Antialiasing (MSAA)", 0f, 3f, 1f, 0f, false,
                () => { var u = Urp; return u != null ? Mathf.Log(Mathf.Max(1, u.msaaSampleCount), 2f) : float.NaN; },
                v => Urp.msaaSampleCount = 1 << Mathf.RoundToInt(v),
                (v, e) => Mathf.RoundToInt(v) == 0 ? "off" : (1 << Mathf.RoundToInt(v)) + "x"));
            _knobs.Add(new Knob("CameraAa", "Screen antialiasing", 0f, 3f, 1f, 0f, false,
                () => { var d = CameraData; return d != null ? (float)(int)d.antialiasing : float.NaN; },
                v => { var d = CameraData; if (d != null) d.antialiasing = (AntialiasingMode)Mathf.RoundToInt(v); },
                (v, e) => new[] { "none", "FXAA (fast, blurs)", "SMAA (light)", "TAA (stable, softer)" }[Mathf.Clamp(Mathf.RoundToInt(v), 0, 3)]));
            _knobs.Add(new Knob("Fov", "Field of view (FOV)", 45f, 150f, 1f, 90f, false,
                () => PlayerCameraMinder.fovFromSettings,
                v =>
                {
                    PlayerCameraMinder.fovFromSettings = v;
                    // The game copies it to the camera every frame, but not for a Solo Mod character: there we do it.
                    Camera c = CameraRig.Active ? null : Game.PlayerCamera;
                    if (c != null) c.fieldOfView = v;
                },
                (v, e) => Mathf.RoundToInt(v) + "°"));
            _knobs.Add(new Knob("Upscaling", "Upscaling filter", 0f, 3f, 1f, 1f, false,
                () => { var u = Urp; return u != null ? (float)(int)u.upscalingFilter : float.NaN; },
                v => Urp.upscalingFilter = (UpscalingFilterSelection)Mathf.RoundToInt(v),
                (v, e) => new[] { "automatic", "linear (soft)", "point (hard pixels)", "FSR 1.0 (sharp)" }[Mathf.Clamp(Mathf.RoundToInt(v), 0, 3)]));
            _knobs.Add(new Knob("ShadowCascades", "Shadow cascades", 1f, 4f, 1f, 4f, false,
                () => { var u = Urp; return u != null ? u.m_ShadowCascadeCount : float.NaN; },
                v => Urp.m_ShadowCascadeCount = Mathf.Clamp(Mathf.RoundToInt(v), 1, 4),
                (v, e) => Mathf.RoundToInt(v).ToString()));
            _knobs.Add(new Knob("ShadowResolution", "Shadow resolution", 8f, 13f, 1f, 12f, false,
                () => { var u = Urp; return u != null ? Mathf.Round(Mathf.Log(Mathf.Max(256, u.mainLightShadowmapResolution), 2f)) : float.NaN; },
                v => Urp.m_MainLightShadowmapResolution = (UnityEngine.Rendering.Universal.ShadowResolution)(1 << Mathf.Clamp(Mathf.RoundToInt(v), 8, 13)),
                (v, e) => (1 << Mathf.Clamp(Mathf.RoundToInt(v), 8, 13)).ToString()));
            _knobs.Add(new Knob("SoftShadows", "Soft shadows", 0f, 1f, 1f, 1f, false,
                () => { var u = Urp; return u != null ? (u.m_SoftShadowsSupported ? 1f : 0f) : float.NaN; },
                v => Urp.m_SoftShadowsSupported = v > 0.5f,
                (v, e) => v > 0.5f ? "yes" : "no (pixelated edges)"));
            _knobs.Add(new Knob("Aniso", "Anisotropic filtering", 0f, 2f, 1f, 2f, false,
                () => (float)(int)QualitySettings.anisotropicFiltering,
                v => QualitySettings.anisotropicFiltering = (AnisotropicFiltering)Mathf.Clamp(Mathf.RoundToInt(v), 0, 2),
                (v, e) => new[] { "off", "where the game asks", "everywhere" }[Mathf.Clamp(Mathf.RoundToInt(v), 0, 2)]));
            _knobs.Add(new Knob("TextureLimit", "Texture quality", 0f, 3f, 1f, 0f, false,
                () => QualitySettings.globalTextureMipmapLimit,
                v => QualitySettings.globalTextureMipmapLimit = Mathf.RoundToInt(v),
                (v, e) => new[] { "full", "half", "quarter", "eighth" }[Mathf.Clamp(Mathf.RoundToInt(v), 0, 3)]));
            _knobs.Add(new Knob("FpsLimit", "FPS limit", 0f, 240f, 5f, 60f, false,
                () => Application.targetFrameRate,
                v => Application.targetFrameRate = v < 5f ? -1 : Mathf.RoundToInt(v),
                (v, e) => v < 5f ? "none" : Mathf.RoundToInt(v).ToString()));
            _knobs.Add(new Knob("VSync", "VSync", 0f, 2f, 1f, 0f, false,
                () => QualitySettings.vSyncCount,
                v => QualitySettings.vSyncCount = Mathf.RoundToInt(v),
                (v, e) => new[] { "off", "on", "half refresh rate" }[Mathf.Clamp(Mathf.RoundToInt(v), 0, 2)]));
        }

        public override void Load(ModConfig c)
        {
            _overlayKey.Load(c);
            _overlay = c.GetBool("Overlay", false);
            _graph = c.GetBool("Overlay.Graph", true);
            foreach (Knob k in _knobs)
            {
                k.On = c.GetBool(k.Id + ".On", false);
                k.Value = Mathf.Clamp(c.GetFloat(k.Id, k.Default), k.Min, k.Max);
            }
            for (int i = 0; i < _systemLevel.Length; i++) _systemLevel[i] = Mathf.Clamp(c.GetInt("System." + i, -1), -1, 5);
            LoadMap(c, "Effect.", _effectForced);
            LoadMap(c, "Feature.", _featureForced);
        }

        public override void Save(ModConfig c)
        {
            _overlayKey.Save(c);
            c.Set("Overlay", _overlay);
            c.Set("Overlay.Graph", _graph);
            foreach (Knob k in _knobs)
            {
                c.Set(k.Id + ".On", k.On);
                c.Set(k.Id, k.Value);
            }
            for (int i = 0; i < _systemLevel.Length; i++) c.Set("System." + i, _systemLevel[i]);
            c.Set("Effect.List", string.Join("|", ToList(_effectForced)));
            c.Set("Feature.List", string.Join("|", ToList(_featureForced)));
        }

        private static List<string> ToList(Dictionary<string, bool> map)
        {
            var list = new List<string>();
            foreach (var kv in map) list.Add(kv.Key + ":" + (kv.Value ? "1" : "0"));
            return list;
        }

        private static void LoadMap(ModConfig c, string prefix, Dictionary<string, bool> map)
        {
            map.Clear();
            foreach (string item in c.GetString(prefix + "List", "").Split('|'))
            {
                int colon = item.LastIndexOf(':');
                if (colon > 0) map[item.Substring(0, colon)] = item.EndsWith("1");
            }
        }

        // ---- applying ----

        public override void Update()
        {
            if (_overlayKey.Down) { _overlay = !_overlay; Dirty = true; }
            Sample();

            // The game re-applies its own values at times (quality change, new scene): keep ours on top.
            if (Time.frameCount % 30 != 0) return;
            foreach (Knob k in _knobs) k.Apply();
            if (!Game.InWorld) return;
            ApplySystems();
            ApplyEffects();
            ApplyFeatures();
        }

        private void ApplySystems()
        {
            int gameLevel;
            try { gameLevel = SettingsHelper.GetQualityLevel(); } catch { return; }
            for (int i = 0; i < _systemLevel.Length; i++)
            {
                int wanted = _systemLevel[i] < 0 ? gameLevel : _systemLevel[i];
                // Nothing to do while a system follows the game and we never touched it.
                if (_systemLevel[i] < 0 && _systemApplied[i] < 0) continue;
                if (_systemApplied[i] == wanted) continue;
                try
                {
                    switch (i)
                    {
                        case 0: { var m = TerrainQualityManager.instance; if (m == null) continue; m.ApplyQualityEffects(wanted); break; }
                        case 1: { var m = UnityEngine.Object.FindObjectOfType<OceanQualityManager>(); if (m == null) continue; m.ApplyQualityEffects(wanted); break; }
                        case 2: { var m = UnityEngine.Object.FindObjectOfType<SkyQualityManager>(); if (m == null) continue; m.ApplyQualityEffects(wanted); break; }
                        case 3: { var m = UnityEngine.Object.FindObjectOfType<HouseLightManager>(); if (m == null) continue; m.ApplyQualityEffects(wanted); break; }
                        case 4: { var m = PostProcessingQualityManger.Instance; if (m == null) continue; m.ApplyQualityEffects(wanted); break; }
                        case 5: { var m = CameraQualityManager.Instance; if (m == null) continue; m.ApplyQualityEffects(wanted); break; }
                    }
                    ModMenuApi.Log.LogInfo($"[Performance] {SystemNames[i]}: quality level {wanted}.");
                }
                catch (Exception ex)
                {
                    ModMenuApi.LogOnce("system" + i, $"[Performance] {SystemNames[i]}: {ex.Message}");
                }
                _systemApplied[i] = _systemLevel[i] < 0 ? -1 : wanted;
            }
        }

        private static VolumeProfile BaseProfile
        {
            get
            {
                try
                {
                    var m = PostProcessingQualityManger.Instance;
                    return m != null ? m.baseVolumeProfile : null;
                }
                catch { return null; }
            }
        }

        private static string EffectName(VolumeComponent c)
        {
            string n = c.name;
            if (string.IsNullOrEmpty(n)) n = c.GetIl2CppType().Name;
            return n.Replace("(Clone)", "").Trim();
        }

        private void ApplyEffects()
        {
            VolumeProfile profile = BaseProfile;
            if (profile == null || profile.components == null) return;
            var list = profile.components;
            for (int i = 0; i < list.Count; i++)
            {
                VolumeComponent c = list[i];
                if (c == null) continue;
                string name = EffectName(c);
                if (!_effectOriginal.ContainsKey(name)) _effectOriginal[name] = c.active;
                // Only effects the user asked to change are touched: the game switches some with its quality level.
                if (_effectForced.TryGetValue(name, out bool forced))
                {
                    if (c.active != forced) c.active = forced;
                    _touched.Add("e:" + name);
                }
                else if (_touched.Remove("e:" + name))
                {
                    c.active = _effectOriginal[name];
                }
                else
                {
                    _effectOriginal[name] = c.active;
                }
            }
        }

        private void ApplyFeatures()
        {
            PlayerCharacter pc = Game.LocalPlayer;
            var data = pc != null && pc.cameraMinder != null ? pc.cameraMinder.universalRendererData : null;
            var list = data != null ? data.rendererFeatures : null;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                ScriptableRendererFeature f = list[i];
                if (f == null) continue;
                string name = f.name;
                if (!_featureOriginal.ContainsKey(name)) _featureOriginal[name] = f.isActive;
                // Only features the user asked to change are touched: the game and other modules switch some themselves.
                if (_featureForced.TryGetValue(name, out bool forced))
                {
                    if (f.isActive != forced) f.SetActive(forced);
                    _touched.Add("f:" + name);
                }
                else if (_touched.Remove("f:" + name))
                {
                    f.SetActive(_featureOriginal[name]);
                }
                else
                {
                    _featureOriginal[name] = f.isActive;
                }
            }
        }

        // ---- statistics ----

        private void Sample()
        {
            float dt = Time.unscaledDeltaTime;
            _frames[_frameIndex] = dt;
            _frameIndex = (_frameIndex + 1) % Samples;
            if (_frameCount < Samples) _frameCount++;

            if (!_overlay && !ModMenuApi.IsMenuOpen) return;
            if (Time.unscaledTime < _nextStats) return;
            _nextStats = Time.unscaledTime + 0.25f;

            float sum = 0f;
            for (int i = 0; i < _frameCount; i++) { sum += _frames[i]; _sorted[i] = _frames[i]; }
            if (_frameCount == 0 || sum <= 0f) return;
            _avgMs = sum / _frameCount * 1000f;
            _fps = _frameCount / sum;
            Array.Sort(_sorted, 0, _frameCount);
            // "1% low": the frame rate of the slowest hundredth of the frames.
            float worst = _sorted[Mathf.Clamp(Mathf.CeilToInt(_frameCount * 0.99f) - 1, 0, _frameCount - 1)];
            _low = worst > 0f ? 1f / worst : 0f;

            if (_timingsBroken) return;
            try
            {
                if (_timings == null) _timings = new Il2CppStructArray<FrameTiming>(1);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                {
                    _cpuMs = (float)_timings[0].cpuFrameTime;
                    _gpuMs = (float)_timings[0].gpuFrameTime;
                }
            }
            catch (Exception ex)
            {
                _timingsBroken = true;
                ModMenuApi.Log.LogInfo($"[Performance] CPU/GPU timings are not available: {ex.Message}");
            }
        }

        private string Bottleneck()
        {
            if (_cpuMs <= 0.01f || _gpuMs <= 0.01f) return null;
            if (_gpuMs > _cpuMs * 1.15f) return "bottleneck: <b>graphics card</b>";
            if (_cpuMs > _gpuMs * 1.15f) return "bottleneck: <b>processor</b>";
            return "CPU and GPU level";
        }

        public override void DrawOverlay()
        {
            if (!_overlay) return;
            float w = 250f, h = _graph ? 86f : 40f;
            var box = new Rect(10f, 10f, w, h);
            Ui.Tint(box, new Color(0f, 0f, 0f, 0.55f));
            string colour = _fps >= 55f ? "#88ff88" : _fps >= 30f ? "#ffee66" : "#ff7766";
            GUI.Label(new Rect(16f, 12f, w - 12f, 20f), $"<color={colour}><b>{_fps:0} FPS</b></color>  min 1%: {_low:0}  {_avgMs:0.0} ms");
            string timings = _cpuMs > 0.01f || _gpuMs > 0.01f ? $"CPU {_cpuMs:0.0} ms  GPU {_gpuMs:0.0} ms" : "CPU/GPU: not available";
            GUI.Label(new Rect(16f, 28f, w - 12f, 20f), "<size=11>" + timings + "</size>");
            if (!_graph) return;

            // Frame times, newest on the right; the line marks 60 FPS.
            float gx = 16f, gy = 50f, gw = w - 12f, gh = 40f;
            const float scaleMs = 50f;
            Ui.Tint(new Rect(gx, gy + gh * (1f - 16.67f / scaleMs), gw, 1f), new Color(1f, 1f, 1f, 0.3f));
            float bar = gw / Samples;
            for (int i = 0; i < _frameCount; i++)
            {
                float ms = _frames[(_frameIndex - _frameCount + i + Samples * 2) % Samples] * 1000f;
                float t = Mathf.Clamp01(ms / scaleMs);
                Color col = ms < 17.5f ? new Color(0.4f, 1f, 0.5f, 0.9f) : ms < 34f ? new Color(1f, 0.9f, 0.3f, 0.9f) : new Color(1f, 0.4f, 0.35f, 0.9f);
                Ui.Tint(new Rect(gx + i * bar, gy + gh * (1f - t), Mathf.Max(1f, bar), gh * t), col);
            }
        }

        // ---- menu ----

        private void Set(string id, float value)
        {
            foreach (Knob k in _knobs) if (k.Id == id) { k.On = true; k.Value = Mathf.Clamp(value, k.Min, k.Max); }
        }

        private void Preset(float scale, float shadows, float lod, float far, bool on)
        {
            void Put(string id, float v)
            {
                foreach (Knob k in _knobs) if (k.Id == id) { k.On = on; k.Value = Mathf.Clamp(v, k.Min, k.Max); }
            }
            Put("RenderScale", scale);
            Put("ShadowDistance", shadows);
            Put("LodBias", lod);
            Put("FarClip", far);
            Dirty = true;
        }

        public override void DrawTab(Ui ui)
        {
            bool changed = false;

            ui.Header("Right now");
            ui.Label($"<b>{_fps:0} FPS</b>  (min 1%: {_low:0})   {_avgMs:0.0} ms per frame");
            string bottleneck = Bottleneck();
            if (bottleneck != null) ui.Label($"CPU {_cpuMs:0.0} ms • GPU {_gpuMs:0.0} ms • {bottleneck}");
            else ui.Note("Separate CPU and GPU timings are not available in this game.");
            try
            {
                var u = Urp;
                float rs = u != null ? u.renderScale : 1f;
                long mem = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
                ui.Note($"Screen {Screen.width}x{Screen.height}, rendering {Mathf.RoundToInt(Screen.width * rs)}x{Mathf.RoundToInt(Screen.height * rs)} • memory {mem} MB • game quality: {SettingsHelper.GetQualityLevel()}", 2);
            }
            catch { }
            changed |= ui.Toggle($"On-screen counter ({_overlayKey.Key})", ref _overlay);
            if (_overlay) changed |= ui.Toggle("With graph", ref _graph);
            changed |= ui.HotkeyRow(_overlayKey);
            ui.Note("If the processor is the bottleneck, lowering the graphics helps little.");

            ui.Header("Quick profiles");
            int hit = ui.Buttons("Game's own", "Quality", "Balanced", "Potato");
            if (hit == 0) { foreach (Knob k in _knobs) k.On = false; Dirty = true; }
            else if (hit == 1) Preset(1.25f, 1.5f, 1.5f, 1f, true);
            else if (hit == 2) Preset(0.85f, 0.7f, 0.8f, 0.8f, true);
            else if (hit == 3) Preset(0.55f, 0.3f, 0.5f, 0.5f, true);
            if (ui.Button("Sharp: 140% supersampling, LOD 4x, 8192 shadows (heavy)"))
            {
                // The recipe of the "Big Visuals" mod: no blur filter, the higher resolution smooths the edges.
                Set("RenderScale", 1.4f); Set("LodBias", 4f); Set("CameraAa", 0f); Set("Upscaling", 1f);
                Set("ShadowCascades", 4f); Set("ShadowResolution", 13f); Set("SoftShadows", 1f); Set("Aniso", 2f);
                Dirty = true;
            }

            ui.Header("Settings");
            ui.Note("Anything switched off keeps the value the game chose.");
            if (GraphicsShare.AntialiasingOwner != null) ui.Note("<color=#ffaa66>\"Screen antialiasing\" is on hold: the Reshade tab's sharpening is using it.</color>", 2);
            foreach (Knob k in _knobs)
            {
                if (k.Broken) { ui.Note(k.Label + ": not available"); continue; }
                changed |= ui.Toggle(k.Label, ref k.On);
                if (!k.On) continue;
                if (ui.Slider("   " + k.Format(k.Value, k.Effective), ref k.Value, k.Min, k.Max, k.Step))
                {
                    if (k.Step >= 1f) k.Value = Mathf.Round(k.Value / k.Step) * k.Step;
                    changed = true;
                }
            }

            ui.Header("Quality per part of the game");
            ui.Note("These are the game's own \"Quality\" levels (0-5), applied to one part only.", 2);
            var levels = new[] { "as the game", "0", "1", "2", "3", "4", "5" };
            for (int i = 0; i < SystemNames.Length; i++)
            {
                int index = _systemLevel[i] + 1;
                if (ui.Choice(SystemNames[i], ref index, levels))
                {
                    _systemLevel[i] = index - 1;
                    changed = true;
                }
            }

            ui.Header("Screen effects");
            if (_effectOriginal.Count == 0) ui.Note("Available once you are in a walk.");
            foreach (var kv in new List<KeyValuePair<string, bool>>(_effectOriginal))
            {
                changed |= ForcedToggle(ui, kv.Key, kv.Value, _effectForced);
            }

            ui.Space();
            ui.Toggle("Show the renderer's advanced features", ref _showAdvanced);
            if (_showAdvanced)
            {
                ui.Note("The game's render passes (ambient occlusion, clouds, blurs...). Switching some off can make the sky or the interface vanish: just switch them back on.", 3);
                foreach (var kv in new List<KeyValuePair<string, bool>>(_featureOriginal))
                {
                    changed |= ForcedToggle(ui, kv.Key, kv.Value, _featureForced);
                }
            }

            if (changed)
            {
                Dirty = true;
                foreach (Knob k in _knobs) k.Apply();
            }
        }

        // Three states: as the game set it, forced on, forced off.
        private static bool ForcedToggle(Ui ui, string name, bool original, Dictionary<string, bool> forced)
        {
            bool isForced = forced.TryGetValue(name, out bool value);
            int index = !isForced ? 0 : value ? 1 : 2;
            var options = new[] { original ? "game (on)" : "game (off)", "on", "off" };
            if (!ui.Choice(name, ref index, options)) return false;
            if (index == 0) forced.Remove(name);
            else forced[name] = index == 1;
            return true;
        }
    }
}
