using System.Collections.Generic;
using UnityEngine;

namespace Vape.UI.Menu
{
    public static class CardColors
    {
        public static readonly Color Accent = C(6, 161, 126);
        public static readonly Color AccentDark = C(5, 134, 105);
        public static readonly Color BgFrame = C(26, 25, 26);
        public static readonly Color BgHover = C(37, 36, 38);
        public static readonly Color BgEnabled = C(46, 45, 47);
        public static readonly Color Border = C(54, 53, 54);
        public static readonly Color BorderHalf = CA(54, 53, 54, 128);
        public static readonly Color ToggleOff = C(54, 53, 54);
        public static readonly Color ToggleKnob = C(26, 25, 26);
        public static readonly Color TextPrimary = C(218, 218, 218);
        public static readonly Color TextSecondary = C(168, 168, 168);
        public static readonly Color TextMuted = C(102, 102, 104);

        private static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
        private static Color CA(int r, int g, int b, int a) => new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    public class CardAnimFloat
    {
        public float Value { get; private set; }
        private bool _forward;

        public CardAnimFloat(bool initial)
        {
            Value = initial ? 1f : 0f;
            _forward = initial;
        }

        public void SetTarget(bool target) => _forward = target;

        public void Update(float deltaTime)
        {
            float target = _forward ? 1f : 0f;
            Value = Mathf.MoveTowards(Value, target, deltaTime / 0.14f);
        }
    }

    public sealed class CardScrollBar
    {
        private bool _dragging;
        private float _grabOffset;

        public bool IsDragging => _dragging;

        public float Draw(Rect viewport, float current, float target, float contentHeight)
        {
            float maxScroll = Mathf.Max(0f, contentHeight - viewport.height);
            if (maxScroll <= 0.5f)
            {
                _dragging = false;
                return 0f;
            }

            Rect track = new Rect(viewport.xMax - 7f, viewport.y + 5f, 4f, viewport.height - 10f);
            float thumbHeight = Mathf.Clamp(track.height * (viewport.height / contentHeight), 32f, track.height);
            float travel = Mathf.Max(0f, track.height - thumbHeight);
            float thumbY = track.y + travel * Mathf.Clamp01(current / maxScroll);
            Rect thumb = new Rect(track.x - 1f, thumbY, 6f, thumbHeight);
            Rect trackHit = new Rect(track.x - 7f, track.y, 18f, track.height);
            Rect thumbHit = new Rect(thumb.x - 6f, thumb.y, thumb.width + 12f, thumb.height);

            Event currentEvent = Event.current;
            bool hovered = trackHit.Contains(currentEvent.mousePosition);
            CardDraw.RoundedRect(track, hovered || _dragging ? CardColors.Border : CardColors.BorderHalf, 2f);
            if (hovered || _dragging)
                CardDraw.RoundedRect(new Rect(thumb.x - 2f, thumb.y, thumb.width + 4f, thumb.height), new Color(CardColors.Accent.r, CardColors.Accent.g, CardColors.Accent.b, 0.18f), 5f);
            CardDraw.RoundedRect(thumb, _dragging ? Color.white : CardColors.Accent, 3f);

            if (currentEvent.type == EventType.ScrollWheel && viewport.Contains(currentEvent.mousePosition))
            {
                target = Mathf.Clamp(target + currentEvent.delta.y * 42f, 0f, maxScroll);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && trackHit.Contains(currentEvent.mousePosition))
            {
                _dragging = true;
                if (thumbHit.Contains(currentEvent.mousePosition))
                {
                    _grabOffset = currentEvent.mousePosition.y - thumb.y;
                }
                else
                {
                    _grabOffset = thumbHeight * 0.5f;
                    target = PositionToScroll(currentEvent.mousePosition.y, track, thumbHeight, _grabOffset, maxScroll);
                }
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && _dragging)
            {
                target = PositionToScroll(currentEvent.mousePosition.y, track, thumbHeight, _grabOffset, maxScroll);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                currentEvent.Use();
            }

            return Mathf.Clamp(target, 0f, maxScroll);
        }

        private static float PositionToScroll(float mouseY, Rect track, float thumbHeight, float grabOffset, float maxScroll)
        {
            float travel = Mathf.Max(1f, track.height - thumbHeight);
            float thumbTop = Mathf.Clamp(mouseY - grabOffset, track.y, track.y + travel);
            return ((thumbTop - track.y) / travel) * maxScroll;
        }
    }

    public static class CardDraw
    {
        private static Texture2D _transparent;
        private static readonly Dictionary<string, Texture2D> RoundedTextures = new Dictionary<string, Texture2D>();

        public static Texture2D Transparent
        {
            get
            {
                if (_transparent == null)
                {
                    _transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _transparent.SetPixel(0, 0, Color.clear);
                    _transparent.Apply();
                    _transparent.hideFlags = HideFlags.HideAndDontSave;
                }
                return _transparent;
            }
        }

        public static void RoundedRect(Rect rect, Color color, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;

            int width = Mathf.CeilToInt(rect.width);
            int height = Mathf.CeilToInt(rect.height);
            int corner = Mathf.Clamp(Mathf.CeilToInt(radius), 0, Mathf.Min(width, height) / 2);
            string key = width + "x" + height + ":" + corner;
            if (!RoundedTextures.TryGetValue(key, out Texture2D texture))
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                var pixels = new Color[width * height];
                float cx = (width - 1) * 0.5f;
                float cy = (height - 1) * 0.5f;
                float innerX = width * 0.5f - corner;
                float innerY = height * 0.5f - corner;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - cx) - innerX, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y - cy) - innerY, 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = corner == 0 ? 1f : Mathf.Clamp01(corner + 0.5f - distance);
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                RoundedTextures[key] = texture;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
            GUI.color = previous;
        }

        public static void RoundedFrame(Rect rect, Color fill, Color border, float radius, float thickness = 1f)
        {
            RoundedRect(rect, border, radius);
            RoundedRect(new Rect(rect.x + thickness, rect.y + thickness, rect.width - thickness * 2f, rect.height - thickness * 2f),
                fill, Mathf.Max(0f, radius - thickness));
        }

        public static bool Toggle(Rect rect, bool value, CardAnimFloat animation)
        {
            animation.SetTarget(value);
            Color track = Color.Lerp(CardColors.ToggleOff, CardColors.Accent, animation.Value);
            RoundedRect(rect, track, rect.height * 0.5f);

            float knobSize = Mathf.Max(4f, rect.height - 6f);
            float knobX = Mathf.Lerp(rect.x + 3f, rect.xMax - knobSize - 3f, animation.Value);
            RoundedRect(new Rect(knobX, rect.y + (rect.height - knobSize) * 0.5f, knobSize, knobSize),
                CardColors.ToggleKnob, knobSize * 0.5f);
            return value;
        }

        public static float Slider(Rect row, string label, float value, float min, float max, string format, string suffix = "")
        {
            GUI.Label(new Rect(row.x + 14f, row.y + 2f, row.width * 0.55f, 20f), label,
                new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = CardColors.TextSecondary } });
            GUI.Label(new Rect(row.xMax - 74f, row.y + 2f, 60f, 20f), value.ToString(format) + suffix,
                new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight, normal = { textColor = CardColors.Accent } });

            float trackX = row.x + 14f;
            float trackWidth = row.width - 28f;
            float trackY = row.y + 31f;
            float ratio = max <= min ? 0f : Mathf.Clamp01((value - min) / (max - min));
            RoundedRect(new Rect(trackX, trackY, trackWidth, 5f), CardColors.BorderHalf, 2.5f);
            RoundedRect(new Rect(trackX, trackY, trackWidth * ratio, 5f), CardColors.Accent, 2.5f);
            RoundedFrame(new Rect(trackX + trackWidth * ratio - 6f, trackY - 3.5f, 12f, 12f), CardColors.BgFrame, CardColors.Accent, 6f, 1.5f);

            Rect input = new Rect(trackX - 8f, trackY - 10f, trackWidth + 16f, 26f);
            if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && input.Contains(Event.current.mousePosition))
            {
                float next = Mathf.Clamp01((Event.current.mousePosition.x - trackX) / trackWidth);
                Event.current.Use();
                return min + (max - min) * next;
            }
            return value;
        }
    }
}
