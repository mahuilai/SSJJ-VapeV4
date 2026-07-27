using UnityEngine;

namespace Vape.UI
{
    public static class UiDraw
    {
        private static Texture2D _white;
        private static bool _ready;
        public static GUIStyle Title;
        public static GUIStyle SubTitle;
        public static GUIStyle Label;
        public static GUIStyle Muted;
        public static GUIStyle Button;
        public static GUIStyle ButtonActive;
        public static GUIStyle Section;
        public static GUIStyle ToggleOn;
        public static GUIStyle ToggleOff;
        public static GUIStyle Value;
        public static GUIStyle Window;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }
                return _white;
            }
        }

        public static void Ensure()
        {
            if (_ready) return;

            Title = MakeLabel(15, FontStyle.Bold, Theme.TextPrimary, TextAnchor.MiddleLeft);
            SubTitle = MakeLabel(11, FontStyle.Normal, Theme.TextSecondary, TextAnchor.MiddleRight);
            Label = MakeLabel(12, FontStyle.Normal, Theme.TextPrimary, TextAnchor.MiddleLeft);
            Muted = MakeLabel(11, FontStyle.Italic, Theme.TextMuted, TextAnchor.MiddleLeft);
            Value = MakeLabel(11, FontStyle.Normal, Theme.AccentHot, TextAnchor.MiddleRight);
            Section = MakeLabel(12, FontStyle.Bold, Theme.Accent, TextAnchor.MiddleLeft);

            Button = MakeButton(Theme.BgCard, Theme.TextPrimary, Theme.Border);
            ButtonActive = MakeButton(Theme.Accent, Color.white, Theme.AccentHot);
            ToggleOn = MakeButton(Theme.Accent, Color.white, Theme.AccentHot);
            ToggleOff = MakeButton(Theme.BgHover, Theme.TextSecondary, Theme.Border);

            Window = new GUIStyle(GUI.skin.window)
            {
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(0, 0, 0, 0),
                normal = { background = White, textColor = Theme.TextPrimary },
                onNormal = { background = White, textColor = Theme.TextPrimary },
                focused = { background = White, textColor = Theme.TextPrimary },
                onFocused = { background = White, textColor = Theme.TextPrimary }
            };

            _ready = true;
        }

        private static GUIStyle MakeLabel(int size, FontStyle style, Color color, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = style,
                alignment = anchor,
                richText = true,
                normal = { textColor = color },
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        private static GUIStyle MakeButton(Color bg, Color text, Color border)
        {
            // Unity GUIStyle can't truly border via color alone; we tint via GUI.backgroundColor at call site.
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26,
                normal = { textColor = text, background = White },
                hover = { textColor = text, background = White },
                active = { textColor = text, background = White },
                focused = { textColor = text, background = White },
                onNormal = { textColor = text, background = White },
                border = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(8, 8, 4, 4)
            };
        }

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        public static void Frame(Rect r, Color fill, Color border, float thickness = 1f)
        {
            Fill(r, fill);
            Fill(new Rect(r.x, r.y, r.width, thickness), border);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), border);
            Fill(new Rect(r.x, r.y, thickness, r.height), border);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), border);
        }

        public static void AccentBar(Rect r)
        {
            Fill(new Rect(r.x, r.y, r.width, 2f), Theme.Accent);
        }

        public static bool PillToggle(Rect r, bool value, string text)
        {
            Frame(r, value ? Theme.AccentSoft : Theme.BgCard, value ? Theme.Accent : Theme.Border);
            var style = value ? ToggleOn : ToggleOff;
            // text drawn manually to avoid default button chrome
            GUI.Label(r, (value ? "●  " : "○  ") + text, Label);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                value = !value;
            return value;
        }

        public static bool Chip(Rect r, bool active, string text)
        {
            Frame(r, active ? Theme.Accent : Theme.BgCard, active ? Theme.AccentHot : Theme.Border);
            var prev = GUI.color;
            GUI.color = active ? Color.white : Theme.TextSecondary;
            GUI.Label(r, text, new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
            GUI.color = prev;
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
    }
}
