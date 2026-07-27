using Vape.Cfg;
using Vape.Render;
using UnityEngine;

namespace Vape.Feature.Visuals
{
    public class C4Timer : MonoBehaviour
    {
        private const int C4_MAX_TIME = 35000;
        private const int BLAST_MAX_TIME = 45000;

        private void OnGUI()
        {
            if (!Config.BombClock) return;

            var gameRule = Contexts.sharedInstance?.gameRule;
            if (gameRule == null || !gameRule.hasC4State) return;

            if (!gameRule.c4State.Active) return;

            int currentTime = gameRule.c4State.Time;
            int maxTime = C4_MAX_TIME;

            var gameModel = Assets.Sources.Free.Data.GameModelLocator.GetInstance()?.GameModel;
            if (gameModel?.RoomData?.RaceType == 8)
            {
                maxTime = BLAST_MAX_TIME;
            }

            int remainingTime = maxTime - currentTime;
            if (remainingTime < 0) remainingTime = 0;

            float remainingSeconds = remainingTime / 1000f;

            float progress = 1f - ((float)currentTime / maxTime);
            if (progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;

            DrawC4Timer(remainingSeconds, progress);
        }

        private void DrawC4Timer(float seconds, float progress)
        {
            float screenCenterX = Screen.width / 2f;

            // 绘制倒计时文本
            float textMarginTop = 150f;

            string timeText = string.Format("{0:F1}s", seconds);
            Vector2 textPos = new Vector2(screenCenterX, textMarginTop);

            Color textColor;
            if (seconds <= 7f) textColor = Vape.UI.Theme.Danger;
            else if (seconds <= 14f) textColor = new Color(1f, 0.4f, 0f);
            else textColor = Vape.UI.Theme.TextPrimary;

            ImmediateRenderer.DrawString(
                textPos,
                timeText,
                textColor,
                true,
                18
            );

            // 绘制进度条-
            float barDistanceFromTop = 25f;
            float glY = Screen.height - barDistanceFromTop;

            float barWidth = 200f;
            float barHeight = 8f;

            Rect barRect = new Rect(
                screenCenterX - barWidth / 2f,
                glY,
                barWidth,
                barHeight
            );

            // 绘制背景
            ImmediateRenderer.DrawBoxFilled(barRect, new Color(0f, 0f, 0f, 0.5f));

            Color barColor;
            if (seconds <= 7f) barColor = Vape.UI.Theme.Danger;
            else if (seconds <= 14f) barColor = new Color(1f, 0.5f, 0f);
            else barColor = Color.Lerp(Vape.UI.Theme.Warning, Vape.UI.Theme.Success, progress);

            // 绘制进度
            Rect fillRect = new Rect(
                barRect.x,
                barRect.y,
                barRect.width * progress,
                barRect.height
            );
            ImmediateRenderer.DrawBoxFilled(fillRect, barColor);

            // 绘制边框
            ImmediateRenderer.DrawBoxOutline(barRect, Vape.UI.Theme.Border, 1f);
        }
    }
}
