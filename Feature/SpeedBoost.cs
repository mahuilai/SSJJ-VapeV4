using Assets.Sources.Components.UserComand;
using Assets.Sources.Utils.Pool;
using SSJJUserCmd;
using System;
using UnityEngine;
using Vape.Cfg;

namespace Vape.Feature
{
    public static class SpeedBoost
    {
        private static bool _keyHeld;
        private static int _randomState;

        public static string LastStatus { get; private set; } = "Idle";
        public static int LastSourceCount { get; private set; }
        public static int LastSendCount { get; private set; }
        public static int MaxSentSeq => 0;
        public static bool HasPending => false;
        public static bool IsFastMode => IsActive;

        public static bool IsActive
        {
            get
            {
                if (!Config.SpeedBoost || BlinkMovement.IsHolding)
                    return false;
                if (Config.SpeedBoostKey == KeyCode.None)
                    return true;
                return _keyHeld;
            }
        }

        public static bool IsBlinkLocalBoostActive => false;
        public static float BlinkSpeedMultiplier =>
            Mathf.Clamp(Config.BlinkSpeedMultiplier, 1f, 4f);

        public static int SequenceStep =>
            Mathf.Clamp(Mathf.FloorToInt(Config.SpeedBoostMultiplier), 1, 30);

        public static int ExtraCommandCount => SequenceStep / 4;

        public static void EnsureDefaults()
        {
            if (Config.SpeedBoostKey == KeyCode.None)
                Config.SpeedBoostKey = KeyCode.N;
            Config.SpeedBoostMultiplier = Mathf.Clamp(Config.SpeedBoostMultiplier, 1f, 30f);
        }

        public static void UpdateHotkey()
        {
            EnsureDefaults();
            _keyHeld = Config.SpeedBoostKey == KeyCode.None ||
                       Input.GetKey(Config.SpeedBoostKey);

            if (!Config.SpeedBoost)
                LastStatus = "Disabled";
            else if (BlinkMovement.IsHolding)
                LastStatus = "Blocked by Freecam";
            else
                LastStatus = IsActive ? $"Active {SequenceStep}x" : "Ready";
        }

        public static void RequestBurst()
        {
            _keyHeld = true;
        }

        public static void Cancel(string status = "Cancelled")
        {
            _keyHeld = false;
            LastStatus = status;
        }

        public static int ApplyFastSaveFields(CommandsComponent commands, UserCmd command)
        {
            EnsureDefaults();
            if (commands == null || command == null)
                return 0;

            int step = IsActive ? SequenceStep : 1;
            if (IsActive)
                commands.Counter += step;

            command.Seq = commands.Counter++;
            LastSourceCount = commands.CommandToSendList?.Count ?? 0;
            return step;
        }

        public static void SaveIntoCommandLists(CommandsComponent commands, UserCmd command)
        {
            if (commands == null || command == null)
                return;

            if (commands.CommandList != null)
            {
                if (commands.CommandList.Count >= 200)
                {
                    UserCmd oldest = commands.CommandList.First.Value;
                    commands.CommandList.RemoveFirst();
                    if (commands.CommandToSendList == null ||
                        !commands.CommandToSendList.Contains(oldest))
                    {
                        UserCmdFactory.Instance().Add(oldest);
                    }
                }
                commands.CommandList.AddLast(command);
            }

            if (commands.CommandToSendList != null)
            {
                if (commands.CommandToSendList.Count >= 5)
                    commands.CommandToSendList.RemoveFirst();
                commands.CommandToSendList.AddLast(command);
                LastSendCount = commands.CommandToSendList.Count;
            }
        }

        public static int NextFrameInterval()
        {
            _randomState = unchecked(_randomState * 1103515245 + 12345);
            uint random = (uint)((_randomState >> 16) & 0x7FFF);
            float center = 6.667f * Mathf.Clamp(Config.SpeedBoostMultiplier, 1f, 30f);
            int minimum = Mathf.Max(1, Mathf.FloorToInt(center * 0.55f));
            int maximum = Mathf.Min(255, Mathf.FloorToInt(center * 1.45f));
            int range = Mathf.Max(2, maximum - minimum + 1);
            return minimum + (int)(random % range);
        }

        public static void ApplyBlinkLocalTiming(UserCmd command)
        {
        }

        public static void OnFastSendCompleted(
            CommandsComponent commands,
            int sentCount,
            int maxSeq)
        {
            LastSendCount = sentCount;
        }

        public static void ResolveMoveInput(UserCmd command, out float forward, out float right)
        {
            forward = command?.MoveForward ?? 0f;
            right = command?.MoveRight ?? 0f;
        }
    }
}
