using Assets.Sources.Free.Data;
using Vape.Cfg;
using Vape.Entity;
using UnityEngine;

namespace Vape.Feature
{
    public class RecoilStrip : MonoBehaviour
    {
        private void Update()
        {
            if (!Config.RecoilStrip)
                return;

            try
            {
                var player = PlayerUpdate.LocalEntity?._entity;
                if (player == null)
                    return;

                if (player.hasPunchOrientation)
                {
                    player.punchOrientation.PunchPitch = 0f;
                    player.punchOrientation.PunchYaw = 0f;
                }

                if (player.hasPunchSmooth)
                {
                    player.punchSmooth.TempPunchPitch = 0f;
                    player.punchSmooth.TempPunchYaw = 0f;
                }

                var gameModel = GameModelLocator.GetInstance()?.GameModel;
                if (gameModel != null)
                    gameModel.ShakeAngleOffect = Vector3.zero;
            }
            catch
            {
            }
        }
    }
}
