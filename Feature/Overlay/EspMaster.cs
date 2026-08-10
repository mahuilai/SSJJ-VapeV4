using Assets.Sources.Utils.Weapon;
using share;
using Vape.Cfg;
using Vape.Entity;
using Vape.Feature.Automation;
using Vape.Feature.Precision;
using Vape.Render;
using Vape.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vape.Feature.Overlay
{
    public class EspMaster : MonoBehaviour
    {
        private readonly struct TextRule
        {
            public readonly Func<PlayerInfo, bool> IsEnabled;  // 开关判定
            public readonly Func<PlayerInfo, string> GetText;  // 文本内容
            public readonly Func<PlayerInfo, Color?> GetColor; // 文本颜色

            // 构造函数
            public TextRule(Func<PlayerInfo, bool> enabled, Func<PlayerInfo, string> text, Func<PlayerInfo, Color?> color = null)
            {
                IsEnabled = enabled;
                GetText = text;
                GetColor = color;
            }
        }

        // 上方堆叠规则
        private static readonly TextRule[] TopRules = {
            new TextRule(p => Config.EspName, p => p.PlayerName),
            new TextRule(p => Config.EspWeapon, p => GetWeaponDisplayText(p), p => GetWeaponColor(p)),
            new TextRule(p => Config.EspYaw, p => $"YAW {p.ViewYaw:F0}"),
            new TextRule(p => Config.EspPitch, p => $"PIT {p.ViewPitch:F0}"),
            //测试用
            //new TextRule(p => true, p => $"武器ID: {p.CurrentWeaponName}"),
            //new TextRule(p => true, p => $"武器槽位: {p.CurrentWeaponId}"),
            //new TextRule(p => true, p => $"武器类型: {p.WeaponDetailType}"),
            //new TextRule(p => true, p => $"角色模型: {p.Career}")
        };

        // 下方堆叠规则
        private static readonly TextRule[] BottomRules = {
            new TextRule(p => Config.EspHealth, p => {
                string fmt = Mathf.Approximately(p.Hp, Mathf.Round(p.Hp)) ? "F0" : "F2";
                return $"HP {p.Hp.ToString(fmt)}";
            }),
            new TextRule(p => Config.EspDist, p => $"{p.Distance:F0}m"),
            new TextRule(p => Config.EspBomb && p.HasC4, p => "BOMB", p => (Color?)Vape.UI.Theme.Danger),
            new TextRule(enabled: p => PlayerUpdate.LocalEntity != null &&PlayerUpdate.LocalEntity.CurrentWeaponName == "wind_spirit" &&WindSpiritRecall.EnemiesOnPaths != null &&IsPlayerOnPathAssist(p),text: p => "PATH",color: p => (Color?)Vape.UI.Theme.Warning)

        };

        private void OnGUI()
        {
            if (!Config.EspMaster) return;
            if (Event.current.type != EventType.Repaint) return;
            var list = PlayerUpdate.EntityList;
            var local = PlayerUpdate.LocalEntity;
            if (list == null || local == null) return;

            int localTeam = local.Team;
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                var player = list[i];
                if (player == null || player.IsDead || player.Team == localTeam) continue;
                DrawEnemy(player);
            }
        }

        private void DrawEnemy(PlayerInfo player)
        {
            // 计算包围盒
            if (!TryGetBoundingBox(player, out Rect rect, out Color color)) return;

            // 绘制图形
            DrawVisuals(player, rect, color);

            // 绘制堆叠文字
            DrawStackedText(player, rect, color);
        }

        // 包围盒计算
        private bool TryGetBoundingBox(PlayerInfo player, out Rect rect, out Color color)
        {
            rect = default;
            color = Vape.UI.Theme.EnemyHidden;

            // 添加空检查
            Transform feetTransform = player.GetPlayerTransform(player.PlayerName);
            Transform headTransform = player.GetValidHeadNub();

            if (feetTransform == null || headTransform == null)
                return false;

            Vector3 screenFeet = ViewportUtility.WorldPointToScreenPoint(feetTransform.position);
            Vector3 screenHead = ViewportUtility.WorldPointToScreenPoint(headTransform.position);

            if (!ViewportUtility.IsScreenPointVisible(screenFeet))
                return false;

            float height = Mathf.Abs(screenHead.y - screenFeet.y);
            float width = height / 2.3f;
            Vector2 center = (screenHead + screenFeet) * 0.5f;

            rect = new Rect(
                center.x - width / 2f - 1f,
                Screen.height - center.y - height / 2f,
                width,
                height
            );

            if (Vape.Feature.Precision.SoftAim._currentTarget?._entity == player._entity)
                color = Vape.UI.Theme.EnemyAim;
            else if (IsVisible(player))
                color = Vape.UI.Theme.EnemyVisible;

            return true;
        }

        // 图形绘制 (方框/血条/骨骼/射线)
        private void DrawVisuals(PlayerInfo player, Rect rect, Color color)
        {
            if (Config.EspBox)
            {
                if (Config.EspBoxStyle == 0)
                    ImmediateRenderer.DrawBoxPro(rect, color, 1.5f);
                else
                    ImmediateRenderer.DrawCornerBoxPro(rect, color, 1.6f, 0.30f);
            }

            if (Config.EspHealthBar)
                EspDraw.DrawVerticalHealthBar(rect, player.HpPercent, 5.3f, 3f);

            if (Config.EspBones)
                EspDraw.DrawSkeleton(player, color, 1.4f);

            if (Config.EspSnap)
            {
                ImmediateRenderer.DrawLine(
                    new Vector2(Screen.width / 2f, Screen.height),
                    new Vector2(rect.center.x, rect.yMax),
                    color, 1.4f
                );
            }
        }

        // 自动堆叠
        private void DrawStackedText(PlayerInfo player, Rect rect, Color defaultColor)
        {
            float centerX = rect.center.x;
            const float lineHeight = 12f;

            // 上方堆叠
            float topY = 0f;
            bool isFirstTop = true;

            foreach (var rule in TopRules)
            {
                if (!rule.IsEnabled(player)) continue;

                if (isFirstTop)
                {
                    topY = Screen.height - rect.yMax - 15f;
                    isFirstTop = false;
                }
                else
                {
                    topY -= lineHeight;
                }

                Color textColor = rule.GetColor != null ? rule.GetColor(player) ?? defaultColor : defaultColor;

                ImmediateRenderer.DrawString(
                    new Vector2(centerX, topY),
                    rule.GetText(player),
                    textColor,
                    true, 10
                );
            }

            // 下方堆叠
            float botY = 0f;
            bool isFirstBot = true;

            foreach (var rule in BottomRules)
            {
                if (!rule.IsEnabled(player)) continue;

                if (isFirstBot)
                {
                    botY = Screen.height - rect.y;
                    isFirstBot = false;
                }
                else
                {
                    botY += lineHeight;
                }

                Color textColor = rule.GetColor != null ? rule.GetColor(player) ?? defaultColor : defaultColor;

                ImmediateRenderer.DrawString(
                    new Vector2(centerX, botY),
                    rule.GetText(player),
                    textColor,
                    true, 10
                );
            }
        }

        // 可见性检测
        private bool IsVisible(PlayerInfo target)
        {
            var forward = SSJJMath.VectorCoordConverter.UnityToSsjj(Camera.main.transform.forward);
            var result = FireUtility.BulletTrace(
                Contexts.sharedInstance.battleRoom.pyEngine.PyEngine,
                PlayerUpdate.LocalEntity._entity,
                Contexts.sharedInstance.player,
                100000f,
                new Vector3D(forward.x, forward.y, forward.z),
                new float[3], new float[3], false
            );
            return result.EntityId == target.Id;
        }

        // 获取武器显示文本
        private static string GetWeaponDisplayText(PlayerInfo player)
        {
            int slotId = player.CurrentWeaponId;
            int weaponType = player.WeaponDetailType;
            string weaponName = player.Weapon;

            // 槽位1：显示武器类型
            if (slotId == 1)
            {
                return GetWeaponTypeName(weaponType, weaponName);
            }
            // 槽位2：副武器
            else if (slotId == 2)
            {
                return $"[SEC]{weaponName}";
            }
            // 槽位3：近战
            else if (slotId == 3)
            {
                return $"[MELEE]{weaponName}";
            }
            // 槽位4：投掷物
            else if (slotId == 4)
            {
                return GetThrowableDisplayText(weaponName);
            }
            // 槽位5：战术
            else if (slotId == 5)
            {
                return $"[TAC]{weaponName}";
            }
            // 其他槽位
            else
            {
                return $"[{slotId}]{weaponName}";
            }
        }

        // 获取武器类型名称
        private static string GetWeaponTypeName(int weaponType, string weaponName)
        {
            switch (weaponType)
            {
                case 0:
                    return $"[PST]{weaponName}";
                case 1:
                    return $"[AR]{weaponName}";
                case 2:
                    return $"[MELEE]{weaponName}";
                case 3:
                    return $"[NADE]{weaponName}";
                case 5:
                    return $"[SR]{weaponName}";
                case 6:
                    return $"[SG]{weaponName}";
                case 10:
                    return $"[MG]{weaponName}";
                case 12:
                    return $"[SMG]{weaponName}";
                default:
                    return $"[{weaponType}]{weaponName}";
            }
        }

        // 投掷物名字定义
        private static readonly HashSet<string> SpecialThrowables = new HashSet<string>
        {"闪光弹","FLash-X","烟雾弹","雾藤","万象","镇宇","天枢","玉衡","月隐","胡峰","极光","暗蚀"};

        // 获取投掷物显示文本
        private static string GetThrowableDisplayText(string weaponName)
        {
            foreach (string special in SpecialThrowables)
            {
                if (weaponName.Contains(special))
                    return $"[NADE]{weaponName}";
            }
            return $"[FRAG]{weaponName}";
        }

        // 获取投掷物显示颜色
        private static Color? GetWeaponColor(PlayerInfo player)
        {
            if (player.CurrentWeaponId == 4)
            {
                foreach (string special in SpecialThrowables)
                {
                    if (player.Weapon.Contains(special))
                        return null;
                }
                return Vape.UI.Theme.VisualPink;
            }
            return null;
        }

        // 检查玩家是否在任何风铃路径上
        private static bool IsPlayerOnPathAssist(PlayerInfo player)
        {
            if (WindSpiritRecall.EnemiesOnPaths == null || WindSpiritRecall.EnemiesOnPaths.Count == 0)
                return false;

            foreach (var pathEnemies in WindSpiritRecall.EnemiesOnPaths.Values)
            {
                foreach (var enemyOnPath in pathEnemies)
                {
                    if (enemyOnPath.Player._entity == player._entity)
                        return true;
                }
            }

            return false;
        }

    }

}
