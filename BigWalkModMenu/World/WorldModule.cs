using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Enviro;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BigWalk.ModMenu.World
{
    [BepInPlugin("com.bigwalk.modmenu.world", "Big Walk Mod Menu - World", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new WorldModule());
        }
    }

    /// <summary>Everything a picture filter is made of. All zero means "no filter".</summary>
    public sealed class FilterSettings
    {
        public float Exposure, Contrast, Saturation, Hue, Temperature, Tint, Sepia, Vignette, Grain, Aberration, Distortion;
        public bool Scanlines;

        public static readonly string[] Keys = { "Exposure", "Contrast", "Saturation", "Hue", "Temperature", "Tint", "Sepia", "Vignette", "Grain", "Aberration", "Distortion" };

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return Exposure; case 1: return Contrast; case 2: return Saturation; case 3: return Hue;
                    case 4: return Temperature; case 5: return Tint; case 6: return Sepia; case 7: return Vignette;
                    case 8: return Grain; case 9: return Aberration; default: return Distortion;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: Exposure = value; break; case 1: Contrast = value; break; case 2: Saturation = value; break; case 3: Hue = value; break;
                    case 4: Temperature = value; break; case 5: Tint = value; break; case 6: Sepia = value; break; case 7: Vignette = value; break;
                    case 8: Grain = value; break; case 9: Aberration = value; break; default: Distortion = value; break;
                }
            }
        }

        public bool IsNeutral
        {
            get
            {
                for (int i = 0; i < Keys.Length; i++) if (Mathf.Abs(this[i]) > 1e-4f) return false;
                return !Scanlines;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Keys.Length; i++) this[i] = 0f;
            Scanlines = false;
        }
    }

    public sealed class WorldModule : MenuModule
    {
        public override string Id => "world";
        public override string Title => "World";
        public override int Order => 50;

        // Time of day.
        private bool _fixTime, _timeApplied;
        private float _time = 12f, _timeWritten = -1f;

        // Sky, light and fog: multipliers on the game's own values.
        private readonly List<Knob> _knobs = new List<Knob>();

        // Picture filter.
        private readonly FilterSettings _filter = new FilterSettings();
        private bool _filterDirty = true;
        private GameObject _volumeObject;
        private Volume _volume;
        private ColorAdjustments _colour;
        private WhiteBalance _balance;
        private ChannelMixer _mixer;
        private Vignette _vignette;
        private FilmGrain _grain;
        private ChromaticAberration _aberration;
        private LensDistortion _distortion;
        private Texture2D _scanlines;
        private bool _volumeFailed;

        private static EnviroManager Env
        {
            get
            {
                try { return EnviroManager.instance; } catch { return null; }
            }
        }

        public WorldModule()
        {
            string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
            Func<float, float, string> pct = (v, e) => Pct(v);

            _knobs.Add(new Knob("Fog", "Fog", 0f, 8f, 0.1f, 1f, true,
                () => { var e = Env; return e != null && e.Fog != null ? e.Fog.customFogDensityModifer : float.NaN; },
                v => Env.Fog.customFogDensityModifer = v, pct));
            _knobs.Add(new Knob("Sun", "Sun and moon light", 0f, 4f, 0.05f, 1f, true,
                () => { var e = Env; return e != null && e.Lighting != null && e.Lighting.Settings != null ? e.Lighting.Settings.directLightIntensityModifier : float.NaN; },
                v => Env.Lighting.Settings.directLightIntensityModifier = v, pct));
            _knobs.Add(new Knob("Ambient", "Ambient light", 0f, 4f, 0.05f, 1f, true,
                () => { var e = Env; return e != null && e.Lighting != null && e.Lighting.Settings != null ? e.Lighting.Settings.ambientIntensityModifier : float.NaN; },
                v => Env.Lighting.Settings.ambientIntensityModifier = v, pct));
            _knobs.Add(new Knob("SkyIntensity", "Sky brightness", 0f, 4f, 0.05f, 1f, true,
                () => { var e = Env; return e != null && e.Sky != null && e.Sky.Settings != null ? e.Sky.Settings.intensity : float.NaN; },
                v => Env.Sky.Settings.intensity = v, pct));
            _knobs.Add(new Knob("SunScale", "Sun size", 0.2f, 15f, 0.1f, 1f, true,
                () => { var e = Env; return e != null && e.Sky != null && e.Sky.Settings != null ? e.Sky.Settings.sunScale : float.NaN; },
                v => Env.Sky.Settings.sunScale = v, pct));
            _knobs.Add(new Knob("MoonScale", "Moon size", 0.2f, 15f, 0.1f, 1f, true,
                () => { var e = Env; return e != null && e.Sky != null && e.Sky.Settings != null ? e.Sky.Settings.moonScale : float.NaN; },
                v => Env.Sky.Settings.moonScale = v, pct));
        }

        public override void Load(ModConfig c)
        {
            _fixTime = c.GetBool("Time.Fixed", false);
            _time = c.GetFloat("Time", 12f);
            foreach (Knob k in _knobs)
            {
                k.On = c.GetBool(k.Id + ".On", false);
                k.Value = Mathf.Clamp(c.GetFloat(k.Id, k.Default), k.Min, k.Max);
            }
            for (int i = 0; i < FilterSettings.Keys.Length; i++) _filter[i] = c.GetFloat("Filter." + FilterSettings.Keys[i], 0f);
            _filter.Scanlines = c.GetBool("Filter.Scanlines", false);
        }

        public override void Save(ModConfig c)
        {
            c.Set("Time.Fixed", _fixTime);
            c.Set("Time", _time);
            foreach (Knob k in _knobs)
            {
                c.Set(k.Id + ".On", k.On);
                c.Set(k.Id, k.Value);
            }
            for (int i = 0; i < FilterSettings.Keys.Length; i++) c.Set("Filter." + FilterSettings.Keys[i], _filter[i]);
            c.Set("Filter.Scanlines", _filter.Scanlines);
        }

        public override void Update()
        {
            if (!Game.InWorld)
            {
                _timeApplied = false;
                _timeWritten = -1f;
                return;
            }

            ApplyTime();
            foreach (Knob k in _knobs) k.Apply();
            ApplyFilter();
        }

        // The game has its own "fixed time" switch (it uses it for some places): only this player sees it.
        private void ApplyTime()
        {
            try
            {
                if (_fixTime)
                {
                    if (!_timeApplied || Mathf.Abs(_timeWritten - _time) > 1e-3f || Time.frameCount % 120 == 0)
                    {
                        SkyManager.SetFixedTime(_time);
                        _timeWritten = _time;
                        _timeApplied = true;
                    }
                }
                else if (_timeApplied)
                {
                    SkyManager.ClearFixedTime();
                    _timeApplied = false;
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("time", $"[World] Fixed time failed: {ex.Message}"); }
        }

        // ---- picture filter ----

        private T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            try
            {
                VolumeComponent c = profile.Add(Il2CppType.Of<T>(), false);
                return c != null ? c.Cast<T>() : null;
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[World] Effect {typeof(T).Name} is not available: {ex.Message}");
                return null;
            }
        }

        public override void OnObjectsLost()
        {
            _scanlines = null;
            _volume = null;
            _filterDirty = true;
        }

        private bool EnsureVolume()
        {
            if (_volume != null && !Game.Alive(_volume)) _volume = null;
            if (_volume != null) return true;
            if (_volumeFailed) return false;
            try
            {
                if (Game.Alive(_volumeObject)) UnityEngine.Object.Destroy(_volumeObject);
                _volumeObject = new GameObject("bwmm_filter_volume");
                UnityEngine.Object.DontDestroyOnLoad(_volumeObject);
                // Same layer as the game's own volume, so the player camera picks it up.
                try
                {
                    var pp = PostProcessingManager.instance;
                    if (pp != null && pp.basePPVolume != null) _volumeObject.layer = pp.basePPVolume.gameObject.layer;
                }
                catch { }

                _volume = _volumeObject.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 1000f;
                _volume.weight = 1f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
                _volume.sharedProfile = profile;

                _colour = Add<ColorAdjustments>(profile);
                _balance = Add<WhiteBalance>(profile);
                _mixer = Add<ChannelMixer>(profile);
                _vignette = Add<Vignette>(profile);
                _grain = Add<FilmGrain>(profile);
                _aberration = Add<ChromaticAberration>(profile);
                _distortion = Add<LensDistortion>(profile);
                ModMenuApi.Log.LogInfo($"[World] Filter volume created on layer {_volumeObject.layer}.");
                _filterDirty = true;
                return true;
            }
            catch (Exception ex)
            {
                _volumeFailed = true;
                ModMenuApi.Log.LogError($"[World] Could not create the filter volume: {ex}");
                return false;
            }
        }

        private static void Put(VolumeParameter<float> p, float value)
        {
            p.overrideState = true;
            p.value = value;
        }

        private void ApplyFilter()
        {
            FilterSettings f = _filter;
            if (f.IsNeutral && _volume == null) return; // nothing was ever switched on
            if (_volume != null && !Game.Alive(_volume)) { _volume = null; _filterDirty = true; }
            if (!_filterDirty && _volume != null) return;
            if (!EnsureVolume()) return;
            _filterDirty = false;

            if (_colour != null)
            {
                _colour.active = Mathf.Abs(f.Exposure) + Mathf.Abs(f.Contrast) + Mathf.Abs(f.Saturation) + Mathf.Abs(f.Hue) > 1e-4f;
                Put(_colour.postExposure, f.Exposure);
                Put(_colour.contrast, f.Contrast);
                Put(_colour.saturation, f.Saturation);
                Put(_colour.hueShift, f.Hue);
            }
            if (_balance != null)
            {
                _balance.active = Mathf.Abs(f.Temperature) + Mathf.Abs(f.Tint) > 1e-4f;
                Put(_balance.temperature, f.Temperature);
                Put(_balance.tint, f.Tint);
            }
            if (_mixer != null)
            {
                // Blend from "unchanged" to the classic sepia matrix.
                float s = Mathf.Clamp01(f.Sepia);
                _mixer.active = s > 1e-4f;
                Put(_mixer.redOutRedIn, Mathf.Lerp(100f, 39.3f, s));
                Put(_mixer.redOutGreenIn, Mathf.Lerp(0f, 76.9f, s));
                Put(_mixer.redOutBlueIn, Mathf.Lerp(0f, 18.9f, s));
                Put(_mixer.greenOutRedIn, Mathf.Lerp(0f, 34.9f, s));
                Put(_mixer.greenOutGreenIn, Mathf.Lerp(100f, 68.6f, s));
                Put(_mixer.greenOutBlueIn, Mathf.Lerp(0f, 16.8f, s));
                Put(_mixer.blueOutRedIn, Mathf.Lerp(0f, 27.2f, s));
                Put(_mixer.blueOutGreenIn, Mathf.Lerp(0f, 53.4f, s));
                Put(_mixer.blueOutBlueIn, Mathf.Lerp(100f, 13.1f, s));
            }
            if (_vignette != null)
            {
                _vignette.active = f.Vignette > 1e-4f;
                Put(_vignette.intensity, f.Vignette);
            }
            if (_grain != null)
            {
                _grain.active = f.Grain > 1e-4f;
                Put(_grain.intensity, f.Grain);
            }
            if (_aberration != null)
            {
                _aberration.active = f.Aberration > 1e-4f;
                Put(_aberration.intensity, f.Aberration);
            }
            if (_distortion != null)
            {
                _distortion.active = Mathf.Abs(f.Distortion) > 1e-4f;
                Put(_distortion.intensity, f.Distortion);
            }
            _volume.enabled = !f.IsNeutral;
        }

        public override void DrawOverlay()
        {
            if (!_filter.Scanlines || !Game.InWorld) return;
            if (!Game.Alive(_scanlines))
            {
                _scanlines = new Texture2D(1, 4, TextureFormat.RGBA32, false);
                _scanlines.hideFlags = HideFlags.HideAndDontSave;
                _scanlines.wrapMode = TextureWrapMode.Repeat;
                _scanlines.filterMode = FilterMode.Point;
                var px = new Il2CppStructArray<Color32>(4);
                px[0] = new Color32(0, 0, 0, 70);
                px[1] = new Color32(0, 0, 0, 25);
                px[2] = new Color32(0, 0, 0, 0);
                px[3] = new Color32(0, 0, 0, 25);
                _scanlines.SetPixels32(px);
                _scanlines.Apply(false);
            }
            GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, Screen.width, Screen.height), _scanlines, new Rect(0f, 0f, 1f, Screen.height / 4f));
        }

        // ---- menu ----

        private static readonly string[] PresetNames = { "None", "Black & white", "Sepia", "Vintage", "VHS", "Dream", "Warm", "Cool", "Vivid" };

        private void Preset(int index)
        {
            FilterSettings f = _filter;
            f.Clear();
            switch (index)
            {
                case 1: f.Saturation = -100f; f.Contrast = 15f; break;
                case 2: f.Sepia = 1f; f.Contrast = 8f; f.Vignette = 0.25f; break;
                case 3: f.Saturation = -25f; f.Contrast = 10f; f.Temperature = 22f; f.Vignette = 0.35f; f.Grain = 0.35f; f.Sepia = 0.25f; break;
                case 4: f.Saturation = 18f; f.Contrast = -8f; f.Aberration = 0.7f; f.Grain = 0.5f; f.Distortion = -0.18f; f.Scanlines = true; break;
                case 5: f.Saturation = 22f; f.Exposure = 0.25f; f.Vignette = 0.4f; f.Tint = 12f; f.Contrast = -12f; break;
                case 6: f.Temperature = 35f; f.Saturation = 8f; break;
                case 7: f.Temperature = -35f; f.Saturation = -5f; break;
                case 8: f.Saturation = 40f; f.Contrast = 12f; break;
            }
            _filterDirty = true;
            Dirty = true;
        }

        public override void DrawTab(Ui ui)
        {
            bool changed = false;

            ui.Header("Time of day");
            float now = -1f;
            try { now = SkyManager.GetCurrentTime(); } catch { }
            ui.Label(now >= 0f ? $"Game time now: {now:0.00}" : "Game time: not available");
            changed |= ui.Toggle("Freeze the time", ref _fixTime);
            if (_fixTime)
            {
                changed |= ui.Slider($"Time: {_time:0.0}", ref _time, 0f, 24f, 0.25f);
                int hit = ui.Buttons("Dawn", "Noon", "Sunset", "Night");
                if (hit >= 0)
                {
                    _time = new[] { 6f, 12f, 18.5f, 0f }[hit];
                    changed = true;
                }
            }
            ui.Note("Only you see it: other players keep the real time.");

            ui.Header("Sky, light and fog");
            foreach (Knob k in _knobs)
            {
                if (k.Broken) { ui.Note(k.Label + ": not available"); continue; }
                changed |= ui.Toggle(k.Label, ref k.On);
                if (k.On) changed |= ui.Slider("   " + k.Format(k.Value, k.Effective), ref k.Value, k.Min, k.Max, k.Step);
            }

            ui.Header("Filter");
            int row = 0;
            while (row < PresetNames.Length)
            {
                int n = Mathf.Min(3, PresetNames.Length - row);
                var labels = new string[n];
                for (int i = 0; i < n; i++) labels[i] = PresetNames[row + i];
                int hit = ui.Buttons(labels);
                if (hit >= 0) Preset(row + hit);
                row += n;
            }

            FilterSettings f = _filter;
            bool fc = false;
            fc |= ui.Slider($"Exposure: {f.Exposure:+0.00;-0.00;0}", ref f.Exposure, -3f, 3f, 0.05f);
            fc |= ui.Slider($"Contrast: {f.Contrast:+0;-0;0}", ref f.Contrast, -100f, 100f, 1f);
            fc |= ui.Slider($"Saturation: {f.Saturation:+0;-0;0}", ref f.Saturation, -100f, 100f, 1f);
            fc |= ui.Slider($"Hue: {f.Hue:+0;-0;0}°", ref f.Hue, -180f, 180f, 1f);
            fc |= ui.Slider($"Temperature: {f.Temperature:+0;-0;0}", ref f.Temperature, -100f, 100f, 1f);
            fc |= ui.Slider($"Tint: {f.Tint:+0;-0;0}", ref f.Tint, -100f, 100f, 1f);
            fc |= ui.Slider($"Sepia: {Mathf.RoundToInt(f.Sepia * 100f)}%", ref f.Sepia, 0f, 1f, 0.05f);
            fc |= ui.Slider($"Vignette: {Mathf.RoundToInt(f.Vignette * 100f)}%", ref f.Vignette, 0f, 1f, 0.05f);
            fc |= ui.Slider($"Grain: {Mathf.RoundToInt(f.Grain * 100f)}%", ref f.Grain, 0f, 1f, 0.05f);
            fc |= ui.Slider($"Chromatic aberration: {Mathf.RoundToInt(f.Aberration * 100f)}%", ref f.Aberration, 0f, 1f, 0.05f);
            fc |= ui.Slider($"Lens distortion: {f.Distortion:+0.00;-0.00;0}", ref f.Distortion, -1f, 1f, 0.02f);
            fc |= ui.Toggle("Old TV scanlines", ref f.Scanlines);
            ui.Note("Grain, aberration and distortion only work if the game ships their shaders: if you see no difference, it does not.", 2);
            if (_volumeFailed) ui.Note("<color=#ff8888>The filter could not start (see the log).</color>");

            if (fc)
            {
                _filterDirty = true;
                changed = true;
            }
            if (changed) Dirty = true;
        }
    }
}
