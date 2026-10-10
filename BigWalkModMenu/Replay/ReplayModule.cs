using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace BigWalk.ModMenu.Replay
{
    [BepInPlugin("com.bigwalk.modmenu.replay", "Big Walk Mod Menu - Replay", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new ReplayModule());
        }
    }

    public sealed class ReplayModule : MenuModule
    {
        /// <summary>A character taken over for the playback, with what is needed to give it back.</summary>
        private sealed class Puppet
        {
            public PlayerCharacter Pc;
            public Transform[] Bones;
            public bool BypassUpdate, BypassLate, BypassFixed, AnimatorOn, Kinematic, HadBody;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3[] BonePositions;
            public Quaternion[] BoneRotations;
        }

        private sealed class PropPuppet
        {
            public Prop Prop;
            public bool Kinematic, HadBody;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private enum CameraMode { Free, Original, Follow }

        public override string Id => "replay";
        public override string Title => "Replay";
        public override int Order => 70;

        private readonly Recorder _recorder = new Recorder();
        private readonly Hotkey _replayKey = new Hotkey("Replay", "Instant replay / exit", KeyCode.F4);
        private readonly Hotkey _recordKey = new Hotkey("Record", "Record the walk: start/stop", KeyCode.F2);
        private readonly object _owner = new object();

        // Playback.
        private Clip _clip;
        private readonly List<Puppet> _puppets = new List<Puppet>();
        private readonly Dictionary<ushort, PropPuppet> _props = new Dictionary<ushort, PropPuppet>();
        private readonly HashSet<ushort> _missingProps = new HashSet<ushort>();
        private float _time, _speed = 1f, _in, _out;
        private bool _playing, _loop = true;
        private CameraMode _cameraMode = CameraMode.Free;
        private int _followIndex;
        private Vector3 _camPosition, _followLast;
        private float _camYaw, _camPitch, _camFov = 60f;
        private bool _followValid;
        private Vector3 _recordedCamPosition;
        private Quaternion _recordedCamRotation = Quaternion.identity;
        private float _recordedCamFov = 60f;
        private readonly Vector3[] _rootPositions = new Vector3[Recorder.MaxPlayers];

        // Video export.
        private bool _exporting;
        private int _exportFps = 30, _exportFrame, _exportTotal;
        private string _exportDir;
        private int _oldCaptureRate;

        // Files.
        private readonly List<string> _files = new List<string>();
        private float _nextScan;
        private string _confirmDelete;
        private float _maxMinutes = 30f;

        private static readonly float[] BufferChoices = { 0f, 30f, 60f, 120f, 300f };
        private static readonly int[] RateChoices = { 15, 20, 30, 60 };
        private static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };

        public bool Active => _clip != null;

        private string ClipDir
        {
            get
            {
                string p = ModMenuApi.Renamed(DataPath, "recordings", "registrazioni");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        private string VideoDir
        {
            get
            {
                string p = Path.Combine(DataPath, "video");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public override void Load(ModConfig c)
        {
            _replayKey.Load(c);
            _recordKey.Load(c);
            _recorder.BufferSeconds = c.GetFloat("Buffer.Seconds", 60f);
            _recorder.Rate = Mathf.Clamp(c.GetInt("Rate", 30), 10, 60);
            _recorder.WithProps = c.GetBool("Props", true);
            _maxMinutes = c.GetFloat("Record.MaxMinutes", 30f);
            _exportFps = c.GetInt("Export.Fps", 30);
            _loop = c.GetBool("Loop", true);
        }

        public override void Save(ModConfig c)
        {
            _replayKey.Save(c);
            _recordKey.Save(c);
            c.Set("Buffer.Seconds", _recorder.BufferSeconds);
            c.Set("Rate", _recorder.Rate);
            c.Set("Props", _recorder.WithProps);
            c.Set("Record.MaxMinutes", _maxMinutes);
            c.Set("Export.Fps", _exportFps);
            c.Set("Loop", _loop);
        }

        // ---- frame loop ----

        public override void Update()
        {
            if (_replayKey.Down)
            {
                if (_exporting) FinishExport(true);
                else if (Active) StopPlayback();
                else StartInstantReplay();
            }
            if (_recordKey.Down) ToggleRecording();

            if (Active)
            {
                // Lost the world or the camera: nothing left to show.
                if (!Game.InWorld || !ReferenceEquals(CameraRig.Owner, _owner)) { StopPlayback(); return; }
                if (_exporting) return; // time is driven frame by frame in LateUpdate

                if (!ModMenuApi.IsMenuOpen)
                {
                    if (Input.GetKeyDown(KeyCode.Space)) _playing = !_playing;
                    float step = 1f / Mathf.Max(5, _recorder.Rate);
                    if (Input.GetKeyDown(KeyCode.RightArrow)) { _playing = false; _time += step; }
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) { _playing = false; _time -= step; }
                }
                if (_playing) _time += Time.unscaledDeltaTime * _speed;
                if (_time > _out)
                {
                    if (_loop) _time = _in;
                    else { _time = _out; _playing = false; }
                }
                _time = Mathf.Clamp(_time, _clip.Start, _clip.End);
                return;
            }

            _recorder.Update();
            if (_recorder.RecordingToFile && _recorder.FileSeconds > _maxMinutes * 60f)
            {
                _recorder.StopFile();
                ModMenuApi.Toast($"Recording stopped: {_maxMinutes:0} minutes reached.");
            }
            if (ModMenuApi.IsMenuOpen && Time.unscaledTime > _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                ScanFiles();
            }
        }

        public override void LateUpdate()
        {
            if (!Active) return;

            if (_exporting)
            {
                _time = _in + _exportFrame / (float)_exportFps;
                if (_time > _out || _exportFrame >= _exportTotal) { FinishExport(false); return; }
            }

            ApplyFrame();
            UpdateCamera();

            if (_exporting)
            {
                ModMenuApi.HideGuiForFrames(3);
                try
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(_exportDir, $"frame_{_exportFrame + 1:000000}.png"), 1);
                }
                catch (Exception ex)
                {
                    ModMenuApi.Log.LogError($"[Replay] Export failed: {ex}");
                    FinishExport(true);
                    return;
                }
                _exportFrame++;
            }
        }

        // ---- starting and stopping ----

        private void StartInstantReplay()
        {
            if (_recorder.BufferedFrames < 10)
            {
                ModMenuApi.Toast(_recorder.BufferSeconds < 1f ? "Instant replay is off (switch it on from the menu)." : "Nothing to replay yet.");
                return;
            }
            Clip clip = _recorder.Snapshot();
            StartPlayback(clip, Mathf.Max(clip.Start, clip.End - 15f));
        }

        private void StartPlayback(Clip clip, float at)
        {
            if (clip == null || clip.Frames.Count < 2)
            {
                ModMenuApi.Toast("Empty recording.");
                return;
            }
            if (!Game.InWorld)
            {
                ModMenuApi.Toast("Join a walk to watch the replay.");
                return;
            }
            if (Active) StopPlayback();
            if (_recorder.RecordingToFile)
            {
                _recorder.StopFile();
                ModMenuApi.Toast("The walk recording was stopped to watch the replay.");
            }
            if (!CameraRig.Acquire(_owner))
            {
                ModMenuApi.Toast("Player camera not found.");
                return;
            }
            InputLock.Set(_owner, true, true, true);

            _clip = clip;
            _in = clip.Start;
            _out = clip.End;
            _time = Mathf.Clamp(at, _in, _out);
            _playing = true;
            _speed = 1f;
            _followValid = false;
            _missingProps.Clear();

            _camPosition = CameraRig.Position;
            Vector3 e = CameraRig.Rotation.eulerAngles;
            _camYaw = e.y;
            _camPitch = e.x > 180f ? e.x - 360f : e.x;
            _camFov = CameraRig.OriginalFov;

            // Take over the characters that are here now, in the order they were recorded.
            List<PlayerCharacter> players = Game.Players;
            for (int i = 0; i < players.Count && i < Recorder.MaxPlayers; i++) _puppets.Add(Take(players[i], clip.BonePaths));
            ModMenuApi.Log.LogInfo($"[Replay] Playing '{clip.Name}': {clip.Frames.Count} frames, {clip.Duration:0.0} s, {_puppets.Count} characters.");
        }

        private static Puppet Take(PlayerCharacter pc, string[] paths)
        {
            var p = new Puppet { Pc = pc, Bones = Recorder.Resolve(pc, paths), Position = pc.transform.position, Rotation = pc.transform.rotation };
            p.BonePositions = new Vector3[p.Bones.Length];
            p.BoneRotations = new Quaternion[p.Bones.Length];
            for (int i = 0; i < p.Bones.Length; i++)
            {
                if (p.Bones[i] == null) continue;
                p.BonePositions[i] = p.Bones[i].localPosition;
                p.BoneRotations[i] = p.Bones[i].localRotation;
            }

            // The game must stop animating the character while the recording drives it.
            p.BypassUpdate = pc.bypassUpdate;
            p.BypassLate = pc.bypassLateUpdate;
            p.BypassFixed = pc.bypassFixedUpdate;
            pc.bypassUpdate = pc.bypassLateUpdate = pc.bypassFixedUpdate = true;
            try
            {
                if (pc.myAnimator != null) { p.AnimatorOn = pc.myAnimator.enabled; pc.myAnimator.enabled = false; }
            }
            catch { }
            try
            {
                if (pc.rb != null) { p.HadBody = true; p.Kinematic = pc.rb.isKinematic; pc.rb.isKinematic = true; }
            }
            catch { }
            return p;
        }

        private static void GiveBack(Puppet p)
        {
            if (p.Pc == null) return;
            try
            {
                for (int i = 0; i < p.Bones.Length; i++)
                {
                    if (p.Bones[i] == null) continue;
                    p.Bones[i].localPosition = p.BonePositions[i];
                    p.Bones[i].localRotation = p.BoneRotations[i];
                }
                p.Pc.transform.SetPositionAndRotation(p.Position, p.Rotation);
                if (p.HadBody && p.Pc.rb != null)
                {
                    p.Pc.rb.position = p.Position;
                    p.Pc.rb.rotation = p.Rotation;
                    p.Pc.rb.isKinematic = p.Kinematic;
                    if (!p.Kinematic)
                    {
                        p.Pc.rb.linearVelocity = Vector3.zero;
                        p.Pc.rb.angularVelocity = Vector3.zero;
                    }
                }
                if (p.Pc.myAnimator != null) p.Pc.myAnimator.enabled = p.AnimatorOn;
                p.Pc.bypassUpdate = p.BypassUpdate;
                p.Pc.bypassLateUpdate = p.BypassLate;
                p.Pc.bypassFixedUpdate = p.BypassFixed;
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Replay] Could not fully restore a character: {ex.Message}"); }
        }

        private void StopPlayback()
        {
            if (_exporting) FinishExport(true);
            foreach (Puppet p in _puppets) GiveBack(p);
            _puppets.Clear();
            foreach (PropPuppet p in _props.Values)
            {
                if (p.Prop == null) continue;
                try
                {
                    p.Prop.transform.SetPositionAndRotation(p.Position, p.Rotation);
                    if (p.HadBody && p.Prop.rb != null) p.Prop.rb.isKinematic = p.Kinematic;
                }
                catch { }
            }
            _props.Clear();
            _clip = null;
            CameraRig.Release(_owner);
            InputLock.Release(_owner);
            _recorder.Clear(); // what was buffered is now out of date
        }

        // ---- applying a moment of the recording ----

        private PropPuppet PropFor(ushort id)
        {
            if (_props.TryGetValue(id, out PropPuppet puppet)) return puppet;
            if (_missingProps.Contains(id)) return null;

            Prop prop = null;
            if (_recorder.LiveProps.TryGetValue(id, out Prop live) && live != null && _clip.PropNames.TryGetValue(id, out string liveName)
                && _recorder.PropNames.TryGetValue(id, out string known) && known == liveName)
            {
                prop = live;
            }
            else if (_clip.PropNames.TryGetValue(id, out string desc))
            {
                // A recording from another session: look the prop up by its saved identity.
                int bar = desc.LastIndexOf('|');
                string guid = bar >= 0 ? desc.Substring(bar + 1) : "";
                var all = Prop.allProps;
                if (guid.Length > 0 && all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Prop candidate = all[i];
                        if (candidate == null) continue;
                        try { if (candidate.savablePropGuid == guid) { prop = candidate; break; } } catch { }
                    }
                }
            }
            if (prop == null) { _missingProps.Add(id); return null; }

            puppet = new PropPuppet { Prop = prop, Position = prop.transform.position, Rotation = prop.transform.rotation };
            try
            {
                if (prop.rb != null) { puppet.HadBody = true; puppet.Kinematic = prop.rb.isKinematic; prop.rb.isKinematic = true; }
            }
            catch { }
            _props[id] = puppet;
            return puppet;
        }

        private void ApplyFrame()
        {
            int index = _clip.IndexAt(_time);
            byte[] a = _clip.Frames[index];
            byte[] b = index + 1 < _clip.Frames.Count ? _clip.Frames[index + 1] : a;
            float ta = _clip.Times[index], tb = index + 1 < _clip.Times.Count ? _clip.Times[index + 1] : ta;
            float k = tb > ta ? Mathf.Clamp01((_time - ta) / (tb - ta)) : 0f;
            // Frames with a different number of players or props cannot be blended one to one.
            if (b.Length != a.Length || tb - ta > 0.5f) { b = a; k = 0f; }

            using (var ra = new BinaryReader(new MemoryStream(a), Encoding.UTF8))
            using (var rb = new BinaryReader(new MemoryStream(b), Encoding.UTF8))
            {
                ra.ReadSingle();
                rb.ReadSingle();
                _recordedCamPosition = Vector3.Lerp(Codec.ReadVector(ra), Codec.ReadVector(rb), k);
                _recordedCamRotation = Quaternion.Slerp(Codec.ReadRotation(ra), Codec.ReadRotation(rb), k);
                _recordedCamFov = Mathf.Lerp(ra.ReadSingle(), rb.ReadSingle(), k);

                int players = ra.ReadByte();
                rb.ReadByte();
                int bones = _clip.BonePaths.Length;
                for (int i = 0; i < players; i++)
                {
                    Vector3 rootPosition = Vector3.Lerp(Codec.ReadVector(ra), Codec.ReadVector(rb), k);
                    Quaternion rootRotation = Quaternion.Slerp(Codec.ReadRotation(ra), Codec.ReadRotation(rb), k);
                    if (i < _rootPositions.Length) _rootPositions[i] = rootPosition;
                    Puppet p = i < _puppets.Count ? _puppets[i] : null;
                    bool alive = p != null && p.Pc != null;
                    if (alive) p.Pc.transform.SetPositionAndRotation(rootPosition, rootRotation);
                    for (int bone = 0; bone < bones; bone++)
                    {
                        Vector3 pos = Vector3.Lerp(Codec.ReadLocal(ra), Codec.ReadLocal(rb), k);
                        Quaternion rot = Quaternion.Slerp(Codec.ReadRotation(ra), Codec.ReadRotation(rb), k);
                        if (!alive) continue;
                        Transform t = p.Bones[bone];
                        if (t == null) continue;
                        t.localPosition = pos;
                        t.localRotation = rot;
                    }
                }

                int props = ra.ReadUInt16();
                rb.ReadUInt16();
                for (int i = 0; i < props; i++)
                {
                    ushort id = ra.ReadUInt16();
                    ushort idB = rb.ReadUInt16();
                    Vector3 pa = Codec.ReadVector(ra), pb = Codec.ReadVector(rb);
                    Quaternion qa = Codec.ReadRotation(ra), qb = Codec.ReadRotation(rb);
                    PropPuppet puppet = PropFor(id);
                    if (puppet == null || puppet.Prop == null) continue;
                    bool same = id == idB;
                    puppet.Prop.transform.SetPositionAndRotation(same ? Vector3.Lerp(pa, pb, k) : pa, same ? Quaternion.Slerp(qa, qb, k) : qa);
                }
            }
        }

        private void UpdateCamera()
        {
            if (_cameraMode == CameraMode.Original)
            {
                CameraRig.SetPose(_recordedCamPosition, _recordedCamRotation, _recordedCamFov);
                return;
            }

            if (_cameraMode == CameraMode.Follow)
            {
                // Carry the camera along with the chosen character, keeping whatever offset the user flew to.
                int i = Mathf.Clamp(_followIndex, 0, Recorder.MaxPlayers - 1);
                Vector3 target = _rootPositions[i];
                if (_followValid) _camPosition += target - _followLast;
                _followLast = target;
                _followValid = true;
            }
            else _followValid = false;

            if (!_exporting) CameraRig.FreeFly(ref _camPosition, ref _camYaw, ref _camPitch, ref _camFov, 5f);
            CameraRig.SetPose(_camPosition, Quaternion.Euler(_camPitch, _camYaw, 0f), _camFov);
        }

        // ---- match recording and files ----

        private void ToggleRecording()
        {
            if (_recorder.RecordingToFile)
            {
                string name = Path.GetFileName(_recorder.FilePath);
                _recorder.StopFile();
                ModMenuApi.Toast("Recording saved: " + name);
                ScanFiles();
                return;
            }
            if (Active) { ModMenuApi.Toast("Leave the replay before recording."); return; }
            if (!Game.InWorld) { ModMenuApi.Toast("Join a walk to record."); return; }
            string path = Path.Combine(ClipDir, "walk_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".bwr");
            if (_recorder.StartFile(path)) ModMenuApi.Toast("Recording the walk.");
            else ModMenuApi.Toast("Could not start recording (see the log).");
        }

        private void SaveClip()
        {
            if (_clip == null) return;
            try
            {
                // Only the part between the two markers.
                var part = new Clip { BonePaths = _clip.BonePaths };
                foreach (var kv in _clip.PropNames) part.PropNames[kv.Key] = kv.Value;
                for (int i = 0; i < _clip.Frames.Count; i++)
                {
                    if (_clip.Times[i] < _in - 0.001f || _clip.Times[i] > _out + 0.001f) continue;
                    part.Frames.Add(_clip.Frames[i]);
                    part.Times.Add(_clip.Times[i]);
                }
                string path = Path.Combine(ClipDir, "clip_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".bwr");
                part.Save(path);
                ModMenuApi.Toast($"Clip saved ({part.Duration:0} s): {Path.GetFileName(path)}");
                ScanFiles();
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogError($"[Replay] Saving the clip failed: {ex}");
                ModMenuApi.Toast("Could not save (see the log).");
            }
        }

        private void ScanFiles()
        {
            try
            {
                var files = new List<string>(Directory.GetFiles(ClipDir, "*.bwr"));
                files.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
                _files.Clear();
                _files.AddRange(files);
            }
            catch (Exception ex) { ModMenuApi.LogOnce("scan", $"[Replay] {ex.Message}"); }
        }

        private void LoadAndPlay(string path)
        {
            try
            {
                Clip clip = Clip.Load(path);
                StartPlayback(clip, clip.Start);
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogError($"[Replay] Could not load '{path}': {ex}");
                ModMenuApi.Toast("Could not open the recording: " + ex.Message);
            }
        }

        // ---- video export ----

        private void StartExport()
        {
            if (_clip == null || _exporting) return;
            _exportTotal = Mathf.Max(1, Mathf.CeilToInt((_out - _in) * _exportFps));
            _exportDir = Path.Combine(VideoDir, "video_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(_exportDir);
            _exportFrame = 0;
            _exporting = true;
            _playing = false;
            // Fixed time per rendered frame, so the game does not skip ahead while each picture is saved.
            _oldCaptureRate = Time.captureFramerate;
            Time.captureFramerate = _exportFps;
            ModMenuApi.Log.LogInfo($"[Replay] Exporting {_exportTotal} frames at {_exportFps} fps to {_exportDir}");
        }

        private void FinishExport(bool cancelled)
        {
            if (!_exporting) return;
            _exporting = false;
            Time.captureFramerate = _oldCaptureRate;
            try
            {
                string name = Path.GetFileName(_exportDir);
                // The frames become a video with ffmpeg on the Linux side (the game runs under Proton).
                string script = "#!/bin/sh\n" +
                                "# Joins the frames into an MP4 video, then deletes them. Needs ffmpeg.\n" +
                                "cd \"$(dirname \"$0\")\" || exit 1\n" +
                                $"ffmpeg -y -framerate {_exportFps} -i frame_%06d.png -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" \"../{name}.mp4\" \\\n" +
                                "  && rm -f frame_*.png && echo \"Done: " + name + ".mp4\"\n";
                File.WriteAllText(Path.Combine(_exportDir, "make_video.sh"), script);
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Replay] Could not write the ffmpeg script: {ex.Message}"); }
            ModMenuApi.Toast(cancelled
                ? $"Export stopped at {_exportFrame} frames."
                : $"{_exportFrame} frames saved. For the video: sh make_video.sh in the folder replay/video.", 8f);
        }

        // ---- overlay ----

        public override void DrawOverlay()
        {
            if (_recorder.RecordingToFile)
            {
                int s = (int)_recorder.FileSeconds;
                var r = new Rect(Screen.width - 150f, 10f, 140f, 22f);
                Ui.Tint(r, new Color(0f, 0f, 0f, 0.55f));
                bool blink = (int)(Time.unscaledTime * 2f) % 2 == 0;
                GUI.Label(new Rect(r.x + 6f, r.y + 1f, r.width, 20f), $"<color={(blink ? "#ff4444" : "#882222")}>●</color> REC {s / 60}:{s % 60:00}");
            }
            if (!Active || ModMenuApi.IsMenuOpen) return;

            // Transport bar along the bottom.
            float w = Mathf.Min(Screen.width - 40f, 900f), h = 78f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height - h - 16f;
            Ui.Tint(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.6f));

            float rel = _time - _clip.Start, total = _clip.Duration;
            GUI.Label(new Rect(x + 10f, y + 4f, 260f, 20f), $"<b>REPLAY</b>  {rel:0.0} / {total:0.0} s   {_speed:0.##}x");
            GUI.Label(new Rect(x + 270f, y + 4f, w - 280f, 20f),
                $"<size=11>Space: pause • ← →: frame • WASD + right button: camera • {_replayKey.Key}: exit</size>");

            float slid = GUI.HorizontalSlider(new Rect(x + 10f, y + 28f, w - 20f, 14f), _time, _clip.Start, _clip.End);
            if (Mathf.Abs(slid - _time) > 0.001f) { _time = slid; _playing = false; }
            // Markers of the part that loops, gets saved and exported.
            float span = Mathf.Max(0.001f, _clip.End - _clip.Start);
            Ui.Tint(new Rect(x + 10f + (w - 20f) * (_in - _clip.Start) / span, y + 24f, 2f, 18f), new Color(0.4f, 1f, 0.4f, 1f));
            Ui.Tint(new Rect(x + 10f + (w - 20f) * (_out - _clip.Start) / span, y + 24f, 2f, 18f), new Color(1f, 0.4f, 0.4f, 1f));

            float bx = x + 10f, by = y + 48f, bh = 22f;
            bool Btn(string label, float width)
            {
                bool hit = GUI.Button(new Rect(bx, by, width, bh), label);
                bx += width + 4f;
                return hit;
            }
            if (Btn(_playing ? "⏸" : "▶", 34f)) _playing = !_playing;
            if (Btn("Slower", 70f)) _speed = StepSpeed(-1);
            if (Btn("Faster", 76f)) _speed = StepSpeed(1);
            if (Btn("Start here", 74f)) { _in = Mathf.Min(_time, _out - 0.1f); }
            if (Btn("End here", 66f)) { _out = Mathf.Max(_time, _in + 0.1f); }
            string camLabel = _cameraMode == CameraMode.Free ? "Camera: free" : _cameraMode == CameraMode.Original ? "Camera: your view" : $"Camera: follows {_followIndex + 1}";
            if (Btn(camLabel, 132f)) NextCamera();
            if (Btn("Save clip", 78f)) SaveClip();
            if (Btn($"Video {_exportFps} fps", 92f)) StartExport();
            if (Btn("Exit", 50f)) StopPlayback();
        }

        private float StepSpeed(int dir)
        {
            int i = Array.IndexOf(Speeds, _speed);
            if (i < 0) i = 3;
            return Speeds[Mathf.Clamp(i + dir, 0, Speeds.Length - 1)];
        }

        private void NextCamera()
        {
            if (_cameraMode == CameraMode.Free) _cameraMode = CameraMode.Original;
            else if (_cameraMode == CameraMode.Original) { _cameraMode = CameraMode.Follow; _followIndex = 0; _followValid = false; }
            else if (_followIndex + 1 < _puppets.Count) { _followIndex++; _followValid = false; }
            else
            {
                _cameraMode = CameraMode.Free;
            }
            if (_cameraMode != CameraMode.Original)
            {
                // Continue from where the camera is now.
                _camPosition = CameraRig.Position;
                Vector3 e = CameraRig.Rotation.eulerAngles;
                _camYaw = e.y;
                _camPitch = e.x > 180f ? e.x - 360f : e.x;
            }
        }

        // ---- menu ----

        public override void DrawTab(Ui ui)
        {
            bool changed = false;

            ui.Header("Instant replay");
            int buffer = Mathf.Max(0, Array.IndexOf(BufferChoices, _recorder.BufferSeconds));
            if (ui.Choice("Keep in memory", ref buffer, new[] { "off", "last 30 s", "last minute", "last 2 minutes", "last 5 minutes" }))
            {
                _recorder.BufferSeconds = BufferChoices[buffer];
                changed = true;
            }
            ui.Note($"In memory now: {_recorder.BufferedSeconds:0} s ({_recorder.BufferedFrames} frames).");
            if (Active)
            {
                if (ui.Button($"Exit the replay ({_replayKey.Key})")) StopPlayback();
            }
            else if (ui.Button($"Replay now ({_replayKey.Key})")) StartInstantReplay();
            changed |= ui.Toggle("Loop", ref _loop);

            ui.Header("Record the walk");
            if (_recorder.RecordingToFile)
            {
                int s = (int)_recorder.FileSeconds;
                ui.Label($"<color=#ff6666>● Recording</color>  {s / 60}:{s % 60:00}");
            }
            if (ui.Button(_recorder.RecordingToFile ? $"Stop and save ({_recordKey.Key})" : $"Start recording ({_recordKey.Key})")) ToggleRecording();
            if (ui.Slider($"Maximum length: {_maxMinutes:0} minutes", ref _maxMinutes, 5f, 120f, 5f)) changed = true;
            ui.Note("It records movement, not video: small files that you can watch from any angle. Voices are not recorded.", 2);

            ui.Header("Quality");
            int rate = Mathf.Max(0, Array.IndexOf(RateChoices, _recorder.Rate));
            if (ui.Choice("Samples per second", ref rate, new[] { "15", "20", "30", "60" }))
            {
                _recorder.Rate = RateChoices[rate];
                _recorder.Clear();
                changed = true;
            }
            changed |= ui.Toggle("Record nearby objects too", ref _recorder.WithProps);
            int fps = _exportFps == 60 ? 1 : 0;
            if (ui.Choice("Exported video", ref fps, new[] { "30 fps", "60 fps" }))
            {
                _exportFps = fps == 1 ? 60 : 30;
                changed = true;
            }
            ui.Note("The video comes out as PNG frames in replay/video (about 2-3 MB each): then \"sh make_video.sh\" joins them into an MP4 and deletes them. Needs ffmpeg.", 3);

            ui.Header("Keys");
            changed |= ui.HotkeyRow(_replayKey);
            changed |= ui.HotkeyRow(_recordKey);

            ui.Header($"Saved recordings ({_files.Count})");
            for (int i = 0; i < _files.Count && i < 25; i++)
            {
                string file = _files[i];
                long kb = 0;
                try { kb = new FileInfo(file).Length / 1024; } catch { }
                ui.Label($"{Path.GetFileNameWithoutExtension(file)}  <color=#aaaaaa>{(kb >= 1024 ? $"{kb / 1024f:0.0} MB" : kb + " KB")}</color>");
                bool confirming = _confirmDelete == file;
                int hit = ui.Buttons("Watch", confirming ? "<b>Confirm</b>" : "Delete");
                if (hit == 0) LoadAndPlay(file);
                else if (hit == 1)
                {
                    if (confirming)
                    {
                        try { File.Delete(file); } catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Replay] Delete failed: {ex.Message}"); }
                        _confirmDelete = null;
                        ScanFiles();
                    }
                    else _confirmDelete = file;
                }
            }
            ui.Note("A recording plays back on the characters present now, in the same order: you need at least as many players as there were.", 2);

            if (changed) Dirty = true;
        }
    }
}
