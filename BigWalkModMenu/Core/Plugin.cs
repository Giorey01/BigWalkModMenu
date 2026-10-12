using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Mirror;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// One tab of the menu. A module lives in its own DLL, registers itself from its plugin's Load()
    /// with ModMenuApi.Register and is driven by the menu host from then on.
    /// </summary>
    public abstract class MenuModule
    {
        /// <summary>Short name without spaces: used for the settings file and data folder.</summary>
        public abstract string Id { get; }
        /// <summary>Text on the tab.</summary>
        public abstract string Title { get; }
        public virtual int Order => 100;

        public ModConfig Config { get; internal set; }
        /// <summary>Set to true when a setting changed; the host saves shortly after.</summary>
        public bool Dirty;

        internal bool Initialised, Disabled;
        internal int Faults;
        internal string LastError;

        /// <summary>Folder for the module's own files (photos, recordings, sounds...).</summary>
        public string DataPath
        {
            get
            {
                string p = Path.Combine(ModMenuApi.RootPath, Id);
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public virtual void Load(ModConfig c) { }
        public virtual void Save(ModConfig c) { }
        /// <summary>Called once from the first Update, when Unity objects can be created.</summary>
        public virtual void Init() { }
        public virtual void Update() { }
        public virtual void LateUpdate() { }
        /// <summary>The module's tab. Only called while the menu is open.</summary>
        public virtual void DrawTab(Ui ui) { }
        /// <summary>Drawn every frame, menu open or not (HUD elements).</summary>
        public virtual void DrawOverlay() { }
        public virtual void OnMenuClosed() { }
        /// <summary>A cached game object turned out to be gone: forget every cached one so they get rebuilt.</summary>
        public virtual void OnObjectsLost() { }
        /// <summary>Self-test hook for development builds; null means "not handled".</summary>
        public virtual string DevCommand(string[] args) => null;
    }

    public static class ModMenuApi
    {
        public const string Version = "1.7.0";
        public static ManualLogSource Log;
        public static string RootPath = "";

        internal static readonly List<MenuModule> Modules = new List<MenuModule>();
        internal static Hotkey CapturingKey;
        private static readonly HashSet<string> _loggedOnce = new HashSet<string>();

        public static bool CapturingHotkey => CapturingKey != null;
        public static bool IsMenuOpen => MenuHost.Instance != null && MenuHost.Instance.IsOpen;
        /// <summary>True for the frames in which a screenshot is taken: nothing of ours must be drawn.</summary>
        public static bool HideAllGui => Time.frameCount <= _hideGuiUntilFrame;
        private static int _hideGuiUntilFrame = -1;

        public static void Register(MenuModule module)
        {
            if (module == null) return;
            foreach (var m in Modules) if (m.Id == module.Id) { Log.LogWarning($"Module '{module.Id}' registered twice, ignored."); return; }
            try
            {
                module.Config = new ModConfig(Path.Combine(RootPath, "config", module.Id + ".cfg"));
                module.Load(module.Config);
            }
            catch (Exception ex)
            {
                Log.LogError($"[{module.Id}] Load failed: {ex}");
            }
            Modules.Add(module);
            Modules.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Title, b.Title));
            Log.LogInfo($"Module registered: {module.Title} ({module.Id})");
        }

        public static T Get<T>() where T : MenuModule
        {
            foreach (var m in Modules) if (m is T t) return t;
            return null;
        }

        /// <summary>Keeps every mod GUI off screen for the next frames (used around screenshots).</summary>
        public static void HideGuiForFrames(int frames) => _hideGuiUntilFrame = Mathf.Max(_hideGuiUntilFrame, Time.frameCount + frames);

        /// <summary>
        /// Path of a data folder or file that had an Italian name up to 1.3.0: what is found under the
        /// old name is moved to the new one, so nobody loses photos or recordings by updating.
        /// </summary>
        public static string Renamed(string parent, string name, string oldName)
        {
            string path = Path.Combine(parent, name);
            try
            {
                string old = Path.Combine(parent, oldName);
                if (Directory.Exists(old) && !Directory.Exists(path)) Directory.Move(old, path);
                else if (File.Exists(old) && !File.Exists(path)) File.Move(old, path);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Could not rename '{oldName}' to '{name}' in {parent}: {ex.Message}");
            }
            return path;
        }

        public static void LogOnce(string key, string message)
        {
            if (_loggedOnce.Add(key)) Log.LogWarning(message);
        }

        /// <summary>Short message at the top of the screen for a couple of seconds.</summary>
        public static void Toast(string text, float seconds = 2.5f)
        {
            MenuHost.ToastText = text;
            MenuHost.ToastUntil = Time.unscaledTime + seconds;
        }

        public static void ApplyPatch(Harmony harmony, Type patchClass)
        {
            try
            {
                var patched = harmony.CreateClassProcessor(patchClass).Patch();
                int count = patched != null ? patched.Count : 0;
                Log.LogInfo($"[Patch] {patchClass.Name}: {(count > 0 ? "OK" : "skipped (target not found)")}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Patch] {patchClass.Name} failed: {ex}");
            }
        }
    }

    [BepInPlugin(Guid, "Big Walk Mod Menu", ModMenuApi.Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.bigwalk.modmenu";

        public override void Load()
        {
            ModMenuApi.Log = Log;
            ModMenuApi.RootPath = Path.Combine(Paths.PluginPath, "BigWalkModMenu");
            Directory.CreateDirectory(ModMenuApi.RootPath);
            Log.LogInfo($"Big Walk Mod Menu {ModMenuApi.Version} is loading...");
            RemoveLegacyFaces();

            ModMenuApi.Register(new GeneralModule());

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<MenuHost>();
                AddComponent<MenuHost>();
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to register MenuHost: {ex}");
            }

            // Each patch is applied on its own so that one failure cannot take the others down.
            var harmony = new Harmony(Guid);
            ModMenuApi.ApplyPatch(harmony, typeof(PlayerHeadUpdatePatch));
        }

        // Up to 1.4.0 the Faces tab was a plugin of its own in BepInEx/plugins/FaceMaskMod. Left there next to
        // the new one it would be loaded too, so it is deleted before BepInEx gets to it (this plugin is
        // loaded first: every tab depends on it). Photos and settings are moved by the Faces tab itself.
        private void RemoveLegacyFaces()
        {
            if (!File.Exists(Path.Combine(ModMenuApi.RootPath, "BigWalk.ModMenu.Faces.dll"))) return;
            string old = Path.Combine(Paths.PluginPath, "FaceMaskMod");
            foreach (string name in new[] { "BigWalk.FaceMaskMod.dll", "StbImageSharp.dll" })
            {
                string file = Path.Combine(old, name);
                if (!File.Exists(file)) continue;
                try
                {
                    File.Delete(file);
                    Log.LogInfo($"Removed the old faces plugin file {file}");
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Could not remove the old faces plugin file {file}: {ex.Message}. Delete it by hand.");
                }
            }
        }
    }

    public sealed class MenuHost : MonoBehaviour
    {
        public static MenuHost Instance;
        internal static string ToastText;
        internal static float ToastUntil;

        private readonly Ui _ui = new Ui();
        private readonly Dictionary<string, float> _contentHeight = new Dictionary<string, float>();
        private readonly Dictionary<string, Vector2> _scroll = new Dictionary<string, Vector2>();
        private readonly object _lockOwner = new object();
        private bool _open, _wasOpen, _matrixBroken, _dragging;
        private Vector2 _dragOffset;
        private string _tab = "general";
        private float _nextSave;

        public MenuHost(IntPtr pointer) : base(pointer)
        {
        }

        public bool IsOpen => _open;

        public void Awake()
        {
            Instance = this;
        }

#if DEV
        internal void DevOpen(bool open, string tab, float scrollY)
        {
            _open = open;
            if (tab != null) _tab = tab;
            if (tab != null && scrollY >= 0f) _scroll[tab] = new Vector2(0f, scrollY);
        }
#endif

        private static void Guard(MenuModule m, string phase, Action action)
        {
            if (m.Disabled) return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                m.Faults++;
                m.LastError = phase + ": " + ex.Message;
                // A dead handle to a game object: the module drops what it had cached and builds it again.
                if (ex is Il2CppInterop.Runtime.ObjectCollectedException)
                {
                    try { m.OnObjectsLost(); } catch { }
                }
                if (m.Faults <= 3) ModMenuApi.Log.LogError($"[{m.Id}] {phase}: {ex}");
                if (m.Faults >= 40)
                {
                    m.Disabled = true;
                    ModMenuApi.Log.LogError($"[{m.Id}] Too many errors, module paused. It can be restarted from its tab.");
                }
            }
        }

        public void Update()
        {
            try
            {
                var general = GeneralModule.Instance;
                if (general != null && (general.MenuKey.Down || general.MenuKeyAlt.Down))
                {
                    _open = !_open;
                }

                bool open = IsOpen;
                bool blockAll = _open && general != null && general.BlockMovement;
                InputLock.Set(_lockOwner, _open, blockAll, _open);
                InputLock.Update();
                Net.Tick();
#if DEV
                Dev.Tick(this);
#endif

                var modules = ModMenuApi.Modules;
                for (int i = 0; i < modules.Count; i++)
                {
                    MenuModule m = modules[i];
                    if (!m.Initialised)
                    {
                        m.Initialised = true;
                        Guard(m, "Init", m.Init);
                    }
                    Guard(m, "Update", m.Update);
                }

                if (_wasOpen && !open)
                {
                    ModMenuApi.CapturingKey = null;
                    Ui.CloseDropdowns();
                    for (int i = 0; i < modules.Count; i++) Guard(modules[i], "OnMenuClosed", modules[i].OnMenuClosed);
                    SaveDirty();
                }
                else if (Time.unscaledTime > _nextSave)
                {
                    _nextSave = Time.unscaledTime + 10f;
                    SaveDirty();
                }
                _wasOpen = open;
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 300 == 0) ModMenuApi.Log.LogError($"MenuHost.Update: {ex}");
            }
        }

        public void LateUpdate()
        {
            var modules = ModMenuApi.Modules;
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].Initialised) Guard(modules[i], "LateUpdate", modules[i].LateUpdate);
            }
            try { CameraRig.Apply(); }
            catch (Exception ex) { ModMenuApi.LogOnce("rig-apply", $"[CameraRig] {ex}"); }
        }

        public void OnApplicationQuit()
        {
            SaveDirty();
        }

        private static void SaveDirty()
        {
            foreach (MenuModule m in ModMenuApi.Modules)
            {
                if (!m.Dirty || m.Config == null) continue;
                m.Dirty = false;
                try
                {
                    m.Save(m.Config);
                    m.Config.Save();
                }
                catch (Exception ex)
                {
                    ModMenuApi.Log.LogWarning($"[{m.Id}] Save failed: {ex.Message}");
                }
            }
        }

        public void OnGUI()
        {
            if (ModMenuApi.HideAllGui) return;
            try
            {
                var modules = ModMenuApi.Modules;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i].Initialised) Guard(modules[i], "DrawOverlay", modules[i].DrawOverlay);
                }

                DrawToast();
                if (IsOpen) DrawWindow();
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 300 == 0) ModMenuApi.Log.LogError($"MenuHost.OnGUI: {ex}");
            }
        }

        private static void DrawToast()
        {
            if (string.IsNullOrEmpty(ToastText) || Time.unscaledTime > ToastUntil) return;
            float w = Mathf.Min(Screen.width - 40f, 560f);
            var rect = new Rect((Screen.width - w) * 0.5f, 24f, w, 26f);
            Ui.Tint(rect, new Color(0f, 0f, 0f, 0.65f));
            GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 20f, 22f), ToastText);
        }

        private void DrawWindow()
        {
            float scale = GeneralModule.Instance != null ? GeneralModule.Instance.EffectiveScale : 1f;
            Matrix4x4 oldMatrix = Matrix4x4.identity;
            bool scaled = false;
            if (!_matrixBroken && Mathf.Abs(scale - 1f) > 0.01f)
            {
                try
                {
                    oldMatrix = GUI.matrix;
                    GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                    scaled = true;
                }
                catch (Exception ex)
                {
                    _matrixBroken = true;
                    ModMenuApi.Log.LogWarning($"GUI scaling is not available: {ex.Message}");
                }
            }
            if (!scaled) scale = 1f;

            int depth = GUI.depth;
            GUI.depth = -1050;

            float sw = Screen.width / scale, sh = Screen.height / scale;
            const float tabsW = 112f;
            float w = 500f, h = Mathf.Min(680f, sh - 16f);
            var general = GeneralModule.Instance;
            float x = sw - w - 16f, y = Mathf.Max(8f, (sh - h) * 0.5f);
            if (general != null && general.WindowX >= 0f)
            {
                x = general.WindowX;
                y = general.WindowY;
            }

            // Dragged by its title bar. Positions are in menu units, so they survive a change of size.
            Event ev = Event.current;
            var title = new Rect(x, y, w, 24f);
            if (ev.type == EventType.MouseDown && ev.button == 0 && title.Contains(ev.mousePosition))
            {
                _dragging = true;
                _dragOffset = ev.mousePosition - new Vector2(x, y);
                ev.Use();
            }
            else if (_dragging && (ev.rawType == EventType.MouseUp || !Input.GetMouseButton(0)))
            {
                _dragging = false;
                if (general != null) general.Dirty = true;
            }
            else if (_dragging && ev.type == EventType.MouseDrag)
            {
                x = ev.mousePosition.x - _dragOffset.x;
                y = ev.mousePosition.y - _dragOffset.y;
                if (general != null) { general.WindowX = Mathf.Max(0f, x); general.WindowY = Mathf.Max(0f, y); }
                ev.Use();
            }
            // Never off screen: at least the title bar stays reachable.
            x = Mathf.Clamp(x, 8f - w + 120f, sw - 120f);
            y = Mathf.Clamp(y, 0f, sh - 30f);

            Ui.Tint(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.07f, 0.78f));
            GUI.Box(new Rect(x, y, w, h), "BIG WALK MOD MENU");
            if (_dragging) Ui.Tint(title, new Color(1f, 1f, 1f, 0.12f));

            // Tabs down the left side.
            var modules = ModMenuApi.Modules;
            MenuModule current = null;
            float ty = y + 26f;
            for (int i = 0; i < modules.Count; i++)
            {
                MenuModule m = modules[i];
                bool selected = m.Id == _tab;
                if (selected) current = m;
                string label = selected ? "<b>" + m.Title + "</b>" : m.Title;
                if (m.Disabled) label = "<color=#ff8888>" + label + "</color>";
                if (selected) Ui.Tint(new Rect(x + 6f, ty, tabsW, 24f), new Color(1f, 1f, 1f, 0.18f));
                if (GUI.Button(new Rect(x + 6f, ty, tabsW, 24f), label))
                {
                    _tab = m.Id;
                    ModMenuApi.CapturingKey = null;
                    Ui.CloseDropdowns();
                }
                ty += 27f;
            }
            if (current == null && modules.Count > 0)
            {
                current = modules[0];
                _tab = current.Id;
            }

            // Content, scrolled when it is taller than the window.
            if (current != null)
            {
                float cx = x + tabsW + 14f, cy = y + 26f, cw = w - tabsW - 20f, ch = h - 34f;
                _contentHeight.TryGetValue(current.Id, out float contentH);
                _scroll.TryGetValue(current.Id, out Vector2 scroll);
                float innerW = cw - 18f;
                scroll = GUI.BeginScrollView(new Rect(cx, cy, cw, ch), scroll, new Rect(0f, 0f, innerW, Mathf.Max(contentH, 10f)));
                _ui.Begin(2f, 2f, innerW - 4f);
                if (current.Disabled)
                {
                    _ui.Label("<color=#ff8888>Tab paused after too many errors.</color>");
                    _ui.Note(current.LastError ?? "", 4);
                    if (_ui.Button("Restart the tab"))
                    {
                        current.Disabled = false;
                        current.Faults = 0;
                    }
                }
                else
                {
                    MenuModule m = current;
                    Guard(m, "DrawTab", () => m.DrawTab(_ui));
                    if (m.Faults > 0 && m.LastError != null)
                    {
                        _ui.Space();
                        _ui.Note("<color=#ffaa66>Last error: " + m.LastError + "</color>", 3);
                    }
                }
                GUI.EndScrollView();
                _scroll[current.Id] = scroll;
                _contentHeight[current.Id] = _ui.Used + 8f;
            }

            GUI.depth = depth;
            if (scaled) GUI.matrix = oldMatrix;
        }
    }

    /// <summary>First tab: settings of the menu itself, the installed modules, and the Solo Mod helpers.</summary>
    public sealed class GeneralModule : MenuModule
    {
        public static GeneralModule Instance;

        public override string Id => "general";
        public override string Title => "General";
        public override int Order => 0;

        public readonly Hotkey MenuKey = new Hotkey("Menu", "Open/close the menu", KeyCode.F8);
        public readonly Hotkey MenuKeyAlt = new Hotkey("MenuAlt", "Open/close (second key)", KeyCode.Insert);
        public float UiScale;          // 0 = automatic
        public bool BlockMovement;
        /// <summary>Send my look, face and emotes to friends who run the mod.</summary>
        public static bool ShareMine = true;
        /// <summary>Show what friends send about themselves.</summary>
        public static bool ShowFriends = true;

        /// <summary>Where the window was dragged to, in menu units; negative = its usual place on the right.</summary>
        public float WindowX = -1f, WindowY = -1f;
        private string _profileNote;
        private bool _profilesLoaded;

        public GeneralModule()
        {
            Instance = this;
        }

        public float EffectiveScale
        {
            get
            {
                if (UiScale > 0.1f) return UiScale;
                return Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            }
        }

        public override void Load(ModConfig c)
        {
            MenuKey.Load(c);
            MenuKeyAlt.Load(c);
            UiScale = c.GetFloat("UiScale", 0f);
            BlockMovement = c.GetBool("BlockMovement", false);
            ShareMine = c.GetBool("Net.ShareMine", true);
            ShowFriends = c.GetBool("Net.ShowFriends", true);
            WindowX = c.GetFloat("WindowX", -1f);
            WindowY = c.GetFloat("WindowY", -1f);
        }

        public override void Save(ModConfig c)
        {
            MenuKey.Save(c);
            MenuKeyAlt.Save(c);
            c.Set("UiScale", UiScale);
            c.Set("BlockMovement", BlockMovement);
            c.Set("Net.ShareMine", ShareMine);
            c.Set("Net.ShowFriends", ShowFriends);
            c.Set("WindowX", WindowX);
            c.Set("WindowY", WindowY);
        }

        public override void Update()
        {
            // Friends' profile files are read once, the first time we are in a world (my own id is known then).
            if (!_profilesLoaded && Game.InWorld && Identity.Key(Game.LocalPlayer).Length > 0)
            {
                _profilesLoaded = true;
                Profiles.LoadAll();
            }

        }

        public override void DrawTab(Ui ui)
        {
            ui.Header("Menu");
            if (ui.HotkeyRow(MenuKey)) Dirty = true;
            if (ui.HotkeyRow(MenuKeyAlt)) Dirty = true;
            ui.Note("Click a key, then press the new one. Esc cancels, Backspace clears the key.", 2);

            float scale = UiScale > 0.1f ? UiScale : 0.9f;
            string scaleLabel = UiScale > 0.1f ? $"Menu size: {Mathf.RoundToInt(UiScale * 100f)}%" : "Menu size: automatic";
            if (ui.Slider(scaleLabel, ref scale, 0.9f, 2.5f, 0.1f))
            {
                UiScale = scale < 0.95f ? 0f : Mathf.Round(scale * 10f) / 10f;
                Dirty = true;
            }
            if (ui.Toggle("Also block movement while the menu is open", ref BlockMovement)) Dirty = true;
            ui.Note("The view always stays still while the menu is open.");
            ui.Note("Drag the menu by its title bar to move it.");
            if (WindowX >= 0f && ui.Button("Put the menu back in its place"))
            {
                WindowX = WindowY = -1f;
                Dirty = true;
            }

            ui.Header("Friends with the mod");
            ui.Label("Network: " + Net.Status);
            if (ui.Toggle("Share my look", ref ShareMine)) Dirty = true;
            if (ui.Toggle("Show my friends' looks", ref ShowFriends)) Dirty = true;
            ui.Note("Only works if the host has the mod too, and everyone runs the same version. Your character's look, face, emotes and speech bubbles are sent.", 3);

            int profileHit = ui.Buttons("Export my profile", "Reload profiles");
            if (profileHit == 0)
            {
                string path = Profiles.ExportMine(out string problem);
                _profileNote = path != null ? "Saved: profiles/" + System.IO.Path.GetFileName(path) : problem;
            }
            else if (profileHit == 1)
            {
                _profileNote = Profiles.LoadAll() + " friend profiles loaded.";
            }
            if (!string.IsNullOrEmpty(_profileNote)) ui.Note(_profileNote);
            ui.Note("Fallback for when the network is not available: send your .bwp file to friends, they put it in BepInEx/plugins/BigWalkModMenu/profiles.", 3);

            ui.Header("Installed tabs");
            foreach (MenuModule m in ModMenuApi.Modules)
            {
                string state = m.Disabled ? "<color=#ff8888>paused</color>" : (m.Faults > 0 ? $"<color=#ffaa66>{m.Faults} errors</color>" : "<color=#88ff88>ok</color>");
                ui.Label($"{m.Title}  <color=#aaaaaa>({m.Id})</color>  {state}");
            }
            ui.Note("Each tab is a DLL in BepInEx/plugins/BigWalkModMenu. Delete the DLL to remove the tab.", 2);

        }
    }
}
