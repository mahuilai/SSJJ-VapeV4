using Vape.Cfg;
using Vape.Entity;
using Vape.UI.Menu;
using UnityEngine;

namespace Vape.Feature.Overlay
{
    public sealed class KeyHud : MonoBehaviour
    {
        private const float PanelWidth = 324f;
        private const float PanelHeight = 108f;
        private GUIStyle _keyStyle;
        private int _styleSize;
        private bool _forward;
        private bool _left;
        private bool _back;
        private bool _right;
        private bool _jump;
        private bool _sprint;

        private void Update()
        {
            _forward = Input.GetKey(KeyCode.W);
            _left = Input.GetKey(KeyCode.A);
            _back = Input.GetKey(KeyCode.S);
            _right = Input.GetKey(KeyCode.D);
            _jump = Input.GetKey(KeyCode.Space);
            _sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private void OnGUI()
        {
            if (!Config.KeyHud || Event.current.type != EventType.Repaint)
                return;
            if (PlayerUpdate.LocalEntity == null || PlayerUpdate.LocalEntity.IsDead)
                return;

            float scale = HudScale();
            EnsureStyles(scale);
            DrawHud(GetPanelRect(scale), scale);
        }

        private Rect GetPanelRect(float scale)
        {
            float width = PanelWidth * scale;
            float height = PanelHeight * scale;
            float x = 20f * scale;
            float csgoScale = CsgoHudScale();
            float anchorBottom = Config.CsgoHud
                ? Screen.height - 112f * csgoScale - 14f * scale
                : Screen.height - 20f * scale;
            float y = anchorBottom - height;

            x = Mathf.Clamp(x, 8f, Mathf.Max(8f, Screen.width - width - 8f));
            y = Mathf.Clamp(y, 8f, Mathf.Max(8f, Screen.height - height - 8f));
            return new Rect(x, y, width, height);
        }

        private void DrawHud(Rect panel, float scale)
        {
            Color accent = new Color(0.89f, 0.72f, 0.31f, 1f);

            DrawKey(new Rect(panel.x + 54f * scale, panel.y, 50f * scale, 50f * scale), "W", _forward, accent, scale);
            DrawKey(new Rect(panel.x, panel.y + 58f * scale, 50f * scale, 50f * scale), "A", _left, accent, scale);
            DrawKey(new Rect(panel.x + 54f * scale, panel.y + 58f * scale, 50f * scale, 50f * scale), "S", _back, accent, scale);
            DrawKey(new Rect(panel.x + 108f * scale, panel.y + 58f * scale, 50f * scale, 50f * scale), "D", _right, accent, scale);
            DrawKey(new Rect(panel.x + 180f * scale, panel.y, 144f * scale, 50f * scale), "SPACE", _jump, accent, scale);
            DrawKey(new Rect(panel.x + 180f * scale, panel.y + 58f * scale, 144f * scale, 50f * scale), "SHIFT", _sprint, accent, scale);
        }

        private void DrawKey(Rect rect, string label, bool active, Color accent, float scale)
        {
            CardDraw.RoundedRect(
                new Rect(rect.x + scale, rect.y + 2f * scale, rect.width, rect.height),
                new Color(0f, 0f, 0f, 0.30f),
                6f * scale);

            Color fill = active
                ? new Color(accent.r, accent.g, accent.b, 0.88f)
                : new Color(0.025f, 0.04f, 0.048f, 0.68f);
            Color outline = active
                ? new Color(1f, 0.9f, 0.58f, 0.96f)
                : new Color(0.9f, 0.96f, 1f, 0.22f);

            if (active)
            {
                CardDraw.RoundedFrame(
                    new Rect(rect.x - scale, rect.y - scale, rect.width + 2f * scale, rect.height + 2f * scale),
                    new Color(0f, 0f, 0f, 0f),
                    new Color(accent.r, accent.g, accent.b, 0.30f),
                    7f * scale,
                    Mathf.Max(1f, scale));
            }
            CardDraw.RoundedFrame(rect, fill, outline, 6f * scale, Mathf.Max(1f, scale));
            CardDraw.RoundedRect(
                new Rect(rect.x + 6f * scale, rect.y + 3f * scale, rect.width - 12f * scale, Mathf.Max(1f, scale)),
                new Color(1f, 1f, 1f, active ? 0.42f : 0.16f),
                0.5f * scale);
            CardDraw.RoundedRect(new Rect(rect.x + 7f * scale, rect.yMax - 4f * scale,
                rect.width - 14f * scale, 2f * scale), active ? Color.white : new Color(accent.r, accent.g, accent.b, 0.28f), scale);
            _keyStyle.normal.textColor = active ? new Color(0.08f, 0.07f, 0.04f, 1f) : new Color(0.94f, 0.98f, 1f, 0.80f);
            GUI.Label(rect, label, _keyStyle);
        }

        private void EnsureStyles(float scale)
        {
            int fontSize = Mathf.Max(15, Mathf.RoundToInt(17f * scale));
            if (_keyStyle != null && _styleSize == fontSize)
                return;

            _keyStyle = new GUIStyle
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            _styleSize = fontSize;
        }

        private static float HudScale()
        {
            return Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.85f, 1.25f);
        }

        private static float CsgoHudScale()
        {
            return Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.67f, 1.35f);
        }
    }
}
