using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vape.UI
{
    /// <summary>
    /// Fast ClickGUI widgets. Avoids per-pixel circle rasterization (that killed FPS).
    /// Uses cached 1x1 white tex + optional small circle atlas.
    /// </summary>
    public static class Widgets
    {
        private static readonly Dictionary<string, float> _anim = new Dictionary<string, float>(64);
        private static Texture2D _white;
        private static Texture2D _circle; // 32x32 soft circle
        private static GUIStyle _label;
        private static GUIStyle _labelRight;
        private static GUIStyle _labelSmall;
        private static GUIStyle _labelCenter;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _brandStyle;
        private static GUIStyle _chipOn;
        private static GUIStyle _chipOff;
        private static GUIStyle _btnLabel;
        private static GUIStyle _keyLabel;
        private static GUIStyle _closeLabel;
        private static GUIStyle _textField;
        private static GUIStyle _invisible;
        private static bool _ready;

        public const float RowH = 26f;
        public const float SwitchW = 34f;
        public const float SwitchH = 16f;

        public static void BeginFrame()
        {
            Ensure();
        }

        public static void Ensure()
        {
            if (_ready) return;

            _white = MakeSolid();
            _circle = MakeCircleTex(32);

            _label = Mk(12, FontStyle.Normal, Theme.TextPrimary, TextAnchor.MiddleLeft);
            _labelRight = Mk(11, FontStyle.Bold, Theme.AccentHot, TextAnchor.MiddleRight);
            _labelSmall = Mk(11, FontStyle.Normal, Theme.TextSecondary, TextAnchor.MiddleLeft);
            _labelCenter = Mk(12, FontStyle.Bold, Theme.TextPrimary, TextAnchor.MiddleCenter);
            _sectionStyle = Mk(11, FontStyle.Bold, Theme.Accent, TextAnchor.MiddleLeft);
            _brandStyle = Mk(18, FontStyle.Bold, Theme.Accent, TextAnchor.MiddleLeft);
            _chipOn = Mk(11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _chipOff = Mk(11, FontStyle.Bold, Theme.TextSecondary, TextAnchor.MiddleCenter);
            _btnLabel = Mk(12, FontStyle.Bold, Theme.TextPrimary, TextAnchor.MiddleCenter);
            _keyLabel = Mk(11, FontStyle.Bold, Theme.TextPrimary, TextAnchor.MiddleCenter);
            _closeLabel = Mk(14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _invisible = new GUIStyle();
            _textField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Theme.TextPrimary, background = _white },
                focused = { textColor = Theme.TextPrimary, background = _white },
                active = { textColor = Theme.TextPrimary, background = _white },
                hover = { textColor = Theme.TextPrimary, background = _white },
                padding = new RectOffset(8, 8, 0, 0)
            };

            _ready = true;
        }

        private static Texture2D MakeSolid()
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            t.SetPixel(0, 0, Color.white);
            t.Apply(false, true);
            return t;
        }

        private static Texture2D MakeCircleTex(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float c = (size - 1) * 0.5f;
            float r = c - 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = d <= r - 0.75f ? (byte)255 : (d >= r + 0.75f ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt((r + 0.75f - d) / 1.5f * 255f), 0, 255));
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            t.SetPixels32(pixels);
            t.Apply(false, true);
            return t;
        }

        private static GUIStyle Mk(int size, FontStyle fs, Color c, TextAnchor a)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fs,
                alignment = a,
                richText = false,
                clipping = TextClipping.Clip,
                normal = { textColor = c },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        public static void Fill(Rect r, Color c)
        {
            if (r.width <= 0.5f || r.height <= 0.5f) return;
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        public static void Circle(Vector2 center, float radius, Color c)
        {
            if (radius <= 0.5f || _circle == null) return;
            Color prev = GUI.color;
            GUI.color = c;
            float d = radius * 2f;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, d, d), _circle);
            GUI.color = prev;
        }

        // Cheap "rounded" look: body rect only (no per-pixel corners). Border optional.
        public static void RoundRect(Rect r, Color fill, float radius = 4f)
        {
            Fill(r, fill);
            // corner soft dots only if large enough — 4 cheap textured circles
            if (radius >= 2f && r.width > radius * 2f && r.height > radius * 2f)
            {
                Circle(new Vector2(r.x + radius, r.y + radius), radius, fill);
                Circle(new Vector2(r.xMax - radius, r.y + radius), radius, fill);
                Circle(new Vector2(r.x + radius, r.yMax - radius), radius, fill);
                Circle(new Vector2(r.xMax - radius, r.yMax - radius), radius, fill);
            }
        }

        public static void Rect(Rect r, Color fill, Color border, float bt = 1f)
        {
            Fill(r, fill);
            if (bt <= 0f) return;
            Fill(new Rect(r.x, r.y, r.width, bt), border);
            Fill(new Rect(r.x, r.yMax - bt, r.width, bt), border);
            Fill(new Rect(r.x, r.y, bt, r.height), border);
            Fill(new Rect(r.xMax - bt, r.y, bt, r.height), border);
        }

        public static void Shadow(Rect r, float spread = 3f)
        {
            // single soft shadow pass only
            Fill(new Rect(r.x - 2f, r.y + 3f, r.width + 4f, r.height + 2f), new Color(0, 0, 0, 0.18f));
        }

        private static float Anim(string id, float target, float speed = 12f)
        {
            if (!_anim.TryGetValue(id, out float v)) v = target;
            // snap when menu not needing fancy anim under load
            float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            v = Mathf.Lerp(v, target, k);
            if (Mathf.Abs(v - target) < 0.005f) v = target;
            _anim[id] = v;
            return v;
        }

        public static Rect Reserve(float height)
            => GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));

        public static void Space(float h = 6f) => GUILayout.Space(h);

        public static void Section(string title)
        {
            Space(8f);
            Rect r = Reserve(16f);
            Fill(new Rect(r.x + 2, r.y + 5, 5, 5), Theme.Accent);
            GUI.Label(new Rect(r.x + 12, r.y, r.width - 12, r.height), title.ToUpperInvariant(), _sectionStyle);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1f), new Color(Theme.Border.r, Theme.Border.g, Theme.Border.b, 0.5f));
            Space(3f);
        }

        public static void BeginCard() { GUILayout.BeginVertical(); Space(2f); }
        public static void EndCard() { Space(2f); GUILayout.EndVertical(); }

        public static void PanelBackground(Rect r)
        {
            Fill(new Rect(r.x + 2, r.y + 3, r.width, r.height), new Color(0, 0, 0, 0.25f));
            Rect(r, Theme.BgPanel, Theme.Border, 1f);
            Fill(new Rect(r.x, r.y, r.width, 2f), Theme.Accent);
            Fill(new Rect(r.x, r.y, r.width, 28f), Theme.BgDeep);
            Fill(new Rect(r.x, r.y + 28f, r.width, 1f), Theme.Border);
        }

        public static void NavBar(Rect r, string brand, string sub)
        {
            Rect(r, Theme.BgDeep, Theme.Border, 1f);
            Fill(new Rect(r.x, r.y, r.width, 2f), Theme.Accent);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 90, 28), brand, _brandStyle);
            GUI.Label(new Rect(r.x + 78, r.y + 16, 80, 18), sub, _labelSmall);
        }

        public static bool ChipTab(Rect r, bool active, string text)
        {
            Fill(r, active ? Theme.Accent : Theme.BgCard);
            Rect(r, Color.clear, active ? Theme.AccentHot : Theme.Border, 1f);
            GUI.Label(r, text, active ? _chipOn : _chipOff);
            return GUI.Button(r, GUIContent.none, _invisible);
        }

        public static bool Toggle(ref bool value, string label)
        {
            Rect row = Reserve(RowH);
            if (row.Contains(Event.current.mousePosition))
                Fill(row, new Color(1, 1, 1, 0.025f));

            GUI.Label(new Rect(row.x + 6, row.y, row.width - SwitchW - 18, row.height), label, _label);

            Rect sw = new Rect(row.xMax - SwitchW - 8, row.y + (row.height - SwitchH) * 0.5f, SwitchW, SwitchH);
            float t = Anim("sw_" + label, value ? 1f : 0f, 14f);
            Color track = Color.Lerp(new Color(0.18f, 0.20f, 0.24f, 1f), Theme.Accent, t);
            // capsule approx: rect + 2 circles
            float rr = SwitchH * 0.5f;
            Fill(new Rect(sw.x + rr, sw.y, sw.width - SwitchH, sw.height), track);
            Circle(new Vector2(sw.x + rr, sw.y + rr), rr, track);
            Circle(new Vector2(sw.xMax - rr, sw.y + rr), rr, track);

            float knobR = rr - 2.5f;
            float knobX = Mathf.Lerp(sw.x + 3f + knobR, sw.xMax - 3f - knobR, t);
            Circle(new Vector2(knobX, sw.y + rr), knobR + 0.6f, new Color(0, 0, 0, 0.25f));
            Circle(new Vector2(knobX, sw.y + rr), knobR, Color.white);

            if (GUI.Button(row, GUIContent.none, _invisible))
                value = !value;
            return value;
        }

        public static void SliderFloat(string label, ref float value, float min, float max, string fmt = "F1")
        {
            Rect row = Reserve(34f);
            GUI.Label(new Rect(row.x + 6, row.y + 1, row.width * 0.55f, 15), label, _labelSmall);
            GUI.Label(new Rect(row.xMax - 54, row.y + 1, 48, 15), value.ToString(fmt), _labelRight);

            Rect track = new Rect(row.x + 6, row.y + 19, row.width - 12, 5f);
            Fill(track, new Color(0.14f, 0.16f, 0.20f, 1f));
            float norm = Mathf.InverseLerp(min, max, value);
            float a = Anim("sl_" + label, norm, 16f);
            float fillW = Mathf.Max(4f, track.width * a);
            Fill(new Rect(track.x, track.y, fillW, track.height), Theme.Accent);
            float kx = track.x + track.width * a;
            Circle(new Vector2(kx, track.y + 2.5f), 6f, Color.white);
            Circle(new Vector2(kx, track.y + 2.5f), 3f, Theme.Accent);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            Rect hit = new Rect(track.x - 4, track.y - 8, track.width + 8, 20);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hit.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width));
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }

        public static void SliderInt(string label, ref int value, int min, int max, string suffix = "")
        {
            float f = value;
            SliderFloat(label, ref f, min, max, "F0");
            value = Mathf.RoundToInt(f);
        }

        public static void SliderIntS(string label, ref int value, int min, int max, string suffix)
        {
            Rect row = Reserve(34f);
            GUI.Label(new Rect(row.x + 6, row.y + 1, row.width * 0.5f, 15), label, _labelSmall);
            GUI.Label(new Rect(row.xMax - 64, row.y + 1, 58, 15), value + suffix, _labelRight);

            Rect track = new Rect(row.x + 6, row.y + 19, row.width - 12, 5f);
            Fill(track, new Color(0.14f, 0.16f, 0.20f, 1f));
            float norm = Mathf.InverseLerp(min, max, value);
            float a = Anim("sli_" + label, norm, 16f);
            Fill(new Rect(track.x, track.y, Mathf.Max(4f, track.width * a), track.height), Theme.Accent);
            float kx = track.x + track.width * a;
            Circle(new Vector2(kx, track.y + 2.5f), 6f, Color.white);
            Circle(new Vector2(kx, track.y + 2.5f), 3f, Theme.Accent);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            Rect hit = new Rect(track.x - 4, track.y - 8, track.width + 8, 20);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hit.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = Mathf.RoundToInt(Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width)));
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = Mathf.RoundToInt(Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width)));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }

        public static void KeyBind(string id, string label, KeyCode key, ref string bindId, ref bool bindWait)
        {
            Rect row = Reserve(RowH);
            GUI.Label(new Rect(row.x + 6, row.y, row.width * 0.45f, row.height), label, _label);
            bool active = bindWait && bindId == id;
            Rect btn = new Rect(row.xMax - 118, row.y + 3, 110, row.height - 6);
            Fill(btn, active ? Theme.Warning : Theme.BgHover);
            Rect(btn, Color.clear, active ? Theme.Warning : Theme.Border, 1f);
            string text = active ? "PRESS KEY" : (key == KeyCode.None ? "NONE" : key.ToString().ToUpperInvariant());
            var st = active
                ? new GUIStyle(_keyLabel) { normal = { textColor = Color.black } }
                : _keyLabel;
            GUI.Label(btn, text, st);
            if (GUI.Button(btn, GUIContent.none, _invisible))
            {
                if (active) { bindId = null; bindWait = false; }
                else { bindId = id; bindWait = true; }
            }
        }

        public static bool Button(string label, float height = 28f)
        {
            Rect r = Reserve(height);
            bool hov = r.Contains(Event.current.mousePosition);
            Fill(r, hov ? Theme.Accent : Theme.BgHover);
            Rect(r, Color.clear, hov ? Theme.AccentHot : Theme.Border, 1f);
            GUI.Label(r, label, _btnLabel);
            return GUI.Button(r, GUIContent.none, _invisible);
        }

        public static int Segment(string id, int index, string[] options)
        {
            if (options == null || options.Length == 0) return index;
            Rect row = Reserve(26f);
            float w = row.width / options.Length;
            Fill(row, Theme.BgDeep);
            for (int i = 0; i < options.Length; i++)
            {
                Rect cell = new Rect(row.x + i * w + 1, row.y + 1, w - 2, row.height - 2);
                bool on = i == index;
                if (on) Fill(cell, Theme.Accent);
                GUI.Label(cell, options[i], on ? _chipOn : _chipOff);
                if (GUI.Button(cell, GUIContent.none, _invisible)) index = i;
            }
            return index;
        }

        public static void Combo(string key, string label, List<string> items, Dictionary<string, ComboState> states, Action<string> onSelect = null)
        {
            if (items == null || items.Count == 0) return;
            if (!states.TryGetValue(key, out var st))
            {
                st = new ComboState();
                states[key] = st;
            }

            Rect row = Reserve(RowH);
            GUI.Label(new Rect(row.x + 6, row.y, 100, row.height), label, _label);
            Rect box = new Rect(row.x + 108, row.y + 3, row.width - 114, row.height - 6);
            Fill(box, Theme.BgCard);
            Rect(box, Color.clear, st.Open ? Theme.Accent : Theme.Border, 1f);
            string show = st.Index >= 0 && st.Index < items.Count ? items[st.Index] : "Select";
            GUI.Label(new Rect(box.x + 8, box.y, box.width - 24, box.height), show, _labelSmall);
            GUI.Label(new Rect(box.xMax - 18, box.y, 14, box.height), st.Open ? "^" : "v", _labelCenter);

            if (GUI.Button(box, GUIContent.none, _invisible))
            {
                st.Open = !st.Open;
                if (st.Open)
                    foreach (var kv in states)
                        if (kv.Key != key) kv.Value.Open = false;
            }

            if (Event.current.type == EventType.Repaint && st.Open)
                st.Dropdown = new Rect(box.x, box.yMax + 2, box.width, Mathf.Min(140, items.Count * 22f + 4));

            if (!st.Open) return;

            Rect(st.Dropdown, Theme.BgDeep, Theme.Accent, 1f);
            GUI.BeginGroup(st.Dropdown);
            st.Scroll = GUI.BeginScrollView(new Rect(0, 0, st.Dropdown.width, st.Dropdown.height), st.Scroll,
                new Rect(0, 0, st.Dropdown.width - 14, items.Count * 22f), false, false);
            for (int i = 0; i < items.Count; i++)
            {
                Rect ir = new Rect(2, i * 22f + 1, st.Dropdown.width - 18, 20);
                if (i == st.Index) Fill(ir, Theme.AccentSoft);
                GUI.Label(new Rect(ir.x + 6, ir.y, ir.width - 6, ir.height), items[i], _labelSmall);
                if (GUI.Button(ir, GUIContent.none, _invisible))
                {
                    st.Index = i;
                    onSelect?.Invoke(items[i]);
                    st.Open = false;
                }
            }
            GUI.EndScrollView();
            GUI.EndGroup();
            GUILayout.Space(st.Dropdown.height);
        }

        public static string TextField(string label, string value, float labelW = 100f)
        {
            Rect row = Reserve(RowH);
            if (!string.IsNullOrEmpty(label))
                GUI.Label(new Rect(row.x + 6, row.y, labelW, row.height), label, _label);
            float x = string.IsNullOrEmpty(label) ? row.x + 6 : row.x + labelW + 8;
            Rect box = new Rect(x, row.y + 3, row.xMax - x - 6, row.height - 6);
            // draw bg without allocating style bg each time
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = Theme.BgDeep;
            // use colored rect under transparent-ish field
            Fill(box, Theme.BgDeep);
            Rect(box, Color.clear, Theme.Border, 1f);
            GUI.backgroundColor = new Color(1, 1, 1, 0); // keep text field chrome minimal
            string result = GUI.TextField(box, value ?? string.Empty, _textField);
            GUI.backgroundColor = prev;
            return result;
        }

        public static void Separator()
        {
            Rect r = Reserve(6f);
            Fill(new Rect(r.x + 6, r.y + 2, r.width - 12, 1f), new Color(Theme.Border.r, Theme.Border.g, Theme.Border.b, 0.45f));
        }

        public class ComboState
        {
            public bool Open;
            public Rect Dropdown;
            public Vector2 Scroll;
            public int Index;
        }
    }
}
