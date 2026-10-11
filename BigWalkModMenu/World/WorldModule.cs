using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Enviro;
using Il2CppInterop.Runtime;
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

        // The filter is made of eight effects; each one is switched on, and checked, on its own.
        public const int Effects = 8;
        public static readonly string[] EffectKeys = { "Colour", "Balance", "Sepia", "Vignette", "Grain", "Aberration", "Distortion", "Scanlines" };
        public static readonly string[] EffectNames = { "Colour", "White balance", "Sepia", "Vignette", "Grain", "Chromatic aberration", "Lens distortion", "Scanlines" };

        public bool Wants(int effect)
        {
            switch (effect)
            {
                case 0: return Mathf.Abs(Exposure) + Mathf.Abs(Contrast) + Mathf.Abs(Saturation) + Mathf.Abs(Hue) > 1e-4f;
                case 1: return Mathf.Abs(Temperature) + Mathf.Abs(Tint) > 1e-4f;
                case 2: return Sepia > 1e-4f;
                case 3: return Vignette > 1e-4f;
                case 4: return Grain > 1e-4f;
                case 5: return Aberration > 1e-4f;
                case 6: return Mathf.Abs(Distortion) > 1e-4f;
                default: return Scanlines;
            }
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
        private bool _volumeFailed;
        private int _rebuilds;
        private float _builtAt;

        // An effect is "live" once it is really switched on. One the game has not run yet is switched on alone,
        // with a note left on disk first: if the game stops within the next moments, the next start finds the
        // note, knows which effect did it and keeps that one off.
        private readonly bool[] _live = new bool[FilterSettings.Effects];
        private readonly bool[] _blocked = new bool[FilterSettings.Effects];
        private readonly bool[] _checked = new bool[FilterSettings.Effects];
        private string _checkedOn = "";
        private bool _checkedOnCompared;
        private int _checking = -1, _checkUntilFrame;
        private float _checkUntil;
        private const string CheckFile = "filter-check.txt";
        private const int VolumeEffects = 7; // the eighth, scanlines, is drawn over the picture

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
            ReadEffects(c.GetString("Filter.Blocked", ""), _blocked);
            ReadEffects(c.GetString("Filter.Checked", ""), _checked);
            _checkedOn = c.GetString("Filter.CheckedOn", "");
            FindInterruptedCheck();
        }

        private static void ReadEffects(string list, bool[] into)
        {
            Array.Clear(into, 0, into.Length);
            foreach (string key in list.Split(','))
            {
                int i = Array.IndexOf(FilterSettings.EffectKeys, key.Trim());
                if (i >= 0) into[i] = true;
            }
        }

        private static string WriteEffects(bool[] from)
        {
            var keys = new List<string>();
            for (int i = 0; i < from.Length; i++) if (from[i]) keys.Add(FilterSettings.EffectKeys[i]);
            return string.Join(",", keys);
        }

        // The note of a check that never finished: the game stopped while that effect was being tried.
        private void FindInterruptedCheck()
        {
            try
            {
                string path = Path.Combine(DataPath, CheckFile);
                if (!File.Exists(path)) return;
                int i = Array.IndexOf(FilterSettings.EffectKeys, File.ReadAllText(path).Trim());
                File.Delete(path);
                if (i < 0) return;
                _blocked[i] = true;
                _checked[i] = false;
                Dirty = true;
                ModMenuApi.Log.LogWarning($"[World] The game stopped right after the filter effect '{FilterSettings.EffectNames[i]}' was switched on last time: it stays off. It can be tried again from the World tab.");
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[World] Could not read the filter check note: {ex.Message}"); }
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
            c.Set("Filter.Blocked", WriteEffects(_blocked));
            c.Set("Filter.Checked", WriteEffects(_checked));
            c.Set("Filter.CheckedOn", _checkedOn);
        }

        public override void Update()
        {
            if (!Game.InWorld)
            {
                _timeApplied = false;
                _timeWritten = -1f;
                // Leaving the world (or the game) in the middle of a check says nothing about the effect.
                if (_checking >= 0)
                {
                    _live[_checking] = false;
                    EndCheck();
                    _filterDirty = true;
                }
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
            _volume = null;
            _filterDirty = true;
        }

        private const string VolumeName = "bwmm_filter_volume";

        private bool VolumeAlive()
        {
            if (_volume == null || !Game.Alive(_volume) || !Game.Alive(_volumeObject)) return false;
            // A part that could not be added stays null; one that was added must still be there.
            return (_colour == null || Game.Alive(_colour)) && (_balance == null || Game.Alive(_balance)) && (_mixer == null || Game.Alive(_mixer))
                && (_vignette == null || Game.Alive(_vignette)) && (_grain == null || Game.Alive(_grain))
                && (_aberration == null || Game.Alive(_aberration)) && (_distortion == null || Game.Alive(_distortion));
        }

        // Removes our volume, also one we lost the handle of: left behind it would keep its filter on for good.
        private void DestroyVolume()
        {
            _volume = null;
            _volumeObject = null;
            _colour = null; _balance = null; _mixer = null; _vignette = null; _grain = null; _aberration = null; _distortion = null;
            for (int i = 0; i < 8; i++)
            {
                GameObject old = GameObject.Find(VolumeName);
                if (old == null) break;
                old.name = VolumeName + "_old"; // Destroy only happens at the end of the frame
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
        }

        private bool EnsureVolume()
        {
            if (_volume != null && VolumeAlive()) return true;
            if (_volumeFailed) return false;
            try
            {
                bool rebuilt = _volume != null || _volumeObject != null;
                DestroyVolume();
                // Built again and again means something keeps taking it away: stop instead of doing it every frame.
                if (Time.unscaledTime - _builtAt > 60f) _rebuilds = 0;
                _builtAt = Time.unscaledTime;
                if (rebuilt && ++_rebuilds > 5) throw new InvalidOperationException("the filter volume was lost " + _rebuilds + " times");
                _volumeObject = new GameObject(VolumeName);
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

        private void BeginCheck(int effect)
        {
            _checking = effect;
            _checkUntilFrame = Time.frameCount + 90;
            _checkUntil = Time.unscaledTime + 2f;
            try { File.WriteAllText(Path.Combine(DataPath, CheckFile), FilterSettings.EffectKeys[effect]); }
            catch (Exception ex) { ModMenuApi.LogOnce("check", $"[World] Could not write the filter check note: {ex.Message}"); }
            ModMenuApi.Log.LogInfo($"[World] Filter: trying '{FilterSettings.EffectNames[effect]}'.");
        }

        private void EndCheck()
        {
            _checking = -1;
            try { File.Delete(Path.Combine(DataPath, CheckFile)); }
            catch (Exception ex) { ModMenuApi.LogOnce("check", $"[World] Could not remove the filter check note: {ex.Message}"); }
        }

        // Decides which effects are on this frame. Returns true when that changed.
        private bool StepEffects()
        {
            if (!_checkedOnCompared)
            {
                // What ran fine on another version of the game or of the mod is checked again.
                _checkedOnCompared = true;
                string now = ModMenuApi.Version;
                try { now = Application.version + "/" + ModMenuApi.Version; } catch { }
                if (_checkedOn != now)
                {
                    _checkedOn = now;
                    Array.Clear(_checked, 0, _checked.Length);
                    Dirty = true;
                }
            }

            FilterSettings f = _filter;
            bool changed = false;
            for (int i = 0; i < FilterSettings.Effects; i++)
            {
                if (_live[i] && (!f.Wants(i) || _blocked[i]))
                {
                    _live[i] = false;
                    changed = true;
                    if (_checking == i) EndCheck();
                }
            }
            if (_checking >= 0 && Time.frameCount >= _checkUntilFrame && Time.unscaledTime >= _checkUntil)
            {
                ModMenuApi.Log.LogInfo($"[World] Filter: '{FilterSettings.EffectNames[_checking]}' runs fine.");
                _checked[_checking] = true;
                Dirty = true;
                EndCheck();
            }
            for (int i = 0; i < FilterSettings.Effects; i++)
            {
                if (_live[i] || _blocked[i] || !f.Wants(i)) continue;
                if (!_checked[i])
                {
                    if (_checking >= 0) continue; // one at a time, so the note names the right one
                    BeginCheck(i);
                }
                _live[i] = true;
                changed = true;
            }
            return changed;
        }

        private bool AnyVolumeEffectLive()
        {
            for (int i = 0; i < VolumeEffects; i++) if (_live[i]) return true;
            return false;
        }

        private void ApplyFilter()
        {
            if (StepEffects()) _filterDirty = true;
            if (_volume == null && !AnyVolumeEffectLive()) return; // nothing was ever switched on
            if (_volume != null && !VolumeAlive()) _filterDirty = true;
            if (!_filterDirty) return;
            if (!EnsureVolume()) return;
            _filterDirty = false;

            try
            {
                WriteVolume();
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // A part of the volume went away under our hands: build it again on the next frame.
                _filterDirty = true;
            }
        }

        private void WriteVolume()
        {
            FilterSettings f = _filter;
            if (_colour != null)
            {
                _colour.active = _live[0];
                Put(_colour.postExposure, f.Exposure);
                Put(_colour.contrast, f.Contrast);
                Put(_colour.saturation, f.Saturation);
                Put(_colour.hueShift, f.Hue);
            }
            if (_balance != null)
            {
                _balance.active = _live[1];
                Put(_balance.temperature, f.Temperature);
                Put(_balance.tint, f.Tint);
            }
            if (_mixer != null)
            {
                // Blend from "unchanged" to the classic sepia matrix.
                float s = Mathf.Clamp01(f.Sepia);
                _mixer.active = _live[2];
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
                _vignette.active = _live[3];
                Put(_vignette.intensity, _live[3] ? f.Vignette : 0f);
            }
            if (_grain != null)
            {
                _grain.active = _live[4];
                Put(_grain.intensity, _live[4] ? f.Grain : 0f);
            }
            if (_aberration != null)
            {
                _aberration.active = _live[5];
                Put(_aberration.intensity, _live[5] ? f.Aberration : 0f);
            }
            if (_distortion != null)
            {
                _distortion.active = _live[6];
                Put(_distortion.intensity, _live[6] ? f.Distortion : 0f);
            }
            _volume.enabled = AnyVolumeEffectLive();
        }

        // Dark lines drawn with the engine's own white texture: nothing of ours is kept between frames.
        public override void DrawOverlay()
        {
            if (!_live[7] || !Game.InWorld) return;
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            Texture2D white = Texture2D.whiteTexture;
            if (white == null) return;

            float pitch = Mathf.Max(4f, Mathf.Ceil(Screen.height / 270f));
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.2f);
            for (float y = 0f; y < Screen.height; y += pitch)
            {
                GUI.DrawTexture(new Rect(0f, y, Screen.width, pitch * 0.5f), white);
            }
            GUI.color = previous;
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
            if (_checking >= 0) ui.Note($"<color=#ffee88>Trying {FilterSettings.EffectNames[_checking]} for the first time: effects come on one at a time.</color>");
            for (int i = 0; i < FilterSettings.Effects; i++)
            {
                if (!_blocked[i]) continue;
                ui.Note($"<color=#ff8888>{FilterSettings.EffectNames[i]} is kept off: the game stopped right after it was switched on.</color>", 2);
                if (ui.Button($"Try {FilterSettings.EffectNames[i]} again"))
                {
                    _blocked[i] = false;
                    _checked[i] = false;
                    fc = true;
                }
            }

            if (fc)
            {
                _filterDirty = true;
                changed = true;
            }
            if (changed) Dirty = true;
        }
    }
}
