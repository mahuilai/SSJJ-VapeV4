using Vape.Entity;
using Vape.UI.Menu;
using UnityEngine;
using SSJJPhysics;

namespace Vape.Feature.Overlay
{
    public class VelocityRing : MonoBehaviour
    {
        private GUIStyle _digitalStyle;
        private GUIStyle _subStyle;
        private int _digitalFontSize;
        private int _subFontSize;
        // 用于记录最高速度残留
        private float _peakSpeed = 0f;
        private float _peakFadeTime = 0f;

        private void Start()
        {
            EnsureStyles(1f);
        }

        private void EnsureStyles(float scale)
        {
            int digitalSize = Mathf.Max(28, Mathf.RoundToInt(34f * scale));
            int subSize = Mathf.Max(10, Mathf.RoundToInt(11f * scale));
            if (_digitalStyle != null && _subStyle != null &&
                _digitalFontSize == digitalSize && _subFontSize == subSize)
                return;

            _digitalStyle = new GUIStyle
            {
                fontSize = digitalSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            _subStyle = new GUIStyle
            {
                fontSize = subSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };
            _digitalFontSize = digitalSize;
            _subFontSize = subSize;
        }

        private void OnGUI()
        {
            if (!Vape.Cfg.Config.VelocityRing) return;
            if (PlayerUpdate.LocalEntity == null || PlayerUpdate.LocalEntity.IsDead) return;

            var localPlayer = PlayerUpdate.LocalEntity;
            float currentSpeed = Vape.Utilities.MathUtility.CalculateHorizontalSpeed(localPlayer.Move.Velocity);

            int logicMax = 0;
            if (localPlayer.Move.PyPlayerMove is BasePyPlayerAdapter adapter)
                logicMax = adapter.GetMaxSpeed();
            if (logicMax <= 0) logicMax = 1;

            float physicalMax = logicMax * 1.25f;

            // 更新最高速度记录
            if (currentSpeed > _peakSpeed)
            {
                _peakSpeed = currentSpeed;
                _peakFadeTime = Time.time + 3.0f; // 保持3秒
            }
            else if (Time.time > _peakFadeTime)
            {
                _peakSpeed = Mathf.Lerp(_peakSpeed, currentSpeed, Time.deltaTime * 2f);
            }

            float peakRatio = Mathf.Clamp01(_peakSpeed / physicalMax);
            float currentRatio = Mathf.Clamp01(currentSpeed / physicalMax);
            Color themeColor = GetThemeColor(currentSpeed, logicMax, physicalMax);
            float scale = HudScale();
            EnsureStyles(scale);
            DrawSpeedHud(currentSpeed, physicalMax, currentRatio, peakRatio, themeColor, scale);
        }

        private void DrawSpeedHud(float speed, float limit, float ratio, float peakRatio, Color accent, float scale)
        {
            float size = 122f * scale;
            float csgoScale = CsgoHudScale();
            float anchorBottom = Vape.Cfg.Config.CsgoHud
                ? Screen.height - 112f * csgoScale - 14f * scale
                : Screen.height - 20f * scale;
            if (Vape.Cfg.Config.KeyHud)
                anchorBottom -= 108f * scale + 14f * scale;
            Rect panel = new Rect(20f * scale, anchorBottom - size, size, size);
            Color gold = new Color(0.89f, 0.72f, 0.31f, 1f);
            Color activeColor = Color.Lerp(gold, accent, 0.24f);
            Vector2 center = panel.center;
            float radius = 51f * scale;
            const int count = 28;
            int active = Mathf.CeilToInt(ratio * count);
            for (int i = 0; i < count; i++)
            {
                float angle = -Mathf.PI * 0.5f + i / (float)count * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                bool lit = i < active;
                float dotRadius = (lit ? 2.7f : 1.9f) * scale;
                Color color = lit ? activeColor : new Color(0.82f, 0.88f, 0.92f, 0.20f);
                CardDraw.RoundedRect(new Rect(point.x - dotRadius, point.y - dotRadius,
                    dotRadius * 2f, dotRadius * 2f), color, dotRadius);
            }

            float peakAngle = -Mathf.PI * 0.5f + peakRatio * Mathf.PI * 2f;
            Vector2 peak = center + new Vector2(Mathf.Cos(peakAngle), Mathf.Sin(peakAngle)) * radius;
            float peakRadius = 4.2f * scale;
            CardDraw.RoundedFrame(new Rect(peak.x - peakRadius, peak.y - peakRadius,
                peakRadius * 2f, peakRadius * 2f), gold, Color.white, peakRadius, Mathf.Max(1f, scale));

            GUI.Label(new Rect(panel.x, panel.y + 25f * scale, panel.width, 15f * scale), "SPEED", _subStyle);
            _digitalStyle.normal.textColor = activeColor;
            GUI.Label(new Rect(panel.x, panel.y + 39f * scale, panel.width, 39f * scale),
                Mathf.FloorToInt(speed).ToString("000"), _digitalStyle);
            GUI.Label(new Rect(panel.x, panel.y + 76f * scale, panel.width, 14f * scale),
                "L " + Mathf.FloorToInt(limit) + "  P " + Mathf.FloorToInt(_peakSpeed), _subStyle);
        }

        private static float HudScale()
        {
            return Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.85f, 1.25f);
        }

        private static float CsgoHudScale()
        {
            return Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.67f, 1.35f);
        }

        private Color GetThemeColor(float current, int logicMax, float physicalMax)
        {
            // 当速度达到或接近最大物理极限时变成粉色
            if (current >= physicalMax - 1f) return Vape.UI.Theme.SpeedPeak;

            // 速度超过逻辑上限 (100%) 立即变色
            if (current > logicMax) return Vape.UI.Theme.SpeedBoost;

            return Vape.UI.Theme.SpeedNormal;
        }

    }
}
