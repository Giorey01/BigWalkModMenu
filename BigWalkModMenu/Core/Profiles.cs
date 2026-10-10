using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// A player's look saved to a file that friends can drop into their own "profiles" folder. It is the
    /// fallback for when the live channel is not available (for instance the host has no mod).
    /// Each module contributes a named part; a profile file is as untrusted as a network message.
    ///
    ///   "BWP1", player key (digits), name, part count, then for each part: id, bytes
    /// </summary>
    public static class Profiles
    {
        private sealed class Part
        {
            public int MaxBytes;
            public Func<byte[]> Export;
            public Action<string, byte[]> Import;
        }

        private static readonly Dictionary<string, Part> _parts = new Dictionary<string, Part>();
        private const string Magic = "BWP1";

        public static string Dir
        {
            get
            {
                string p = ModMenuApi.Renamed(ModMenuApi.RootPath, "profiles", "profili");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        /// <summary>export: this player's data, or null when there is nothing. import: (player key, data) from a friend's file.</summary>
        public static void Register(string id, int maxBytes, Func<byte[]> export, Action<string, byte[]> import)
        {
            _parts[id] = new Part { MaxBytes = Math.Min(maxBytes, 60000), Export = export, Import = import };
        }

        private static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 24) return false;
            foreach (char c in key) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>Writes my profile and returns the file path, or null with a reason.</summary>
        public static string ExportMine(out string problem)
        {
            problem = null;
            PlayerCharacter me = Game.LocalPlayer;
            string key = Identity.Key(me);
            if (!IsKey(key)) { problem = "Join a walk first: your game ID is needed."; return null; }

            var w = new WireWriter();
            w.Raw(Encoding.ASCII.GetBytes(Magic)).Text(key, 24).Text(Identity.Name(me), 40);
            var parts = new List<KeyValuePair<string, byte[]>>();
            foreach (var kv in _parts)
            {
                try
                {
                    byte[] data = kv.Value.Export();
                    if (data != null && data.Length > 0 && data.Length <= kv.Value.MaxBytes) parts.Add(new KeyValuePair<string, byte[]>(kv.Key, data));
                }
                catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Profiles] Part '{kv.Key}' could not be exported: {ex.Message}"); }
            }
            w.U8(parts.Count);
            foreach (var part in parts) w.Text(part.Key, 16).Bytes(part.Value, 0, part.Value.Length);

            try
            {
                string path = Path.Combine(Dir, key + ".bwp");
                File.WriteAllBytes(path, w.ToArray());
                return path;
            }
            catch (Exception ex)
            {
                problem = "Could not write the file: " + ex.Message;
                return null;
            }
        }

        /// <summary>Reads every friend's profile in the folder. Returns how many were accepted.</summary>
        public static int LoadAll()
        {
            int loaded = 0;
            string mine = Identity.Key(Game.LocalPlayer);
            string[] files;
            try { files = Directory.GetFiles(Dir, "*.bwp"); } catch { return 0; }
            foreach (string file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length < 8 || info.Length > 100000) continue;
                    var r = new WireReader(File.ReadAllBytes(file));
                    if (Encoding.ASCII.GetString(r.Raw(4)) != Magic) continue;
                    string key = r.Text(24);
                    r.Text(40);
                    int count = r.Int(0, 8);
                    if (r.Failed || !IsKey(key) || key == mine) continue;
                    for (int i = 0; i < count; i++)
                    {
                        string id = r.Text(16);
                        byte[] data = r.Bytes(60000);
                        if (r.Failed) break;
                        if (!_parts.TryGetValue(id, out Part part) || data.Length > part.MaxBytes) continue;
                        try { part.Import(key, data); }
                        catch (Exception ex) { ModMenuApi.Log.LogWarning($"[Profiles] {Path.GetFileName(file)}, part '{id}': {ex.Message}"); }
                    }
                    if (!r.Failed) loaded++;
                }
                catch (Exception ex)
                {
                    ModMenuApi.Log.LogWarning($"[Profiles] {Path.GetFileName(file)}: {ex.Message}");
                }
            }
            ModMenuApi.Log.LogInfo($"[Profiles] {loaded} friend profile(s) loaded from {files.Length} file(s).");
            return loaded;
        }
    }
}
