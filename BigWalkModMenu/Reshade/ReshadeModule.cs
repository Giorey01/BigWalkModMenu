using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BigWalk.ModMenu.Reshade
{
    [BepInPlugin("com.bigwalk.modmenu.reshade", "Big Walk Mod Menu - Reshade", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new ReshadeModule());
        }
    }

    /// <summary>One look: every value the tab can change. Index access keeps load, save and presets short.</summary>
    public sealed class Look
    {
        public const int Bloom = 0, BloomThreshold = 1, BloomScatter = 2, BloomTint = 3, Dirt = 4, Flare = 5, Streaks = 6,
            Tonemap = 7, Exposure = 8, Contrast = 9, Saturation = 10, Shadows = 11, Highlights = 12, Sharpen = 13, Count = 14;

        public static readonly string[] Keys =
        {
            "Bloom", "BloomThreshold", "BloomScatter", "BloomTint", "Dirt", "Flare", "Streaks",
            "Tonemap", "Exposure", "Contrast", "Saturation", "Shadows", "Highlights", "Sharpen"
        };

        // Bloom threshold and scatter only matter while the bloom is on: their resting value is not zero.
        public static readonly float[] Neutral = { 0f, 0.9f, 0.7f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };

        public readonly float[] V = (float[])Neutral.Clone();

        public float this[int i]
        {
            get => V[i];
            set => V[i] = value;
        }

        public void Set(params float[] values)
        {
            for (int i = 0; i < Count; i++) V[i] = i < values.Length ? values[i] : Neutral[i];
        }
    }

    /// <summary>
    /// The look of a ReShade preset rebuilt with the game's own renderer: a global URP volume of ours
    /// (bloom with lens dirt, screen-space lens flares, filmic tone mapping, tone and colour) plus the
    /// sharpening pass that URP runs after temporal antialiasing. No injected DLL and no shader of ours.
    /// </summary>
    public sealed class ReshadeModule : MenuModule
    {
        public override string Id => "reshade";
        public override string Title => "Reshade";
        public override int Order => 52;

        private static readonly string[] TonemapNames = { "game's own", "neutral", "filmic (ACES)" };
        private static readonly string[] PresetNames = { "Cinematic", "Vivid", "Dream", "Dramatic", "Sharpen only" };

        private readonly Hotkey _toggleKey = new Hotkey("Toggle", "Effects on/off", KeyCode.None);
        private readonly Look _look = new Look();
        private bool _enabled;
        private float _strength = 1f;
        private bool _dirty = true;

        private GameObject _volumeObject;
        private Volume _volume;
        private Bloom _bloom;
        private ScreenSpaceLensFlare _flare;
        private Tonemapping _tonemap;
        private ColorAdjustments _colour;
        private ShadowsMidtonesHighlights _smh;
        private Texture2D _dirt;
        private bool _volumeFailed, _reported;

        // Sharpening borrows the player camera's antialiasing mode.
        private UniversalAdditionalCameraData _cameraData;
        private AntialiasingMode _savedAa;
        private float _savedCas;
        private bool _sharpening;
        private const string Owner = "reshade";

        public ReshadeModule()
        {
            ApplyPreset(0); // what a first-time user sees when switching the tab on
        }

        public override void Load(ModConfig c)
        {
            _toggleKey.Load(c);
            _enabled = c.GetBool("Enabled", false);
            _strength = Mathf.Clamp01(c.GetFloat("Strength", 1f));
            for (int i = 0; i < Look.Count; i++) _look[i] = c.GetFloat(Look.Keys[i], _look[i]);
            _dirty = true;
        }

        public override void Save(ModConfig c)
        {
            _toggleKey.Save(c);
            c.Set("Enabled", _enabled);
            c.Set("Strength", _strength);
            for (int i = 0; i < Look.Count; i++) c.Set(Look.Keys[i], _look[i]);
        }

        private void ApplyPreset(int index)
        {
            Look l = _look;
            switch (index)
            {
                // "Cinematic Reshade": cool bloom with lens dirt, lens flares, filmic curve, vivid tone, sharpening.
                //             bloom thr   scat  tint  dirt  flare streak tone exp    con  sat  shad   high  sharp
                case 0: l.Set(1.1f, 0.9f, 0.75f, -0.5f, 1.2f, 0.6f, 0.15f, 2f, 0.5f, 8f, 10f, 0.15f, 0f, 0.8f); break;
                case 1: l.Set(0.5f, 1.0f, 0.65f, 0f, 0f, 0f, 0f, 1f, 0.1f, 14f, 30f, 0.05f, 0f, 0.6f); break;
                case 2: l.Set(2.4f, 0.6f, 0.9f, 0.4f, 0.6f, 0.3f, 0f, 1f, 0.2f, -10f, 6f, 0.2f, 0.1f, 0f); break;
                case 3: l.Set(0.8f, 1.0f, 0.7f, -0.3f, 0.5f, 0.4f, 0.4f, 2f, 0.25f, 28f, -12f, -0.08f, -0.05f, 0.5f); break;
                default: l.Set(); l[Look.Sharpen] = 0.8f; break;
            }
            _dirty = true;
        }

        // ---- volume ----

        public override void OnObjectsLost()
        {
            _volume = null;
            _dirt = null;
            _cameraData = null;
            _dirty = true;
        }

        private T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            try
            {
                VolumeComponent c = profile.Add(Il2CppType.Of<T>(), false);
                return c != null ? c.Cast<T>() : null;
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[Reshade] Effect {typeof(T).Name} is not available: {ex.Message}");
                return null;
            }
        }

        private bool EnsureVolume()
        {
            if (_volume != null && !Game.Alive(_volume)) _volume = null;
            if (_volume != null) return true;
            if (_volumeFailed) return false;
            try
            {
                if (Game.Alive(_volumeObject)) UnityEngine.Object.Destroy(_volumeObject);
                _volumeObject = new GameObject("bwmm_reshade_volume");
                UnityEngine.Object.DontDestroyOnLoad(_volumeObject);
                try
                {
                    var pp = PostProcessingManager.instance;
                    if (pp != null && pp.basePPVolume != null) _volumeObject.layer = pp.basePPVolume.gameObject.layer;
                }
                catch { }

                _volume = _volumeObject.AddComponent<Volume>();
                _volume.isGlobal = true;
                // Above the game's volumes, below the colour filter of the World tab (1000), which wins where both act.
                _volume.priority = 900f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
                _volume.sharedProfile = profile;

                _bloom = Add<Bloom>(profile);
                _flare = Add<ScreenSpaceLensFlare>(profile);
                _tonemap = Add<Tonemapping>(profile);
                _colour = Add<ColorAdjustments>(profile);
                _smh = Add<ShadowsMidtonesHighlights>(profile);
                ModMenuApi.Log.LogInfo($"[Reshade] Volume created on layer {_volumeObject.layer}.");
                _dirty = true;
                return true;
            }
            catch (Exception ex)
            {
                _volumeFailed = true;
                ModMenuApi.Log.LogError($"[Reshade] Could not create the volume: {ex}");
                return false;
            }
        }

        private static void Put(VolumeParameter<float> p, float value)
        {
            p.overrideState = true;
            p.value = value;
        }

        // -1 = cool blue (the preset's bloom tint), 0 = white, +1 = warm.
        private static Color TintColour(float t)
        {
            var cool = new Color(0.62f, 0.78f, 1f, 1f);
            var warm = new Color(1f, 0.8f, 0.58f, 1f);
            return t < 0f ? Color.Lerp(Color.white, cool, -t) : Color.Lerp(Color.white, warm, t);
        }

        private void ApplyVolume()
        {
            bool on = _enabled && _strength > 0.001f;
            if (!on && _volume == null) return;
            if (_volume != null && !Game.Alive(_volume)) { _volume = null; _dirty = true; }
            if (!_dirty && _volume != null) return;
            if (!EnsureVolume()) return;
            _dirty = false;
            Look l = _look;

            if (_bloom != null)
            {
                _bloom.active = l[Look.Bloom] > 1e-3f;
                Put(_bloom.intensity, l[Look.Bloom]);
                Put(_bloom.threshold, l[Look.BloomThreshold]);
                Put(_bloom.scatter, l[Look.BloomScatter]);
                _bloom.tint.overrideState = true;
                _bloom.tint.value = TintColour(l[Look.BloomTint]);
                _bloom.highQualityFiltering.overrideState = true;
                _bloom.highQualityFiltering.value = true;
                _bloom.dirtTexture.overrideState = true;
                _bloom.dirtTexture.value = l[Look.Dirt] > 1e-3f ? DirtTexture : null;
                Put(_bloom.dirtIntensity, l[Look.Dirt]);
            }
            if (_flare != null)
            {
                // The flares are built from the bloom's blurred picture: without bloom there is nothing to draw them from.
                _flare.active = l[Look.Flare] > 1e-3f && l[Look.Bloom] > 1e-3f;
                Put(_flare.intensity, l[Look.Flare]);
                _flare.tintColor.overrideState = true;
                _flare.tintColor.value = TintColour(l[Look.BloomTint] * 0.6f);
                Put(_flare.firstFlareIntensity, 1f);
                Put(_flare.secondaryFlareIntensity, 1f);
                Put(_flare.warpedFlareIntensity, 1f);
                Put(_flare.streaksIntensity, l[Look.Streaks] * 4f);
                Put(_flare.streaksLength, 0.5f);
                Put(_flare.chromaticAbberationIntensity, 0.5f);
            }
            if (_tonemap != null)
            {
                int mode = Mathf.Clamp(Mathf.RoundToInt(l[Look.Tonemap]), 0, 2);
                _tonemap.active = mode > 0;
                _tonemap.mode.overrideState = true;
                _tonemap.mode.value = mode == 2 ? TonemappingMode.ACES : TonemappingMode.Neutral;
            }
            if (_colour != null)
            {
                _colour.active = Mathf.Abs(l[Look.Exposure]) + Mathf.Abs(l[Look.Contrast]) + Mathf.Abs(l[Look.Saturation]) > 1e-3f;
                Put(_colour.postExposure, l[Look.Exposure]);
                Put(_colour.contrast, l[Look.Contrast]);
                Put(_colour.saturation, l[Look.Saturation]);
            }
            if (_smh != null)
            {
                _smh.active = Mathf.Abs(l[Look.Shadows]) + Mathf.Abs(l[Look.Highlights]) > 1e-3f;
                // The fourth component is the brightness offset of that range; colour is left white.
                _smh.shadows.overrideState = true;
                _smh.shadows.value = new Vector4(1f, 1f, 1f, l[Look.Shadows]);
                _smh.highlights.overrideState = true;
                _smh.highlights.value = new Vector4(1f, 1f, 1f, l[Look.Highlights]);
            }
            _volume.weight = _strength;
            _volume.enabled = on;
        }

        // ---- lens dirt ----

        // Soft out-of-focus specks, the kind a dusty lens shows against a bright light. Drawn once, in code.
        private Texture2D DirtTexture
        {
            get
            {
                if (Game.Alive(_dirt)) return _dirt;
                const int w = 512, h = 288;
                var buffer = new float[w * h];
                var rng = new System.Random(7);
                for (int n = 0; n < 150; n++)
                {
                    float cx = (float)rng.NextDouble() * w, cy = (float)rng.NextDouble() * h;
                    float r = n < 14 ? 30f + (float)rng.NextDouble() * 40f : 3f + (float)(rng.NextDouble() * rng.NextDouble()) * 22f;
                    float strength = (n < 14 ? 0.10f : 0.22f) * (0.4f + (float)rng.NextDouble() * 0.6f);
                    int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(w - 1, (int)(cx + r));
                    int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(h - 1, (int)(cy + r));
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                            if (d >= 1f) continue;
                            // Flat disc with a slightly brighter rim, like a bokeh spot.
                            float a = d < 0.82f ? 0.75f + 0.25f * (d / 0.82f) : (1f - d) / 0.18f;
                            buffer[y * w + x] += strength * a;
                        }
                    }
                }
                var pixels = new Il2CppStructArray<Color32>(w * h);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        // More dirt shows towards the edges of the picture.
                        float ex = (x - w * 0.5f) / (w * 0.5f), ey = (y - h * 0.5f) / (h * 0.5f);
                        float edge = 0.45f + 0.55f * Mathf.Clamp01(ex * ex + ey * ey);
                        byte v = (byte)(Mathf.Clamp01(buffer[y * w + x] * edge) * 255f);
                        pixels[y * w + x] = new Color32(v, v, v, 255);
                    }
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.SetPixels32(pixels);
                tex.Apply(false);
                _dirt = tex;
                return tex;
            }
        }

        // ---- sharpening ----

        private UniversalAdditionalCameraData CameraData
        {
            get
            {
                Camera cam = Game.PlayerCamera;
                if (cam == null) return null;
                if (!Game.Alive(_cameraData) || _cameraData == null || _cameraData.gameObject != cam.gameObject)
                {
                    _cameraData = cam.GetComponent<UniversalAdditionalCameraData>();
                    _sharpening = false; // a new camera: nothing of ours is on it
                }
                return _cameraData;
            }
        }

        private void ApplySharpen()
        {
            float wanted = _enabled ? Mathf.Clamp01(_look[Look.Sharpen] * _strength) : 0f;
            try
            {
                UniversalAdditionalCameraData data = CameraData;
                if (data == null)
                {
                    if (_sharpening) { _sharpening = false; ReleaseAa(); }
                    return;
                }
                if (wanted > 0.001f)
                {
                    if (!_sharpening)
                    {
                        if (GraphicsShare.AntialiasingOwner != null && GraphicsShare.AntialiasingOwner != Owner) return;
                        _savedAa = data.antialiasing;
                        _savedCas = data.m_TaaSettings.m_ContrastAdaptiveSharpening;
                        GraphicsShare.AntialiasingOwner = Owner;
                        _sharpening = true;
                    }
                    if (data.antialiasing != AntialiasingMode.TemporalAntiAliasing) data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
                    TemporalAA.Settings taa = data.m_TaaSettings;
                    if (Mathf.Abs(taa.m_ContrastAdaptiveSharpening - wanted) > 1e-3f)
                    {
                        taa.m_ContrastAdaptiveSharpening = wanted;
                        data.m_TaaSettings = taa;
                    }
                }
                else if (_sharpening)
                {
                    _sharpening = false;
                    TemporalAA.Settings taa = data.m_TaaSettings;
                    taa.m_ContrastAdaptiveSharpening = _savedCas;
                    data.m_TaaSettings = taa;
                    data.antialiasing = _savedAa;
                    ReleaseAa();
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("reshade-sharpen", $"[Reshade] Sharpening failed: {ex}"); }
        }

        private static void ReleaseAa()
        {
            if (GraphicsShare.AntialiasingOwner == Owner) GraphicsShare.AntialiasingOwner = null;
        }

        // ---- per frame ----

        public override void Update()
        {
            if (_toggleKey.Down)
            {
                _enabled = !_enabled;
                _dirty = true;
                Dirty = true;
                ModMenuApi.Toast(_enabled ? "Reshade: on" : "Reshade: off");
            }
            if (!Game.InWorld) return;
            if (!_reported) Report();
            ApplyVolume();
            if (Time.frameCount % 10 == 0) ApplySharpen();
        }

        // What the game renders with, written once: the values our effects sit on top of.
        private void Report()
        {
            _reported = true;
            try
            {
                var urp = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.TryCast<UniversalRenderPipelineAsset>() : null;
                Camera cam = Game.PlayerCamera;
                var data = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
                string hdr = urp != null ? urp.supportsHDR + "/" + urp.hdrColorBufferPrecision : "?";
                string grading = urp != null ? urp.colorGradingMode.ToString() : "?";
                string aa = data != null ? data.antialiasing.ToString() : "?";
                string stack = "?";
                try { stack = data != null && data.cameraStack != null ? data.cameraStack.Count.ToString() : "0"; } catch { }
                ModMenuApi.Log.LogInfo($"[Reshade] Renderer: HDR {hdr}, grading {grading}, camera AA {aa}, MSAA {(urp != null ? urp.msaaSampleCount : 0)}, stacked cameras {stack}, post-processing {(data != null && data.renderPostProcessing)}.");
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Reshade] Renderer report failed: {ex.Message}"); }
        }

        // ---- menu ----

        private bool Value(Ui ui, string label, int i, float min, float max, float step, string format = "0.00")
        {
            float v = _look[i];
            if (!ui.Slider(label + ": " + v.ToString(format), ref v, min, max, step)) return false;
            _look[i] = v;
            return true;
        }

        public override void DrawTab(Ui ui)
        {
            bool changed = false;

            ui.Header("Reshade");
            changed |= ui.Toggle("Effects", ref _enabled);
            changed |= ui.HotkeyRow(_toggleKey);
            changed |= ui.Slider($"Overall strength: {Mathf.RoundToInt(_strength * 100f)}%", ref _strength, 0f, 1f, 0.05f);
            ui.Note("Built with the game's own renderer: no ReShade install needed. Only you see it, and it shows up in photos too.", 2);

            ui.Header("Ready-made looks");
            int row = 0;
            while (row < PresetNames.Length)
            {
                int n = Mathf.Min(3, PresetNames.Length - row);
                var labels = new string[n];
                for (int i = 0; i < n; i++) labels[i] = PresetNames[row + i];
                int hit = ui.Buttons(labels);
                if (hit >= 0)
                {
                    ApplyPreset(row + hit);
                    _enabled = true;
                    changed = true;
                }
                row += n;
            }

            ui.Header("Bloom");
            changed |= Value(ui, "Intensity", Look.Bloom, 0f, 5f, 0.05f);
            if (_look[Look.Bloom] > 1e-3f)
            {
                changed |= Value(ui, "Threshold (lower = more glow)", Look.BloomThreshold, 0f, 2f, 0.05f);
                changed |= Value(ui, "Halo width", Look.BloomScatter, 0f, 1f, 0.05f);
                changed |= Value(ui, "Tint (− cool, + warm)", Look.BloomTint, -1f, 1f, 0.05f, "+0.00;-0.00;0");
                changed |= Value(ui, "Lens dirt", Look.Dirt, 0f, 4f, 0.1f, "0.0");
                changed |= Value(ui, "Lens flares", Look.Flare, 0f, 3f, 0.05f);
                if (_look[Look.Flare] > 1e-3f) changed |= Value(ui, "Light streaks", Look.Streaks, 0f, 1f, 0.05f);
            }
            else
            {
                ui.Note("Lens dirt and flares come from the bloom: they can be set while it is on.");
            }

            ui.Header("Tone and colour");
            int tone = Mathf.Clamp(Mathf.RoundToInt(_look[Look.Tonemap]), 0, 2);
            if (ui.Choice("Tone curve", ref tone, TonemapNames))
            {
                _look[Look.Tonemap] = tone;
                changed = true;
            }
            changed |= Value(ui, "Exposure", Look.Exposure, -2f, 2f, 0.05f, "+0.00;-0.00;0");
            changed |= Value(ui, "Contrast", Look.Contrast, -50f, 50f, 1f, "+0;-0;0");
            changed |= Value(ui, "Saturation", Look.Saturation, -60f, 60f, 1f, "+0;-0;0");
            changed |= Value(ui, "Shadows (+ lifts them)", Look.Shadows, -0.5f, 0.5f, 0.01f, "+0.00;-0.00;0");
            changed |= Value(ui, "Highlights", Look.Highlights, -0.5f, 0.5f, 0.01f, "+0.00;-0.00;0");
            ui.Note("If you also use the Filter in the World tab, it decides exposure, contrast and saturation.", 2);

            ui.Header("Sharpening");
            changed |= Value(ui, "Sharpening", Look.Sharpen, 0f, 1f, 0.05f);
            if (_look[Look.Sharpen] > 1e-3f)
            {
                if (_enabled && !_sharpening && GraphicsShare.AntialiasingOwner != null && GraphicsShare.AntialiasingOwner != Owner)
                    ui.Note("<color=#ffaa66>Another tab is using the antialiasing: sharpening is waiting.</color>");
                else
                    ui.Note("Switches antialiasing to TAA, because the game only sharpens after that. While it is on, \"Screen antialiasing\" in the Performance tab is on hold.", 3);
            }
            if (_volumeFailed) ui.Note("<color=#ff8888>The effects could not start (see the log).</color>");

            if (changed)
            {
                _dirty = true;
                Dirty = true;
            }
        }
    }
}
