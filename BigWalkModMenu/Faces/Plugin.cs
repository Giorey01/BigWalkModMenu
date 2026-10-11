using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BigWalk.ModMenu;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using StbImageSharp;
using UnityEngine;

namespace BigWalk.ModMenu.Faces
{
    [BepInPlugin("com.bigwalk.modmenu.faces", "Big Walk Mod Menu - Faces", ModMenuApi.Version)]
    [BepInDependency(BigWalk.ModMenu.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource;
        public static string PluginPath = "";
        public static string FacesDirectory = "";
        public static string DefaultFacesDirectory = "";

        /// <summary>Points the mod at a folder of photos; empty or missing means the one next to the plugin.</summary>
        public static bool UseFacesDirectory(string path)
        {
            bool ok = !string.IsNullOrEmpty(path) && Directory.Exists(path);
            FacesDirectory = ok ? path : DefaultFacesDirectory;
            FaceManager.ReloadFaces();
            return ok;
        }
        public static string SettingsDirectory = "";

        public override void Load()
        {
            LogSource = Log;

            PluginPath = Path.Combine(ModMenuApi.RootPath, "faces");
            DefaultFacesDirectory = Path.Combine(PluginPath, "photos");
            FacesDirectory = DefaultFacesDirectory;
            SettingsDirectory = Path.Combine(PluginPath, "settings");
            MoveLegacyData();
            Directory.CreateDirectory(DefaultFacesDirectory);
            Directory.CreateDirectory(SettingsDirectory);

            // The faces are read when the tab loads its settings: the folder may be somewhere else.
            GlobalSettings.Load();

            ModMenuApi.Register(new FacesModule());
        }

        /// <summary>The photo folder of the stand-alone plugin (up to 1.4.0), which a saved setting may still point at.</summary>
        public static string LegacyFacesDirectory => Path.Combine(Paths.PluginPath, "FaceMaskMod", "faces");

        // Up to 1.4.0 this tab was a plugin of its own in BepInEx/plugins/FaceMaskMod: photos, per-face
        // settings and friends' faces are moved here, so nobody loses them by updating.
        private static void MoveLegacyData()
        {
            string old = Path.Combine(Paths.PluginPath, "FaceMaskMod");
            if (!Directory.Exists(old)) return;
            MoveInto(Path.Combine(old, "faces"), DefaultFacesDirectory);
            MoveInto(Path.Combine(old, "settings"), SettingsDirectory);
            MoveInto(Path.Combine(old, "amici"), Path.Combine(PluginPath, "friends"));
            MoveInto(Path.Combine(old, "friends"), Path.Combine(PluginPath, "friends"));
            try
            {
                if (Directory.GetFileSystemEntries(old).Length == 0) Directory.Delete(old);
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"[Faces] Could not remove the old folder {old}: {ex.Message}");
            }
        }

        // Files already present at the destination are left alone, and their old copy stays where it was.
        private static void MoveInto(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            try
            {
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from))
                {
                    string target = Path.Combine(to, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Move(file, target);
                }
                foreach (string dir in Directory.GetDirectories(from)) MoveInto(dir, Path.Combine(to, Path.GetFileName(dir)));
                if (Directory.GetFileSystemEntries(from).Length == 0) Directory.Delete(from);
                LogSource.LogInfo($"[Faces] Moved {from} to {to}");
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"[Faces] Could not move {from} to {to}: {ex.Message}");
            }
        }
    }

    /// <summary>The "Faces" tab of the mod menu.</summary>
    public sealed class FacesModule : MenuModule
    {
        private readonly FaceMaskBehaviour _faces = new FaceMaskBehaviour();

        public override string Id => "faces";
        public override string Title => "Faces";
        public override int Order => 10;

        private readonly FolderPicker _picker = new FolderPicker();
        private string _folder = "";      // empty = the folder next to the plugin
        private bool _folderMissing;

        public override void Load(ModConfig c)
        {
            _folder = c.GetString("Folder", "");
            if (string.Equals(_folder.TrimEnd('/', '\\'), Plugin.LegacyFacesDirectory, StringComparison.OrdinalIgnoreCase)) _folder = "";
            _folderMissing = _folder.Length > 0 && !Plugin.UseFacesDirectory(_folder);
            if (_folder.Length == 0) Plugin.UseFacesDirectory("");
            Webcam.Load(c);
        }

        public override void Save(ModConfig c)
        {
            c.Set("Folder", _folder);
            Webcam.Save(c);
        }

        public override void Init()
        {
            FaceSync.Init();
            Webcam.Init();
        }

        private void SetFolder(string path)
        {
            _folder = path ?? "";
            _folderMissing = _folder.Length > 0 && !Plugin.UseFacesDirectory(_folder);
            if (_folder.Length == 0) Plugin.UseFacesDirectory("");
            _faces.ApplyAssignedFaces();
            Dirty = true;
        }

        private void DrawFolder(Ui ui)
        {
            ui.Header("Photo folder");
            if (_picker.IsOpen)
            {
                string chosen = _picker.Draw(ui);
                if (chosen != null) SetFolder(chosen);
                return;
            }
            ui.Note(Plugin.FacesDirectory, 2);
            if (_folderMissing) ui.Note("<color=#ffaa66>The chosen folder is gone: using the mod's own folder.</color>");
            int hit = ui.Buttons("Change…", "The mod's own", "Open");
            if (hit == 0) _picker.Open(Plugin.FacesDirectory);
            else if (hit == 1) SetFolder("");
            else if (hit == 2) _faces.OpenFacesFolder();
            ui.Note($"{Math.Max(0, FaceManager.AvailableFaces.Count - 1)} photos (.png or .jpg). Keep them wherever you like, outside the game too.");
        }

        public override void Update()
        {
            Webcam.Update();
            _faces.Update();
            FaceSync.Update(_faces);
        }

        public override void DrawOverlay() => Webcam.DrawOverlay();
        public override void DrawTab(Ui ui)
        {
            DrawFolder(ui);
            if (_picker.IsOpen) return;
            ui.Space(26f);
            _faces.Draw(ui);
            if (Webcam.Draw(ui)) Dirty = true;
        }
        public override void OnMenuClosed() => FaceMaskBehaviour.SaveUnsavedSettings();
    }

    /// <summary>Per-face placement of the photo on the head. Saved next to the plugin.</summary>
    public sealed class FaceSettings
    {
        public float Zoom = 1.0f;      // bigger = face bigger on the head
        public float CenterX = 0.5f;   // photo point that lands on the front of the head (0 = left, 1 = right)
        public float CenterY = 0.5f;   // 0 = top of the photo, 1 = bottom
        public float Wrap = 0.35f;     // 0 = flat projection (exact from the front), 1 = wrapped around the sides
        public bool Mirror = false;
        public bool HasFill;           // optional "Fill=r,g,b" in the .cfg: fixed colour for the back of the head
        public Color32 Fill;
    }

    public sealed class FaceEntry
    {
        public string Name;
        public byte[] Source;          // RGBA, top-down
        public int Width, Height;
        public Color32 Fill;           // colour used for the back of the head and transparent areas
        public FaceSettings Settings = new FaceSettings();
        public Texture2D Texture, Preview;
        public Il2CppStructArray<Color32> Pixels, PreviewPixels;
        public byte[] Buffer, PreviewBuffer;
        public Material Material;
        public bool Dirty = true;      // full-resolution bake pending
        public bool PreviewDirty;      // low-resolution bake pending (while a control is being dragged)
        public bool Live;              // moving picture (webcam): only ever the small texture, baked for every new picture
        public bool LiveDirty;
        public float LastChange;
        public bool SettingsUnsaved;
    }

    public static class FaceManager
    {
        public const string NoneFace = "<None>";
        public const int BakeSize = 768;
        public const int PreviewSize = 256;
        // The baked texture covers angles up to BakeExtent * 90 degrees away from the front of the head.
        public const float BakeExtent = 1.4f;
        private const float BaseSize = 0.8f;
        private const float FadeStartDeg = 95f, FadeEndDeg = 125f;

        public static List<string> AvailableFaces = new List<string>();
        private static readonly Dictionary<string, FaceEntry> _faces = new Dictionary<string, FaceEntry>();
        private static Shader _cachedShader;

        public static void ReloadFaces()
        {
            AvailableFaces.Clear();
            AvailableFaces.Add(NoneFace);
            foreach (var e in _faces.Values) e.Source = null; // force re-read from disk, keep texture/material objects

            if (!Directory.Exists(Plugin.FacesDirectory)) return;

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(Plugin.FacesDirectory))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                {
                    names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            AvailableFaces.AddRange(names);
            Plugin.LogSource?.LogInfo($"[FaceManager] {names.Count} faces in {Plugin.FacesDirectory}: {string.Join(", ", names)}");
        }

        public static FaceEntry GetFace(string faceName)
        {
            if (string.IsNullOrEmpty(faceName) || faceName == NoneFace) return null;

            if (!_faces.TryGetValue(faceName, out var entry))
            {
                entry = new FaceEntry { Name = faceName };
                LoadSettings(entry);
                _faces[faceName] = entry;
            }
            if (entry.Source == null && !LoadSource(entry)) return null;
            return entry;
        }

        private static bool LoadSource(FaceEntry entry)
        {
            string found = null;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
            {
                string p = Path.Combine(Plugin.FacesDirectory, entry.Name + ext);
                if (File.Exists(p)) { found = p; break; }
            }
            if (found == null) return false;

            try
            {
                StbImage.stbi_set_flip_vertically_on_load(0);
                ImageResult img = ImageResult.FromMemory(File.ReadAllBytes(found), ColorComponents.RedGreenBlueAlpha);
                if (img == null || img.Width <= 0 || img.Height <= 0 || img.Data == null) return false;

                entry.Source = img.Data;
                entry.Width = img.Width;
                entry.Height = img.Height;
                entry.Fill = entry.Settings.HasFill ? entry.Settings.Fill : ComputeFill(entry);
                entry.Dirty = true;
                Plugin.LogSource?.LogInfo($"[FaceManager] Loaded '{entry.Name}' ({img.Width}x{img.Height}), fill colour ({entry.Fill.r},{entry.Fill.g},{entry.Fill.b})");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogError($"[FaceManager] Failed loading '{found}': {ex.Message}");
                return false;
            }
        }

        // Colour for everything the photo does not cover: the hair at the top of the cut-out,
        // or the photo border when the picture has no transparency.
        private static Color32 ComputeFill(FaceEntry e)
        {
            byte[] d = e.Source;
            int w = e.Width, h = e.Height;
            int top = -1, bottom = -1;
            bool hasTransparency = false;
            for (int y = 0; y < h; y++)
            {
                bool rowOpaque = false;
                for (int x = 0; x < w; x++)
                {
                    byte a = d[(y * w + x) * 4 + 3];
                    if (a > 200) rowOpaque = true; else hasTransparency = true;
                }
                if (rowOpaque) { if (top < 0) top = y; bottom = y; }
            }
            if (top < 0) return new Color32(128, 128, 128, 255);

            long r = 0, g = 0, b = 0, n = 0;
            if (hasTransparency)
            {
                int limit = top + Math.Max(1, (int)((bottom - top) * 0.18f));
                for (int y = top; y <= limit; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int o = (y * w + x) * 4;
                        if (d[o + 3] > 200) { r += d[o]; g += d[o + 1]; b += d[o + 2]; n++; }
                    }
            }
            else
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (x > 1 && x < w - 2 && y > 1 && y < h - 2) continue;
                        int o = (y * w + x) * 4;
                        r += d[o]; g += d[o + 1]; b += d[o + 2]; n++;
                    }
            }
            if (n == 0) return new Color32(128, 128, 128, 255);
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }

        private static Texture2D CreateTexture(string name, int n, bool mips)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, mips);
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            tex.anisoLevel = mips ? 4 : 1;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>
        /// Bakes the photo into a texture laid out by angle from the front of the head: the texture centre
        /// is the point straight ahead, distance from the centre is the angle away from it. The head mesh
        /// (see HeadMeshFactory) uses the same layout, so every placement control only needs a re-bake.
        /// The preview bake is a small texture used while a control is being dragged.
        /// </summary>
        public static void Bake(FaceEntry e, bool preview = false)
        {
            if (e == null || e.Source == null) return;
            // Handles to textures and pixel arrays can go dead (seen in play on another texture): rebuild them.
            if (e.Texture != null && (!Game.Alive(e.Texture) || !Game.Alive(e.Pixels))) { e.Texture = null; e.Material = null; }
            if (e.Preview != null && (!Game.Alive(e.Preview) || !Game.Alive(e.PreviewPixels))) e.Preview = null;
            int n = preview ? PreviewSize : BakeSize;
            if (preview && e.Preview == null)
            {
                e.Preview = CreateTexture("CustomFacePreview_" + e.Name, n, false);
                e.PreviewPixels = new Il2CppStructArray<Color32>(n * n);
                e.PreviewBuffer = new byte[n * n * 4];
            }
            else if (!preview && e.Texture == null)
            {
                e.Texture = CreateTexture("CustomFace_" + e.Name, n, true);
                e.Pixels = new Il2CppStructArray<Color32>(n * n);
                e.Buffer = new byte[n * n * 4];
            }
            Texture2D tex = preview ? e.Preview : e.Texture;
            Il2CppStructArray<Color32> pixels = preview ? e.PreviewPixels : e.Pixels;
            byte[] buf = preview ? e.PreviewBuffer : e.Buffer;

            FaceSettings s = e.Settings;
            byte[] src = e.Source;
            int w = e.Width, h = e.Height;
            float fr = e.Fill.r, fg = e.Fill.g, fb = e.Fill.b;
            // Photo pixels per unit of projected radius (1 = edge of the front hemisphere).
            float pxPerUnit = BaseSize / MathF.Max(0.05f, s.Zoom) * h * 0.5f;
            float cx = s.CenterX * w, cy = s.CenterY * h;
            float wrap = Math.Clamp(s.Wrap, 0f, 1f);
            float mirror = s.Mirror ? -1f : 1f;
            const float halfPi = 1.5707964f;

            // Plain managed maths on managed arrays only: no engine calls here, so rows can run in parallel.
            Parallel.For(0, n, j =>
            {
                float qy = ((j + 0.5f) / n * 2f - 1f) * BakeExtent; // j = 0 is the bottom row
                int o4 = j * n * 4;
                for (int i = 0; i < n; i++, o4 += 4)
                {
                    float qx = ((i + 0.5f) / n * 2f - 1f) * BakeExtent;
                    float rho = MathF.Sqrt(qx * qx + qy * qy) + 1e-6f;
                    float theta = MathF.Min(rho * halfPi, 3.1415927f);
                    float thetaDeg = theta * 57.29578f;

                    float r = fr, g = fg, b = fb;
                    bool border = i < 2 || j < 2 || i >= n - 2 || j >= n - 2;
                    if (!border && thetaDeg < FadeEndDeg)
                    {
                        float sin = MathF.Sin(theta);
                        float flat = theta <= halfPi ? sin : 2f - sin;
                        float radius = (1f - wrap) * flat + wrap * rho;
                        float px = cx + mirror * qx / rho * radius * pxPerUnit - 0.5f;
                        float py = cy - qy / rho * radius * pxPerUnit - 0.5f;

                        int x0 = (int)MathF.Floor(px), y0 = (int)MathF.Floor(py);
                        float tx = px - x0, ty = py - y0;
                        float sr = 0f, sg = 0f, sb = 0f;
                        for (int k = 0; k < 4; k++)
                        {
                            int x = x0 + (k & 1), y = y0 + (k >> 1);
                            float weight = ((k & 1) == 0 ? 1f - tx : tx) * ((k >> 1) == 0 ? 1f - ty : ty);
                            if (x < 0 || y < 0 || x >= w || y >= h)
                            {
                                sr += fr * weight; sg += fg * weight; sb += fb * weight;
                            }
                            else
                            {
                                int o = (y * w + x) * 4;
                                float a = src[o + 3] * (1f / 255f);
                                sr += (fr + (src[o] - fr) * a) * weight;
                                sg += (fg + (src[o + 1] - fg) * a) * weight;
                                sb += (fb + (src[o + 2] - fb) * a) * weight;
                            }
                        }
                        float fade = Math.Clamp((thetaDeg - FadeStartDeg) / (FadeEndDeg - FadeStartDeg), 0f, 1f);
                        fade = fade * fade * (3f - 2f * fade);
                        r = sr + (fr - sr) * fade; g = sg + (fg - sg) * fade; b = sb + (fb - sb) * fade;
                    }
                    buf[o4] = (byte)r; buf[o4 + 1] = (byte)g; buf[o4 + 2] = (byte)b; buf[o4 + 3] = 255;
                }
            });

            // Color32 is four bytes (r, g, b, a); the il2cpp array data starts after its 4-pointer header.
            Marshal.Copy(buf, 0, IntPtr.Add(pixels.Pointer, 4 * IntPtr.Size), buf.Length);
            tex.SetPixels32(pixels);
            tex.Apply(!preview);

            if (preview) e.PreviewDirty = false;
            else { e.Dirty = false; e.PreviewDirty = false; }
            SetMaterialTexture(e.Material, tex);
        }

        private static void SetMaterialTexture(Material mat, Texture2D tex)
        {
            if (mat == null || tex == null) return;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        public static Shader GetFaceShader()
        {
            if (_cachedShader != null) return _cachedShader;
            foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Unlit/Texture" })
            {
                var s = Shader.Find(name);
                if (s != null)
                {
                    Plugin.LogSource?.LogInfo($"[FaceManager] Using shader '{name}'.");
                    _cachedShader = s;
                    return s;
                }
            }
            Plugin.LogSource?.LogError("[FaceManager] No usable shader found for the face material.");
            return null;
        }

        public static Material GetMaterial(FaceEntry e)
        {
            if (e == null) return null;
            if (e.Texture != null && !Game.Alive(e.Texture)) { e.Texture = null; e.Material = null; }
            if (e.Material != null && !Game.Alive(e.Material)) e.Material = null;
            Texture2D texture;
            if (e.Live)
            {
                if (e.Preview != null && !Game.Alive(e.Preview)) { e.Preview = null; e.Material = null; }
                if (e.Preview == null) Bake(e, true);
                texture = e.Preview;
            }
            else
            {
                if (e.Texture == null) Bake(e);
                texture = e.Texture;
            }
            if (texture == null) return null;
            if (e.Material != null) return e.Material;

            Shader shader = GetFaceShader();
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.name = "CustomFace_" + e.Name;
            mat.hideFlags = HideFlags.HideAndDontSave;
            SetMaterialTexture(mat, texture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // double-sided
            e.Material = mat;
            return mat;
        }

        private static string SettingsPath(string faceName) => Path.Combine(Plugin.SettingsDirectory, faceName + ".cfg");

        private static void LoadSettings(FaceEntry e)
        {
            try
            {
                string path = SettingsPath(e.Name);
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    if (key == "Mirror") { e.Settings.Mirror = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); continue; }
                    if (key == "Fill")
                    {
                        string[] rgb = val.Split(',');
                        if (rgb.Length == 3 && byte.TryParse(rgb[0].Trim(), out byte fr) && byte.TryParse(rgb[1].Trim(), out byte fg) && byte.TryParse(rgb[2].Trim(), out byte fb))
                        {
                            e.Settings.HasFill = true;
                            e.Settings.Fill = new Color32(fr, fg, fb, 255);
                        }
                        continue;
                    }
                    if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) continue;
                    if (key == "Zoom") e.Settings.Zoom = f;
                    else if (key == "CenterX") e.Settings.CenterX = f;
                    else if (key == "CenterY") e.Settings.CenterY = f;
                    else if (key == "Wrap") e.Settings.Wrap = f;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[FaceManager] Could not read settings for '{e.Name}': {ex.Message}");
            }
        }

        public static void SaveSettings(FaceEntry e)
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                var lines = new List<string>
                {
                    "Zoom=" + e.Settings.Zoom.ToString("0.###", c),
                    "CenterX=" + e.Settings.CenterX.ToString("0.###", c),
                    "CenterY=" + e.Settings.CenterY.ToString("0.###", c),
                    "Wrap=" + e.Settings.Wrap.ToString("0.###", c),
                    "Mirror=" + (e.Settings.Mirror ? "1" : "0"),
                };
                if (e.Settings.HasFill) lines.Add($"Fill={e.Settings.Fill.r},{e.Settings.Fill.g},{e.Settings.Fill.b}");
                File.WriteAllLines(SettingsPath(e.Name), lines);
                e.SettingsUnsaved = false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[FaceManager] Could not save settings for '{e.Name}': {ex.Message}");
            }
        }

        public static IEnumerable<FaceEntry> LoadedFaces => _faces.Values;
    }

    /// <summary>Settings that are not tied to a photo: head size for everyone and the nose of each slot.</summary>
    public static class GlobalSettings
    {
        public static float HeadScale = 1f;
        public static readonly bool[] NoseVisible = new bool[4];
        public static readonly bool[] NoseCustomColour = new bool[4];
        public static readonly float[] NoseHue = new float[4];
        public static readonly float[] NoseSaturation = { 1f, 1f, 1f, 1f };
        public static readonly float[] NoseValue = { 1f, 1f, 1f, 1f };
        public static readonly string[] SlotFace = { "", "", "", "" }; // face last assigned to each player
        public static readonly string[] TorsoFace = { "", "", "", "" }; // image printed on the torso
        public static bool Unsaved;

        private static string FilePath => Path.Combine(Plugin.SettingsDirectory, "_global.cfg");

        public static Color NoseColour(int slot) => Hsv(NoseHue[slot], NoseSaturation[slot], NoseValue[slot]);

        public static Color Hsv(float h, float s, float v)
        {
            h = (h - MathF.Floor(h)) * 6f;
            int sector = (int)h;
            float f = h - sector, p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (sector)
            {
                case 0: return new Color(v, t, p, 1f);
                case 1: return new Color(q, v, p, 1f);
                case 2: return new Color(p, v, t, 1f);
                case 3: return new Color(p, q, v, 1f);
                case 4: return new Color(t, p, v, 1f);
                default: return new Color(v, p, q, 1f);
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var c = CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string[] vals = line.Substring(eq + 1).Split(',');
                    if (key == "HeadScale")
                    {
                        if (float.TryParse(vals[0], NumberStyles.Float, c, out float hs)) HeadScale = Mathf.Clamp(hs, 0.3f, 4f);
                        continue;
                    }
                    if (key == "SlotFace" || key == "TorsoFace")
                    {
                        string[] target = key == "SlotFace" ? SlotFace : TorsoFace;
                        for (int i = 0; i < 4 && i < vals.Length; i++) target[i] = vals[i].Trim();
                        continue;
                    }
                    for (int i = 0; i < 4 && i < vals.Length; i++)
                    {
                        if (!float.TryParse(vals[i], NumberStyles.Float, c, out float f)) continue;
                        if (key == "NoseVisible") NoseVisible[i] = f > 0.5f;
                        else if (key == "NoseCustomColour") NoseCustomColour[i] = f > 0.5f;
                        else if (key == "NoseHue") NoseHue[i] = f;
                        else if (key == "NoseSaturation") NoseSaturation[i] = f;
                        else if (key == "NoseValue") NoseValue[i] = f;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[GlobalSettings] Could not read settings: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                string Join(float[] a) => string.Join(",", Array.ConvertAll(a, x => x.ToString("0.###", c)));
                string JoinB(bool[] a) => string.Join(",", Array.ConvertAll(a, x => x ? "1" : "0"));
                File.WriteAllLines(FilePath, new[]
                {
                    "HeadScale=" + HeadScale.ToString("0.###", c),
                    "NoseVisible=" + JoinB(NoseVisible),
                    "NoseCustomColour=" + JoinB(NoseCustomColour),
                    "NoseHue=" + Join(NoseHue),
                    "NoseSaturation=" + Join(NoseSaturation),
                    "NoseValue=" + Join(NoseValue),
                    "SlotFace=" + string.Join(",", SlotFace),
                    "TorsoFace=" + string.Join(",", TorsoFace),
                });
                Unsaved = false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[GlobalSettings] Could not save settings: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The game's head mesh has no usable UVs (it is flat-coloured, and its second submesh is the two eye
    /// discs on the sides of the head), so the head is replaced with a sphere of the same size whose UVs
    /// match the baked face texture. Submesh 1 is left empty so the eye material draws nothing.
    /// </summary>
    public static class HeadMeshFactory
    {
        private const int Rings = 40, Segments = 64;
        private static readonly Dictionary<string, Mesh> _cache = new Dictionary<string, Mesh>();

        public static Mesh Get(Vector3 center, float radius, Vector3 front, Vector3 up)
        {
            string key = $"{center.x:0.0000}|{center.y:0.0000}|{center.z:0.0000}|{radius:0.0000}|{front}|{up}";
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            Vector3 side = Vector3.Cross(front, up); // to the right for someone looking at the face
            int vertCount = (Rings + 1) * (Segments + 1);
            var verts = new Il2CppStructArray<Vector3>(vertCount);
            var normals = new Il2CppStructArray<Vector3>(vertCount);
            var uvs = new Il2CppStructArray<Vector2>(vertCount);
            var tris = new Il2CppStructArray<int>(Rings * Segments * 6);

            int v = 0;
            for (int i = 0; i <= Rings; i++)
            {
                float theta = Mathf.PI * i / Rings;          // angle away from the front
                float rho = theta / (Mathf.PI * 0.5f) / (2f * FaceManager.BakeExtent);
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int j = 0; j <= Segments; j++)
                {
                    float phi = 2f * Mathf.PI * j / Segments;
                    float cosP = Mathf.Cos(phi), sinP = Mathf.Sin(phi);
                    Vector3 dir = front * cosT + (side * cosP + up * sinP) * sinT;
                    verts[v] = center + dir * radius;
                    normals[v] = dir;
                    uvs[v] = new Vector2(0.5f + rho * cosP, 0.5f + rho * sinP);
                    v++;
                }
            }

            int t = 0;
            for (int i = 0; i < Rings; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = i * (Segments + 1) + j, b = a + Segments + 1, c = a + 1, d = b + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name = "CustomFace_HeadSphere";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.subMeshCount = 2;
            mesh.RecalculateBounds();
            _cache[key] = mesh;
            return mesh;
        }
    }

    public sealed class FaceMaskBehaviour
    {
        private sealed class HeadState
        {
            public Renderer Renderer;
            public MeshFilter Filter;
            public Mesh OriginalMesh;
            public Material OriginalMaterial;
            public Vector3 Front;
        }

        private sealed class ScaleState
        {
            public Transform Container;
            public Vector3 Scale, Position;
            public float Radius; // head radius in the container's parent space, at the original scale
        }

        public static FaceMaskBehaviour Instance;

        public static int YawSteps = 0, RollSteps = 0; // manual 90-degree corrections of the face direction

        private readonly string[] _slotAssignedFace = { FaceManager.NoneFace, FaceManager.NoneFace, FaceManager.NoneFace, FaceManager.NoneFace };
        private readonly Dictionary<int, HeadState> _heads = new Dictionary<int, HeadState>();
        private readonly HashSet<int> _unusableHeads = new HashSet<int>();
        private readonly HashSet<int> _colouredNoses = new HashSet<int>();
        private readonly Dictionary<int, ScaleState> _scales = new Dictionary<int, ScaleState>();
        private MaterialPropertyBlock _noseBlock;
        private string _lookColourProperty;
        private Texture2D _hueWheel;
        private int _tab;

        private int _editSlot;
        private int _activeDropdownSlot = -1;
        private Vector2 _dropdownScroll = Vector2.zero;

        public FaceMaskBehaviour()
        {
            Instance = this;
            for (int i = 0; i < 4; i++)
            {
                if (FaceManager.AvailableFaces.Contains(GlobalSettings.SlotFace[i])) _slotAssignedFace[i] = GlobalSettings.SlotFace[i];
            }
        }

        public void Update()
        {
            try
            {
                // While a control is dragged only the small preview is baked; the full texture follows
                // once the value has been left alone for a moment.
                foreach (var face in FaceManager.LoadedFaces)
                {
                    if (face.Source == null || face.Texture == null) continue;
                    if (face.PreviewDirty) FaceManager.Bake(face, true);
                    else if (face.Dirty && Time.unscaledTime - face.LastChange > 0.25f) FaceManager.Bake(face);
                }

                // Faces received from friends: bake again when their placement changes.
                foreach (var remote in FaceSync.Remote.Values)
                {
                    FaceEntry e = remote.Entry;
                    if (e != null && e.Source != null && e.Texture != null && e.Dirty) FaceManager.Bake(e);
                }

                // Moving pictures: mine and the friends'.
                BakeLive(Webcam.Entry);
                foreach (var remote in FaceSync.Remote.Values) BakeLive(remote.Video);

                // The game can reassign head materials (look changes, respawns), so keep enforcing.
                if (Time.frameCount % 20 == 0)
                {
                    ApplyAssignedFaces();
                    ApplyHeadScaleToAll();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogError($"Error in FaceMaskBehaviour.Update: {ex}");
            }
        }

        // Only once the picture is on a head (that is when its texture gets made).
        private static void BakeLive(FaceEntry e)
        {
            if (e == null || !e.LiveDirty || e.Source == null || e.Preview == null) return;
            e.LiveDirty = false;
            FaceManager.Bake(e, true);
        }

        public static void SaveUnsavedSettings()
        {
            foreach (var face in FaceManager.LoadedFaces)
            {
                if (face.SettingsUnsaved) FaceManager.SaveSettings(face);
            }
            if (GlobalSettings.Unsaved) GlobalSettings.Save();
        }

        public void ApplyAssignedFaces()
        {
            for (int i = 0; i < 4; i++)
            {
                var pc = Solo.GetPlayerForSlot(i);
                if (pc == null) continue;
                ApplyFaceToPlayer(pc, i);
            }
        }

        private static bool IsNose(Renderer r) => r.name.ToLowerInvariant().Contains("nose");

        public void ApplyFaceToPlayer(PlayerCharacter pc, int slot)
        {
            if (pc == null || pc.looks == null || pc.looks.headRenderers == null) return;

            // A friend's own choice (sent by their mod) wins over what was picked for that slot here.
            RemoteFace remote = FaceSync.For(pc);
            FaceEntry face;
            if (remote != null) face = Webcam.VideoOf(remote) ?? (remote.HasFace ? remote.Entry : null);
            else
            {
                bool mine = false;
                try { mine = Webcam.Showing && pc.isLocalPlayer; } catch { }
                face = mine ? Webcam.Entry : FaceManager.GetFace(_slotAssignedFace[slot]);
            }
            Material faceMat = face != null ? FaceManager.GetMaterial(face) : null;
            bool noseVisible = remote != null ? remote.NoseVisible : GlobalSettings.NoseVisible[slot];
            bool noseCustom = remote != null ? remote.NoseCustom : GlobalSettings.NoseCustomColour[slot];
            Color noseColour = remote != null ? remote.NoseColour : GlobalSettings.NoseColour(slot);
            // The original nose is only hidden on heads that carry a photo.
            bool hideNose = faceMat != null && !noseVisible;
            bool noseColourReleased = false;

            // headRenderers holds the head LODs and the nose LODs.
            foreach (var lr in pc.looks.headRenderers)
            {
                Renderer r = lr != null ? lr.renderer : null;
                if (r == null) continue;
                try
                {
                    if (IsNose(r)) noseColourReleased |= ApplyToNose(r, noseCustom, noseColour, hideNose);
                    else if (faceMat != null) ApplyToHead(r, faceMat);
                    else RestoreHead(r);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogError($"[Faces] Head '{r.name}': {ex}");
                }
            }

            ApplyTorsoPrint(pc, slot);

            if (noseColourReleased)
            {
                // Let the game put its own look colour back on the renderers we just released.
                try { pc.looks.SetColor(pc.looks.headRenderers, pc.looks.headColor); }
                catch (Exception ex) { Plugin.LogSource?.LogWarning($"[Faces] Could not restore the nose colour: {ex.Message}"); }
            }
        }

        // Returns true when a custom colour was just removed from this renderer.
        private bool ApplyToNose(Renderer r, bool customColour, Color colour, bool hide)
        {
            if (r.forceRenderingOff != hide) r.forceRenderingOff = hide;

            int id = r.GetInstanceID();
            if (customColour)
            {
                if (!Game.Alive(_noseBlock)) _noseBlock = new MaterialPropertyBlock();
                if (_lookColourProperty == null)
                {
                    try { _lookColourProperty = PlayerLooks.lookColorPropertyName; } catch { }
                    if (string.IsNullOrEmpty(_lookColourProperty)) _lookColourProperty = "_Color0";
                    Plugin.LogSource?.LogInfo($"[Faces] Nose colour property: '{_lookColourProperty}'");
                }
                // The game writes its look colour through a property block too, so keep ours on top.
                r.GetPropertyBlock(_noseBlock);
                _noseBlock.SetColor(_lookColourProperty, colour);
                r.SetPropertyBlock(_noseBlock);
                _colouredNoses.Add(id);
                return false;
            }
            if (_colouredNoses.Remove(id))
            {
                r.SetPropertyBlock(null);
                return true;
            }
            return false;
        }

        // The torso is a ball like the head, so a picture can be wrapped on it the same way. It has no
        // nose to tell where the front is: the direction the head faces is used instead.
        private void ApplyTorsoPrint(PlayerCharacter pc, int slot)
        {
            Transform torso = Game.FindDeep(pc.transform, "torso_mesh");
            if (torso == null) return;
            FaceEntry print = FaceManager.GetFace(GlobalSettings.TorsoFace[slot]);
            Material mat = print != null ? FaceManager.GetMaterial(print) : null;

            Transform head = mat != null ? Game.HeadContainer(pc) : null;
            for (int i = 0; i < torso.childCount; i++)
            {
                Transform child = torso.GetChild(i);
                if (child == null || !child.name.StartsWith("BodyBall")) continue;
                Renderer r = child.GetComponent<Renderer>();
                if (r == null) continue;
                try
                {
                    if (mat == null) { RestoreHead(r); continue; }
                    if (_heads.ContainsKey(r.GetInstanceID())) { ApplyToHead(r, mat); continue; }
                    if (head == null) continue;
                    // Prefab layout: the face is at local -X of the head. Wait for a moment when the head
                    // looks roughly straight ahead, so the direction snaps cleanly to one of the ball's axes.
                    Vector3 front = SnapToAxis(child.InverseTransformDirection(head.TransformDirection(Vector3.left)).normalized);
                    if (front != Vector3.zero) ApplyToHead(r, mat, front);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogError($"[Faces] Torso '{child.name}': {ex}");
                }
            }
        }

        public void ApplyHeadScaleToAll()
        {
            var all = PlayerCharacter.allPlayerCharacters;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                try { ApplyHeadScale(all[i]); }
                catch (Exception ex) { Plugin.LogSource?.LogError($"[Faces] Head scale: {ex}"); }
            }
        }

        // Scales the object that holds the head LODs and the nose, and lifts it so the head keeps
        // resting on the neck instead of growing into the body.
        private void ApplyHeadScale(PlayerCharacter pc)
        {
            if (pc == null || pc.looks == null || pc.looks.headRenderers == null) return;
            float k = GlobalSettings.HeadScale;

            int id = pc.GetInstanceID();
            if (!_scales.TryGetValue(id, out var st) || st.Container == null)
            {
                if (Mathf.Abs(k - 1f) < 0.001f) return; // nothing to do until the size is actually changed

                Transform container = null;
                Renderer sample = null;
                foreach (var lr in pc.looks.headRenderers)
                {
                    Renderer r = lr != null ? lr.renderer : null;
                    if (r == null || IsNose(r)) continue;
                    if (r.transform.name == "head_mesh") { container = r.transform; sample = r; break; }
                    if (container == null) { container = r.transform.parent != null ? r.transform.parent : r.transform; sample = r; }
                }
                if (container == null || container.parent == null) return;

                var filter = sample.GetComponent<MeshFilter>();
                float meshRadius = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.extents.y : 0f;
                float parentScale = container.parent.lossyScale.y;
                st = new ScaleState
                {
                    Container = container,
                    Scale = container.localScale,
                    Position = container.localPosition,
                    Radius = Mathf.Abs(parentScale) > 1e-5f ? meshRadius * sample.transform.lossyScale.y / parentScale : 0f,
                };
                _scales[id] = st;
                Plugin.LogSource?.LogInfo($"[Faces] Head scale target '{container.name}': scale {st.Scale}, pos {st.Position}, radius {st.Radius:0.000}");
            }

            Vector3 scale = st.Scale * k;
            Vector3 position = st.Position + Vector3.up * ((k - 1f) * st.Radius);
            if ((st.Container.localScale - scale).sqrMagnitude > 1e-8f) st.Container.localScale = scale;
            if ((st.Container.localPosition - position).sqrMagnitude > 1e-8f) st.Container.localPosition = position;
        }

        // frontOverride: where the picture points, in the renderer's own space, for parts without a nose.
        private void ApplyToHead(Renderer r, Material faceMat, Vector3? frontOverride = null)
        {
            int id = r.GetInstanceID();
            if (_unusableHeads.Contains(id)) return;
            if (!_heads.TryGetValue(id, out var state))
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    _unusableHeads.Add(id);
                    Plugin.LogSource?.LogWarning($"[Faces] Head renderer '{r.name}' has no MeshFilter/mesh, skipped.");
                    return;
                }

                var mats = r.sharedMaterials;
                state = new HeadState
                {
                    Renderer = r,
                    Filter = filter,
                    OriginalMesh = filter.sharedMesh,
                    OriginalMaterial = mats != null && mats.Length > 0 ? mats[0] : null,
                };

                // The nose sits straight ahead of the head centre, which tells us where the face points.
                Transform searchRoot = r.transform.parent != null ? r.transform.parent : r.transform;
                Vector3 center = state.OriginalMesh.bounds.center;
                state.Front = Vector3.left; // prefab layout: the nose is at local -X
                bool noseFound = false;
                if (frontOverride.HasValue) { state.Front = frontOverride.Value; noseFound = true; }
                else foreach (var t in searchRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || !t.name.ToLowerInvariant().Contains("nose")) continue;
                    Vector3 toNose = r.transform.InverseTransformPoint(t.position) - center;
                    if (toNose.magnitude > 1e-4f)
                    {
                        Vector3 snapped = SnapToAxis(toNose);
                        if (snapped != Vector3.zero) { state.Front = snapped; noseFound = true; break; }
                    }
                }

                _heads[id] = state;
                Plugin.LogSource?.LogInfo($"[Faces] Head '{r.name}': mesh '{state.OriginalMesh.name}' bounds {state.OriginalMesh.bounds.center}/{state.OriginalMesh.bounds.extents}, " +
                                          $"front {state.Front} ({(frontOverride.HasValue ? "from the head" : noseFound ? "from nose" : "default")}), {(mats != null ? mats.Length : 0)} material(s).");
            }

            Bounds b = state.OriginalMesh.bounds;
            float radius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            Vector3 front = state.Front;
            Vector3 up = Mathf.Abs(front.y) > 0.5f ? Vector3.forward : Vector3.up;
            for (int k = 0; k < (YawSteps & 3); k++) front = Vector3.Cross(up, front);
            for (int k = 0; k < (RollSteps & 3); k++) up = Vector3.Cross(front, up);

            Mesh sphere = HeadMeshFactory.Get(b.center, radius, front, up);
            if (state.Filter.sharedMesh != sphere) state.Filter.sharedMesh = sphere;

            var current = r.sharedMaterials;
            if (current != null && current.Length > 0 && current[0] != faceMat)
            {
                current[0] = faceMat;
                r.sharedMaterials = current;
            }
        }

        private void RestoreHead(Renderer r)
        {
            if (!_heads.TryGetValue(r.GetInstanceID(), out var state)) return;

            if (state.Filter != null && state.OriginalMesh != null && state.Filter.sharedMesh != state.OriginalMesh)
            {
                state.Filter.sharedMesh = state.OriginalMesh;
            }
            var current = r.sharedMaterials;
            if (state.OriginalMaterial != null && current != null && current.Length > 0 && current[0] != state.OriginalMaterial)
            {
                current[0] = state.OriginalMaterial;
                r.sharedMaterials = current;
            }
            _heads.Remove(r.GetInstanceID());
        }

        private static Vector3 SnapToAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            float max = Mathf.Max(ax, Mathf.Max(ay, az));
            if (max < 0.9f * v.magnitude) return Vector3.zero;
            if (max == ax) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (max == ay) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        public string GetSlotFace(int slot)
        {
            return slot >= 0 && slot < _slotAssignedFace.Length ? _slotAssignedFace[slot] : FaceManager.NoneFace;
        }

        public void SetSlotFace(int slot, string faceName)
        {
            if (slot < 0 || slot >= _slotAssignedFace.Length) return;
            _slotAssignedFace[slot] = faceName;
            _editSlot = slot;
            GlobalSettings.SlotFace[slot] = faceName == FaceManager.NoneFace ? "" : faceName;
            GlobalSettings.Unsaved = true;
            Plugin.LogSource?.LogInfo($"[Faces] Slot {slot + 1} set to face: '{faceName}'");

            var pc = Solo.GetPlayerForSlot(slot);
            if (pc != null) ApplyFaceToPlayer(pc, slot);
        }

        public void CycleSlotFace(int slot, int dir)
        {
            int count = FaceManager.AvailableFaces.Count;
            if (count == 0) return;
            int idx = FaceManager.AvailableFaces.IndexOf(GetSlotFace(slot));
            if (idx < 0) idx = 0;
            SetSlotFace(slot, FaceManager.AvailableFaces[(idx + dir + count) % count]);
        }

        public void OpenFacesFolder() => Game.OpenFolder(Plugin.FacesDirectory, Plugin.FacesDirectory);

        private static void Touch(FaceEntry face)
        {
            face.Dirty = true;
            face.PreviewDirty = true;
            face.LastChange = Time.unscaledTime;
            face.SettingsUnsaved = true;
        }

        // Label, -/+ steppers and a slider on one row. Returns the new value.
        private static float SliderRow(float x, float y, float width, string label, float value, float min, float max, float step)
        {
            GUI.Label(new Rect(x, y, width - 58f, 18f), label);
            float result = value;
            if (GUI.Button(new Rect(x + width - 52f, y, 24f, 18f), "-")) result -= step;
            if (GUI.Button(new Rect(x + width - 26f, y, 24f, 18f), "+")) result += step;
            float slid = GUI.HorizontalSlider(new Rect(x + 2f, y + 21f, width - 4f, 14f), value, min, max);
            if (Mathf.Abs(slid - value) > 0.0005f) result = slid;
            return Mathf.Clamp(result, min, max);
        }

        private void DrawFaceTab(float x, float y, float width)
        {
            FaceEntry face = FaceManager.GetFace(GetSlotFace(_editSlot));
            if (face == null)
            {
                GUI.Label(new Rect(x, y + 4f, width, 40f), $"Player {_editSlot + 1}: no face assigned.\nPick one above to adjust it.");
                return;
            }

            FaceSettings s = face.Settings;
            GUI.Label(new Rect(x, y, width, 18f), $"<b>Adjusting:</b> {face.Name} (Player {_editSlot + 1})");

            float zoom = SliderRow(x, y + 20f, width, $"Zoom: {Mathf.RoundToInt(s.Zoom * 100f)}%", s.Zoom, 0.4f, 3.0f, 0.05f);
            float cx = SliderRow(x, y + 58f, width, $"Move sideways: {Mathf.RoundToInt((s.CenterX - 0.5f) * 100f):+0;-0;0}", s.CenterX, 0f, 1f, 0.01f);
            float cy = SliderRow(x, y + 96f, width, $"Move up/down: {Mathf.RoundToInt((s.CenterY - 0.5f) * 100f):+0;-0;0}", s.CenterY, 0f, 1f, 0.01f);
            float wrap = SliderRow(x, y + 134f, width, $"Wrap around the sides: {Mathf.RoundToInt(s.Wrap * 100f)}%", s.Wrap, 0f, 1f, 0.05f);
            if (zoom != s.Zoom || cx != s.CenterX || cy != s.CenterY || wrap != s.Wrap)
            {
                s.Zoom = zoom; s.CenterX = cx; s.CenterY = cy; s.Wrap = wrap;
                Touch(face);
            }

            float half = (width - 4f) * 0.5f;
            if (GUI.Button(new Rect(x, y + 172f, half, 18f), s.Mirror ? "Mirror: <b>ON</b>" : "Mirror: OFF"))
            {
                s.Mirror = !s.Mirror;
                Touch(face);
            }
            if (GUI.Button(new Rect(x + half + 4f, y + 172f, half, 18f), "Reset adjustment"))
            {
                face.Settings = new FaceSettings { HasFill = s.HasFill, Fill = s.Fill };
                Touch(face);
            }
        }

        private void DrawTorsoTab(float x, float y, float width)
        {
            int slot = _editSlot;
            GUI.Label(new Rect(x, y, width, 18f), $"<b>Print on the torso of Player {slot + 1}</b>");

            var names = FaceManager.AvailableFaces;
            string current = GlobalSettings.TorsoFace[slot];
            int index = Mathf.Max(0, names.IndexOf(current));
            int picked = index;
            if (GUI.Button(new Rect(x, y + 24f, 24f, 20f), "<")) picked = (index - 1 + names.Count) % names.Count;
            GUI.Box(new Rect(x + 26f, y + 24f, width - 52f, 20f), names.Count > 0 ? names[index] : "-");
            if (GUI.Button(new Rect(x + width - 24f, y + 24f, 24f, 20f), ">")) picked = (index + 1) % names.Count;
            if (picked != index && names.Count > 0)
            {
                GlobalSettings.TorsoFace[slot] = names[picked] == FaceManager.NoneFace ? "" : names[picked];
                GlobalSettings.Unsaved = true;
                ApplyAssignedFaces();
            }

            GUI.Label(new Rect(x, y + 52f, width, 110f),
                "<size=11><color=#dddddd>Wraps a picture from the photo folder around the torso sphere, as for the head.\n\n" +
                "Zoom, position and wrap are the picture's own: adjust them in the Face section by assigning it to a head for a moment.\n\n" +
                "It shows as soon as the character looks straight ahead.</color></size>");
        }

        private static void DrawSwatch(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // Hue by angle, saturation by distance from the centre; brightness has its own slider.
        private Texture2D GetHueWheel()
        {
            if (Game.Alive(_hueWheel)) return _hueWheel;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Il2CppStructArray<Color32>(n * n);
            float c = (n - 1) * 0.5f;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float dx = (i - c) / c, dy = (j - c) / c;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    Color col = GlobalSettings.Hsv(MathF.Atan2(dy, dx) / (2f * MathF.PI), MathF.Min(dist, 1f), 1f);
                    float alpha = Math.Clamp((1f - dist) * c, 0f, 1f); // soft edge
                    pixels[j * n + i] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            _hueWheel = tex;
            return tex;
        }

        private void DrawNoseTab(float x, float y, float width)
        {
            int slot = _editSlot;
            bool changed = false;
            GUI.Label(new Rect(x, y, width, 18f), $"<b>Nose of Player {slot + 1}</b>");

            // Colour wheel.
            const float wheelSize = 104f;
            Rect wheel = new Rect(x, y + 20f, wheelSize, wheelSize);
            GUI.DrawTexture(wheel, GetHueWheel());

            Event ev = Event.current;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && wheel.Contains(ev.mousePosition))
            {
                float dx = (ev.mousePosition.x - wheel.center.x) / (wheelSize * 0.5f);
                float dy = (wheel.center.y - ev.mousePosition.y) / (wheelSize * 0.5f);
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= 1.05f)
                {
                    float hue = MathF.Atan2(dy, dx) / (2f * MathF.PI);
                    GlobalSettings.NoseHue[slot] = hue - MathF.Floor(hue);
                    GlobalSettings.NoseSaturation[slot] = MathF.Min(dist, 1f);
                    GlobalSettings.NoseCustomColour[slot] = true;
                    changed = true;
                    ev.Use();
                }
            }

            float angle = GlobalSettings.NoseHue[slot] * 2f * MathF.PI;
            float markerR = GlobalSettings.NoseSaturation[slot] * wheelSize * 0.5f;
            float mx = wheel.center.x + MathF.Cos(angle) * markerR, my = wheel.center.y - MathF.Sin(angle) * markerR;
            DrawSwatch(new Rect(mx - 4f, my - 4f, 8f, 8f), Color.black);
            DrawSwatch(new Rect(mx - 2.5f, my - 2.5f, 5f, 5f), Color.white);

            // Controls to the right of the wheel.
            float rx = x + wheelSize + 10f, rw = width - wheelSize - 10f;
            if (GUI.Button(new Rect(rx, y + 20f, rw, 20f), GlobalSettings.NoseVisible[slot] ? "Original nose: <b>visible</b>" : "Original nose: hidden"))
            {
                GlobalSettings.NoseVisible[slot] = !GlobalSettings.NoseVisible[slot];
                changed = true;
            }
            if (GUI.Button(new Rect(rx, y + 43f, rw, 20f), GlobalSettings.NoseCustomColour[slot] ? "Colour: <b>custom</b>" : "Colour: original"))
            {
                GlobalSettings.NoseCustomColour[slot] = !GlobalSettings.NoseCustomColour[slot];
                changed = true;
            }
            float value = SliderRow(rx, y + 66f, rw, $"Brightness: {Mathf.RoundToInt(GlobalSettings.NoseValue[slot] * 100f)}%", GlobalSettings.NoseValue[slot], 0f, 1f, 0.05f);
            if (value != GlobalSettings.NoseValue[slot])
            {
                GlobalSettings.NoseValue[slot] = value;
                GlobalSettings.NoseCustomColour[slot] = true;
                changed = true;
            }
            DrawSwatch(new Rect(rx, y + 106f, rw, 16f), GlobalSettings.NoseColour(slot));

            // Head size, shared by every player.
            float scale = SliderRow(x, y + 130f, width - 52f, $"Head size (everyone): {Mathf.RoundToInt(GlobalSettings.HeadScale * 100f)}%", GlobalSettings.HeadScale, 0.3f, 4f, 0.05f);
            if (GUI.Button(new Rect(x + width - 48f, y + 130f, 48f, 18f), "100%")) scale = 1f;
            if (scale != GlobalSettings.HeadScale)
            {
                GlobalSettings.HeadScale = scale;
                GlobalSettings.Unsaved = true;
                ApplyHeadScaleToAll();
            }

            if (changed)
            {
                GlobalSettings.Unsaved = true;
                ApplyAssignedFaces();
            }
        }

        // Drawn inside the mod menu's "Faces" tab. The layout is the one of the old stand-alone panel,
        // which had a 22 px title bar and 8 px margins around its content.
        public void Draw(Ui ui)
        {
            float panelW = ui.Width + 16f;
            float panelX = ui.X - 8f;
            float panelY = ui.Y - 22f;

            GUI.Label(new Rect(panelX + 14f, panelY + 22f, panelW - 28f, 20f), "<b>Pick a face for each player:</b>");

            for (int i = 0; i < 4; i++)
            {
                float rowY = panelY + 44f + i * 44f;
                GUI.Box(new Rect(panelX + 8f, rowY, panelW - 16f, 40f), "");

                var pc = Solo.GetPlayerForSlot(i);
                string status = pc != null
                    ? (pc.isLocalPlayer ? "<color=#88ff88>(Active)</color>" : "<color=#ffff88>(Follower)</color>")
                    : "<color=#aaaaaa>(Not spawned)</color>";
                GUI.Label(new Rect(panelX + 14f, rowY + 1f, 220f, 18f), $"<b>Player {i + 1}</b> {status}");

                if (GUI.Button(new Rect(panelX + 14f, rowY + 18f, 24f, 19f), "<")) CycleSlotFace(i, -1);

                string curFace = GetSlotFace(i);
                if (GUI.Button(new Rect(panelX + 40f, rowY + 18f, 170f, 19f), curFace + " ▼"))
                {
                    _activeDropdownSlot = _activeDropdownSlot == i ? -1 : i;
                }

                if (GUI.Button(new Rect(panelX + 212f, rowY + 18f, 24f, 19f), ">")) CycleSlotFace(i, 1);

                string editLabel = _editSlot == i ? "<b>Adjust ●</b>" : "Adjust";
                if (GUI.Button(new Rect(panelX + 242f, rowY + 18f, panelW - 256f, 19f), editLabel)) _editSlot = i;
            }

            // Adjustments for the slot picked with "Regola": the photo, or the nose and head size.
            float adjY = panelY + 224f;
            float innerX = panelX + 14f, innerW = panelW - 28f;
            float half = (innerW - 4f) * 0.5f;
            float third = (innerW - 8f) / 3f;
            GUI.Box(new Rect(panelX + 8f, adjY, panelW - 16f, 240f), "");
            if (GUI.Button(new Rect(innerX, adjY + 3f, third, 20f), _tab == 0 ? "<b>[ Face ]</b>" : "Face")) _tab = 0;
            if (GUI.Button(new Rect(innerX + third + 4f, adjY + 3f, third, 20f), _tab == 1 ? "<b>[ Nose and head ]</b>" : "Nose and head")) _tab = 1;
            if (GUI.Button(new Rect(innerX + (third + 4f) * 2f, adjY + 3f, third, 20f), _tab == 2 ? "<b>[ Torso ]</b>" : "Torso")) _tab = 2;

            if (_tab == 0) DrawFaceTab(innerX, adjY + 26f, innerW);
            else if (_tab == 1) DrawNoseTab(innerX, adjY + 26f, innerW);
            else DrawTorsoTab(innerX, adjY + 26f, innerW);

            float orientY = adjY + 218f;
            if (GUI.Button(new Rect(innerX, orientY, half, 18f), $"Turn face 90\u00b0 ({(YawSteps & 3) * 90})"))
            {
                YawSteps++;
                ApplyAssignedFaces();
            }
            if (GUI.Button(new Rect(innerX + half + 4f, orientY, half, 18f), $"Roll face 90\u00b0 ({(RollSteps & 3) * 90})"))
            {
                RollSteps++;
                ApplyAssignedFaces();
            }

            float toolY = adjY + 246f;
            float toolW = (panelW - 20f) * 0.5f;
            if (GUI.Button(new Rect(panelX + 8f, toolY, toolW, 22f), "Reload photos"))
            {
                FaceManager.ReloadFaces();
                ApplyAssignedFaces();
            }
            if (GUI.Button(new Rect(panelX + 12f + toolW, toolY, toolW, 22f), "Open the folder"))
            {
                OpenFacesFolder();
            }

            GUI.Label(new Rect(panelX + 10f, toolY + 24f, panelW - 20f, 36f),
                "<size=10><color=#dddddd>Put .png/.jpg files in the photo folder (above). Adjustments are saved when the menu closes.</color></size>");

            DrawDropdown(panelX, panelY);

            ui.Reserve(toolY + 60f - (panelY + 22f));
        }

        private void DrawDropdown(float panelX, float panelY)
        {
            if (_activeDropdownSlot < 0) return;

            int s = _activeDropdownSlot;
            float rowY = panelY + 44f + s * 44f;
            float popX = panelX + 40f;
            float popY = rowY + 38f;
            float popW = 200f;
            float itemH = 22f;

            int total = FaceManager.AvailableFaces.Count;
            float popH = Mathf.Min(total, 6) * itemH + 6f;
            if (popY + popH > Screen.height - 10f) popY = rowY - popH;

            if (Event.current.type == EventType.MouseDown)
            {
                Vector2 mousePos = Event.current.mousePosition;
                Rect popRect = new Rect(popX, popY, popW, popH);
                Rect btnRect = new Rect(panelX + 40f, rowY + 18f, 170f, 19f);
                if (!popRect.Contains(mousePos) && !btnRect.Contains(mousePos)) _activeDropdownSlot = -1;
            }

            GUI.Box(new Rect(popX, popY, popW, popH), "");
            _dropdownScroll = GUI.BeginScrollView(new Rect(popX, popY, popW, popH), _dropdownScroll, new Rect(0, 0, popW - 20f, total * itemH));
            for (int k = 0; k < total; k++)
            {
                string faceName = FaceManager.AvailableFaces[k];
                if (GUI.Button(new Rect(2f, k * itemH, popW - 24f, itemH - 2f), faceName))
                {
                    SetSlotFace(s, faceName);
                    _activeDropdownSlot = -1;
                }
            }
            GUI.EndScrollView();
        }
    }
}
