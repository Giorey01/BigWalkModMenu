using System;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// One graphics value the mod can take over. While it is off the game's own value is left alone;
    /// when it is switched off again the value found before is put back.
    /// </summary>
    public sealed class Knob
    {
        public readonly string Id, Label;
        public readonly float Min, Max, Step, Default;
        public readonly bool Multiplier; // the value multiplies the game's own instead of replacing it
        public readonly Func<float> Get;
        public readonly Action<float> Set;
        public readonly Func<float, float, string> Format; // (value, effective) -> text
        public bool On;
        public float Value;
        public bool Broken;

        private float _original, _lastWritten = float.NaN;
        private bool _hasOriginal;

        public Knob(string id, string label, float min, float max, float step, float def, bool multiplier,
            Func<float> get, Action<float> set, Func<float, float, string> format)
        {
            Id = id; Label = label; Min = min; Max = max; Step = step; Default = def; Value = def;
            Multiplier = multiplier; Get = get; Set = set; Format = format;
        }

        public float Effective => Multiplier && _hasOriginal ? _original * Value : Value;
        public bool HasOriginal => _hasOriginal;
        public float Original => _original;

        public void Apply()
        {
            if (Broken) return;
            try
            {
                float current = Get();
                if (float.IsNaN(current)) return; // the object it lives on is not there yet
                if (On)
                {
                    // A value we did not write means the game applied its own again: that is the new baseline.
                    if (!_hasOriginal || Mathf.Abs(current - _lastWritten) > 1e-3f)
                    {
                        _original = current;
                        _hasOriginal = true;
                    }
                    float target = Multiplier ? _original * Value : Value;
                    if (Mathf.Abs(current - target) > 1e-3f) Set(target);
                    _lastWritten = Get();
                }
                else if (_hasOriginal)
                {
                    Set(_original);
                    _hasOriginal = false;
                    _lastWritten = float.NaN;
                }
            }
            catch (Exception ex)
            {
                Broken = true;
                ModMenuApi.Log.LogWarning($"[Knob] '{Id}' is not available: {ex.Message}");
            }
        }
    }

    /// <summary>Settings that two tabs could both want to drive: the one that takes it names itself here.</summary>
    public static class GraphicsShare
    {
        /// <summary>Not null while a tab needs a specific antialiasing mode on the player camera.</summary>
        public static string AntialiasingOwner;
    }
}
