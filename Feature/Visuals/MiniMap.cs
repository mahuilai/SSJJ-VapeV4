using Vape.Cfg;
using Vape.Entity;
using Vape.Render;
using Vape.UI;
using Vape.Utilities;
using UnityEngine;

namespace Vape.Feature.Visuals
{
    public class MiniMap : MonoBehaviour
    {
        private const float Radius = 92f;

        private Vector2 Anchor => new Vector2(Screen.width - Radius - 36f, Radius + 36f);

        private void OnGUI()
        {
            if (!Config.MiniMap || PlayerUpdate.LocalEntity == null || PlayerUpdate.MainCamera == null)
                return;

            DrawFrame();
            DrawEnemyMarkers();
        }

        private void DrawFrame()
        {
            Vector2 c = Anchor;
            // soft fill
            ImmediateRenderer.DrawCircleFilled(c, Radius, Theme.RadarFill, 48);
            ImmediateRenderer.DrawCircleOutline(c, Radius, 64, Theme.RadarRing);
            ImmediateRenderer.DrawCircleOutline(c, Radius * 0.66f, 48, new Color(Theme.RadarRing.r, Theme.RadarRing.g, Theme.RadarRing.b, 0.35f));
            ImmediateRenderer.DrawCircleOutline(c, Radius * 0.33f, 32, new Color(Theme.RadarRing.r, Theme.RadarRing.g, Theme.RadarRing.b, 0.25f));

            // cross
            ImmediateRenderer.DrawLine(c + Vector2.left * Radius, c + Vector2.right * Radius, Theme.RadarCross, 1f);
            ImmediateRenderer.DrawLine(c + Vector2.down * Radius, c + Vector2.up * Radius, Theme.RadarCross, 1f);

            // local player
            ImmediateRenderer.DrawCircleFilled(c, 3.2f, Theme.Accent, 12);
            ImmediateRenderer.DrawString(c + new Vector2(0f, -Radius - 14f), "RADAR", Theme.AccentHot, true, 11);
        }

        private void DrawEnemyMarkers()
        {
            if (PlayerUpdate.EntityList == null) return;

            Vector3 cameraPosition = PlayerUpdate.MainCamera.transform.position;
            float cameraYaw = PlayerUpdate.LocalEntity.ViewPos.y;
            Quaternion radarRotation = Quaternion.AngleAxis(cameraYaw, Vector3.back);
            int localTeam = PlayerUpdate.LocalEntity.Team;
            Vector2 c = Anchor;

            foreach (var enemy in PlayerUpdate.EntityList)
            {
                if (enemy == null || !enemy._entity.hasBasicInfo || enemy.IsDead || enemy.Team == localTeam)
                    continue;

                var feet = enemy.GetPlayerTransform(enemy.PlayerName);
                if (feet == null) continue;

                Vector3 relative = feet.position - cameraPosition;
                Vector2 flat = new Vector2(relative.x, relative.z);
                Vector2 rotated = radarRotation * flat;
                Vector2 scaled = rotated * 0.045f;
                Vector2 clamped = Vector2.ClampMagnitude(scaled, Radius - 8f);
                Vector2 pos = c + clamped;

                float enemyYaw = enemy.ViewPos.y;
                Quaternion enemyRot = Quaternion.AngleAxis(enemyYaw, Vector3.forward);
                Vector3 dir = radarRotation * enemyRot * Vector3.up;

                ImmediateRenderer.DrawCircleFilled(pos, 4.5f, new Color(0f, 0f, 0f, 0.65f), 12);
                ImmediateRenderer.DrawCircleFilled(pos, 3.3f, Theme.RadarEnemy, 12);

                Vector2 tip = pos + new Vector2(dir.x, dir.y) * 9f;
                ImmediateRenderer.DrawLine(pos, tip, Theme.RadarEnemy, 1.5f);
            }
        }
    }
}
