using Assets.Sources.Components.Player.UnityObjects;
using Entitas;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Vape.Entity
{
    public class PlayerUpdate : MonoBehaviour
    {
        public static List<PlayerInfo> EntityList = new List<PlayerInfo>(32);
        public static PlayerInfo LocalEntity;
        public static PlayerInfo CameraEntity;
        public static PlayerInfo PredictionEntity;
        public static Camera MainCamera;

        private static readonly FieldInfo CachedFlagField =
            typeof(ThirdPersonUnityObjectsComponent).GetField("_playerCached",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        private static readonly FieldInfo CacheDictField =
            typeof(ThirdPersonUnityObjectsComponent).GetField("_playerCache",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        private IGroup<PlayerEntity> _playerGroup;
        private int _frame;
        private Camera _cachedCam;

        private void Update()
        {
            try
            {
                if (Contexts.sharedInstance?.player == null) return;

                if (_playerGroup == null)
                {
                    _playerGroup = Contexts.sharedInstance.player.GetGroup(PlayerMatcher.AllOf(
                        PlayerMatcher.BasicInfo,
                        PlayerMatcher.ThirdPersonUnityObjects));
                }

                _frame++;
                // Only invalidate transform caches periodically. Doing it every frame
                // forces full bone dictionary rebuilds and tanks FPS.
                if ((_frame & 7) == 0)
                    ResetPlayerTransformCache(_playerGroup);

                RetrievePlayerInfo(_playerGroup);

                // Camera.main is expensive; refresh every few frames
                if (_cachedCam == null || (_frame & 15) == 0)
                    _cachedCam = Camera.main;
                MainCamera = _cachedCam;
            }
            catch (Exception ex)
            {
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Entity] update failed: {ex}");
#else
                _ = ex;
#endif
            }
        }

        private void RetrievePlayerInfo(IGroup<PlayerEntity> playerEntities)
        {
            LocalEntity = null;
            CameraEntity = null;
            PredictionEntity = null;
            EntityList.Clear();

            if (playerEntities == null) return;

            foreach (var player in playerEntities)
            {
                bool special = false;

                if (player.isCameraOwner)
                {
                    CameraEntity = new PlayerInfo(player);
                    special = true;
                }
                if (player.isMyPlayer)
                {
                    LocalEntity = new PlayerInfo(player);
                    special = true;
                }
                if (player.isPrediction)
                {
                    PredictionEntity = new PlayerInfo(player);
                    special = true;
                }

                if (!special)
                    EntityList.Add(new PlayerInfo(player));
            }
        }

        private static void ResetPlayerTransformCache(IGroup<PlayerEntity> playerEntities)
        {
            if (playerEntities == null || CachedFlagField == null) return;

            foreach (PlayerEntity player in playerEntities)
            {
                var comp = player?.thirdPersonUnityObjects;
                if (comp == null) continue;
                CachedFlagField.SetValue(comp, false);
                CacheDictField?.SetValue(comp, null);
            }
        }
    }
}
