using Vape.Cfg;
using Vape.Entity;
using Vape.Feature.Precision;
using Vape.Render;
using Vape.UI;
using Vape.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vape.Feature.Overlay
{
    public class StateStrip : MonoBehaviour
    {
        private readonly struct IndicatorRule
        {
            public readonly Func<bool> IsEnabled;
            public readonly Func<string> GetText;
            public readonly Func<Color> GetColor;

            public IndicatorRule(Func<bool> enabled, Func<string> text, Func<Color> color = null)
            {
                IsEnabled = enabled;
                GetText = text;
                GetColor = color ?? (() => Theme.HudText);
            }
        }

        private static readonly IndicatorRule[] Rules =
        {
            new IndicatorRule(
                () => Config.AutoFireDelay && AutoFire.IsActive,
                () => $"AUTO FIRE  {AutoFire.RemainingTime:F1}s",
                () => Theme.Warning),
            new IndicatorRule(
                () => BlinkMovement.IsActive,
                () => "FREECAM",
                () => Theme.Success),
            new IndicatorRule(
                () => Config.HardAim,
                () => "AIM ASSIST",
                () => Theme.Danger),
            new IndicatorRule(
                () => Config.Desync,
                () => "DESYNC",
                () => Theme.Success),
        };

        private void OnGUI()
        {
            if (!Config.StateStrip) return;
            if (PlayerUpdate.LocalEntity == null || PlayerUpdate.LocalEntity.IsDead) return;

            var active = new List<(string text, Color color)>();
            foreach (var rule in Rules)
            {
                if (rule.IsEnabled())
                    active.Add((rule.GetText(), rule.GetColor()));
            }
            if (active.Count == 0) return;

            if (Config.OrbitCam && Vape.Features.Menu.forceThirdPerson)
                DrawWorld(active);
            else
                DrawScreen(active);
        }

        private void DrawScreen(List<(string text, Color color)> items)
        {
            float w = 176f;
            float h = 26f;
            float gap = 7f;
            float x = Screen.width - w - 26f;
            float y = 30f;

            for (int i = 0; i < items.Count; i++)
            {
                var (text, color) = items[i];
                var r = new Rect(x, y + i * (h + gap), w, h);
                ImmediateRenderer.DrawPill(r, Theme.HudPill, color, text, color, 12);
            }
        }

        private void DrawWorld(List<(string text, Color color)> items)
        {
            if (PlayerUpdate.MainCamera == null) return;
            Transform spine = PlayerUpdate.LocalEntity.GetPlayerTransform("Bip01_Spine");
            if (spine == null) return;

            Vector3 sp = ViewportUtility.WorldPointToScreenPoint(spine.position);
            if (!ViewportUtility.IsScreenPointVisible(sp)) return;

            Vector2 center = new Vector2(sp.x - 110f, sp.y);
            float h = 20f;
            float total = items.Count * (h + 4f);
            float startY = center.y - total * 0.5f;

            for (int i = 0; i < items.Count; i++)
            {
                var (text, color) = items[i];
                var r = new Rect(center.x - 60f, startY + i * (h + 4f), 120f, h);
                ImmediateRenderer.DrawPill(r, Theme.HudPill, color, text, color, 11);
            }
        }
    }
}
