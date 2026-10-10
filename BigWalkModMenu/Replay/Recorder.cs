using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace BigWalk.ModMenu.Replay
{
    /// <summary>
    /// Samples players, nearby props and the camera at a fixed rate. The samples go to a rolling
    /// buffer (instant replay) and, while a match recording is running, straight to a file.
    /// </summary>
    public sealed class Recorder
    {
        public const int MaxPlayers = 8;

        public int Rate = 30;
        public float BufferSeconds = 60f;
        public bool WithProps = true;
        public float PropRadius = 35f;

        public string[] BonePaths = new string[0];
        public readonly Dictionary<ushort, string> PropNames = new Dictionary<ushort, string>();
        /// <summary>Props seen in this session, by the id used in the frames.</summary>
        public readonly Dictionary<ushort, Prop> LiveProps = new Dictionary<ushort, Prop>();

        private readonly Queue<byte[]> _frames = new Queue<byte[]>();
        private readonly Queue<float> _times = new Queue<float>();
        private readonly Dictionary<int, Transform[]> _bones = new Dictionary<int, Transform[]>();
        private readonly Dictionary<int, ushort> _propIds = new Dictionary<int, ushort>();
        private readonly List<Prop> _nearProps = new List<Prop>();
        private readonly MemoryStream _stream = new MemoryStream(8192);
        private readonly BinaryWriter _writer;
        private float _accumulator, _clock, _nextPropScan;
        private ushort _nextPropId = 1;

        // Match recording.
        private BinaryWriter _file;
        private readonly HashSet<ushort> _filePropsWritten = new HashSet<ushort>();
        public string FilePath { get; private set; }
        public float FileSeconds { get; private set; }
        public bool RecordingToFile => _file != null;

        public int BufferedFrames => _frames.Count;
        public float BufferedSeconds => _times.Count > 1 ? _clock - _times.Peek() : 0f;

        public Recorder()
        {
            _writer = new BinaryWriter(_stream, Encoding.UTF8);
        }

        public void Clear()
        {
            _frames.Clear();
            _times.Clear();
        }

        /// <summary>A copy of what is in the rolling buffer right now.</summary>
        public Clip Snapshot()
        {
            var clip = new Clip { BonePaths = BonePaths, Name = "replay" };
            foreach (var kv in PropNames) clip.PropNames[kv.Key] = kv.Value;
            clip.Frames.AddRange(_frames);
            clip.Times.AddRange(_times);
            return clip;
        }

        // ---- bones ----

        private static bool IsBone(string name)
        {
            if (name == "dummy" || name == "Armature") return true;
            if (name.EndsWith("_end") || name.EndsWith("_empty")) return false;
            return name.Contains("_bone") || name.StartsWith("SH_") || name.StartsWith("SHL_");
        }

        private static string PathFrom(Transform root, Transform t)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private void DiscoverBones(PlayerCharacter pc)
        {
            Transform dummy = null;
            try { dummy = pc.myAnimator != null ? pc.myAnimator.transform : null; } catch { }
            if (dummy == null) dummy = Game.FindDeep(pc.transform, "dummy");
            if (dummy == null) return;

            var paths = new List<string>();
            foreach (Transform t in dummy.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && IsBone(t.name)) paths.Add(PathFrom(pc.transform, t));
            }
            BonePaths = paths.ToArray();
            ModMenuApi.Log.LogInfo($"[Replay] {BonePaths.Length} bones recorded per player.");
        }

        public Transform[] BonesOf(PlayerCharacter pc)
        {
            int id = pc.GetInstanceID();
            if (_bones.TryGetValue(id, out Transform[] bones) && bones.Length == BonePaths.Length) return bones;
            bones = Resolve(pc, BonePaths);
            _bones[id] = bones;
            return bones;
        }

        public static Transform[] Resolve(PlayerCharacter pc, string[] paths)
        {
            var bones = new Transform[paths.Length];
            for (int i = 0; i < paths.Length; i++) bones[i] = pc.transform.Find(paths[i]);
            return bones;
        }

        // ---- sampling ----

        public void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _clock += dt;
            if (!Game.InWorld)
            {
                if (RecordingToFile) StopFile();
                if (_frames.Count > 0) Clear();
                return;
            }
            bool wanted = BufferSeconds > 0.5f || RecordingToFile;
            if (!wanted) { if (_frames.Count > 0) Clear(); return; }

            _accumulator += dt;
            float step = 1f / Mathf.Max(5, Rate);
            if (_accumulator < step) return;
            _accumulator = Mathf.Min(_accumulator - step, step); // never try to catch up after a long hitch

            byte[] frame;
            try { frame = Sample(); }
            catch (Exception ex) { ModMenuApi.LogOnce("sample", $"[Replay] Sampling failed: {ex}"); return; }
            if (frame == null) return;

            if (BufferSeconds > 0.5f)
            {
                _frames.Enqueue(frame);
                _times.Enqueue(_clock);
                while (_times.Count > 2 && _clock - _times.Peek() > BufferSeconds)
                {
                    _frames.Dequeue();
                    _times.Dequeue();
                }
            }
            else if (_frames.Count > 0) Clear();

            if (_file != null) WriteToFile(frame);
        }

        private byte[] Sample()
        {
            List<PlayerCharacter> players = Game.Players;
            if (players.Count == 0) return null;
            if (BonePaths.Length == 0)
            {
                DiscoverBones(players[0]);
                if (BonePaths.Length == 0) return null;
            }

            _stream.SetLength(0);
            BinaryWriter w = _writer;
            w.Write(_clock);

            UnityEngine.Camera cam = Game.PlayerCamera;
            if (cam != null)
            {
                Codec.Write(w, cam.transform.position);
                Codec.Write(w, cam.transform.rotation);
                w.Write(cam.fieldOfView);
            }
            else
            {
                Codec.Write(w, players[0].transform.position);
                Codec.Write(w, Quaternion.identity);
                w.Write(60f);
            }

            int count = Mathf.Min(players.Count, MaxPlayers);
            w.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                PlayerCharacter pc = players[i];
                Codec.Write(w, pc.transform.position);
                Codec.Write(w, pc.transform.rotation);
                Transform[] bones = BonesOf(pc);
                for (int b = 0; b < bones.Length; b++)
                {
                    Transform t = bones[b];
                    if (t != null)
                    {
                        Codec.WriteLocal(w, t.localPosition);
                        Codec.Write(w, t.localRotation);
                    }
                    else
                    {
                        Codec.WriteLocal(w, Vector3.zero);
                        Codec.Write(w, Quaternion.identity);
                    }
                }
            }

            if (WithProps && _clock > _nextPropScan)
            {
                _nextPropScan = _clock + 2f;
                ScanProps(players, count);
            }
            int props = 0;
            long countAt = _stream.Position;
            w.Write((ushort)0);
            if (WithProps)
            {
                foreach (Prop prop in _nearProps)
                {
                    if (prop == null) continue;
                    w.Write(_propIds[prop.GetInstanceID()]);
                    Codec.Write(w, prop.transform.position);
                    Codec.Write(w, prop.transform.rotation);
                    props++;
                }
            }
            w.Flush();
            long end = _stream.Position;
            _stream.Position = countAt;
            w.Write((ushort)props);
            w.Flush();
            _stream.Position = end;
            return _stream.ToArray();
        }

        private void ScanProps(List<PlayerCharacter> players, int count)
        {
            _nearProps.Clear();
            var all = Prop.allProps;
            if (all == null) return;
            float r2 = PropRadius * PropRadius;
            for (int i = 0; i < all.Count && _nearProps.Count < 400; i++)
            {
                Prop prop = all[i];
                if (prop == null) continue;
                Vector3 p = prop.transform.position;
                bool near = false;
                for (int k = 0; k < count && !near; k++) near = (players[k].transform.position - p).sqrMagnitude < r2;
                if (!near) continue;

                int id = prop.GetInstanceID();
                if (!_propIds.TryGetValue(id, out ushort pid))
                {
                    if (_nextPropId == ushort.MaxValue) continue;
                    pid = _nextPropId++;
                    _propIds[id] = pid;
                    string guid = "";
                    try { guid = prop.savablePropGuid ?? ""; } catch { }
                    PropNames[pid] = prop.name + "|" + guid;
                    LiveProps[pid] = prop;
                }
                _nearProps.Add(prop);
            }
        }

        // ---- match recording ----

        public bool StartFile(string path)
        {
            if (_file != null) return true;
            try
            {
                if (BonePaths.Length == 0 && Game.Players.Count > 0) DiscoverBones(Game.Players[0]);
                if (BonePaths.Length == 0) return false;
                var gz = new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest);
                _file = new BinaryWriter(gz, Encoding.UTF8);
                Clip.WriteHeader(_file, BonePaths);
                _filePropsWritten.Clear();
                FilePath = path;
                FileSeconds = 0f;
                return true;
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogError($"[Replay] Could not start the recording: {ex}");
                _file = null;
                return false;
            }
        }

        private void WriteToFile(byte[] frame)
        {
            try
            {
                foreach (Prop prop in _nearProps)
                {
                    if (prop == null) continue;
                    ushort id = _propIds[prop.GetInstanceID()];
                    if (_filePropsWritten.Add(id)) Clip.WriteProp(_file, id, PropNames[id]);
                }
                Clip.WriteFrame(_file, frame);
                FileSeconds += 1f / Mathf.Max(5, Rate);
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogError($"[Replay] Recording stopped, write error: {ex.Message}");
                StopFile();
            }
        }

        public void StopFile()
        {
            if (_file == null) return;
            try
            {
                _file.Write((byte)0);
                _file.Dispose();
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Replay] Closing the recording: {ex.Message}"); }
            _file = null;
        }
    }
}
