using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Dissonance.Audio.Playback;
using HarmonyLib;
using UnityEngine;

namespace BigWalk.ModMenu.Voice
{
    [BepInPlugin("com.bigwalk.modmenu.voice", "Big Walk Mod Menu - Voce", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new VoiceModule());
            var harmony = new Harmony("com.bigwalk.modmenu.voice");
            ModMenuApi.ApplyPatch(harmony, typeof(VoiceControlUpdatePatch));
            ModMenuApi.ApplyPatch(harmony, typeof(VoiceMakeupGainPatch));
        }
    }

    /// <summary>The volume chosen for one person, remembered by platform id from one session to the next.</summary>
    public sealed class Listener
    {
        public string Key;      // platform id; empty for a character the game has no id for (kept for this session only)
        public string Name = "";
        public float Volume = 1f;
    }

    /// <summary>One voice playing in the world right now.</summary>
    public sealed class Voice
    {
        public PlayerVoicePlaybackControl Control;
        public Listener Listener;   // null for my own character
        public float NextResolve;
        public float Base = 1f, Written = float.NaN;
        public float Level, Peak;
        public bool Clipping;
    }

    /// <summary>
    /// Per-player voice volume. The game levels every voice on its own: each frame
    /// PlayerVoicePlaybackControl.Update asks VoiceMakeupGain.Evaluate for a gain and stores it in the
    /// player's SamplePlaybackComponent.MakeupGain, which the audio thread applies before the game's
    /// compressor and soft clipper. We multiply that gain, so a boosted voice goes through the same
    /// limiter as any other and no code of ours runs on the audio thread.
    /// </summary>
    public sealed class VoiceModule : MenuModule
    {
        public const float MaxVolume = 15f;
        public const float Step = 0.25f;

        public static VoiceModule Instance;

        public override string Id => "voice";
        public override string Title => "Voce";
        public override int Order => 55;

        private readonly Hotkey _muteKey = new Hotkey("MuteAll", "Silenzia/riattiva tutte le voci", KeyCode.None);
        private bool _enabled = true, _muteAll, _showRemembered;
        private float _defaultVolume = 1f;

        private readonly Dictionary<string, Listener> _known = new Dictionary<string, Listener>(StringComparer.Ordinal);
        private readonly Dictionary<IntPtr, Voice> _voices = new Dictionary<IntPtr, Voice>();
        private readonly List<Voice> _present = new List<Voice>();
        private readonly List<IntPtr> _gone = new List<IntPtr>();
        private float _nextSweep;

        // Set by the Update patch for the length of one PlayerVoicePlaybackControl.Update call.
        internal float PendingMultiplier = 1f;
        internal bool EvaluateSeen;

        public VoiceModule()
        {
            Instance = this;
        }

        public override void Load(ModConfig c)
        {
            _muteKey.Load(c);
            _enabled = c.GetBool("Enabled", true);
            _defaultVolume = Clamp(c.GetFloat("DefaultVolume", 1f));
            foreach (string key in c.Keys)
            {
                if (!key.StartsWith("Vol.", StringComparison.Ordinal)) continue;
                string id = key.Substring(4);
                if (id.Length == 0) continue;
                _known[id] = new Listener { Key = id, Volume = Clamp(c.GetFloat(key, 1f)), Name = c.GetString("Name." + id, "") };
            }
        }

        public override void Save(ModConfig c)
        {
            _muteKey.Save(c);
            c.Set("Enabled", _enabled);
            c.Set("DefaultVolume", _defaultVolume);
            foreach (string key in c.Keys)
            {
                if (key.StartsWith("Vol.", StringComparison.Ordinal) || key.StartsWith("Name.", StringComparison.Ordinal)) c.Remove(key);
            }
            foreach (Listener l in _known.Values)
            {
                c.Set("Vol." + l.Key, l.Volume);
                // One line per value in the file: a name cannot bring its own line break.
                c.Set("Name." + l.Key, (l.Name ?? "").Replace('\n', ' ').Replace('\r', ' '));
            }
        }

        private static float Clamp(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 1f : Mathf.Clamp(v, 0f, MaxVolume);

        // ---- called from the patches ----

        internal Voice VoiceFor(PlayerVoicePlaybackControl control)
        {
            IntPtr ptr = control.Pointer;
            if (!_voices.TryGetValue(ptr, out Voice v))
            {
                v = new Voice { Control = control };
                _voices[ptr] = v;
            }
            if (v.Listener == null && Time.unscaledTime >= v.NextResolve)
            {
                v.NextResolve = Time.unscaledTime + 1f;
                Resolve(v);
            }
            return v;
        }

        // The platform id arrives a moment after the character does: until then the default volume applies.
        private void Resolve(Voice v)
        {
            PlayerCharacter pc = v.Control.playerCharacter;
            if (pc == null || pc.isLocalPlayer) return;
            string key = Identity.Key(pc);
            string name = Identity.Name(pc);
            if (name.Length == 0)
            {
                try { name = pc.playerNetworking != null ? pc.playerNetworking.moderationName ?? "" : ""; } catch { }
            }

            if (key.Length == 0)
            {
                // No id (a Solo Mod character, or a player still joining): a volume for this session only.
                if (v.Listener == null && pc.netId != 0 && Time.unscaledTime > 0f)
                {
                    PlayerNetworking pn = pc.playerNetworking;
                    bool idWillNeverCome = pn != null && pn.userPlatformId == 0 && string.IsNullOrEmpty(pn.epicUserId) && pc.isServer;
                    if (idWillNeverCome) v.Listener = new Listener { Key = "", Name = name, Volume = _defaultVolume };
                }
                return;
            }

            if (!_known.TryGetValue(key, out Listener l))
            {
                l = new Listener { Key = key, Name = name, Volume = _defaultVolume };
                _known[key] = l;
                Dirty = true;
                ModMenuApi.Log.LogInfo($"[Voice] New player '{name}' ({key}): volume {l.Volume:0.##}x");
            }
            else if (name.Length > 0 && l.Name != name)
            {
                l.Name = name;
                Dirty = true;
            }
            v.Listener = l;
        }

        internal float MultiplierFor(Voice v)
        {
            if (!_enabled) return 1f;
            PlayerCharacter pc = v.Control.playerCharacter;
            if (pc != null && pc.isLocalPlayer) return 1f;
            if (_muteAll) return 0f;
            return v.Listener != null ? v.Listener.Volume : _defaultVolume;
        }

#if DEV
        // Runs the game's gain function through our patch, with and without a multiplier in force.
        public override string DevCommand(string[] args)
        {
            float plain = VoiceMakeupGain.Evaluate("bwmm-selftest", 0.05f, true, 0.02f);
            bool seen = EvaluateSeen;
            PendingMultiplier = 3f;
            float boosted = VoiceMakeupGain.Evaluate("bwmm-selftest", 0.05f, true, 0f);
            PendingMultiplier = 1f;
            EvaluateSeen = false;
            var info = HarmonyLib.Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(typeof(PlayerVoicePlaybackControl), "Update"));
            int controls = PlayerVoicePlaybackControl.controls != null ? PlayerVoicePlaybackControl.controls.Count : -1;
            return $"evaluate plain={plain:0.####} x3={boosted:0.####} ratio={(plain > 0f ? boosted / plain : 0f):0.###} patchRan={seen} updatePrefixes={(info != null ? info.Prefixes.Count : -1)} updatePostfixes={(info != null ? info.Postfixes.Count : -1)} controls={controls} known={_known.Count} gameVoiceVolume={VoiceMakeupGain.Volume:0.##}";
        }
#endif

        // ---- per frame ----

        public override void Update()
        {
            if (_muteKey.Down)
            {
                _muteAll = !_muteAll;
                ModMenuApi.Toast(_muteAll ? "Voci degli altri: silenziate" : "Voci degli altri: riattivate");
            }

            if (Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + 2f;
            _gone.Clear();
            foreach (var kv in _voices)
            {
                if (!Game.Alive(kv.Value.Control) || kv.Value.Control == null) _gone.Add(kv.Key);
            }
            foreach (IntPtr p in _gone) _voices.Remove(p);
        }

        public override void OnObjectsLost()
        {
            _voices.Clear();
        }

        private void ReadMeters(Voice v)
        {
            v.Level = 0f;
            v.Peak = 0f;
            v.Clipping = false;
            SamplePlaybackComponent sp = v.Control.dissonanceSampleProvider;
            if (sp == null || !sp.HasActiveSession) return;
            v.Level = sp.OutputARV;
            v.Peak = sp.PreClipPeak;
            // Above the knee the game's soft clipper is bending the wave: audible as distortion.
            v.Clipping = v.Peak > 1.2f;
        }

        // ---- menu ----

        private static string Percent(float volume) => Mathf.RoundToInt(volume * 100f) + "%";

        // The slider is curved (position squared) so that 0-200% takes half of it and the boost the rest.
        private static float ToSlider(float volume) => MathF.Sqrt(Mathf.Clamp01(volume / MaxVolume));
        private static float FromSlider(float t) => Mathf.Round(t * t * MaxVolume / 0.05f) * 0.05f;

        private bool VolumeRow(Ui ui, string label, ref float volume)
        {
            float before = volume;
            float t = ToSlider(volume);
            float slid = t;
            string text = volume <= 0f ? label + ": <b>muto</b>" : label + ": <b>" + Percent(volume) + "</b>";
            Rect r = ui.Reserve(34f);
            GUI.Label(new Rect(r.x, r.y, r.width, 18f), text);
            slid = GUI.HorizontalSlider(new Rect(r.x + 2f, r.y + 20f, r.width - 4f, 14f), t, 0f, 1f);
            if (Mathf.Abs(slid - t) > 0.0005f) volume = FromSlider(slid);

            int hit = ui.Buttons("Muto", "−25%", "+25%", "100%");
            if (hit == 0) volume = 0f;
            else if (hit == 1) volume = Mathf.Round((volume - Step) / Step) * Step;
            else if (hit == 2) volume = Mathf.Round((volume + Step) / Step) * Step;
            else if (hit == 3) volume = 1f;
            volume = Clamp(volume);
            return volume != before;
        }

        private static void Meter(Ui ui, Voice v)
        {
            Rect r = ui.Reserve(6f);
            Ui.Tint(r, new Color(1f, 1f, 1f, 0.10f));
            // Speech sits around 0.1-0.3 of full scale on average: a square root makes the bar readable.
            float w = Mathf.Clamp01(MathF.Sqrt(Mathf.Max(0f, v.Level))) * r.width;
            if (w > 0.5f) Ui.Tint(new Rect(r.x, r.y, w, r.height), v.Clipping ? new Color(1f, 0.45f, 0.3f, 0.95f) : new Color(0.45f, 0.9f, 0.5f, 0.9f));
        }

        public override void DrawTab(Ui ui)
        {
            bool changed = false;

            ui.Header("Voci degli altri giocatori");
            changed |= ui.Toggle("Volume per giocatore", ref _enabled);
            if (!_enabled)
            {
                ui.Note("Spento: tutti si sentono come decide il gioco.");
                if (changed) Dirty = true;
                return;
            }

            ui.Toggle("Silenzia tutti (finché non lo togli)", ref _muteAll);
            changed |= ui.HotkeyRow(_muteKey);

            ui.Header("Nuovi giocatori");
            changed |= VolumeRow(ui, "Volume di chi incontri per la prima volta", ref _defaultVolume);
            ui.Note("Chi non hai mai regolato parte da questo volume. Chi hai già regolato tiene il suo, anche nelle prossime partite.", 2);

            ui.Header("In partita adesso");
            _present.Clear();
            try
            {
                var controls = PlayerVoicePlaybackControl.controls;
                if (controls != null)
                {
                    for (int i = 0; i < controls.Count; i++)
                    {
                        PlayerVoicePlaybackControl control = controls[i];
                        if (control == null) continue;
                        PlayerCharacter pc = control.playerCharacter;
                        if (pc == null || pc.isLocalPlayer) continue;
                        _present.Add(VoiceFor(control));
                    }
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("voice-list", $"[Voice] Could not list the voices: {ex.Message}"); }

            if (_present.Count == 0)
            {
                ui.Note(Game.InWorld ? "Non c'è nessun altro giocatore con la voce attiva." : "Entra in una partita per vedere i giocatori.");
            }
            for (int i = 0; i < _present.Count; i++)
            {
                Voice v = _present[i];
                ReadMeters(v);
                Listener l = v.Listener;
                string name = l != null && !string.IsNullOrEmpty(l.Name) ? l.Name : "Giocatore " + (i + 1);
                if (l == null)
                {
                    ui.Label(name + ": <color=#aaaaaa>in arrivo… (" + Percent(_defaultVolume) + ")</color>");
                    Meter(ui, v);
                    continue;
                }
                if (VolumeRow(ui, name, ref l.Volume)) changed |= l.Key.Length > 0;
                Meter(ui, v);
                if (v.Clipping) ui.Note("<color=#ffaa66>La voce sta saturando: a questo volume si sente distorta.</color>");
                ui.Space(4f);
            }

            if (_present.Count > 1)
            {
                int hit = ui.Buttons("Tutti a 100%", "Tutti come i nuovi");
                if (hit >= 0)
                {
                    foreach (Voice v in _present)
                    {
                        if (v.Listener == null) continue;
                        v.Listener.Volume = hit == 0 ? 1f : _defaultVolume;
                    }
                    changed = true;
                }
            }

            ui.Header("Giocatori ricordati");
            ui.Toggle($"Mostra l'elenco ({_known.Count})", ref _showRemembered);
            if (_showRemembered)
            {
                string forget = null;
                foreach (Listener l in _known.Values)
                {
                    string name = string.IsNullOrEmpty(l.Name) ? l.Key : l.Name;
                    Rect r = ui.Reserve(Ui.Row);
                    GUI.Label(new Rect(r.x, r.y, r.width - 96f, r.height), name + "  <color=#aaaaaa>" + (l.Volume <= 0f ? "muto" : Percent(l.Volume)) + "</color>");
                    if (GUI.Button(new Rect(r.x + r.width - 92f, r.y, 92f, r.height), "Dimentica")) forget = l.Key;
                }
                if (forget != null)
                {
                    _known.Remove(forget);
                    foreach (Voice v in _voices.Values)
                    {
                        if (v.Listener != null && v.Listener.Key == forget) { v.Listener = null; v.NextResolve = 0f; }
                    }
                    changed = true;
                }
                if (_known.Count == 0) ui.Note("Ancora nessuno.");
            }

            ui.Space();
            ui.Note("Da 0% (muto) a 1500%. Il gioco livella già le voci e le fa passare da un limitatore: oltre il 300% circa una voce normale diventa più forte ma anche più schiacciata. Lo senti solo tu.", 4);

            if (changed) Dirty = true;
        }
    }

    [HarmonyPatch(typeof(PlayerVoicePlaybackControl), nameof(PlayerVoicePlaybackControl.Update))]
    public static class VoiceControlUpdatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerVoicePlaybackControl __instance, out Voice __state)
        {
            __state = null;
            VoiceModule m = VoiceModule.Instance;
            if (m == null || __instance == null) return;
            try
            {
                Voice v = m.VoiceFor(__instance);
                m.PendingMultiplier = m.MultiplierFor(v);
                m.EvaluateSeen = false;
                __state = v;
            }
            catch (Exception ex)
            {
                m.PendingMultiplier = 1f;
                ModMenuApi.LogOnce("voice-prefix", $"[Voice] Update prefix: {ex}");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(PlayerVoicePlaybackControl __instance, Voice __state)
        {
            VoiceModule m = VoiceModule.Instance;
            if (m == null) return;
            float mult = m.PendingMultiplier;
            bool seen = m.EvaluateSeen;
            m.PendingMultiplier = 1f;
            m.EvaluateSeen = false;
            if (__state == null) return;
            try
            {
                SamplePlaybackComponent sp = __instance.dissonanceSampleProvider;
                if (sp == null) return;
                if (seen)
                {
                    // The gain the game stored this frame is already the multiplied one.
                    __state.Written = sp.MakeupGain;
                    return;
                }
                // The game left the gain alone this frame: whatever is there and is not ours is its own value.
                float current = sp.MakeupGain;
                if (float.IsNaN(__state.Written) || Mathf.Abs(current - __state.Written) > 1e-6f) __state.Base = current;
                float target = __state.Base * mult;
                if (Mathf.Abs(current - target) > 1e-6f) sp.MakeupGain = target;
                __state.Written = target;
            }
            catch (Exception ex)
            {
                ModMenuApi.LogOnce("voice-postfix", $"[Voice] Update postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(VoiceMakeupGain), nameof(VoiceMakeupGain.Evaluate))]
    public static class VoiceMakeupGainPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            VoiceModule m = VoiceModule.Instance;
            if (m == null) return;
            m.EvaluateSeen = true;
            __result *= m.PendingMultiplier;
        }
    }
}
