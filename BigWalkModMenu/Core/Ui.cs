using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// Small vertical layout on top of GUI.* with fixed rects (GUILayout is avoided on purpose: it is
    /// the part of IMGUI that most often breaks under IL2CPP). Every control takes the full width and
    /// moves the cursor down.
    /// </summary>
    public sealed class Ui
    {
        public float X, Y, Width;
        private float _startY;
        private static Texture2D _hueWheel;
        private static string _openDropdown;

        public const float Row = 20f;
        public const float Gap = 3f;

        public void Begin(float x, float y, float width)
        {
            X = x;
            Y = y;
            Width = width;
            _startY = y;
        }

        public float Used => Y - _startY;

        public void Space(float h = 6f) => Y += h;

        /// <summary>Takes a block of the given height for custom drawing.</summary>
        public Rect Reserve(float height)
        {
            var r = new Rect(X, Y, Width, height);
            Y += height + Gap;
            return r;
        }

        public void Header(string text)
        {
            Y += 4f;
            Tint(new Rect(X, Y, Width, Row), new Color(1f, 1f, 1f, 0.08f));
            GUI.Label(new Rect(X + 4f, Y, Width - 8f, Row), "<b>" + text + "</b>");
            Y += Row + Gap;
        }

        public void Label(string text, int lines = 1)
        {
            float h = 18f * lines;
            GUI.Label(new Rect(X, Y, Width, h + 2f), text);
            Y += h + Gap;
        }

        public void Note(string text, int lines = 1)
        {
            float h = 14f * lines;
            GUI.Label(new Rect(X, Y, Width, h + 4f), "<size=10><color=#cfcfcf>" + text + "</color></size>");
            Y += h + Gap + 1f;
        }

        public bool Button(string text)
        {
            bool hit = GUI.Button(new Rect(X, Y, Width, Row), text);
            Y += Row + Gap;
            return hit;
        }

        /// <summary>Buttons side by side. Returns the index of the one clicked, or -1.</summary>
        public int Buttons(params string[] labels)
        {
            int hit = -1;
            float w = (Width - Gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                if (GUI.Button(new Rect(X + i * (w + Gap), Y, w, Row), labels[i])) hit = i;
            }
            Y += Row + Gap;
            return hit;
        }

        /// <summary>Returns true when the value was flipped.</summary>
        public bool Toggle(string label, ref bool value)
        {
            string text = value ? label + ": <b>ON</b>" : label + ": OFF";
            bool hit = GUI.Button(new Rect(X, Y, Width, Row), text);
            Y += Row + Gap;
            if (hit) value = !value;
            return hit;
        }

        /// <summary>Label with -/+ steppers and a slider below. Returns true when the value changed.</summary>
        public bool Slider(string label, ref float value, float min, float max, float step)
        {
            GUI.Label(new Rect(X, Y, Width - 58f, 18f), label);
            float result = value;
            if (GUI.Button(new Rect(X + Width - 52f, Y, 24f, 18f), "-")) result -= step;
            if (GUI.Button(new Rect(X + Width - 26f, Y, 24f, 18f), "+")) result += step;
            float slid = GUI.HorizontalSlider(new Rect(X + 2f, Y + 21f, Width - 4f, 14f), value, min, max);
            if (Mathf.Abs(slid - value) > (max - min) * 0.0005f) result = slid;
            result = Mathf.Clamp(result, min, max);
            Y += 38f;
            if (result == value) return false;
            value = result;
            return true;
        }

        public bool SliderInt(string label, ref int value, int min, int max)
        {
            float f = value;
            if (!Slider(label, ref f, min, max, 1f)) return false;
            int v = Mathf.RoundToInt(f);
            if (v == value) return false;
            value = v;
            return true;
        }

        /// <summary>"label: &lt; option &gt;" cycling through the options.</summary>
        public bool Choice(string label, ref int index, IList<string> options)
        {
            if (options == null || options.Count == 0) { Label(label + ": -"); return false; }
            int i = Mathf.Clamp(index, 0, options.Count - 1);
            float lw = Width * 0.42f;
            GUI.Label(new Rect(X, Y, lw, Row), label);
            float bx = X + lw, bw = Width - lw;
            int result = i;
            if (GUI.Button(new Rect(bx, Y, 24f, Row), "<")) result = (i - 1 + options.Count) % options.Count;
            GUI.Box(new Rect(bx + 26f, Y, bw - 52f, Row), options[i]);
            if (GUI.Button(new Rect(bx + bw - 24f, Y, 24f, Row), ">")) result = (i + 1) % options.Count;
            Y += Row + Gap;
            if (result == index) return false;
            index = result;
            return true;
        }

        /// <summary>A button that unfolds the list of options right below itself.</summary>
        public bool Dropdown(string id, string label, IList<string> options, ref int index)
        {
            string current = options != null && index >= 0 && index < options.Count ? options[index] : "-";
            bool open = _openDropdown == id;
            if (GUI.Button(new Rect(X, Y, Width, Row), label + ": <b>" + current + "</b> " + (open ? "▲" : "▼")))
            {
                _openDropdown = open ? null : id;
                open = !open;
            }
            Y += Row + Gap;
            if (!open || options == null) return false;

            bool changed = false;
            for (int i = 0; i < options.Count; i++)
            {
                string text = i == index ? "<b>● " + options[i] + "</b>" : options[i];
                if (GUI.Button(new Rect(X + 14f, Y, Width - 14f, Row - 1f), text))
                {
                    changed = i != index;
                    index = i;
                    _openDropdown = null;
                }
                Y += Row;
            }
            Y += Gap;
            return changed;
        }

        public static void CloseDropdowns() => _openDropdown = null;

        public void Swatch(Color colour, float height = 14f)
        {
            Tint(new Rect(X, Y, Width, height), colour);
            Y += height + Gap;
        }

        /// <summary>Hue by angle and saturation by distance on a wheel, brightness on a slider beside it.</summary>
        public bool ColorWheel(ref float hue, ref float saturation, ref float value)
        {
            const float size = 104f;
            bool changed = false;
            Rect wheel = new Rect(X, Y, size, size);
            GUI.DrawTexture(wheel, HueWheel);

            Event ev = Event.current;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && wheel.Contains(ev.mousePosition))
            {
                float dx = (ev.mousePosition.x - wheel.center.x) / (size * 0.5f);
                float dy = (wheel.center.y - ev.mousePosition.y) / (size * 0.5f);
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= 1.05f)
                {
                    float h = MathF.Atan2(dy, dx) / (2f * MathF.PI);
                    hue = h - MathF.Floor(h);
                    saturation = MathF.Min(dist, 1f);
                    changed = true;
                    ev.Use();
                }
            }

            float angle = hue * 2f * MathF.PI;
            float markerR = saturation * size * 0.5f;
            float mx = wheel.center.x + MathF.Cos(angle) * markerR, my = wheel.center.y - MathF.Sin(angle) * markerR;
            Tint(new Rect(mx - 4f, my - 4f, 8f, 8f), Color.black);
            Tint(new Rect(mx - 2.5f, my - 2.5f, 5f, 5f), Color.white);

            // Brightness and the resulting colour, to the right of the wheel.
            float ox = X, oy = Y, ow = Width;
            X = ox + size + 10f;
            Width = ow - size - 10f;
            changed |= Slider($"Brightness: {Mathf.RoundToInt(value * 100f)}%", ref value, 0f, 1f, 0.05f);
            Swatch(Game.Hsv(hue, saturation, value), 22f);
            X = ox;
            Width = ow;
            Y = oy + size + Gap + 2f;
            return changed;
        }

        /// <summary>Shows the key and lets the user press a new one. Esc cancels, Backspace clears.</summary>
        public bool HotkeyRow(Hotkey key)
        {
            bool capturing = ReferenceEquals(ModMenuApi.CapturingKey, key);
            float lw = Width * 0.6f;
            GUI.Label(new Rect(X, Y, lw, Row), key.Label);
            string text = capturing ? "<b>press a key…</b>" : (key.Key == KeyCode.None ? "none" : key.Key.ToString());
            if (GUI.Button(new Rect(X + lw, Y, Width - lw, Row), text))
            {
                ModMenuApi.CapturingKey = capturing ? null : key;
                capturing = !capturing;
            }
            Y += Row + Gap;

            if (!capturing) return false;
            Event ev = Event.current;
            if (ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None) return false;
            KeyCode pressed = ev.keyCode;
            ev.Use();
            ModMenuApi.CapturingKey = null;
            if (pressed == KeyCode.Escape) return false;
            key.Key = pressed == KeyCode.Backspace || pressed == KeyCode.Delete ? KeyCode.None : pressed;
            return true;
        }

        public static void Tint(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static Texture2D HueWheel
        {
            get
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
                        Color col = Game.Hsv(MathF.Atan2(dy, dx) / (2f * MathF.PI), MathF.Min(dist, 1f), 1f);
                        float alpha = Math.Clamp((1f - dist) * c, 0f, 1f); // soft edge
                        pixels[j * n + i] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(alpha * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false);
                _hueWheel = tex;
                return tex;
            }
        }
    }
}
