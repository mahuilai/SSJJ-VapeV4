using UnityEngine;

namespace Vape.Cfg
{
    public class Config
    {
        // Interface
        // 0 = English, 1 = Chinese. This is persisted with the rest of the profile.
        public static int Language;

        // Soft Aim
        public static bool SoftAim = true;
        public static KeyCode SoftAimKey = KeyCode.Mouse0;
        public static bool SoftAimVisCheck = true;
        public static bool SoftAimLine;
        public static bool SoftAimFovDraw;
        public static int SoftAimFov;
        public static bool SoftAimSmoothOn;
        public static float SoftAimSmooth = 5f;
        public static int SoftAimBone;

        // Auto Fire
        public static bool AutoFire;
        public static bool AutoFireNoScope;
        public static bool BlockSecondary;
        public static bool RecoilStrip;
        public static bool RecoilSmooth;
        public static bool AutoFireDelay;
        public static float AutoFireHold = 3f;

        // Hard Aim
        public static bool HardAim;
        public static bool HardAimOnKey;
        public static KeyCode HardAimKey;
        public static bool ConePredict;
        public static float Accurary;

        // Angle Fix
        public static bool AngleFix;
        public static bool AngleFixRandom;
        public static KeyCode AngleFixKey;

        // Desync / Packet
        public static bool Desync;
        public static float DesyncPitch;
        public static float DesyncYaw;
        public static int DesyncMode;
        public static float DesyncJitterMin;
        public static float DesyncJitterMax;
        public static int DesyncSpin;

        // Camera
        public static bool OrbitCam;
        public static KeyCode OrbitKey;
        public static int OrbitFov = 90;
        public static bool LensCustom;
        public static float LensFov = 90f;

        // Motion
        // 八向连跳 (Aura 式): 自动起跳 + 空中八向变向, 默认左Alt可改
        public static bool Bhop8Dir;
        public static KeyCode BhopKey = KeyCode.LeftAlt;
        public static int BhopActivationMode; // 0 = Hold, 1 = Toggle
        public static bool Airglide;
        public static bool GhostStep;
        public static bool SpeedBoost;
        public static KeyCode SpeedBoostKey = KeyCode.N;
        public static float SpeedBoostMultiplier = 18f;
        public static int SpeedBoostFrames = 10;
        public static int SpeedBoostFrameInterval = 16;
        public static bool SpeedBoostRewriteRenderTime = true;
        public static bool BlinkMove;
        public static KeyCode BlinkMoveKey = KeyCode.LeftAlt;
        public static int BlinkMaxPackets = 600;
        public static int BlinkSyncPacketsPerFrame = 8;
        public static float BlinkSpeedMultiplier = 2f;

        // ESP core
        public static bool EspMaster = true;
        public static bool EspBox = true;
        public static bool EspDist = true;
        public static bool EspName = true;
        public static bool EspHealthBar = true;
        public static bool EspBomb;
        public static bool EspHealth = true;
        public static bool EspBones;
        public static bool EspWeapon = true;
        public static bool EspSnap;
        public static bool EspYaw;
        public static bool EspPitch;
        public static int EspBoxStyle;
        public static bool EspCube;

        // Vision extras
        public static bool ModelGlow;
        public static bool AntiFlash;
        public static bool HitNumbers = true;
        public static bool ShotPath;
        public static bool ObserverPanel;
        public static bool MiniMap;
        public static bool Reticle;
        public static bool StateStrip = true;
        public static bool VelocityRing = true;
        public static bool KeyHud = true;
        public static bool CsgoHud;
        public static bool PhysxModel;
        public static bool PhysxBlackMap;
        public static float PhysxModelDistance = 120f;
        public static bool LootTags = true;
        public static bool LootGlow = true;
        public static bool ProjectileTags = true;
        public static bool FieldTags = true;
        public static bool BombClock;

        // Utility
        public static bool AutoSpam;
        public static string SpamText;
        public static bool PathAssist;

        // Crouch Assist (蹲跳辅助) — 移植自 Aura
        public static bool CrouchAssist;
        public static KeyCode CrouchAssistKey = KeyCode.LeftControl;

        // Instant Sniper (瞬间开镜) — 移植自 Aura
        public static bool InstantSniper;
        public static KeyCode InstantSniperKey = KeyCode.Mouse3;
    }
}
