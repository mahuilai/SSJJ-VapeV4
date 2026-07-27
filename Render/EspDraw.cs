using Vape.Entity;
using Vape.UI;
using Vape.Utilities;
using SSJJBase.String;
using System.Collections.Generic;
using UnityEngine;

namespace Vape.Render
{
    public class EspDraw
    {
        public static void DrawVerticalHealthBar(Rect targetRect, float healthPercent,
                                   float barWidth = 3.5f, float barSpacing = 3f,
                                   bool onLeft = true)
        {
            healthPercent = Mathf.Clamp01(healthPercent);

            Rect bg = onLeft
                ? new Rect(targetRect.x - barWidth - barSpacing, targetRect.y, barWidth, targetRect.height)
                : new Rect(targetRect.x + targetRect.width + barSpacing, targetRect.y, barWidth, targetRect.height);

            ImmediateRenderer.DrawBoxFilled(bg, Theme.HpBack);
            ImmediateRenderer.DrawBoxOutline(bg, new Color(0f, 0f, 0f, 0.9f), 1f);

            float fillH = bg.height * healthPercent;
            // fill from bottom
            Rect fill = new Rect(bg.x + 0.5f, bg.y + (bg.height - fillH), bg.width - 1f, fillH);
            Color hp = Theme.LerpHp(healthPercent);
            ImmediateRenderer.DrawGradientBar(fill, hp, new Color(hp.r * 0.55f, hp.g * 0.55f, hp.b * 0.55f, hp.a));

            // tip marker
            if (fillH > 2f)
            {
                ImmediateRenderer.DrawBoxFilled(new Rect(fill.x - 1f, fill.y, fill.width + 2f, 2f), Color.white);
            }
        }

        public static void DrawSkeleton(PlayerInfo enemy, Color color, float thickness = 1.35f)
        {
            if (enemy == null) return;

            Dictionary<IgnoreCaseString, Vector3> boneScreenPositions = new Dictionary<IgnoreCaseString, Vector3>();
            var allTransforms = enemy.GetPlayerAllTransform();
            if (allTransforms == null) return;

            foreach (var bonePair in allTransforms)
            {
                if (bonePair.Value == null) continue;
                Vector3 worldPos = bonePair.Value.position;
                Vector3 screenPos = ViewportUtility.WorldPointToScreenPoint(worldPos);
                boneScreenPositions[bonePair.Key] = screenPos;
            }

            const string Pelvis = "Bip01_Pelvis";
            const string Spine = "Bip01_Spine";
            const string Spine1 = "Bip01_Spine1";
            const string Spine2 = "Bip01_Spine2";
            const string Neck = "Bip01_Neck";
            const string Head = "Bip01_Head";
            const string LThigh = "Bip01_L_Thigh";
            const string LCalf = "Bip01_L_Calf";
            const string LFoot = "Bip01_L_Foot";
            const string RThigh = "Bip01_R_Thigh";
            const string RCalf = "Bip01_R_Calf";
            const string RFoot = "Bip01_R_Foot";
            const string LClavicle = "Bip01_L_Clavicle";
            const string LUpperArm = "Bip01_L_UpperArm";
            const string LForearm = "Bip01_L_Forearm";
            const string LHand = "Bip01_L_Hand";
            const string RClavicle = "Bip01_R_Clavicle";
            const string RUpperArm = "Bip01_R_UpperArm";
            const string RForearm = "Bip01_R_Forearm";
            const string RHand = "Bip01_R_Hand";

            Color outline = new Color(0f, 0f, 0f, color.a * 0.75f);
            void Link(string a, string b)
            {
                DrawBoneConnection(boneScreenPositions, a, b, outline, thickness + 1.1f);
                DrawBoneConnection(boneScreenPositions, a, b, color, thickness);
            }

            Link(Pelvis, Spine); Link(Spine, Spine1); Link(Spine1, Spine2); Link(Spine2, Neck); Link(Neck, Head);
            Link(Pelvis, LThigh); Link(LThigh, LCalf); Link(LCalf, LFoot);
            Link(Pelvis, RThigh); Link(RThigh, RCalf); Link(RCalf, RFoot);
            Link(Spine2, LClavicle); Link(LClavicle, LUpperArm); Link(LUpperArm, LForearm); Link(LForearm, LHand);
            Link(Spine2, RClavicle); Link(RClavicle, RUpperArm); Link(RUpperArm, RForearm); Link(RForearm, RHand);

            if (enemy.Career == "rpg_by_parasitism")
            {
                Transform bone05 = enemy.GetPlayerTransform("Bone05");
                if (bone05 != null)
                {
                    Vector3 bone05ScreenPos = ViewportUtility.WorldPointToScreenPoint(bone05.position);
                    Vector2 bone05Pos = new Vector2(bone05ScreenPos.x, Screen.height - bone05ScreenPos.y);
                    ImmediateRenderer.DrawCircleFilled(bone05Pos, 4.5f, color, 14);
                }
            }
            else if (boneScreenPositions.TryGetValue(Head, out Vector3 headScreenPos) &&
                     enemy.GetPlayerTransform("Bip01_Head")?.GetChild(0) != null)
            {
                Vector3 headNubScreenPos = ViewportUtility.WorldPointToScreenPoint(
                    enemy.GetPlayerTransform("Bip01_Head").GetChild(0).position);
                Vector3 headCenter = (headScreenPos + headNubScreenPos) * 0.5f;
                Vector2 headCenterPos = new Vector2(headCenter.x, Screen.height - headCenter.y);
                float headRadius = Vector3.Distance(headScreenPos, headNubScreenPos) * 0.5f;
                ImmediateRenderer.DrawCircleOutline(headCenterPos, headRadius + 1f, 28, outline);
                ImmediateRenderer.DrawCircleOutline(headCenterPos, headRadius, 28, color);
            }
        }

        private static void DrawBoneConnection(
            Dictionary<IgnoreCaseString, Vector3> boneScreenPositions,
            string boneName1,
            string boneName2,
            Color color,
            float thickness = 1f)
        {
            if (boneScreenPositions.TryGetValue(boneName1, out Vector3 startPos) &&
                boneScreenPositions.TryGetValue(boneName2, out Vector3 endPos))
            {
                Vector2 start = new Vector2(startPos.x, Screen.height - startPos.y);
                Vector2 end = new Vector2(endPos.x, Screen.height - endPos.y);
                ImmediateRenderer.DrawLine(start, end, color, thickness);
            }
        }
    }
}
