using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>Plain "Key=Value" settings file, one per module.</summary>
    public sealed class ModConfig
    {
        private readonly string _path;
        private readonly SortedDictionary<string, string> _values = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public ModConfig(string path)
        {
            _path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    _values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[Config] Could not read '{path}': {ex.Message}");
            }
        }

        public bool Has(string key) => _values.ContainsKey(key);

        /// <summary>Every key in the file, for modules that keep one entry per player or per item.</summary>
        public List<string> Keys => new List<string>(_values.Keys);

        public void Remove(string key) => _values.Remove(key);

        public string GetString(string key, string def) => _values.TryGetValue(key, out string v) ? v : def;

        public float GetFloat(string key, float def)
        {
            return _values.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;
        }

        public int GetInt(string key, int def)
        {
            return _values.TryGetValue(key, out string v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : def;
        }

        public bool GetBool(string key, bool def)
        {
            if (!_values.TryGetValue(key, out string v)) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public KeyCode GetKey(string key, KeyCode def)
        {
            return _values.TryGetValue(key, out string v) && Enum.TryParse(v, true, out KeyCode k) ? k : def;
        }

        public void Set(string key, string value) => _values[key] = value ?? "";
        public void Set(string key, float value) => _values[key] = value.ToString("0.####", CultureInfo.InvariantCulture);
        public void Set(string key, int value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);
        public void Set(string key, bool value) => _values[key] = value ? "1" : "0";
        public void Set(string key, KeyCode value) => _values[key] = value.ToString();

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var lines = new List<string>(_values.Count);
                foreach (var kv in _values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(_path, lines);
            }
            catch (Exception ex)
            {
                ModMenuApi.Log.LogWarning($"[Config] Could not write '{_path}': {ex.Message}");
            }
        }
    }

    /// <summary>A key the user can rebind from the menu. KeyCode.None means "no key".</summary>
    public sealed class Hotkey
    {
        public readonly string Id;
        public readonly string Label;
        public readonly KeyCode Default;
        public KeyCode Key;

        public Hotkey(string id, string label, KeyCode def)
        {
            Id = id;
            Label = label;
            Default = def;
            Key = def;
        }

        // Never while the player is typing in the game's chat: the key is part of the text then.
        public bool Down => Key != KeyCode.None && !ModMenuApi.CapturingHotkey && !Game.IsTyping && Input.GetKeyDown(Key);
        public bool Held => Key != KeyCode.None && !ModMenuApi.CapturingHotkey && !Game.IsTyping && Input.GetKey(Key);

        public void Load(ModConfig c) => Key = c.GetKey("Key." + Id, Default);
        public void Save(ModConfig c) => c.Set("Key." + Id, Key);
    }
}
