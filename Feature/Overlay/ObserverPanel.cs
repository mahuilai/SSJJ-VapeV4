using Vape.Cfg;
using Vape.Render;
using Vape.UI;
using UnityEngine;

namespace Vape.Feature.Overlay
{
    public class ObserverPanel : MonoBehaviour
    {
        private void OnGUI()
        {
            if (!Config.ObserverPanel) return;

            var watchers = Contexts.sharedInstance.battleRoom.playerInfo.ObserverList;
            if (watchers == null || watchers.Count == 0) return;

            float width = 168f;
            float row = 18f;
            float title = 24f;
            float height = title + 8f + watchers.Count * row + 8f;
            float x = 16f;
            float y = Screen.height * 0.5f - height * 0.5f;

            var panel = new Rect(x, y, width, height);
            ImmediateRenderer.DrawPanel(panel, Theme.SpectatorPanel, Theme.Border);
            ImmediateRenderer.DrawString(new Vector2(x + width * 0.5f, y + 5f), "OBSERVERS", Theme.SpectatorTitle, true, 12);

            for (int i = 0; i < watchers.Count; i++)
            {
                float yy = y + title + 4f + i * row;
                ImmediateRenderer.DrawBoxFilled(new Rect(x + 8f, yy + 7f, 4f, 4f), Theme.Accent);
                ImmediateRenderer.DrawString(new Vector2(x + 18f, yy), watchers[i], Theme.SpectatorName, false, 11);
            }
        }
    }
}
