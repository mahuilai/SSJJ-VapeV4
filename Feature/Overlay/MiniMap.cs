using Vape.Cfg;
using Vape.Entity;
using Vape.Render;
using Vape.UI;
using Vape.Utilities;
using UnityEngine;

namespace Vape.Feature.Overlay
{
    /// <summary>
    /// 全功能雷达小地图 — 移植并增强自 Aura MiniMapHooks.cs
    /// 新增: 高度层指示器 (低/同/高)、C4携带者图标、SOS状态标记、死亡玩家图标
    /// </summary>
    public class MiniMap : MonoBehaviour
    {
        private const float Radius = 92f;
        // 高度差阈值 (单位: 游戏坐标, 约 2m)
        private const float HeightThresholdHigh = 200f;
        private const float HeightThresholdLow  = -200f;

        private Vector2 Anchor => new Vector2(Screen.width - Radius - 36f, Radius + 36f);

        // 颜色定义
        private static readonly Color ColorHigh     = new Color(0.28f, 0.78f, 1.00f, 0.95f); // 蓝 = 上层
        private static readonly Color ColorSame     = new Color(1.00f, 0.30f, 0.30f, 0.95f); // 红 = 同层
        private static readonly Color ColorLow      = new Color(1.00f, 0.75f, 0.10f, 0.95f); // 黄 = 下层
        private static readonly Color ColorC4       = new Color(1.00f, 0.55f, 0.05f, 1.00f); // 橙 = C4携带
        private static readonly Color ColorSos      = new Color(0.95f, 0.90f, 0.05f, 1.00f); // 黄白 = SOS
        private static readonly Color ColorDead     = new Color(0.45f, 0.45f, 0.45f, 0.60f); // 灰 = 死亡
        private static readonly Color ColorOutline  = new Color(0f, 0f, 0f, 0.70f);

        private void OnGUI()
        {
            if (!Config.MiniMap || Config.CsgoHud || PlayerUpdate.LocalEntity == null || PlayerUpdate.MainCamera == null)
                return;

            DrawFrame();
            DrawAllMarkers();
        }

        private void DrawFrame()
        {
            Vector2 c = Anchor;
            // 背景填充
            ImmediateRenderer.DrawCircleFilled(c, Radius, Theme.RadarFill, 48);
            ImmediateRenderer.DrawCircleOutline(c, Radius, 64, Theme.RadarRing);
            ImmediateRenderer.DrawCircleOutline(c, Radius * 0.66f, 48, new Color(Theme.RadarRing.r, Theme.RadarRing.g, Theme.RadarRing.b, 0.35f));
            ImmediateRenderer.DrawCircleOutline(c, Radius * 0.33f, 32, new Color(Theme.RadarRing.r, Theme.RadarRing.g, Theme.RadarRing.b, 0.25f));

            // 十字线
            ImmediateRenderer.DrawLine(c + Vector2.left * Radius, c + Vector2.right * Radius, Theme.RadarCross, 1f);
            ImmediateRenderer.DrawLine(c + Vector2.down * Radius, c + Vector2.up * Radius, Theme.RadarCross, 1f);

            // 本地玩家中心点 (带方向箭头)
            ImmediateRenderer.DrawCircleFilled(c, 4.0f, Theme.Accent, 12);
            float localYaw = PlayerUpdate.LocalEntity.ViewPos.y;
            Quaternion localRot = Quaternion.AngleAxis(localYaw, Vector3.forward);
            Vector3 localDir = localRot * Vector3.up;
            Vector2 localTip = c + new Vector2(localDir.x, localDir.y) * 10f;
            ImmediateRenderer.DrawLine(c, localTip, Theme.AccentHot, 2f);

            ImmediateRenderer.DrawString(c + new Vector2(0f, -Radius - 14f), "RADAR", Theme.AccentHot, true, 11);
        }

        private void DrawAllMarkers()
        {
            if (PlayerUpdate.EntityList == null) return;

            Vector3 localPos    = PlayerUpdate.LocalEntity.Position;
            float   cameraYaw   = PlayerUpdate.LocalEntity.ViewPos.y;
            Quaternion radarRot = Quaternion.AngleAxis(cameraYaw, Vector3.back);
            int localTeam       = PlayerUpdate.LocalEntity.Team;
            Vector2 c           = Anchor;

            foreach (var player in PlayerUpdate.EntityList)
            {
                if (player == null || !player._entity.hasBasicInfo)
                    continue;

                // 跳过本地玩家自己
                if (player == PlayerUpdate.LocalEntity)
                    continue;

                // 判断是否同队
                bool isAlly = player.Team == localTeam;

                // 获取位置
                var tf = player.GetPlayerTransform(player.PlayerName);
                Vector3 worldPos = tf != null ? tf.position : player.Position;

                Vector3 relative = worldPos - localPos;
                Vector2 flat     = new Vector2(relative.x, relative.z);
                Vector2 rotated  = radarRot * flat;
                Vector2 scaled   = rotated * 0.045f;
                Vector2 clamped  = Vector2.ClampMagnitude(scaled, Radius - 9f);
                Vector2 pos      = c + clamped;

                // 高度差 → 颜色
                float heightDelta = relative.y;
                Color dotColor;
                if (player.IsDead)
                    dotColor = ColorDead;
                else if (isAlly)
                    dotColor = new Color(0.30f, 1.00f, 0.30f, 0.90f); // 绿 = 队友
                else if (heightDelta > HeightThresholdHigh)
                    dotColor = ColorHigh;
                else if (heightDelta < HeightThresholdLow)
                    dotColor = ColorLow;
                else
                    dotColor = ColorSame;

                // 检测 C4 携带者 (通过 SOS 标记字段或武器类型)
                bool isC4Carrier = false;
                try
                {
                    if (player._entity.hasBasicInfo && !player.IsDead)
                    {
                        // 武器类型 5 = C4
                        isC4Carrier = player._entity.currentWeapon?.Weapon == 5;
                    }
                }
                catch { }

                // 检测 SOS 状态 (通过反射, 避免对不同版本 Assembly-CSharp 的编译依赖)
                bool isSos = false;
                try
                {
                    var entityObj = (object)player._entity;
                    var hasSosProp = entityObj.GetType().GetProperty("hasSos");
                    if (hasSosProp != null && (bool)hasSosProp.GetValue(entityObj, null))
                    {
                        var sosProp = entityObj.GetType().GetProperty("sos");
                        if (sosProp != null)
                        {
                            var sosComp = sosProp.GetValue(entityObj, null);
                            if (sosComp != null)
                            {
                                var sosField = sosComp.GetType().GetProperty("Sos");
                                if (sosField != null)
                                    isSos = (bool)sosField.GetValue(sosComp, null);
                            }
                        }
                    }
                }
                catch { }

                // 绘制标记点 (死亡玩家缩小)
                float dotRadius = player.IsDead ? 2.5f : 4.0f;
                ImmediateRenderer.DrawCircleFilled(pos, dotRadius + 1.5f, ColorOutline, 12);
                ImmediateRenderer.DrawCircleFilled(pos, dotRadius, dotColor, 12);

                if (!player.IsDead)
                {
                    // 方向箭头
                    float enemyYaw   = player.ViewPos.y;
                    Quaternion eRot  = Quaternion.AngleAxis(enemyYaw, Vector3.forward);
                    Vector3 eDir     = radarRot * eRot * Vector3.up;
                    Vector2 tip      = pos + new Vector2(eDir.x, eDir.y) * 8f;
                    ImmediateRenderer.DrawLine(pos, tip, dotColor, 1.5f);

                    // 高度指示符 (小三角 ▲/▼)
                    if (heightDelta > HeightThresholdHigh)
                        ImmediateRenderer.DrawString(pos + new Vector2(6f, -4f), "▲", ColorHigh, false, 9);
                    else if (heightDelta < HeightThresholdLow)
                        ImmediateRenderer.DrawString(pos + new Vector2(6f, -4f), "▼", ColorLow, false, 9);

                    // C4 携带者标记
                    if (isC4Carrier)
                    {
                        ImmediateRenderer.DrawCircleOutline(pos, dotRadius + 4f, 12, ColorC4);
                        ImmediateRenderer.DrawString(pos + new Vector2(0f, -dotRadius - 12f), "C4", ColorC4, true, 9);
                    }

                    // SOS 标记
                    if (isSos)
                        ImmediateRenderer.DrawString(pos + new Vector2(0f, dotRadius + 4f), "SOS", ColorSos, true, 9);
                }
            }
        }
    }
}
