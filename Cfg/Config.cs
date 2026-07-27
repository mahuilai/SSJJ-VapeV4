using UnityEngine;

namespace Vape.Cfg
{
    public class Config
    {
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

        // History Hit
        public static bool HistoryHit;
        public static int HistoryWindow = 200;
        public static bool HistoryPreferLive = true;
        public static bool HistoryNoWall = true;
        public static bool HistoryTrail;
        public static bool HistoryAutoShoot;

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
        public static bool PacketHold;
        public static int PacketHoldTicks = 6;

        // Camera
        public static bool OrbitCam;
        public static KeyCode OrbitKey;
        public static int OrbitFov;
        public static bool LensCustom;
        public static float LensFov;

        // Motion
        public static bool AutoHop = true;
        public static bool AirPath;
        public static bool Airglide;
        public static KeyCode AirPathKey = KeyCode.Mouse4;
        public static bool GhostStep;

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
        public static bool LootTags = true;
        public static bool LootGlow = true;
        public static bool ProjectileTags = true;
        public static bool FieldTags = true;
        public static bool BombClock;

        // Utility
        public static bool AutoSpam;
        public static string SpamText;
        public static bool PathAssist;
    }
}
