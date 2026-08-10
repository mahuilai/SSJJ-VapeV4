using NetData;
using SSJJUserCmd;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Vape.Feature
{
    public static class ProtocolProbe
    {
        private const int MaxCommandSamples = 8192;
        private const int MaxSnapshotSamples = 4096;

        private static readonly List<CommandSample> Commands =
            new List<CommandSample>(MaxCommandSamples);
        private static readonly List<SnapshotSample> Snapshots =
            new List<SnapshotSample>(MaxSnapshotSamples);

        private static bool _capturing;
        private static int _lastSnapshotSeq = int.MinValue;
        private static float _startedAt;

        public static bool IsCapturing => _capturing;
        public static string LastExportPath { get; private set; } = string.Empty;

        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Start();
            }

            if (!_capturing)
                return;

            CaptureLatestSnapshot();

            if (Input.GetKeyDown(KeyCode.F10))
            {
                StopAndExport();
            }
        }

        public static void RecordCommand(UserCmd command)
        {
            if (!_capturing || command == null || Commands.Count >= MaxCommandSamples)
                return;

            int acknowledgedSnapshot = 0;
            try
            {
                acknowledgedSnapshot = Contexts.sharedInstance?.snapshot?.snapshots?.LatestSnapshotSeqId ?? 0;
            }
            catch
            {
            }

            Commands.Add(new CommandSample
            {
                LocalMilliseconds = ElapsedMilliseconds(),
                Seq = command.Seq,
                RenderTime = command.RenderTime,
                FrameInterval = command.FrameInterval,
                Buttons = command.Buttons,
                BagId = command.BagId,
                Weapon = command.Weapon,
                MoveForward = command.MoveForward,
                MoveRight = command.MoveRight,
                SnapshotAck = acknowledgedSnapshot
            });
        }

        private static void Start()
        {
            Commands.Clear();
            Snapshots.Clear();
            _lastSnapshotSeq = int.MinValue;
            _startedAt = Time.realtimeSinceStartup;
            LastExportPath = string.Empty;
            _capturing = true;
            CaptureLatestSnapshot();
        }

        private static void CaptureLatestSnapshot()
        {
            if (Snapshots.Count >= MaxSnapshotSamples)
                return;

            try
            {
                var snapshot = Contexts.sharedInstance?.snapshot?.snapshots?.LatestSnapshot;
                if (snapshot == null || snapshot.SeqId == _lastSnapshotSeq)
                    return;

                _lastSnapshotSeq = snapshot.SeqId;
                PlayerEntityData self = null;
                if (snapshot.Self != 0)
                {
                    snapshot.PlayerEntityDataDict.TryGetValue(snapshot.Self, out self);
                }

                BaseWeaponData weaponData = null;
                var currentWeapon = Contexts.sharedInstance?.weapon?.currentWeaponEntity;
                if (currentWeapon?.basicInfo?.Data != null)
                {
                    weaponData = currentWeapon.basicInfo.Data;
                }

                Snapshots.Add(new SnapshotSample
                {
                    LocalMilliseconds = ElapsedMilliseconds(),
                    SeqId = snapshot.SeqId,
                    LastCmd = snapshot.LastCmd,
                    ServerTime = snapshot.ServerTime,
                    SelfClientTime = self?.ClientTime ?? 0,
                    CmdSequence = self?.CmdSequence ?? 0,
                    PlayerStateFlag = self?.PlayerStateFlag ?? 0,
                    InvincibleTime = self?.InvincibleTime ?? 0,
                    Hp = self?.Hp ?? 0f,
                    CurrentBagId = self?.CurrentBagId ?? 0,
                    CurrentWeapon = self?.CurrentWeapon ?? 0,
                    WeaponName = weaponData?.WeaponName ?? self?.CurrentWeaponName ?? string.Empty,
                    NextSkillTimer = weaponData?.NextSkillTimer ?? 0L,
                    Power2 = weaponData?.Power2 ?? 0,
                    MaxPower2 = weaponData?.MaxPower2 ?? 0
                });
            }
            catch
            {
            }
        }

        private static void StopAndExport()
        {
            CaptureLatestSnapshot();
            _capturing = false;

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string fileName = "SSJJ_ProtocolProbe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
                string path = Path.Combine(desktop, fileName);
                File.WriteAllText(path, BuildCsv(), new UTF8Encoding(false));
                LastExportPath = path;
            }
            catch
            {
                LastExportPath = string.Empty;
            }
        }

        private static int ElapsedMilliseconds()
        {
            return Mathf.Max(0, Mathf.RoundToInt((Time.realtimeSinceStartup - _startedAt) * 1000f));
        }

        private static string BuildCsv()
        {
            var builder = new StringBuilder(Commands.Count * 96 + Snapshots.Count * 128);
            builder.AppendLine("kind,local_ms,seq,render_time,frame_interval,buttons_hex,bag,weapon,move_forward,move_right,snapshot_ack,snapshot_seq,last_cmd,server_time,self_client_time,cmd_sequence,state_hex,invincible_time,hp,current_bag,current_weapon,weapon_name,next_skill_timer,power2,max_power2");

            foreach (CommandSample sample in Commands)
            {
                builder.Append("cmd,")
                    .Append(sample.LocalMilliseconds).Append(',')
                    .Append(sample.Seq).Append(',')
                    .Append(sample.RenderTime).Append(',')
                    .Append(sample.FrameInterval).Append(',')
                    .Append("0x").Append(sample.Buttons.ToString("X8", CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.BagId).Append(',')
                    .Append(sample.Weapon).Append(',')
                    .Append(sample.MoveForward.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.MoveRight.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.SnapshotAck)
                    .AppendLine(",,,,,,,,,,,,,,");
            }

            foreach (SnapshotSample sample in Snapshots)
            {
                builder.Append("snapshot,")
                    .Append(sample.LocalMilliseconds).Append(",,,,,,,,,,")
                    .Append(sample.SeqId).Append(',')
                    .Append(sample.LastCmd).Append(',')
                    .Append(sample.ServerTime).Append(',')
                    .Append(sample.SelfClientTime).Append(',')
                    .Append(sample.CmdSequence).Append(',')
                    .Append("0x").Append(sample.PlayerStateFlag.ToString("X8", CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.InvincibleTime).Append(',')
                    .Append(sample.Hp.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.CurrentBagId).Append(',')
                    .Append(sample.CurrentWeapon).Append(',')
                    .Append(EscapeCsv(sample.WeaponName)).Append(',')
                    .Append(sample.NextSkillTimer).Append(',')
                    .Append(sample.Power2).Append(',')
                    .Append(sample.MaxPower2)
                    .AppendLine();
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private struct CommandSample
        {
            public int LocalMilliseconds;
            public int Seq;
            public int RenderTime;
            public int FrameInterval;
            public int Buttons;
            public int BagId;
            public int Weapon;
            public float MoveForward;
            public float MoveRight;
            public int SnapshotAck;
        }

        private struct SnapshotSample
        {
            public int LocalMilliseconds;
            public int SeqId;
            public int LastCmd;
            public int ServerTime;
            public int SelfClientTime;
            public int CmdSequence;
            public int PlayerStateFlag;
            public int InvincibleTime;
            public float Hp;
            public int CurrentBagId;
            public int CurrentWeapon;
            public string WeaponName;
            public long NextSkillTimer;
            public int Power2;
            public int MaxPower2;
        }
    }
}
