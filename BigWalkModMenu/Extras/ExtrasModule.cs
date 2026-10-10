using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace BigWalk.ModMenu.Extras
{
    [BepInPlugin("com.bigwalk.modmenu.extras", "Big Walk Mod Menu - Extras", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new ExtrasModule());
        }
    }

    public sealed class ExtrasModule : MenuModule
    {
        public override string Id => "extras";
        public override string Title => "Extras";
        public override int Order => 60;

        private int _section;
        private static readonly string[] Sections = { "Bubbles", "Emotes", "Music", "Sounds", "Diary" };

        public override void Load(ModConfig c)
        {
            _section = Mathf.Clamp(c.GetInt("Section", 0), 0, Sections.Length - 1);
            _musicVolume = c.GetFloat("Music.Volume", 0.5f);
            _shuffle = c.GetBool("Music.Shuffle", false);
            _soundVolume = c.GetFloat("Sounds.Volume", 0.8f);
            _bubbleSeconds = c.GetFloat("Bubble.Seconds", 6f);
            _totalDistance = c.GetFloat("Diary.TotalDistance", 0f);
            _totalSeconds = c.GetFloat("Diary.TotalSeconds", 0f);
            _totalSessions = c.GetInt("Diary.Sessions", 0);
            for (int i = 0; i < _soundKeys.Length; i++) _soundKeys[i].Load(c);
            for (int i = 0; i < _emoteKeys.Length; i++) _emoteKeys[i].Load(c);
        }

        public override void Save(ModConfig c)
        {
            c.Set("Section", _section);
            c.Set("Music.Volume", _musicVolume);
            c.Set("Music.Shuffle", _shuffle);
            c.Set("Sounds.Volume", _soundVolume);
            c.Set("Bubble.Seconds", _bubbleSeconds);
            c.Set("Diary.TotalDistance", _totalDistance + _distance);
            c.Set("Diary.TotalSeconds", _totalSeconds + _seconds);
            c.Set("Diary.Sessions", _totalSessions);
            for (int i = 0; i < _soundKeys.Length; i++) _soundKeys[i].Save(c);
            for (int i = 0; i < _emoteKeys.Length; i++) _emoteKeys[i].Save(c);
        }

        public override void Init()
        {
            Directory.CreateDirectory(MusicDir);
            Directory.CreateDirectory(SoundDir);
            Directory.CreateDirectory(StickerDir);
            if (!File.Exists(BubbleFile))
            {
                File.WriteAllLines(BubbleFile, new[] { "Hi!", "LOL", "?", "!", "Wait for me", "Follow me", "This way!", "Nice!", "Oops", "<3", "zzz", "Photo!" });
            }
            else if (!Config.GetBool("Bubble.English", false))
            {
                TranslateOldBubbles();
            }
            if (!Config.GetBool("Bubble.English", false))
            {
                Config.Set("Bubble.English", true);
                Dirty = true;
            }
            LoadBubbleTexts();

            Net.Register(EmoteMessage, false, OnEmote);
            Net.Register(BubbleMessage, false, OnBubble);
        }

        // ---- shared with friends who run the mod ----

        public const byte EmoteMessage = 17, BubbleMessage = 18;

        private static int SlotOf(uint netId)
        {
            if (netId == 0) return -1;
            for (int i = 0; i < 4; i++)
            {
                if (Identity.NetId(Solo.GetPlayerForSlot(i)) == netId) return i;
            }
            return -1;
        }

        private void OnEmote(uint sender, byte[] body)
        {
            if (!GeneralModule.ShowFriends) return;
            var r = new WireReader(body);
            int kind = r.Int(0, EmoteNames.Length - 1);
            int slot = SlotOf(sender);
            if (r.Failed || slot < 0) return;
            StartEmote(slot, kind, false);
        }

        private void OnBubble(uint sender, byte[] body)
        {
            if (!GeneralModule.ShowFriends) return;
            var r = new WireReader(body);
            float seconds = Mathf.Clamp(r.U8(), 2, 60);
            string text = r.Text(40);
            int slot = SlotOf(sender);
            if (r.Failed || slot < 0) return;
            _bubbles[slot] = text.Length == 0 ? null : new Bubble { Text = text, Until = Time.unscaledTime + seconds };
        }

        public override void Update()
        {
            UpdateAudio();
            UpdateDiary();
            for (int i = 0; i < _emoteKeys.Length; i++)
            {
                if (_emoteKeys[i].Down) StartEmote(LocalSlot(), i);
            }
        }

        public override void LateUpdate()
        {
            UpdateEmotes();
        }

        public override void DrawOverlay()
        {
            DrawBubbles();
        }

        public override void DrawTab(Ui ui)
        {
            // Two rows of section buttons.
            var labels = new string[Sections.Length];
            for (int i = 0; i < Sections.Length; i++) labels[i] = i == _section ? "<b>[" + Sections[i] + "]</b>" : Sections[i];
            int hit = ui.Buttons(labels[0], labels[1], labels[2]);
            if (hit >= 0) { _section = hit; Dirty = true; }
            hit = ui.Buttons(labels[3], labels[4]);
            if (hit >= 0) { _section = 3 + hit; Dirty = true; }

            switch (_section)
            {
                case 0: DrawBubbleTab(ui); break;
                case 1: DrawEmoteTab(ui); break;
                case 2: DrawMusicTab(ui); break;
                case 3: DrawSoundTab(ui); break;
                default: DrawDiaryTab(ui); break;
            }
        }

        private static int LocalSlot()
        {
            for (int i = 0; i < 4; i++)
            {
                PlayerCharacter pc = Solo.GetPlayerForSlot(i);
                try { if (pc != null && pc.isLocalPlayer) return i; } catch { }
            }
            return 0;
        }

        private static string[] SlotLabels()
        {
            var labels = new string[4];
            for (int i = 0; i < 4; i++)
            {
                PlayerCharacter pc = Solo.GetPlayerForSlot(i);
                labels[i] = pc != null ? Game.PlayerLabel(pc, i) : $"Player {i + 1} (absent)";
            }
            return labels;
        }

        // =====================================================================
        // Speech bubbles and stickers above the heads
        // =====================================================================

        private sealed class Bubble
        {
            public string Text;
            public Texture2D Sticker;
            public float Until; // 0 = until removed
        }

        private readonly Bubble[] _bubbles = new Bubble[4];
        private readonly List<string> _bubbleTexts = new List<string>();
        private readonly List<string> _stickerFiles = new List<string>();
        private readonly Dictionary<string, Texture2D> _stickers = new Dictionary<string, Texture2D>();
        private int _bubbleSlot;
        private float _bubbleSeconds = 6f;
        private GUIStyle _bubbleStyle;
        private bool _bubbleStyleFailed;

        private string BubbleFile => ModMenuApi.Renamed(DataPath, "bubbles.txt", "fumetti.txt");
        private string StickerDir => ModMenuApi.Renamed(DataPath, "stickers", "adesivi");

        // The texts written by the Italian versions (up to 1.3.0), in the order of the English ones.
        private static readonly string[] OldBubbles = { "Ciao!", "Aspettatemi", "Seguitemi", "Di qua!", "Bravo!", "Ops", "Foto!" };
        private static readonly string[] NewBubbles = { "Hi!", "Wait for me", "Follow me", "This way!", "Nice!", "Oops", "Photo!" };

        // A bubbles.txt made before the interface became English keeps its Italian texts: translate the
        // ones nobody touched, once. Lines the player wrote or changed stay as they are.
        private void TranslateOldBubbles()
        {
            try
            {
                string[] lines = File.ReadAllLines(BubbleFile);
                int changed = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    int k = Array.IndexOf(OldBubbles, lines[i].Trim());
                    if (k < 0) continue;
                    lines[i] = NewBubbles[k];
                    changed++;
                }
                if (changed == 0) return;
                File.WriteAllLines(BubbleFile, lines);
                ModMenuApi.Log.LogInfo($"[Extras] bubbles.txt: {changed} Italian texts from an older version translated to English.");
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Extras] bubbles.txt: {ex.Message}"); }
        }

        private void LoadBubbleTexts()
        {
            _bubbleTexts.Clear();
            try
            {
                foreach (string line in File.ReadAllLines(BubbleFile))
                {
                    string t = line.Trim();
                    if (t.Length > 0) _bubbleTexts.Add(t);
                }
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Extras] bubbles.txt: {ex.Message}"); }

            _stickerFiles.Clear();
            try
            {
                foreach (string f in Directory.GetFiles(StickerDir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") _stickerFiles.Add(f);
                }
                _stickerFiles.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        private void SetBubble(int slot, string text, Texture2D sticker)
        {
            _bubbles[slot] = new Bubble { Text = text, Sticker = sticker, Until = _bubbleSeconds >= 60f ? 0f : Time.unscaledTime + _bubbleSeconds };
            // Text above my own head goes to friends too (stickers are pictures and stay local).
            if (sticker == null) ShareBubble(slot, text);
        }

        private void ShareBubble(int slot, string text)
        {
            if (slot != LocalSlot() || !GeneralModule.ShareMine || !Net.Ready) return;
            Net.Send(BubbleMessage, new WireWriter().U8((int)Mathf.Clamp(_bubbleSeconds, 2f, 60f)).Text(text, 40).ToArray());
        }

        private void DrawBubbles()
        {
            if (!Game.InWorld) return;
            UnityEngine.Camera cam = Game.PlayerCamera;
            if (cam == null) return;

            for (int slot = 0; slot < 4; slot++)
            {
                Bubble b = _bubbles[slot];
                if (b == null) continue;
                if (b.Until > 0f && Time.unscaledTime > b.Until) { _bubbles[slot] = null; continue; }

                PlayerCharacter pc = Solo.GetPlayerForSlot(slot);
                Transform head = Game.HeadBone(pc);
                if (head == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(head.position + Vector3.up * 0.62f);
                if (sp.z < 0.35f) continue; // behind the camera, or it is your own head in first person

                float scale = Mathf.Clamp(3.2f / sp.z, 0.35f, 1.6f) * Screen.height / 1080f;
                float cx = sp.x, bottom = Screen.height - sp.y;

                if (b.Sticker != null)
                {
                    float w = 150f * scale, h = w * b.Sticker.height / Mathf.Max(1, b.Sticker.width);
                    GUI.DrawTexture(new Rect(cx - w * 0.5f, bottom - h, w, h), b.Sticker);
                    continue;
                }

                int size = Mathf.Clamp(Mathf.RoundToInt(30f * scale), 9, 60);
                float width = Mathf.Max(size * 1.6f, b.Text.Length * size * 0.62f) + size, height = size * 1.7f;
                var rect = new Rect(cx - width * 0.5f, bottom - height - size * 0.4f, width, height);
                Ui.Tint(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), new Color(0f, 0f, 0f, 0.85f));
                Ui.Tint(rect, Color.white);
                Ui.Tint(new Rect(cx - size * 0.2f, rect.yMax, size * 0.4f, size * 0.4f), Color.white); // little tail

                if (!Game.Alive(_bubbleStyle) && !_bubbleStyleFailed)
                {
                    try
                    {
                        _bubbleStyle = new GUIStyle(GUI.skin.label);
                        _bubbleStyle.alignment = TextAnchor.MiddleCenter;
                        _bubbleStyle.richText = true;
                    }
                    catch (Exception ex)
                    {
                        _bubbleStyleFailed = true;
                        ModMenuApi.Log.LogWarning($"[Extras] Bubble text style: {ex.Message}");
                    }
                }
                string rich = $"<size={size}><color=#111111><b>{b.Text}</b></color></size>";
                if (Game.Alive(_bubbleStyle)) GUI.Label(rect, rich, _bubbleStyle);
                else GUI.Label(new Rect(rect.x + size * 0.5f, rect.y, rect.width, rect.height), rich);
            }
        }

        private void DrawBubbleTab(Ui ui)
        {
            ui.Header("Speech bubble over the head");
            ui.Choice("Over", ref _bubbleSlot, SlotLabels());
            float seconds = _bubbleSeconds;
            if (ui.Slider(seconds >= 60f ? "Stays: until removed" : $"Stays: {seconds:0} s", ref seconds, 2f, 60f, 1f))
            {
                _bubbleSeconds = seconds;
                Dirty = true;
            }
            if (ui.Button("Remove the bubble"))
            {
                _bubbles[_bubbleSlot] = null;
                ShareBubble(_bubbleSlot, "");
            }

            // Three per row.
            for (int i = 0; i < _bubbleTexts.Count; i += 3)
            {
                int n = Mathf.Min(3, _bubbleTexts.Count - i);
                var row = new string[n];
                for (int k = 0; k < n; k++) row[k] = _bubbleTexts[i + k];
                int hit = ui.Buttons(row);
                if (hit >= 0) SetBubble(_bubbleSlot, _bubbleTexts[i + hit], null);
            }

            ui.Header($"Stickers ({_stickerFiles.Count})");
            foreach (string file in _stickerFiles)
            {
                if (!ui.Button(Path.GetFileNameWithoutExtension(file))) continue;
                if (!_stickers.TryGetValue(file, out Texture2D tex) || !Game.Alive(tex))
                {
                    tex = Images.LoadTexture(file, 256);
                    _stickers[file] = tex;
                }
                if (tex != null) SetBubble(_bubbleSlot, "", tex);
            }
            if (ui.Button("Reload texts and stickers")) LoadBubbleTexts();
            ui.Note("Texts: one per line in extras/bubbles.txt. Stickers: PNG images in the folder extras/stickers.", 2);
            ui.Note("Friends with the mod see the texts over your head too. Stickers, and bubbles put on other players, are seen only by you.", 2);
        }

        // =====================================================================
        // Emotes: short moves of the whole body
        // =====================================================================

        private static readonly string[] EmoteNames = { "Spin", "Hops", "Somersault", "Shivers", "Bow", "Dance" };
        private static readonly float[] EmoteSeconds = { 1.2f, 1.4f, 0.9f, 1.0f, 1.5f, 4.0f };

        private sealed class Emote
        {
            public PlayerCharacter Pc;
            public Transform Body;
            public Vector3 Position;
            public Quaternion Rotation;
            public int Kind;
            public float Start;
        }

        private readonly Dictionary<int, Emote> _emotes = new Dictionary<int, Emote>();
        private readonly List<int> _finished = new List<int>();
        private readonly Hotkey[] _emoteKeys = MakeKeys("Emote", EmoteNames);
        private int _emoteSlot;

        private static Hotkey[] MakeKeys(string prefix, string[] names)
        {
            var keys = new Hotkey[names.Length];
            for (int i = 0; i < names.Length; i++) keys[i] = new Hotkey(prefix + i, names[i], KeyCode.None);
            return keys;
        }

        private void StartEmote(int slot, int kind, bool share = true)
        {
            PlayerCharacter pc = Solo.GetPlayerForSlot(slot);
            if (pc == null) return;
            if (share && slot == LocalSlot() && GeneralModule.ShareMine && Net.Ready) Net.Send(EmoteMessage, new byte[] { (byte)kind });
            int id = pc.GetInstanceID();
            if (_emotes.TryGetValue(id, out Emote running)) Restore(running);

            // The object that carries the animated body ("dummy" in the prefab).
            Transform body = null;
            try { body = pc.myAnimator != null ? pc.myAnimator.transform : null; } catch { }
            if (body == null) body = Game.FindDeep(pc.transform, "dummy");
            if (body == null) { ModMenuApi.LogOnce("emote", "[Extras] Body object not found, emotes are not available."); return; }

            _emotes[id] = new Emote { Pc = pc, Body = body, Position = body.localPosition, Rotation = body.localRotation, Kind = kind, Start = Time.time };
        }

        private static void Restore(Emote e)
        {
            if (e.Body == null) return;
            e.Body.localPosition = e.Position;
            e.Body.localRotation = e.Rotation;
        }

        private void UpdateEmotes()
        {
            if (_emotes.Count == 0) return;
            _finished.Clear();
            foreach (var kv in _emotes)
            {
                Emote e = kv.Value;
                if (e.Pc == null || e.Body == null) { _finished.Add(kv.Key); continue; }
                float duration = EmoteSeconds[e.Kind];
                float t = (Time.time - e.Start) / duration;
                if (t >= 1f) { Restore(e); _finished.Add(kv.Key); continue; }

                Vector3 offset = Vector3.zero;
                Quaternion turn = Quaternion.identity;
                bool aroundMiddle = false;
                float ease = t * t * (3f - 2f * t);
                switch (e.Kind)
                {
                    case 0: turn = Quaternion.Euler(0f, 720f * ease, 0f); break;
                    case 1: offset.y = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * 0.45f; break;
                    case 2: turn = Quaternion.Euler(360f * ease, 0f, 0f); offset.y = Mathf.Sin(t * Mathf.PI) * 0.7f; aroundMiddle = true; break;
                    case 3: offset = new Vector3(Mathf.Sin(Time.time * 71f), 0f, Mathf.Cos(Time.time * 53f)) * 0.035f; break;
                    case 4: turn = Quaternion.Euler(Mathf.Sin(t * Mathf.PI) * 55f, 0f, 0f); break;
                    default:
                        float beat = (Time.time - e.Start) * 2f * Mathf.PI * 1.8f;
                        turn = Quaternion.Euler(0f, Mathf.Sin(beat * 0.5f) * 25f, Mathf.Sin(beat) * 14f);
                        offset.y = Mathf.Abs(Mathf.Sin(beat)) * 0.1f;
                        break;
                }

                Vector3 position = e.Position + offset;
                if (aroundMiddle)
                {
                    // Turn around the middle of the body instead of the feet.
                    Vector3 pivot = e.Position + Vector3.up * 0.75f;
                    position = pivot + turn * (e.Position - pivot) + offset;
                }
                e.Body.localPosition = position;
                e.Body.localRotation = e.Rotation * turn;
            }
            foreach (int id in _finished) _emotes.Remove(id);
        }

        private void DrawEmoteTab(Ui ui)
        {
            ui.Header("Emotes");
            ui.Choice("Who", ref _emoteSlot, SlotLabels());
            for (int i = 0; i < EmoteNames.Length; i += 2)
            {
                int hit = ui.Buttons(EmoteNames[i], EmoteNames[i + 1]);
                if (hit >= 0) StartEmote(_emoteSlot, i + hit);
            }
            ui.Note("Friends with the mod see yours too; you see them in selfie or third person. The ones you make others do are seen only by you.", 3);
            ui.Header("Shortcuts (for yourself)");
            foreach (Hotkey k in _emoteKeys) if (ui.HotkeyRow(k)) Dirty = true;
        }

        // =====================================================================
        // Music player and sound board
        // =====================================================================

        private AudioSource _music, _sounds;
        private readonly List<string> _tracks = new List<string>();
        private readonly List<string> _soundFiles = new List<string>();
        private readonly Dictionary<string, AudioClip> _soundClips = new Dictionary<string, AudioClip>();
        private readonly Hotkey[] _soundKeys = MakeKeys("Sound", new[] { "Sound 1", "Sound 2", "Sound 3", "Sound 4", "Sound 5", "Sound 6" });
        private readonly System.Random _random = new System.Random();
        private int _track = -1;
        private Task<Pcm> _loading;
        private string _loadingName, _musicStatus = "";
        private bool _paused, _shuffle, _wasPlaying;
        private float _musicVolume = 0.5f, _soundVolume = 0.8f, _nextAudioScan;
        private AudioClip _musicClip;

        private string MusicDir => ModMenuApi.Renamed(DataPath, "music", "musica");
        private string SoundDir => ModMenuApi.Renamed(DataPath, "sounds", "suoni");

        private bool EnsureAudio()
        {
            if (Game.Alive(_music) && Game.Alive(_sounds)) return true;
            try
            {
                var go = new GameObject("bwmm_audio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _music = go.AddComponent<AudioSource>();
                _music.playOnAwake = false;
                _music.spatialBlend = 0f;
                _sounds = go.AddComponent<AudioSource>();
                _sounds.playOnAwake = false;
                _sounds.spatialBlend = 0f;
                return true;
            }
            catch (Exception ex)
            {
                ModMenuApi.LogOnce("audio", $"[Extras] Audio output not available: {ex.Message}");
                return false;
            }
        }

        private static void Scan(string dir, List<string> into)
        {
            into.Clear();
            try
            {
                foreach (string f in Directory.GetFiles(dir)) if (AudioLoader.IsAudio(f)) into.Add(f);
                into.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        private void PlayTrack(int index)
        {
            if (_tracks.Count == 0 || !EnsureAudio()) return;
            _track = (index % _tracks.Count + _tracks.Count) % _tracks.Count;
            string path = _tracks[_track];
            _loadingName = Path.GetFileNameWithoutExtension(path);
            _musicStatus = "Loading " + _loadingName + "…";
            _music.Stop();
            _paused = false;
            _wasPlaying = false;
            // Decoding a whole song takes a moment: do it away from the game's thread.
            _loading = Task.Run(() => AudioLoader.Decode(path));
        }

        private void NextTrack(int step)
        {
            if (_tracks.Count == 0) return;
            if (_shuffle && _tracks.Count > 1)
            {
                int next;
                do { next = _random.Next(_tracks.Count); } while (next == _track);
                PlayTrack(next);
            }
            else PlayTrack(_track + step);
        }

        private void UpdateAudio()
        {
            if (Time.unscaledTime > _nextAudioScan && (ModMenuApi.IsMenuOpen || _nextAudioScan == 0f))
            {
                _nextAudioScan = Time.unscaledTime + 3f;
                Scan(MusicDir, _tracks);
                Scan(SoundDir, _soundFiles);
            }

            for (int i = 0; i < _soundKeys.Length; i++)
            {
                if (_soundKeys[i].Down && i < _soundFiles.Count) PlaySound(_soundFiles[i]);
            }

            if (_loading != null && _loading.IsCompleted)
            {
                Pcm pcm = _loading.Status == TaskStatus.RanToCompletion ? _loading.Result : new Pcm { Error = "read error" };
                _loading = null;
                if (pcm.Error != null || pcm.Samples == null)
                {
                    _musicStatus = $"{_loadingName}: {pcm.Error ?? "empty"}";
                    ModMenuApi.Log.LogWarning("[Extras] " + _musicStatus);
                }
                else
                {
                    if (_musicClip != null) UnityEngine.Object.Destroy(_musicClip);
                    _musicClip = AudioLoader.ToClip(pcm, _loadingName);
                    _music.clip = _musicClip;
                    _music.volume = _musicVolume;
                    _music.Play();
                    _wasPlaying = true;
                    _musicStatus = _loadingName;
                }
            }

            if (!Game.Alive(_music)) return;
            _music.volume = _musicVolume;
            // End of the song: on to the next one.
            if (_wasPlaying && !_paused && _loading == null && !_music.isPlaying && _music.clip != null && Application.isFocused)
            {
                _wasPlaying = false;
                NextTrack(1);
            }
        }

        private void PlaySound(string path)
        {
            if (!EnsureAudio()) return;
            if (!_soundClips.TryGetValue(path, out AudioClip clip) || !Game.Alive(clip))
            {
                Pcm pcm = AudioLoader.Decode(path);
                if (pcm.Error != null)
                {
                    ModMenuApi.Toast($"{Path.GetFileName(path)}: {pcm.Error}");
                    return;
                }
                clip = AudioLoader.ToClip(pcm, Path.GetFileNameWithoutExtension(path));
                _soundClips[path] = clip;
            }
            if (clip != null) _sounds.PlayOneShot(clip, _soundVolume);
        }

        private void DrawMusicTab(Ui ui)
        {
            ui.Header("Music");
            ui.Label(string.IsNullOrEmpty(_musicStatus) ? "Nothing playing" : "♪ " + _musicStatus);
            if (_music != null && _music.clip != null && _loading == null)
            {
                float len = _music.clip.length, at = _music.time;
                ui.Note($"{(int)at / 60}:{(int)at % 60:00} / {(int)len / 60}:{(int)len % 60:00}");
            }
            int hit = ui.Buttons("⏮", _paused || _music == null || !_music.isPlaying ? "▶ Play" : "⏸ Pause", "⏹ Stop", "⏭");
            if (hit == 0) NextTrack(-1);
            else if (hit == 3) NextTrack(1);
            else if (hit == 2 && _music != null) { _music.Stop(); _wasPlaying = false; _paused = false; _musicStatus = ""; }
            else if (hit == 1)
            {
                if (_music == null || _music.clip == null) PlayTrack(Mathf.Max(0, _track));
                else if (_music.isPlaying) { _music.Pause(); _paused = true; }
                else { _music.UnPause(); if (!_music.isPlaying) _music.Play(); _paused = false; _wasPlaying = true; }
            }
            if (ui.Slider($"Volume: {Mathf.RoundToInt(_musicVolume * 100f)}%", ref _musicVolume, 0f, 1f, 0.05f)) Dirty = true;
            if (ui.Toggle("Shuffle", ref _shuffle)) Dirty = true;

            ui.Header($"Tracks ({_tracks.Count})");
            for (int i = 0; i < _tracks.Count && i < 60; i++)
            {
                string name = Path.GetFileNameWithoutExtension(_tracks[i]);
                if (ui.Button(i == _track ? "<b>▶ " + name + "</b>" : name)) PlayTrack(i);
            }
            ui.Note("Put MP3, OGG or WAV files in BepInEx/plugins/BigWalkModMenu/extras/music. Only you hear it.", 2);
        }

        private void DrawSoundTab(Ui ui)
        {
            ui.Header($"Sounds ({_soundFiles.Count})");
            if (ui.Slider($"Volume: {Mathf.RoundToInt(_soundVolume * 100f)}%", ref _soundVolume, 0f, 1f, 0.05f)) Dirty = true;
            for (int i = 0; i < _soundFiles.Count && i < 40; i++)
            {
                string name = Path.GetFileNameWithoutExtension(_soundFiles[i]);
                if (ui.Button(i < _soundKeys.Length ? $"{i + 1}. {name}" : name)) PlaySound(_soundFiles[i]);
            }
            ui.Note("Put short files (WAV, OGG, MP3) in extras/sounds. Only you hear them: they do not go through the microphone.", 2);
            ui.Header("Shortcuts (first 6 sounds)");
            foreach (Hotkey k in _soundKeys) if (ui.HotkeyRow(k)) Dirty = true;
        }

        // =====================================================================
        // Travel diary
        // =====================================================================

        private bool _inSession;
        private Vector3 _lastPosition;
        private float _distance, _seconds, _topSpeed, _highest, _lowest, _nextSample;
        private float _totalDistance, _totalSeconds;
        private int _totalSessions;
        private float _nextDiarySave;
        private DateTime _sessionStart;
        private readonly List<Vector2> _path = new List<Vector2>();
        private const float StepLength = 0.65f; // metres per step, a rough guess

        private void UpdateDiary()
        {
            PlayerCharacter pc = Game.LocalPlayer;
            if (pc == null)
            {
                if (_inSession) EndSession();
                return;
            }

            Vector3 p = pc.transform.position;
            if (!_inSession)
            {
                _inSession = true;
                _sessionStart = DateTime.Now;
                _lastPosition = p;
                _distance = _seconds = _topSpeed = 0f;
                _highest = _lowest = p.y;
                _path.Clear();
                _path.Add(new Vector2(p.x, p.z));
                _nextSample = Time.unscaledTime + 0.5f;
                _totalSessions++;
                return;
            }

            _seconds += Time.unscaledDeltaTime;
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + 0.5f;

            Vector3 delta = p - _lastPosition;
            float flat = new Vector2(delta.x, delta.z).magnitude;
            _lastPosition = p;
            // A jump this long in half a second is a teleport, not walking.
            if (flat < 30f)
            {
                _distance += flat;
                _topSpeed = Mathf.Max(_topSpeed, flat / 0.5f);
            }
            _highest = Mathf.Max(_highest, p.y);
            _lowest = Mathf.Min(_lowest, p.y);

            var point = new Vector2(p.x, p.z);
            if ((_path[_path.Count - 1] - point).sqrMagnitude > 4f && _path.Count < 20000) _path.Add(point);
            if (Time.unscaledTime > _nextDiarySave)
            {
                _nextDiarySave = Time.unscaledTime + 60f;
                Dirty = true; // keep the totals safe if the game is closed mid-walk
            }
        }

        private void EndSession()
        {
            _inSession = false;
            try
            {
                string file = ModMenuApi.Renamed(DataPath, "diary.csv", "diario.csv");
                if (!File.Exists(file)) File.WriteAllText(file, "start;minutes;metres;estimated_steps;top_speed_ms;height_range_m\n");
                var c = CultureInfo.InvariantCulture;
                File.AppendAllText(file, string.Join(";", _sessionStart.ToString("yyyy-MM-dd HH:mm"), (_seconds / 60f).ToString("0.0", c),
                    _distance.ToString("0", c), Mathf.RoundToInt(_distance / StepLength).ToString(c), _topSpeed.ToString("0.0", c), (_highest - _lowest).ToString("0", c)) + "\n");
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Extras] Diary: {ex.Message}"); }

            _totalDistance += _distance;
            _totalSeconds += _seconds;
            _distance = _seconds = 0f;
            Dirty = true;
        }

        private static string Duration(float seconds)
        {
            int m = (int)(seconds / 60f);
            return m >= 60 ? $"{m / 60} h {m % 60} min" : $"{m} min";
        }

        private static string Metres(float m) => m >= 1000f ? $"{m / 1000f:0.00} km" : $"{m:0} m";

        private void DrawDiaryTab(Ui ui)
        {
            ui.Header("This walk");
            if (!_inSession) ui.Note("Starts when you join a walk.");
            ui.Label($"Distance: <b>{Metres(_distance)}</b>   Steps (estimated): <b>{Mathf.RoundToInt(_distance / StepLength)}</b>");
            ui.Label($"Time: <b>{Duration(_seconds)}</b>   Average: {(_seconds > 1f ? _distance / _seconds * 3.6f : 0f):0.0} km/h");
            ui.Label($"Top speed: {_topSpeed * 3.6f:0.0} km/h   Height range: {_highest - _lowest:0} m");

            ui.Header("Route");
            DrawPath(ui.Reserve(ui.Width * 0.7f));

            ui.Header("In total");
            ui.Label($"Distance: <b>{Metres(_totalDistance + _distance)}</b>   Time: <b>{Duration(_totalSeconds + _seconds)}</b>");
            ui.Label($"Walks: {_totalSessions}");
            ui.Note("Every finished walk is added to extras/diary.csv.");
        }

        private void DrawPath(Rect area)
        {
            Ui.Tint(area, new Color(1f, 1f, 1f, 0.06f));
            if (_path.Count < 2) return;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in _path)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            float span = Mathf.Max(maxX - minX, maxY - minY, 20f);
            float pad = 8f;
            float scale = Mathf.Min(area.width - pad * 2f, area.height - pad * 2f) / span;
            float ox = area.x + (area.width - (maxX - minX) * scale) * 0.5f, oy = area.y + (area.height - (maxY - minY) * scale) * 0.5f;

            int stride = Mathf.Max(1, _path.Count / 1200);
            var colour = new Color(0.5f, 0.9f, 1f, 0.9f);
            for (int i = 0; i < _path.Count; i += stride)
            {
                Vector2 p = _path[i];
                // North (+Z) up.
                Ui.Tint(new Rect(ox + (p.x - minX) * scale - 1f, oy + (maxY - p.y) * scale - 1f, 2f, 2f), colour);
            }
            Vector2 first = _path[0], last = _path[_path.Count - 1];
            Ui.Tint(new Rect(ox + (first.x - minX) * scale - 3f, oy + (maxY - first.y) * scale - 3f, 6f, 6f), new Color(0.4f, 1f, 0.4f, 1f));
            Ui.Tint(new Rect(ox + (last.x - minX) * scale - 3f, oy + (maxY - last.y) * scale - 3f, 6f, 6f), new Color(1f, 0.4f, 0.3f, 1f));
            GUI.Label(new Rect(area.x + 4f, area.yMax - 16f, area.width, 16f), $"<size=10>green: start • red: you • side {span:0} m</size>");
        }
    }
}
