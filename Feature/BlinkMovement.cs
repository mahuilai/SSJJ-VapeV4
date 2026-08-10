using System.Collections.Generic;
using SSJJUserCmd;
using UnityEngine;
using Vape.Cfg;

namespace Vape.Feature
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BlinkMovement : MonoBehaviour
    {
        private const int MinimumPacketLimit = 10;
        private const int MaximumPacketLimit = 1200;

        private static bool s_keyHeld;
        private static int s_passThroughFrame = -1;
        private static bool s_hasSoulCameraPosition;
        private static bool s_soulCameraAnchored;
        private static bool s_waitForKeyRelease;
        private static bool s_soulFreecamInitialized;
        private static int s_soulArrivalFrames;
        private static float s_soulAnchorStartedAt;
        private static float s_baseMoveSpeed = 600f;
        private static Vector3 s_soulCameraPosition;
        private static readonly object s_commandLock = new object();
        private static readonly LinkedList<UserCmd> s_pendingCommands = new LinkedList<UserCmd>();
        private static UserCmd s_stagedSoulCommand;

        public static bool IsHolding => Config.BlinkMove && s_keyHeld;
        public static bool IsActive => IsHolding || QueuedPackets > 0 || s_soulCameraAnchored;
        public static bool IsSoulCameraAnchored => s_soulCameraAnchored;
        public static int QueuedCommands
        {
            get
            {
                lock (s_commandLock)
                    return s_pendingCommands.Count;
            }
        }
        public static int QueuedPackets =>
            global::HookManager.BlinkChokedPacketCount + Mathf.CeilToInt(QueuedCommands / 5f);
        public static int MaxPackets => Mathf.Clamp(Config.BlinkMaxPackets, MinimumPacketLimit, MaximumPacketLimit);
        public static string LastStatus { get; private set; } = "Idle";

        private void Update()
        {
            EnsureDefaults();

            if (global::HookManager.IsBlinkPacketDrainRequested)
            {
                global::HookManager.DrainBlinkPackets(Mathf.Clamp(Config.BlinkSyncPacketsPerFrame, 2, 32));
                if (!IsHolding && QueuedPackets == 0 && !s_soulCameraAnchored)
                    LastStatus = "Synced";
            }

            if (!Config.BlinkMove || Config.BlinkMoveKey == KeyCode.None)
            {
                if (s_keyHeld || QueuedPackets > 0)
                    Release("Disabled");
                else
                {
                    s_keyHeld = false;
                    ClearSoulCameraAnchor("Disabled");
                }
                return;
            }

            bool held = Input.GetKey(Config.BlinkMoveKey);
            if (s_waitForKeyRelease)
            {
                if (held)
                {
                    LastStatus = QueuedPackets > 0 ? "Syncing" : "Arriving";
                    return;
                }
                s_waitForKeyRelease = false;
            }

            if (held && !s_keyHeld && s_soulCameraAnchored)
            {
                LastStatus = QueuedPackets > 0 ? "Syncing" : "Arriving";
                return;
            }

            if (held && !s_keyHeld)
            {
                s_keyHeld = true;
                s_passThroughFrame = -1;
                s_hasSoulCameraPosition = false;
                s_soulFreecamInitialized = false;
                s_soulArrivalFrames = 0;
                SpeedBoost.Cancel("Blink");
                LastStatus = "Freecam";
            }
            else if (!held && s_keyHeld)
            {
                Release("Released");
            }
            else if (held)
            {
                LastStatus = "Freecam";
            }
        }

        private static void EnsureDefaults()
        {
            if (Config.BlinkMoveKey == KeyCode.None)
                return;

            Config.BlinkMaxPackets = Mathf.Clamp(
                Config.BlinkMaxPackets,
                MinimumPacketLimit,
                MaximumPacketLimit);
            Config.BlinkSyncPacketsPerFrame = Mathf.Clamp(Config.BlinkSyncPacketsPerFrame, 2, 32);
        }

        private static void Release(string status)
        {
            s_keyHeld = false;
            s_passThroughFrame = -1;
            s_waitForKeyRelease = false;
            ClearSoulCameraAnchor(status);
            LastStatus = status;
        }

        public static void BeginSoulSync(string status)
        {
            if (s_soulCameraAnchored)
                return;

            s_keyHeld = false;
            s_waitForKeyRelease = true;
            s_passThroughFrame = -1;
            s_soulCameraAnchored = s_hasSoulCameraPosition && QueuedPackets > 0;
            s_soulArrivalFrames = 0;
            s_soulAnchorStartedAt = Time.unscaledTime;
            LastStatus = s_soulCameraAnchored ? "Syncing" : status;
            global::HookManager.RequestBlinkPacketDrain();
        }

        public static void RequestActionPassThrough()
        {
            if (!IsHolding)
                return;

            s_passThroughFrame = Time.frameCount;
            LastStatus = "Syncing";
            global::HookManager.RequestChokedPacketFlush();
        }

        public static bool ShouldPassThroughCurrentFrame()
        {
            return s_passThroughFrame >= 0 && Time.frameCount <= s_passThroughFrame;
        }

        public static void NotifyQueueChanged(int count)
        {
            if (IsHolding)
                LastStatus = count > 0 ? "Choking" : "Holding";
            else if (count == 0 && LastStatus == "Syncing" && !s_soulCameraAnchored)
                LastStatus = "Synced";
        }

        public static Vector3 ResolveSoulCameraPosition(Vector3 predictedCameraPosition)
        {
            if (IsHolding)
            {
                if (!s_soulFreecamInitialized)
                {
                    s_soulCameraPosition = predictedCameraPosition;
                    s_soulFreecamInitialized = true;
                    s_hasSoulCameraPosition = true;
                }

                UpdateSoulFreecamPosition();
                s_soulCameraAnchored = false;
                s_soulArrivalFrames = 0;
                return s_soulCameraPosition;
            }

            if (!s_soulCameraAnchored)
                return predictedCameraPosition;

            bool pathSent = QueuedPackets == 0 && !global::HookManager.IsBlinkPacketDrainRequested;
            bool hasAuthoritativeBody = TryGetAuthoritativeBodyPosition(out Vector3 bodyPosition);
            float horizontalDistance = hasAuthoritativeBody
                ? new Vector2(
                    bodyPosition.x - s_soulCameraPosition.x,
                    bodyPosition.y - s_soulCameraPosition.y).magnitude
                : float.MaxValue;
            if (pathSent && horizontalDistance <= 35f)
            {
                s_soulArrivalFrames++;
                if (s_soulArrivalFrames >= 4)
                {
                    ClearSoulCameraAnchor("Synced");
                    return predictedCameraPosition;
                }
            }
            else
            {
                s_soulArrivalFrames = 0;
            }

            if (pathSent && Time.unscaledTime - s_soulAnchorStartedAt >= 3f)
            {
                ClearSoulCameraAnchor("Sync Timeout");
                return predictedCameraPosition;
            }

            LastStatus = pathSent ? "Arriving" : "Syncing";
            return s_soulCameraPosition;
        }

        public static void NotifyBaseMoveSpeed(int speed)
        {
            if (speed > 0)
                s_baseMoveSpeed = speed;
        }

        public static bool CaptureAndSuppressSoulCommand(UserCmd command)
        {
            if (!IsHolding || command == null)
                return false;

            lock (s_commandLock)
                s_stagedSoulCommand = null;

            command.CleanButtonFlag(1 | 2 | 4 | 8 | 32);
            command.MoveForward = 0f;
            command.MoveRight = 0f;
            command.AxisX = 0f;
            command.AxisY = 0f;
            command.NotMove = true;
            LastStatus = "Freecam";
            return true;
        }

        public static void CommitStagedSoulCommand(int sequence)
        {
            bool reachedLimit = false;
            lock (s_commandLock)
            {
                if (s_stagedSoulCommand == null)
                    return;

                s_stagedSoulCommand.Seq = sequence;
                if (s_pendingCommands.Last == null ||
                    s_pendingCommands.Last.Value.Seq != sequence)
                {
                    s_pendingCommands.AddLast(s_stagedSoulCommand);
                }
                s_stagedSoulCommand = null;
                reachedLimit = Mathf.CeilToInt(s_pendingCommands.Count / 5f) >= MaxPackets;
            }

            LastStatus = "Choking";
            if (reachedLimit)
                BeginSoulSync("Limit");
        }

        private static void UpdateSoulFreecamPosition()
        {
            float forwardInput = 0f;
            float rightInput = 0f;
            if (Input.GetKey(KeyCode.W)) forwardInput += 1f;
            if (Input.GetKey(KeyCode.S)) forwardInput -= 1f;
            if (Input.GetKey(KeyCode.D)) rightInput += 1f;
            if (Input.GetKey(KeyCode.A)) rightInput -= 1f;
            if (Mathf.Abs(forwardInput) < 0.01f && Mathf.Abs(rightInput) < 0.01f)
                return;

            float yaw = Contexts.sharedInstance?.worldCamera?.cameraTransform?.Yaw ?? 0f;
            Quaternion rotation = Quaternion.Euler(0f, -yaw, 0f);
            Vector3 direction = rotation * (Vector3.forward * forwardInput + Vector3.right * rightInput);
            direction.y = 0f;
            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            float multiplier = Mathf.Clamp(Config.BlinkSpeedMultiplier, 1f, 4f);
            Vector3 unityPosition = SSJJMath.VectorCoordConverter.SsjjToUnity(s_soulCameraPosition);
            unityPosition += direction * (s_baseMoveSpeed * multiplier * Time.unscaledDeltaTime);
            s_soulCameraPosition = SSJJMath.VectorCoordConverter.UnityToSsjj(unityPosition);
        }

        private static bool TryGetAuthoritativeBodyPosition(out Vector3 position)
        {
            position = Vector3.zero;
            PlayerEntity player = Contexts.sharedInstance?.player?.myPlayerEntity;
            if (player == null || !player.hasFpos)
                return false;

            position = player.GetCompenstatePos(player.fpos.Change.GetPosIndex());
            return true;
        }

        private static void ClearSoulCameraAnchor(string status)
        {
            if (!s_soulCameraAnchored && !s_hasSoulCameraPosition)
                return;

            s_hasSoulCameraPosition = false;
            s_soulCameraAnchored = false;
            s_soulFreecamInitialized = false;
            s_soulArrivalFrames = 0;
            s_soulAnchorStartedAt = 0f;
            LastStatus = status;
        }

        public static bool CaptureCommands(LinkedList<UserCmd> commands)
        {
            if (commands == null || commands.Count == 0)
                return false;

            lock (s_commandLock)
            {
                for (var node = commands.First; node != null; node = node.Next)
                {
                    UserCmd command = node.Value;
                    if (command == null)
                        continue;

                    if (s_pendingCommands.Last != null &&
                        s_pendingCommands.Last.Value.Seq == command.Seq)
                    {
                        continue;
                    }

                    s_pendingCommands.AddLast(CloneCommand(command));
                }
                LastStatus = s_pendingCommands.Count > 0 ? "Choking" : "Holding";
                return Mathf.CeilToInt(s_pendingCommands.Count / 5f) >= MaxPackets;
            }
        }

        public static LinkedList<UserCmd> TakeCommands(LinkedList<UserCmd> currentCommands)
        {
            var result = new LinkedList<UserCmd>();
            lock (s_commandLock)
            {
                while (s_pendingCommands.First != null)
                {
                    result.AddLast(s_pendingCommands.First.Value);
                    s_pendingCommands.RemoveFirst();
                }
            }

            if (currentCommands != null)
            {
                for (var node = currentCommands.First; node != null; node = node.Next)
                {
                    UserCmd command = node.Value;
                    if (command == null)
                        continue;

                    if (result.Last != null && result.Last.Value.Seq == command.Seq)
                        continue;
                    result.AddLast(CloneCommand(command));
                }
            }

            LastStatus = result.Count > 0 ? "Syncing" : (IsHolding ? "Holding" : "Idle");
            return result;
        }

        public static void NotifyCommandsSent(int count, bool success, bool useTcp)
        {
            if (success)
                LastStatus = $"Sent {count} {(useTcp ? "TCP" : "UDP")}";
            else
                LastStatus = "Send Failed";
        }

        private static UserCmd CloneCommand(UserCmd source)
        {
            return new UserCmd
            {
                Seq = source.Seq,
                FrameInterval = source.FrameInterval,
                RenderTime = source.RenderTime,
                Buttons = source.Buttons,
                Weapon = source.Weapon,
                CameraYaw = source.CameraYaw,
                CameraPitch = source.CameraPitch,
                PredicatedOnce = source.PredicatedOnce,
                AxisX = source.AxisX,
                AxisY = source.AxisY,
                RandomSeed = source.RandomSeed,
                MoveForward = source.MoveForward,
                MoveRight = source.MoveRight,
                QButton = source.QButton,
                BagId = source.BagId,
                NotMove = source.NotMove
            };
        }

        public static void Reset(bool discardQueuedPackets)
        {
            s_keyHeld = false;
            s_passThroughFrame = -1;
            s_hasSoulCameraPosition = false;
            s_soulCameraAnchored = false;
            s_waitForKeyRelease = false;
            s_soulFreecamInitialized = false;
            s_soulArrivalFrames = 0;
            s_soulAnchorStartedAt = 0f;
            LastStatus = "Idle";
            if (discardQueuedPackets)
            {
                lock (s_commandLock)
                {
                    s_pendingCommands.Clear();
                    s_stagedSoulCommand = null;
                }
                global::HookManager.ClearChokedPackets();
            }
        }
    }
}
