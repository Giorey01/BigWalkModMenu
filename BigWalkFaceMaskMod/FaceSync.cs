using System;
using System.Collections.Generic;
using System.IO;
using BigWalk.ModMenu;
using UnityEngine;

namespace BigWalk.FaceMaskMod
{
    /// <summary>What a friend sent about their own head.</summary>
    public sealed class RemoteFace
    {
        public bool HasFace;
        public uint Hash;
        public FaceEntry Entry;                 // null until the picture has arrived
        public readonly FaceSettings Settings = new FaceSettings();
        public bool NoseVisible, NoseCustom;
        public Color NoseColour = Color.white;
        public readonly ChunkAssembler Assembler = new ChunkAssembler(FaceSync.MaxImageBytes, FaceSync.MaxChunks);
    }

    /// <summary>
    /// Sends the face on my own character to friends who run the mod, and keeps what they send.
    /// The picture travels once per session as a small JPEG in numbered pieces; the placement
    /// (zoom, position, wrap) and the nose settings travel separately and are tiny.
    ///
    ///   meta  (retained, key 0): version, hasFace, hash, total bytes, piece size, zoom, centre, wrap,
    ///                            mirror, fill colour, nose visible / custom / hue, saturation, value
    ///   piece (retained, key 1 + index): hash, bytes
    /// </summary>
    public static class FaceSync
    {
        public const byte MetaMessage = 20, ChunkMessage = 21;
        public const int MaxImageBytes = 22000, MaxChunks = 30, MaxSide = 256;
        private const byte WireVersion = 1;

        public static readonly Dictionary<uint, RemoteFace> Remote = new Dictionary<uint, RemoteFace>();
        private static readonly List<uint> _gone = new List<uint>();

        // Sending.
        private static string _sentSignature;
        private static uint _sentHash;
        private static byte[] _image;            // JPEG of the face currently on my head
        private static string _imageOf;          // which face and file version it was made from
        private static uint _imageHash;
        private static int _chunkSize, _nextChunk = -1;
        private static float _nextCheck, _nextChunkAt;
        private static bool _resend, _sentHadFace;

        private static string CacheDir
        {
            get
            {
                string p = Path.Combine(Plugin.PluginPath, "amici");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static void Init()
        {
            Net.Register(MetaMessage, true, OnMeta);
            Net.Register(ChunkMessage, true, OnChunk);
            Net.Connected += () => { _resend = true; };
            Profiles.Register("face", MaxImageBytes + 100, ExportProfile, ImportProfile);
        }

        // ---- sending ----

        private static int LocalSlot()
        {
            for (int i = 0; i < 4; i++)
            {
                PlayerCharacter pc = Solo.GetPlayerForSlot(i);
                try { if (pc != null && pc.isLocalPlayer) return i; } catch { }
            }
            return -1;
        }

        // Shrinks the face and encodes it as JPEG small enough to send.
        private static bool BuildImage(FaceEntry face)
        {
            string id = face.Name + "/" + face.Width + "x" + face.Height + "/" + face.Source.Length + "/" + face.Fill.r + "," + face.Fill.g + "," + face.Fill.b;
            if (_imageOf == id && _image != null) return true;
            _image = null;
            try
            {
                foreach (int side in new[] { MaxSide, 192, 128 })
                {
                    byte[] small = Images.FlattenAndShrink(face.Source, face.Width, face.Height, side, face.Fill.r, face.Fill.g, face.Fill.b, out int w, out int h);
                    // Quality stays at 85 or below: above that the encoder changes its layout and friends would refuse the file.
                    foreach (int quality in new[] { 85, 72, 58 })
                    {
                        byte[] jpg = Images.EncodeJpeg(small, w, h, quality);
                        if (jpg.Length > MaxImageBytes || !Images.IsOwnJpeg(jpg, MaxSide)) continue;
                        _image = jpg;
                        _imageOf = id;
                        _imageHash = Fnv.Hash(jpg);
                        Plugin.LogSource?.LogInfo($"[FaceSync] '{face.Name}' packed for friends: {w}x{h}, quality {quality}, {jpg.Length} bytes.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[FaceSync] Could not pack '{face.Name}': {ex.Message}");
            }
            return false;
        }

        private static byte[] BuildMeta(int slot, FaceEntry face)
        {
            var w = new WireWriter();
            w.U8(0).U8(WireVersion).Bool(face != null);
            _chunkSize = Mathf.Clamp(Net.MaxBody - 16, 200, 900);
            if (face != null)
            {
                FaceSettings s = face.Settings;
                w.U32(_imageHash).U16(_image.Length).U16(_chunkSize);
                w.Q16(s.Zoom, 0.4f, 3f).Q16(s.CenterX, 0f, 1f).Q16(s.CenterY, 0f, 1f).Q8(s.Wrap, 0f, 1f).Bool(s.Mirror);
                w.U8(face.Fill.r).U8(face.Fill.g).U8(face.Fill.b);
            }
            bool share = GeneralModule.ShareMine;
            w.Bool(share && GlobalSettings.NoseVisible[slot]).Bool(share && GlobalSettings.NoseCustomColour[slot]);
            w.Q8(GlobalSettings.NoseHue[slot], 0f, 1f).Q8(GlobalSettings.NoseSaturation[slot], 0f, 1f).Q8(GlobalSettings.NoseValue[slot], 0f, 1f);
            return w.ToArray();
        }

        // ---- profile files (the fallback without the live channel) ----

        private static readonly Dictionary<string, RemoteFace> _fromFile = new Dictionary<string, RemoteFace>();
        private static readonly Dictionary<int, string> _keys = new Dictionary<int, string>();
        private static float _nextKeyRefresh;
        private static FaceMaskBehaviour _behaviour;

        private static byte[] ExportProfile()
        {
            int slot = LocalSlot();
            if (slot < 0 || _behaviour == null) return null;
            FaceEntry face = FaceManager.GetFace(_behaviour.GetSlotFace(slot));
            if (face != null && (face.Source == null || !BuildImage(face))) face = null;
            byte[] meta = BuildMeta(slot, face);
            var w = new WireWriter().Bytes(meta, 0, meta.Length);
            if (face != null) w.Bytes(_image, 0, _image.Length);
            return w.ToArray();
        }

        private static void ImportProfile(string key, byte[] data)
        {
            var r = new WireReader(data);
            byte[] meta = r.Bytes(64);
            if (r.Failed) return;
            var rf = new RemoteFace();
            if (!ReadMeta(meta, rf, out uint hash, out _, out _)) return;
            if (rf.HasFace)
            {
                byte[] image = r.Bytes(MaxImageBytes);
                if (r.Failed || Fnv.Hash(image) != hash || !SetImage(0, rf, image)) return;
                rf.Hash = hash;
            }
            if (_fromFile.TryGetValue(key, out RemoteFace old)) Release(old.Entry);
            _fromFile[key] = rf;
        }

        public static void Update(FaceMaskBehaviour faces)
        {
            _behaviour = faces;
            PruneGone();
            if (!Net.Ready) { _nextChunk = -1; return; }

            // One piece at a time, about fifteen a second: well under the host's limit per player.
            if (_nextChunk >= 0 && _image != null)
            {
                if (Time.unscaledTime < _nextChunkAt) return;
                _nextChunkAt = Time.unscaledTime + 0.065f;
                int total = (_image.Length + _chunkSize - 1) / _chunkSize;
                if (_nextChunk < total)
                {
                    int offset = _nextChunk * _chunkSize, count = Math.Min(_chunkSize, _image.Length - offset);
                    byte[] body = new WireWriter().U8(1 + _nextChunk).U32(_imageHash).Bytes(_image, offset, count).ToArray();
                    if (!Net.Send(ChunkMessage, body)) { _nextChunk = -1; _resend = true; return; }
                    _nextChunk++;
                }
                if (_nextChunk >= total) _nextChunk = -1;
                return;
            }

            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;

            int slot = LocalSlot();
            if (slot < 0) return;
            FaceEntry face = GeneralModule.ShareMine ? FaceManager.GetFace(faces.GetSlotFace(slot)) : null;
            if (face != null && (face.Source == null || !BuildImage(face))) face = null;

            byte[] meta = BuildMeta(slot, face);

            string signature = Convert.ToBase64String(meta);
            if (!_resend && signature == _sentSignature) return;
            if (!Net.Send(MetaMessage, meta)) return;
            // The pieces go out again whenever a face appears: someone may have joined while there was none.
            bool imageChanged = face != null && (_resend || !_sentHadFace || _sentHash != _imageHash);
            _sentSignature = signature;
            _sentHadFace = face != null;
            _resend = false;
            if (face != null) _sentHash = _imageHash;
            if (imageChanged) _nextChunk = 0;
        }

        // ---- receiving ----

        private static string CacheFile(uint sender, uint hash)
        {
            string key = Identity.Key(Identity.ByNetId(sender));
            // Only digits from the game's own id and a hash computed here: nothing from the message becomes a path.
            foreach (char c in key) if (c < '0' || c > '9') return null;
            return key.Length == 0 ? null : Path.Combine(CacheDir, key + "_" + hash.ToString("x8") + ".jpg");
        }

        // Fills in everything except the picture. Returns false when the data is not a valid description.
        private static bool ReadMeta(byte[] body, RemoteFace rf, out uint hash, out int total, out int chunkSize)
        {
            hash = 0;
            total = chunkSize = 0;
            var r = new WireReader(body);
            r.U8();
            if (r.U8() != WireVersion) return false;
            bool hasFace = r.Bool();
            float zoom = 1f, cx = 0.5f, cy = 0.5f, wrap = 0.35f;
            bool mirror = false;
            byte fr = 0, fg = 0, fb = 0;
            if (hasFace)
            {
                hash = r.U32();
                total = r.U16();
                chunkSize = r.U16();
                zoom = r.Q16(0.4f, 3f);
                cx = r.Q16(0f, 1f);
                cy = r.Q16(0f, 1f);
                wrap = r.Q8(0f, 1f);
                mirror = r.Bool();
                fr = r.U8(); fg = r.U8(); fb = r.U8();
            }
            bool noseVisible = r.Bool(), noseCustom = r.Bool();
            float nh = r.Q8(0f, 1f), ns = r.Q8(0f, 1f), nv = r.Q8(0f, 1f);
            if (r.Failed) return false;

            rf.HasFace = hasFace;
            rf.NoseVisible = noseVisible;
            rf.NoseCustom = noseCustom;
            rf.NoseColour = Game.Hsv(nh, ns, nv);
            FaceSettings s = rf.Settings;
            s.Zoom = zoom; s.CenterX = cx; s.CenterY = cy; s.Wrap = wrap; s.Mirror = mirror;
            s.HasFill = true;
            s.Fill = new Color32(fr, fg, fb, 255);
            return true;
        }

        private static void OnMeta(uint sender, byte[] body)
        {
            bool known = Remote.TryGetValue(sender, out RemoteFace rf);
            if (!known) rf = new RemoteFace();
            // A description that does not parse changes nothing (and a first one is not kept at all).
            if (!ReadMeta(body, rf, out uint hash, out int total, out int chunkSize)) return;
            if (!known) Remote[sender] = rf;
            if (!rf.HasFace) return;
            FaceSettings s = rf.Settings;

            if (rf.Entry != null && rf.Hash == hash)
            {
                // Same picture, new placement.
                rf.Entry.Fill = s.Fill;
                rf.Entry.Dirty = true;
                rf.Entry.LastChange = 0f;
                return;
            }

            rf.Hash = hash;
            string cached = CacheFile(sender, hash);
            try
            {
                if (cached != null && File.Exists(cached))
                {
                    byte[] file = File.ReadAllBytes(cached);
                    if (file.Length <= MaxImageBytes && Fnv.Hash(file) == hash && SetImage(sender, rf, file)) return;
                }
            }
            catch { }
            // Not seen before: wait for the pieces.
            rf.Assembler.Begin(hash, total, chunkSize);
        }

        private static void OnChunk(uint sender, byte[] body)
        {
            if (!Remote.TryGetValue(sender, out RemoteFace rf) || !rf.Assembler.Active) return;
            var r = new WireReader(body);
            int index = r.U8() - 1;
            uint hash = r.U32();
            byte[] piece = r.Bytes(2048);
            if (r.Failed || r.Remaining != 0) return;

            byte[] file = rf.Assembler.Add(hash, index, piece);
            if (file == null) return;
            if (!SetImage(sender, rf, file))
            {
                Plugin.LogSource?.LogWarning($"[FaceSync] Picture from player {sender} refused (not a valid face image).");
                return;
            }
            try
            {
                string cached = CacheFile(sender, hash);
                if (cached != null) File.WriteAllBytes(cached, file);
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"[FaceSync] Could not save a friend's face: {ex.Message}"); }
        }

        private static bool SetImage(uint sender, RemoteFace rf, byte[] file)
        {
            if (!Images.DecodeOwnJpeg(file, MaxSide, out byte[] rgba, out int width, out int height)) return false;
            FaceEntry old = rf.Entry;
            rf.Entry = new FaceEntry
            {
                Name = "amico_" + sender,
                Source = rgba,
                Width = width,
                Height = height,
                Fill = rf.Settings.Fill,
                Settings = rf.Settings,
                Dirty = true,
            };
            Release(old);
            Plugin.LogSource?.LogInfo($"[FaceSync] Face of player {sender} received ({width}x{height}, {file.Length} bytes).");
            return true;
        }

        private static void Release(FaceEntry e)
        {
            if (e == null) return;
            try
            {
                if (e.Material != null) UnityEngine.Object.Destroy(e.Material);
                if (e.Texture != null) UnityEngine.Object.Destroy(e.Texture);
                if (e.Preview != null) UnityEngine.Object.Destroy(e.Preview);
            }
            catch { }
        }

        private static void PruneGone()
        {
            if (Remote.Count == 0 || Time.frameCount % 300 != 0) return;
            _gone.Clear();
            foreach (uint id in Remote.Keys) if (Identity.ByNetId(id) == null) _gone.Add(id);
            foreach (uint id in _gone)
            {
                Release(Remote[id].Entry);
                Remote.Remove(id);
            }
        }

        /// <summary>What this character's owner chose, or null to use the local settings.</summary>
        public static RemoteFace For(PlayerCharacter pc)
        {
            if (!GeneralModule.ShowFriends || pc == null || (Remote.Count == 0 && _fromFile.Count == 0)) return null;
            try { if (pc.isLocalPlayer) return null; } catch { }
            if (Remote.TryGetValue(Identity.NetId(pc), out RemoteFace rf)) return rf;
            if (_fromFile.Count == 0) return null;

            // Profile files are matched by the player's id, which the game learns a moment after they appear.
            int id = pc.GetInstanceID();
            if (Time.unscaledTime > _nextKeyRefresh)
            {
                _nextKeyRefresh = Time.unscaledTime + 3f;
                _keys.Clear();
            }
            if (!_keys.TryGetValue(id, out string key))
            {
                key = Identity.Key(pc);
                _keys[id] = key;
            }
            return key.Length > 0 && _fromFile.TryGetValue(key, out RemoteFace fromFile) ? fromFile : null;
        }
    }
}
