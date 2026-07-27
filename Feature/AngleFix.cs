using NetData;
using Vape.Cfg;
using Vape.Entity;
using Vape.Feature.Legit;
using System.Collections.Generic;
using UnityEngine;

namespace Vape.Feature
{
    public class AngleFix : MonoBehaviour
    {
        public static Dictionary<int, (float originalPitch, bool isFakeApplied, float lastActualPitch)>
         targetAngleFixDict = new Dictionary<int, (float, bool, float)>();

        public static Dictionary<int, float> _lastExecutionTimes = new Dictionary<int, float>();
        private void Update()
        {
            if (Config.AngleFixRandom)
            {
                RandomPlayerViewPitch();
                return;
            }
            AngleFixAngle();
        }

        private void RandomPlayerViewPitch()
        {
            foreach (var player in PlayerUpdate.EntityList)
            {
                if (player.Team != PlayerUpdate.LocalEntity.Team && isAA(player))
                {
                    int playerId = player.Id;
                    float currentTime = Time.time;
                    if (_lastExecutionTimes.TryGetValue(playerId, out float lastTime) &&
            currentTime - lastTime < 0.05f)
                    {
                        return;
                    }
                    _lastExecutionTimes[playerId] = currentTime;

                    player._entity.basicInfo.Current.ViewPitch = -player._entity.basicInfo.Current.ViewPitch;
                }
            }
        }
        private bool isAA(PlayerInfo playerEntity)
        {
            PlayerEntityData basic = playerEntity._entity.basicInfo.Current;
            if (basic.ViewPitch > 30f || basic.ViewPitch < -30f)
            {
                return true;
            }
            return false;
        }
        private void AngleFixAngle()
        {
            if (!Config.AngleFix && SoftAim._currentTarget != null)
            {
                return;
            }
            if (SoftAim._currentTarget != null)
            {
                bool isKeyDown = Input.GetKey(Config.AngleFixKey);
                bool isKeyUp = Input.GetKeyUp(Config.AngleFixKey);

                int targetID = SoftAim._currentTarget.Id;

                if (!targetAngleFixDict.ContainsKey(targetID))
                {
                    targetAngleFixDict[targetID] = (
                        originalPitch: SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch,
                        isFakeApplied: false,
                        lastActualPitch: SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch
                    );
                }

                var (originalPitch, isFakeApplied, lastActualPitch) = targetAngleFixDict[targetID];

                if (!isFakeApplied)
                {
                    lastActualPitch = SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch;
                }

                if (isKeyDown)
                {
                    if (!isFakeApplied)
                    {
                        originalPitch = SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch;
                        isFakeApplied = true;
                    }

                    SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch = -originalPitch;
                }
                else if (isKeyUp && isFakeApplied)
                {
                    SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch = lastActualPitch;
                    isFakeApplied = false;
                }
                else if (!isKeyDown && isFakeApplied)
                {
                    SoftAim._currentTarget._entity.basicInfo.Current.ViewPitch = -originalPitch;
                }

                targetAngleFixDict[targetID] = (originalPitch, isFakeApplied, lastActualPitch);
            }
        }
    }
}
