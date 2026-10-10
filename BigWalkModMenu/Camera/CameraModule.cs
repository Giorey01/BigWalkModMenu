using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BigWalk.ModMenu.Camera
{
    [BepInPlugin("com.bigwalk.modmenu.camera", "Big Walk Mod Menu - Camera", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModMenuApi.Register(new CameraModule());
        }
    }

    public sealed class CameraModule : MenuModule
    {
        private enum Mode { Off, Selfie, ThirdPerson, Free }

        public override string Id => "camera";
        public override string Title => "Camera";
        public override int Order => 30;

        private readonly Hotkey _selfieKey = new Hotkey("Selfie", "Selfie", KeyCode.F6);
        private readonly Hotkey _thirdKey = new Hotkey("Third", "Third person", KeyCode.F5);
        private readonly Hotkey _freeKey = new Hotkey("Free", "Photo mode (free camera)", KeyCode.F7);
        private readonly Hotkey _shotKey = new Hotkey("Shot", "Take a photo", KeyCode.F9);
        private readonly Hotkey _hudKey = new Hotkey("Hud", "Hide/show the interface", KeyCode.F10);

        private Mode _mode;
        private readonly object _rigOwner = new object();

        // Selfie / third person.
        private float _selfieDistance = 1.6f, _selfieHeight = 0.15f, _selfieAngle, _selfieFov = 55f;
        private float _thirdDistance = 3.2f, _thirdHeight = 0.5f, _thirdFov = 65f;
        private Vector3 _smoothPosition;
        private bool _smoothValid;

        // Free camera.
        private Vector3 _freePosition;
        private float _freeYaw, _freePitch, _freeRoll, _freeFov = 60f, _freeSpeed = 4f;

        // Screen.
        private bool _hideHud, _bars;
        private bool _uiCameraWasOn = true, _hudApplied, _featuresLogged;
        private readonly List<ScriptableRendererFeature> _uiFeatures = new List<ScriptableRendererFeature>();
        private int _superSize = 1;
        private bool _hideHudInPhotos = true;
        private int _shotStage, _shotAtFrame;
        private bool _shotRestoreHud;
        private string _lastShot;

        // Album.
        private readonly List<string> _photos = new List<string>();
        private readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();
        private float _nextScan;
        private string _selected;
        private Texture2D _selectedTex;
        private string _confirmDelete;

        private string PhotoDir
        {
            get
            {
                string p = ModMenuApi.Renamed(DataPath, "photos", "foto");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public override void Load(ModConfig c)
        {
            foreach (Hotkey k in new[] { _selfieKey, _thirdKey, _freeKey, _shotKey, _hudKey }) k.Load(c);
            _selfieDistance = c.GetFloat("Selfie.Distance", _selfieDistance);
            _selfieHeight = c.GetFloat("Selfie.Height", _selfieHeight);
            _selfieFov = c.GetFloat("Selfie.Fov", _selfieFov);
            _thirdDistance = c.GetFloat("Third.Distance", _thirdDistance);
            _thirdHeight = c.GetFloat("Third.Height", _thirdHeight);
            _thirdFov = c.GetFloat("Third.Fov", _thirdFov);
            _freeSpeed = c.GetFloat("Free.Speed", _freeSpeed);
            _superSize = Mathf.Clamp(c.GetInt("Photo.SuperSize", 1), 1, 4);
            _hideHudInPhotos = c.GetBool("Photo.HideHud", true);
            _bars = c.GetBool("Bars", false);
        }

        public override void Save(ModConfig c)
        {
            foreach (Hotkey k in new[] { _selfieKey, _thirdKey, _freeKey, _shotKey, _hudKey }) k.Save(c);
            c.Set("Selfie.Distance", _selfieDistance);
            c.Set("Selfie.Height", _selfieHeight);
            c.Set("Selfie.Fov", _selfieFov);
            c.Set("Third.Distance", _thirdDistance);
            c.Set("Third.Height", _thirdHeight);
            c.Set("Third.Fov", _thirdFov);
            c.Set("Free.Speed", _freeSpeed);
            c.Set("Photo.SuperSize", _superSize);
            c.Set("Photo.HideHud", _hideHudInPhotos);
            c.Set("Bars", _bars);
        }

        // ---- modes ----

#if DEV
        // Test bridge: "mod camera selfie|third|free|off".
        public override string DevCommand(string[] a)
        {
            if (a.Length > 2 && Enum.TryParse(a[2] == "third" ? "ThirdPerson" : a[2], true, out Mode mode) && mode != _mode) SetMode(mode);
            return "mode=" + _mode;
        }
#endif

        private void SetMode(Mode mode)
        {
            if (mode == _mode) mode = Mode.Off; // the same key again switches the mode off
            if (mode != Mode.Off && !Game.InWorld)
            {
                ModMenuApi.Toast("Join a walk to use the camera.");
                return;
            }

            if (mode == Mode.Off)
            {
                CameraRig.Release(_rigOwner);
                InputLock.Release(_rigOwner);
                _mode = Mode.Off;
                return;
            }

            if (!CameraRig.Acquire(_rigOwner))
            {
                ModMenuApi.Toast("Player camera not found.");
                return;
            }
            _mode = mode;
            _smoothValid = false;
            if (mode == Mode.Free)
            {
                _freePosition = CameraRig.Position;
                Vector3 e = CameraRig.Rotation.eulerAngles;
                _freeYaw = e.y;
                _freePitch = e.x > 180f ? e.x - 360f : e.x;
                _freeRoll = 0f;
                _freeFov = CameraRig.OriginalFov;
                // The keyboard and mouse now drive the camera instead of the character.
                InputLock.Set(_rigOwner, true, true, true);
                ModMenuApi.Toast("Free camera: WASD move, Q/E down/up, right mouse button to look, wheel to zoom, Shift fast.", 5f);
            }
            else
            {
                InputLock.Release(_rigOwner);
            }
        }

        public override void Update()
        {
            if (_selfieKey.Down) SetMode(Mode.Selfie);
            if (_thirdKey.Down) SetMode(Mode.ThirdPerson);
            if (_freeKey.Down) SetMode(Mode.Free);
            if (_shotKey.Down) TakePhoto();
            if (_hudKey.Down) _hideHud = !_hideHud;

            // Someone else (the replay) took the camera, or the player left the world.
            if (_mode != Mode.Off && !ReferenceEquals(CameraRig.Owner, _rigOwner))
            {
                InputLock.Release(_rigOwner);
                _mode = Mode.Off;
            }

            ApplyHud(_hideHud);
            UpdateShot();

            if (ModMenuApi.IsMenuOpen && Time.unscaledTime > _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                ScanPhotos();
            }
        }

        public override void LateUpdate()
        {
            if (_mode == Mode.Off || !CameraRig.Active) return;

            if (_mode == Mode.Free)
            {
                CameraRig.FreeFly(ref _freePosition, ref _freeYaw, ref _freePitch, ref _freeFov, _freeSpeed);
                if (Input.GetKey(KeyCode.Z)) _freeRoll += 40f * Time.unscaledDeltaTime;
                if (Input.GetKey(KeyCode.C)) _freeRoll -= 40f * Time.unscaledDeltaTime;
                if (Input.GetKeyDown(KeyCode.X)) _freeRoll = 0f;
                CameraRig.SetPose(_freePosition, Quaternion.Euler(_freePitch, _freeYaw, _freeRoll), _freeFov);
                return;
            }

            Transform pivot = CameraRig.Pivot;
            if (pivot == null) return;
            Vector3 eye = pivot.position;
            Vector3 forward = pivot.forward;
            Vector3 target;
            Quaternion rotation;
            float fov;

            if (_mode == Mode.Selfie)
            {
                // In front of the face, at eye level, turned around the player by the chosen angle.
                Vector3 flat = new Vector3(forward.x, 0f, forward.z);
                if (flat.sqrMagnitude < 1e-4f) flat = pivot.up; // looking straight up or down
                flat = Quaternion.AngleAxis(_selfieAngle, Vector3.up) * flat.normalized;
                target = eye + flat * _selfieDistance + Vector3.up * _selfieHeight;
                rotation = Quaternion.LookRotation(eye - target, Vector3.up);
                fov = _selfieFov;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !ModMenuApi.IsMenuOpen)
                {
                    _selfieDistance = Mathf.Clamp(_selfieDistance - wheel * 0.15f, 0.5f, 8f);
                    Dirty = true;
                }
            }
            else
            {
                // Behind the head, looking the same way as the player.
                target = eye - forward * _thirdDistance + Vector3.up * _thirdHeight;
                rotation = Quaternion.LookRotation(forward, Vector3.up);
                fov = _thirdFov;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !ModMenuApi.IsMenuOpen)
                {
                    _thirdDistance = Mathf.Clamp(_thirdDistance - wheel * 0.25f, 0.8f, 15f);
                    Dirty = true;
                }
            }

            // A little smoothing so footsteps do not shake the picture.
            if (!_smoothValid || (target - _smoothPosition).sqrMagnitude > 25f) { _smoothPosition = target; _smoothValid = true; }
            else _smoothPosition = Vector3.Lerp(_smoothPosition, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            if (_mode == Mode.Selfie) rotation = Quaternion.LookRotation(eye - _smoothPosition, Vector3.up);
            CameraRig.SetPose(_smoothPosition, rotation, fov);
        }

        // ---- game interface and photos ----

        // The game's interface is drawn by a second camera and, depending on the version, by a URP
        // renderer feature: switch off both and remember what was on.
        private void ApplyHud(bool hide)
        {
            if (hide == _hudApplied) return;
            try
            {
                UnityEngine.Camera ui = Game.PlayerUiCamera;
                if (hide)
                {
                    if (ui != null) { _uiCameraWasOn = ui.enabled; ui.enabled = false; }
                    _uiFeatures.Clear();
                    PlayerCharacter pc = Game.LocalPlayer;
                    var data = pc != null && pc.cameraMinder != null ? pc.cameraMinder.universalRendererData : null;
                    var features = data != null ? data.rendererFeatures : null;
                    if (features != null)
                    {
                        for (int i = 0; i < features.Count; i++)
                        {
                            ScriptableRendererFeature f = features[i];
                            if (f == null) continue;
                            if (!_featuresLogged) ModMenuApi.Log.LogInfo($"[Camera] Renderer feature: '{f.name}' active={f.isActive}");
                            if (f.isActive && f.name.Contains("UI"))
                            {
                                f.SetActive(false);
                                _uiFeatures.Add(f);
                            }
                        }
                        _featuresLogged = true;
                    }
                }
                else
                {
                    if (ui != null) ui.enabled = _uiCameraWasOn;
                    foreach (ScriptableRendererFeature f in _uiFeatures) if (f != null) f.SetActive(true);
                    _uiFeatures.Clear();
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("hud", $"[Camera] HUD switch failed: {ex.Message}"); }
            _hudApplied = hide;
        }

        public void TakePhoto()
        {
            if (_shotStage != 0) return;
            // Our own GUI must be gone before the frame that gets captured.
            ModMenuApi.HideGuiForFrames(6);
            _shotRestoreHud = _hideHudInPhotos && !_hideHud;
            if (_shotRestoreHud) _hideHud = true;
            _shotStage = 1;
            _shotAtFrame = Time.frameCount + 2;
        }

        private void UpdateShot()
        {
            if (_shotStage == 0 || Time.frameCount < _shotAtFrame) return;
            if (_shotStage == 1)
            {
                try
                {
                    _lastShot = Path.Combine(PhotoDir, "bigwalk_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                    ScreenCapture.CaptureScreenshot(_lastShot, _superSize);
                }
                catch (Exception ex)
                {
                    ModMenuApi.Log.LogError($"[Camera] Screenshot failed: {ex}");
                    ModMenuApi.Toast("Could not take the photo (see the log).");
                    _lastShot = null;
                }
                // The capture happens at the end of this frame: keep everything hidden a little longer.
                _shotStage = 2;
                _shotAtFrame = Time.frameCount + 3;
                return;
            }

            if (_shotRestoreHud) _hideHud = false;
            if (_lastShot != null) ModMenuApi.Toast("Photo saved: " + Path.GetFileName(_lastShot));
            _shotStage = 0;
            _nextScan = Time.unscaledTime + 1f;
        }

        public override void DrawOverlay()
        {
            if (_bars)
            {
                // Cinema bars for a 2.39:1 picture.
                float h = (Screen.height - Screen.width / 2.39f) * 0.5f;
                if (h > 1f)
                {
                    Ui.Tint(new Rect(0f, 0f, Screen.width, h), Color.black);
                    Ui.Tint(new Rect(0f, Screen.height - h, Screen.width, h), Color.black);
                }
            }
            if (_mode == Mode.Free && !ModMenuApi.IsMenuOpen)
            {
                var r = new Rect(12f, Screen.height - 34f, 620f, 22f);
                Ui.Tint(r, new Color(0f, 0f, 0f, 0.45f));
                GUI.Label(new Rect(r.x + 6f, r.y + 1f, r.width, 20f),
                    $"<size=11>Free camera • FOV {_freeFov:0}° • Z/C tilt, X level • {_shotKey.Key}: photo • {_freeKey.Key}: exit</size>");
            }
        }

        // ---- album ----

        private void ScanPhotos()
        {
            try
            {
                var files = new List<string>(Directory.GetFiles(PhotoDir, "*.png"));
                files.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a))); // newest first (names start with the date)
                _photos.Clear();
                _photos.AddRange(files);
            }
            catch (Exception ex) { ModMenuApi.LogOnce("album", $"[Camera] Album: {ex.Message}"); }
        }

        private Texture2D Thumb(string path, ref bool budget)
        {
            if (_thumbs.TryGetValue(path, out Texture2D tex) && (tex == null || Game.Alive(tex))) return tex;
            if (!budget) return null; // one decode per frame keeps the menu responsive
            budget = false;
            try
            {
                // A file still being written is tried again later.
                if ((DateTime.Now - File.GetLastWriteTime(path)).TotalSeconds < 1.5) return null;
            }
            catch { }
            tex = Images.LoadTexture(path, 160);
            _thumbs[path] = tex;
            return tex;
        }

        private void DrawAlbum(Ui ui)
        {
            ui.Header($"Album ({_photos.Count} photos)");
            if (_selected != null && File.Exists(_selected))
            {
                if (!Game.Alive(_selectedTex)) _selectedTex = Images.LoadTexture(_selected, 512);
                if (_selectedTex != null)
                {
                    float h = ui.Width * _selectedTex.height / Mathf.Max(1, _selectedTex.width);
                    GUI.DrawTexture(ui.Reserve(h), _selectedTex);
                }
                ui.Note(Path.GetFileName(_selected));
                bool confirming = _confirmDelete == _selected;
                int hit = ui.Buttons("Close", confirming ? "<b>Confirm delete</b>" : "Delete");
                if (hit == 0) CloseSelected();
                else if (hit == 1)
                {
                    if (confirming)
                    {
                        try { File.Delete(_selected); } catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Camera] Delete failed: {ex.Message}"); }
                        _thumbs.Remove(_selected);
                        CloseSelected();
                        ScanPhotos();
                    }
                    else _confirmDelete = _selected;
                }
                return;
            }

            if (_photos.Count == 0)
            {
                ui.Note("No photos yet. Take one with the button above.");
                return;
            }

            const int columns = 3;
            float gap = 4f;
            float cell = (ui.Width - gap * (columns - 1)) / columns;
            float cellH = cell * 9f / 16f;
            int shown = Mathf.Min(_photos.Count, 30);
            int rows = (shown + columns - 1) / columns;
            Rect area = ui.Reserve(rows * (cellH + gap));
            bool budget = true;
            for (int i = 0; i < shown; i++)
            {
                var r = new Rect(area.x + (i % columns) * (cell + gap), area.y + (i / columns) * (cellH + gap), cell, cellH);
                Texture2D tex = Thumb(_photos[i], ref budget);
                if (tex != null) GUI.DrawTexture(r, tex);
                else Ui.Tint(r, new Color(1f, 1f, 1f, 0.08f));
                if (GUI.Button(r, "", GUIStyle.none))
                {
                    _selected = _photos[i];
                    _selectedTex = null;
                    _confirmDelete = null;
                }
            }
            if (_photos.Count > shown) ui.Note($"Showing the {shown} most recent.");
        }

        private void CloseSelected()
        {
            if (Game.Alive(_selectedTex)) UnityEngine.Object.Destroy(_selectedTex);
            _selectedTex = null;
            _selected = null;
            _confirmDelete = null;
        }

        private void OpenFolder() => Game.OpenFolder(PhotoDir, "BepInEx/plugins/BigWalkModMenu/camera/photos");

        // ---- menu ----

        private static readonly string[] SuperSizes = { "screen (1x)", "double (2x)", "triple (3x)", "quadruple (4x)" };

        public override void DrawTab(Ui ui)
        {
            bool changed = false;

            ui.Header("Mode");
            string[] names = { "Normal", "Selfie", "Third person", "Photo (free)" };
            for (int i = 0; i < names.Length; i++) if ((int)_mode == i) names[i] = "<b>[" + names[i] + "]</b>";
            int hit = ui.Buttons(names[0], names[1]);
            if (hit == 0 && _mode != Mode.Off) SetMode(_mode);
            else if (hit == 1 && _mode != Mode.Selfie) SetMode(Mode.Selfie);
            hit = ui.Buttons(names[2], names[3]);
            if (hit == 0 && _mode != Mode.ThirdPerson) SetMode(Mode.ThirdPerson);
            else if (hit == 1 && _mode != Mode.Free) SetMode(Mode.Free);

            if (_mode == Mode.Selfie || _mode == Mode.Off)
            {
                ui.Header("Selfie");
                changed |= ui.Slider($"Distance: {_selfieDistance:0.0} m", ref _selfieDistance, 0.5f, 8f, 0.1f);
                changed |= ui.Slider($"Height: {_selfieHeight:+0.00;-0.00;0} m", ref _selfieHeight, -1f, 2f, 0.05f);
                ui.Slider($"Angle around you: {_selfieAngle:0}°", ref _selfieAngle, -180f, 180f, 5f);
                changed |= ui.Slider($"Field of view: {_selfieFov:0}°", ref _selfieFov, 15f, 110f, 1f);
            }
            if (_mode == Mode.ThirdPerson)
            {
                ui.Header("Third person");
                changed |= ui.Slider($"Distance: {_thirdDistance:0.0} m", ref _thirdDistance, 0.8f, 15f, 0.1f);
                changed |= ui.Slider($"Height: {_thirdHeight:+0.00;-0.00;0} m", ref _thirdHeight, -1f, 4f, 0.05f);
                changed |= ui.Slider($"Field of view: {_thirdFov:0}°", ref _thirdFov, 15f, 110f, 1f);
            }
            if (_mode == Mode.Free)
            {
                ui.Header("Free camera");
                ui.Slider($"Field of view: {_freeFov:0}°", ref _freeFov, 8f, 120f, 1f);
                ui.Slider($"Tilt: {_freeRoll:0}°", ref _freeRoll, -90f, 90f, 1f);
                changed |= ui.Slider($"Speed: {_freeSpeed:0.0} m/s", ref _freeSpeed, 0.5f, 30f, 0.5f);
                ui.Note("WASD move, Q/E down/up, right mouse button to look, wheel to zoom, Shift fast, Ctrl slow. The character stays where it is.", 3);
            }

            ui.Header("Photos");
            if (ui.Button($"Take one now ({_shotKey.Key})")) TakePhoto();
            int size = _superSize - 1;
            if (ui.Choice("Resolution", ref size, SuperSizes)) { _superSize = size + 1; changed = true; }
            changed |= ui.Toggle("Leave the game's interface out of photos", ref _hideHudInPhotos);
            if (ui.Button("Open the photo folder")) OpenFolder();

            ui.Header("Screen");
            ui.Toggle("Hide the game's interface", ref _hideHud);
            changed |= ui.Toggle("Cinema black bars", ref _bars);

            ui.Header("Keys");
            foreach (Hotkey k in new[] { _selfieKey, _thirdKey, _freeKey, _shotKey, _hudKey }) changed |= ui.HotkeyRow(k);

            DrawAlbum(ui);

            if (changed) Dirty = true;
        }
    }
}
