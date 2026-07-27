using UnityEngine;

namespace Vape.UI
{
    public static class Theme
    {
        public static Color Accent = new Color(0.30f, 0.64f, 1.00f, 1f);
        public static Color AccentSoft = new Color(0.30f, 0.64f, 1.00f, 0.28f);
        public static Color AccentHot = new Color(0.45f, 0.78f, 1.00f, 1f);
        public static Color Danger = new Color(0.95f, 0.28f, 0.35f, 1f);
        public static Color Success = new Color(0.24f, 0.86f, 0.59f, 1f);
        public static Color Warning = new Color(1.00f, 0.78f, 0.20f, 1f);

        public static Color BgDeep = new Color(0.04f, 0.05f, 0.07f, 0.97f);
        public static Color BgPanel = new Color(0.07f, 0.08f, 0.10f, 0.96f);
        public static Color BgCard = new Color(0.10f, 0.11f, 0.14f, 0.94f);
        public static Color BgHover = new Color(0.15f, 0.17f, 0.21f, 1f);
        public static Color Border = new Color(0.20f, 0.23f, 0.28f, 1f);
        public static Color BorderActive = new Color(0.30f, 0.64f, 1.00f, 0.90f);

        public static Color TextPrimary = new Color(0.94f, 0.95f, 0.97f, 1f);
        public static Color TextSecondary = new Color(0.62f, 0.66f, 0.72f, 1f);
        public static Color TextMuted = new Color(0.42f, 0.46f, 0.52f, 1f);

        public static Color EnemyVisible = new Color(0.98f, 0.32f, 0.38f, 1f);
        public static Color EnemyHidden = new Color(0.25f, 0.90f, 0.62f, 1f);
        public static Color EnemyAim = new Color(1.00f, 0.88f, 0.25f, 1f);
        public static Color EnemyBacktrack = new Color(0.78f, 0.14f, 0.22f, 0.88f);
        public static Color HpHigh = new Color(0.25f, 0.92f, 0.58f, 1f);
        public static Color HpLow = new Color(0.96f, 0.22f, 0.30f, 1f);
        public static Color HpBack = new Color(0.06f, 0.07f, 0.09f, 0.88f);
        public static Color ItemEsp = new Color(0.93f, 0.95f, 0.98f, 1f);
        public static Color MoveEsp = new Color(0.18f, 0.92f, 0.96f, 1f);
        public static Color BuffEsp = new Color(0.86f, 0.38f, 1.00f, 1f);
        public static Color RadarRing = new Color(0.28f, 0.34f, 0.42f, 0.75f);
        public static Color RadarFill = new Color(0.05f, 0.07f, 0.10f, 0.55f);
        public static Color RadarEnemy = new Color(0.30f, 0.78f, 1.00f, 1f);
        public static Color RadarCross = new Color(0.35f, 0.42f, 0.50f, 0.55f);
        public static Color SpectatorTitle = new Color(0.30f, 0.78f, 1.00f, 1f);
        public static Color SpectatorName = new Color(0.90f, 0.92f, 0.95f, 1f);
        public static Color SpectatorPanel = new Color(0.05f, 0.06f, 0.08f, 0.72f);
        public static Color HudText = new Color(0.92f, 0.94f, 0.98f, 1f);
        public static Color HudAccent = new Color(0.30f, 0.64f, 1.00f, 1f);
        public static Color HudPill = new Color(0.08f, 0.10f, 0.14f, 0.82f);
        public static Color SpeedNormal = new Color(0.92f, 0.94f, 0.98f, 1f);
        public static Color SpeedBoost = new Color(0.18f, 0.95f, 0.90f, 1f);
        public static Color SpeedPeak = new Color(1.00f, 0.45f, 0.75f, 1f);
        public static Color Tracer = new Color(0.12f, 0.12f, 0.14f, 0.85f);
        public static Color Crosshair = new Color(0.95f, 0.96f, 0.98f, 0.92f);
        public static Color GlowOuter = new Color(0.30f, 0.64f, 1.00f, 0.18f);

        public static Color LerpHp(float t) => Color.Lerp(HpLow, HpHigh, Mathf.Clamp01(t));
    }
}
