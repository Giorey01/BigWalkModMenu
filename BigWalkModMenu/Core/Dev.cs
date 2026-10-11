#if DEV
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// Test bridge, only in builds made with -p:Dev=1 and only while the folder "dev" exists next to the
    /// DLLs. Commands are lines dropped in dev/in.txt, answers are appended to dev/out.txt. It lets a
    /// script open tabs, change settings, take screenshots and press the game's own menu buttons.
    /// </summary>
    internal static class Dev
    {
        private static float _next;
        private static string _dir;
        private static readonly Queue<string> _pending = new Queue<string>();
        private static int _waitFrames;

        internal static void Tick(MenuHost host)
        {
            if (_waitFrames > 0) { _waitFrames--; return; }
            if (_pending.Count == 0)
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.25f;
                _dir ??= Path.Combine(ModMenuApi.RootPath, "dev");
                string file = Path.Combine(_dir, "in.txt");
                if (!File.Exists(file)) return;
                string[] lines;
                try { lines = File.ReadAllLines(file); File.Delete(file); } catch { return; }
                foreach (string l in lines) if (l.Trim().Length > 0) _pending.Enqueue(l.Trim());
            }
            while (_pending.Count > 0 && _waitFrames == 0)
            {
                string line = _pending.Dequeue();
                string answer;
                try { answer = Run(host, line); } catch (Exception ex) { answer = "ERROR " + ex; }
                try { File.AppendAllText(Path.Combine(_dir, "out.txt"), "> " + line + "\n" + answer + "\n"); } catch { }
            }
        }

        private static string Run(MenuHost host, string line)
        {
            string[] a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (a[0])
            {
                case "wait": _waitFrames = int.Parse(a[1]); return "ok";
                case "open": host.DevOpen(true, a.Length > 1 ? a[1] : null, a.Length > 2 ? float.Parse(a[2]) : -1f); return "ok";
                case "close": host.DevOpen(false, null, -1f); return "ok";
                case "shot":
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, a[1] + ".png"));
                    _waitFrames = 3;
                    return "ok";
                case "set":
                {
                    MenuModule m = ModMenuApi.Modules.Find(x => x.Id == a[1]);
                    if (m == null) return "no module";
                    m.Save(m.Config);
                    m.Config.Set(a[2], a.Length > 3 ? string.Join(" ", a, 3, a.Length - 3) : "");
                    m.Load(m.Config);
                    m.Dirty = true;
                    return "ok";
                }
                case "window":
                {
                    var g = GeneralModule.Instance;
                    if (a.Length > 2) { g.WindowX = float.Parse(a[1]); g.WindowY = float.Parse(a[2]); g.Dirty = true; }
                    return $"window={g.WindowX},{g.WindowY}";
                }
                case "mod":
                {
                    MenuModule m = ModMenuApi.Modules.Find(x => x.Id == a[1]);
                    if (m == null) return "no module";
                    return m.DevCommand(a) ?? "not handled";
                }
                case "state":
                {
                    var cam = Game.PlayerCamera;
                    string camInfo = "";
                    if (cam != null)
                    {
                        var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                        camInfo = $" fov={cam.fieldOfView:0.#} fovSetting={PlayerCameraMinder.fovFromSettings:0.#} aa={(data != null ? data.antialiasing.ToString() : "?")} cas={(data != null ? data.m_TaaSettings.m_ContrastAdaptiveSharpening : -1f):0.##} aaOwner={GraphicsShare.AntialiasingOwner}";
                    }
                    string s = $"inWorld={Game.InWorld} players={Game.Players.Count} frame={Time.frameCount} fps={1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):0} screen={Screen.width}x{Screen.height} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}{camInfo}";
                    foreach (MenuModule m in ModMenuApi.Modules) s += $"\n  {m.Id}: faults={m.Faults} disabled={m.Disabled} err={m.LastError}";
                    return s;
                }
                case "find":
                {
                    // Active objects of a game type: "find HostMenuSelect"
                    Type t = GameType(a[1]);
                    if (t == null) return "no type";
                    var found = Resources.FindObjectsOfTypeAll(Il2CppType.From(t));
                    string s = found.Length + " found";
                    foreach (var o in found)
                    {
                        var c = o.TryCast<Component>();
                        s += "\n  " + o.name + (c != null ? " active=" + c.gameObject.activeInHierarchy : "");
                    }
                    return s;
                }
                case "call":
                {
                    // Method on the first active object of a game type, private ones too: "call HostMenuConfirm ActionStart".
                    // One number, as a third word, is passed to a method that takes an int, a bool, a float or an enum.
                    Type t = GameType(a[1]);
                    if (t == null) return "no type";
                    MethodInfo method = null;
                    foreach (MethodInfo candidate in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (candidate.Name == a[2] && candidate.GetParameters().Length == a.Length - 3) { method = candidate; break; }
                    }
                    if (method == null) return "no method";
                    object[] args = null;
                    if (a.Length > 3)
                    {
                        Type pt = method.GetParameters()[0].ParameterType;
                        args = new[] { pt.IsEnum ? Enum.ToObject(pt, int.Parse(a[3])) : Convert.ChangeType(a[3], pt, System.Globalization.CultureInfo.InvariantCulture) };
                    }
                    if (method.IsStatic) return "ok " + method.Invoke(null, args);
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(t)))
                    {
                        var c = o.TryCast<Component>();
                        if (c == null || !c.gameObject.activeInHierarchy) continue;
                        object target = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(t).Invoke(o, null);
                        return "ok " + method.Invoke(target, args);
                    }
                    return "no active object";
                }
                case "awake":
                {
                    // A character nobody moves falls asleep after a while, and the game blurs and darkens the
                    // picture: no good for screenshots. "awake" keeps every character awake, "awake off" stops.
                    bool keep = a.Length < 2 || a[1] != "off";
                    int n = 0;
                    foreach (PlayerCharacter pc in UnityEngine.Object.FindObjectsOfType<PlayerCharacter>())
                    {
                        if (pc == null || pc.sleeper == null) continue;
                        pc.sleeper.preventSleeping = keep;
                        if (keep) pc.sleeper.RecordAction();
                        n++;
                    }
                    return "ok characters=" + n;
                }
                case "host":
                {
                    // Picks the n-th save on the host screen.
                    var select = UnityEngine.Object.FindObjectOfType<HostMenuSelect>();
                    if (select == null) return "no HostMenuSelect";
                    int i = a.Length > 1 ? int.Parse(a[1]) : 0;
                    if (select.cards == null || select.cards.Count <= i) return "cards=" + (select.cards != null ? select.cards.Count : -1);
                    select.ActionSelectSaveData(select.cards[i].saveData);
                    return "ok cards=" + select.cards.Count;
                }
                default: return "unknown command";
            }
        }

        private static Type GameType(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name;
                if (an != "Assembly-CSharp" && an != "DissonanceVoip") continue;
                Type t = asm.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
#endif
