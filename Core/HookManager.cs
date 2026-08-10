using Assets.Scripts.QuickRuntimeConsole;
using Assets.Sources.Components.Camera;
using Assets.Sources.Components.Interface.Info.Camera;
using Assets.Sources.Components.Player;
using Assets.Sources.Components.Snapshot;
using Assets.Sources.Components.UserComand;
using Assets.Sources.Config;
using Assets.Sources.Info.Camera.CameraLogic;
using Assets.Sources.Modules.Player.Orientation;
using Assets.Sources.Modules.Ui.UiEventCondition;
using Assets.Sources.Modules.WorldCamera;
using Assets.Sources.Networking.Server;
using Assets.Sources.Snapshots;
using Assets.Sources.Systems.PacketHandle.Handlers;
using Assets.Sources.Systems.UserCommand;
using Assets.Sources.Ui.Model.Common;
using Assets.Sources.Ui.ViewModel.Common;
using Assets.Sources.Utils;
using Assets.Sources.Utils.Player;
using Assets.Sources.Utils.Weapon;
using Assets.Sources.Utils.Playback;
using config;
using I2.Loc;
using Vape.Core.Hook;
using NetData;
using physics;
using share;
using Vape;
using Vape.Cfg;
using Vape.Engine;
using Vape.Entity;
using Vape.Extension;
using Vape.Feature;
using Vape.Feature.Precision;
using Vape.Feature.Overlay;
using Vape.Features;
using Vape.Utilities;
using SSJJBase.Singleton;
using SSJJBase.Utility;
using SSJJMath;
using SSJJNetworking.Packet;
using SSJJPhysics;
using SSJJUserCmd;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using weapon;
using zlib;
using Vector3 = UnityEngine.Vector3;

public class HookManager
{
    private static readonly List<MethodHook> s_monoHooks = new List<MethodHook>();
    private static readonly BindingFlags s_bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    // Shared send queue used by Freecam movement synchronization.
    public static List<UdpPacket> chokedPackets = new List<UdpPacket>();
    private static readonly object s_chokedPacketsLock = new object();
    private static bool isSendingChoked = false;
    private static bool s_flushChokedRequested;
    private static BattleServer s_lastUdpServer;
    private static bool s_sendingBlinkCommandBatch;
    private static bool s_chokedPacketsOwnedByBlink;
    private static bool s_blinkDrainRequested;

    public static int ChokedPacketCount
    {
        get
        {
            lock (s_chokedPacketsLock)
                return chokedPackets.Count;
        }
    }

    public static int BlinkChokedPacketCount
    {
        get
        {
            lock (s_chokedPacketsLock)
                return s_chokedPacketsOwnedByBlink ? chokedPackets.Count : 0;
        }
    }

    public static bool IsBlinkPacketDrainRequested
    {
        get
        {
            lock (s_chokedPacketsLock)
                return s_blinkDrainRequested;
        }
    }

    // 代理方法类 - 存放所有会被MonoHook修改的方法
    public static class OriginalProxies
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void ScreenNow_Original(AbstractCaptureSnapshot self)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void ExclusiveUpdateScreen_Original(ExclusiveCaptureSnapshot self)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void WindowUpdateScreen_Original(WindowCaptureSnapshot self)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void WindowHdcUpdateScreen_Original(WindowHdcCaptureSnapshot self)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static IEnumerator CaptureCamera_Original(CaptureCameraManager self)
        {
            yield break;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void Send_Original(NetEaseCloudManager instance, byte[] bytes, int methodId)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void SetStartupLanguage_Original()
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static int GetPlayerSpeed_Original(IPyPlayerMove playerMove, IPyUserCmd userCmd)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void InterceptNew_Original(PostProcessUserCommandSystem commandSystem, UserCmd userCommand)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void OnAfterPredication_Original(CameraLogicToTransformSystem cameraSystem)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static float GetCurrentCmdYaw_Original(ICameraLogic cameraLogic)
        {
            return 0f;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static float GetCurrentCmdPitch_Original(ICameraLogic cameraLogic)
        {
            return 0f;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static bool IsActive_Original(TpsCameraLogic cameraLogic)
        {
            return false;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void TpsCameraUpdate_Original(TpsCameraLogic cameraLogic)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static short LastCameraYaw_Original(CommandsComponent commandsComponent)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static short LastCameraPitch_Original(CommandsComponent commandsComponent)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void MakeCommand_Original(ComputeUserCommandSystem system, UserCmd command, PlayerEntity player)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void ChangeBag_Original(ComputeUserCommandSystem system, UserCmd command, PlayerEntity player)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void OnPlayback_Original(PlayerOrientationPlabackSystem system)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void OnPredicate_Original(PlayerOrientationPredicationSystem system, PlayerEntity player, IUserCmd command)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void PredictCmdOnCamera_Original(PlayerOrientationPredicationSystem system, PlayerEntity player, IUserCmd command)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static byte[] GetUserCmdBytes_Original(SendUserCommandSystem self, LinkedList<UserCmd> sendCmdList, SnapshotsComponent snapshots, out int datalen, bool isTcp)
        {
            datalen = 0;
            return null;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void SaveUserCmd_Original(SendUserCommandSystem self, CommandsComponent commands, UserCmd command)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void SendUserCommands_Original(SendUserCommandSystem self, CommandsComponent commands, SnapshotsComponent snapshots)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static TraceResult Fire_Original(
            PlayerEntity player,
            WeaponEntity weapon,
            int randomSeed,
            float range,
            float[] spreadX,
            float[] spreadY,
            bool knife = false)
        {
            return default;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void FpsDisplayUpdate_Original(FpsDisplay instance)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void SendUdpData_Original(BattleServer server, int methodId, byte[] data = null)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void HitPlayerHandler_Original(HitPlayerHandler self, GameServerSetupData data)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void FlashExplosionSetData_Original(FlashExplosionModel self, GrenadeExplosionEventEntityData ov, GrenadeExplosionEventEntityData data)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void PackChatMsg_Original(UnityProxyHandler handler, UnityProxyData data, string message)
        {
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public static void QuickRuntimeConsoleUpdate_Original(QuickRuntimeConsole console)
        {
        }
    }

    // 创建Hook方法
    public static bool CreateMonoHook(Type targetType, string originalMethodName, MethodInfo hookMethod, MethodInfo proxyMethod)
    {
        return CreateMonoHook(targetType, originalMethodName, null, hookMethod, proxyMethod);
    }

    public static bool CreateMonoHook(Type targetType, string originalMethodName, Type[] parameterTypes, MethodInfo hookMethod, MethodInfo proxyMethod)
    {
        if (targetType is null || string.IsNullOrEmpty(originalMethodName) || hookMethod is null || proxyMethod is null)
            return false;

        MethodInfo originalMethod = parameterTypes == null
            ? targetType.GetMethod(originalMethodName, s_bindingFlags)
            : targetType.GetMethod(originalMethodName, s_bindingFlags, null, parameterTypes, null);
        if (originalMethod is null)
            return false;

        try
        {
            var hook = new MethodHook(
                targetMethod: originalMethod,
                replacementMethod: hookMethod,
                proxyMethod: proxyMethod,
                data: $"{targetType.Name}.{originalMethodName}"
            );

            hook.Install();
            s_monoHooks.Add(hook);
            return true;
        }
        catch (Exception ex)
        {
            #if Debug_Log
            global::System.Console.WriteLine($"创建Hook失败 ({targetType.Name}.{originalMethodName}): {ex.Message}");
            #endif
            return false;
        }
    }

    // 移除所有Hook
    public static void RemoveAllHooks()
    {
        if (s_monoHooks is null)
            return;

        foreach (MethodHook hook in s_monoHooks)
        {
            try
            {
                if (hook.isHooked)
                {
                    hook.Uninstall();
                }
            }
            catch (Exception ex)
            {
                #if Debug_Log
                global::System.Console.WriteLine($"撤销钩子时出错: {ex.Message}");
                #endif
            }
        }

        s_monoHooks.Clear();
    }

    #region Hook初始化
    public static void StartHook()
    {
        // 反截图相关Hook
        CreateMonoHook(typeof(AbstractCaptureSnapshot), "ScreenNow",
            typeof(HookManager).GetMethod(nameof(ScreenNow_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.ScreenNow_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(ExclusiveCaptureSnapshot), "UpdateScreen",
            typeof(HookManager).GetMethod(nameof(UpdateScreen_Hook1), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.ExclusiveUpdateScreen_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(WindowCaptureSnapshot), "UpdateScreen",
            typeof(HookManager).GetMethod(nameof(UpdateScreen_Hook2), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.WindowUpdateScreen_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(WindowHdcCaptureSnapshot), "UpdateScreen",
            typeof(HookManager).GetMethod(nameof(UpdateScreen_Hook3), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.WindowHdcUpdateScreen_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(CaptureCameraManager), "CaptureCamera",
            typeof(HookManager).GetMethod(nameof(CaptureCamera_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.CaptureCamera_Original), s_bindingFlags)
        );

        // 截图上传Hook
        CreateMonoHook(typeof(NetEaseCloudManager), "Send",
            typeof(HookManager).GetMethod(nameof(Send_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.Send_Original), s_bindingFlags)
        );

        // 本地化相关Hook
        CreateMonoHook(typeof(LocalizationManager), "SelectStartupLanguage",
            typeof(HookManager).GetMethod(nameof(SetStartupLanguage_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.SetStartupLanguage_Original), s_bindingFlags)
        );

        // 玩家移动相关Hook
        CreateMonoHook(typeof(PlayerSpeedUtil), "GetPlayerSpeed",
            typeof(HookManager).GetMethod(nameof(GetPlayerSpeed_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.GetPlayerSpeed_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(PostProcessUserCommandSystem), "InterceptNew",
            typeof(HookManager).GetMethod(nameof(InterceptNew_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.InterceptNew_Original), s_bindingFlags)
        );

        // 相机逻辑相关Hook
        CreateMonoHook(typeof(CameraLogicToTransformSystem), "OnAfterPredication",
            typeof(HookManager).GetMethod(nameof(AfterPrediction_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.OnAfterPredication_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(CameraFunction), "GetCurrentCmdYaw",
            typeof(HookManager).GetMethod(nameof(GetCurrentCmdYaw_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.GetCurrentCmdYaw_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(CameraFunction), "GetCurrentCmdPitch",
            typeof(HookManager).GetMethod(nameof(GetCurrentCmdPitch_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.GetCurrentCmdPitch_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(TpsCameraLogic), "IsActive",
            typeof(HookManager).GetMethod(nameof(IsActive_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.IsActive_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(TpsCameraLogic), "Update",
            typeof(HookManager).GetMethod(nameof(TpsCameraUpdate_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.TpsCameraUpdate_Original), s_bindingFlags)
        );

        // 命令组件相关Hook
        CreateMonoHook(typeof(CommandsComponent), "LastCameraYaw",
            typeof(HookManager).GetMethod(nameof(LastCameraYaw_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.LastCameraYaw_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(CommandsComponent), "LastCameraPitch",
            typeof(HookManager).GetMethod(nameof(LastCameraPitch_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.LastCameraPitch_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(ComputeUserCommandSystem), "MakeCommand",
            typeof(HookManager).GetMethod(nameof(MakeCommand_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.MakeCommand_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(ComputeUserCommandSystem), "ChangeBag", new[] { typeof(UserCmd), typeof(PlayerEntity) },
            typeof(HookManager).GetMethod(nameof(ChangeBag_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.ChangeBag_Original), s_bindingFlags)
        );

        // 玩家朝向相关Hook
        CreateMonoHook(typeof(PlayerOrientationPlabackSystem), "OnPlayback",
            typeof(HookManager).GetMethod(nameof(OnPlayback_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.OnPlayback_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(PlayerOrientationPredicationSystem), "OnPredicate",
            typeof(HookManager).GetMethod(nameof(OnPredicate_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.OnPredicate_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(PlayerOrientationPredicationSystem), "PredictCmdOnCamera",
            typeof(HookManager).GetMethod(nameof(PredictCameraCommand_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.PredictCmdOnCamera_Original), s_bindingFlags)
        );

        // 用户命令发送相关Hook
        CreateMonoHook(typeof(SendUserCommandSystem), "SaveUserCmd", new[] { typeof(CommandsComponent), typeof(UserCmd) },
            typeof(HookManager).GetMethod(nameof(SaveUserCmd_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.SaveUserCmd_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(SendUserCommandSystem), "SendUserCommands", new[] { typeof(CommandsComponent), typeof(SnapshotsComponent) },
            typeof(HookManager).GetMethod(nameof(SendUserCommands_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.SendUserCommands_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(SendUserCommandSystem), "GetUserCmdBytes",
            typeof(HookManager).GetMethod(nameof(GetUserCmdBytes_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.GetUserCmdBytes_Original), s_bindingFlags)
        );
        CreateMonoHook(typeof(FireUtility), "Fire",
            typeof(HookManager).GetMethod(nameof(Fire_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.Fire_Original), s_bindingFlags)
        );

        // 帧率显示相关Hook
        CreateMonoHook(typeof(FpsDisplay), "Update",
            typeof(HookManager).GetMethod(nameof(FpsDisplay_Update_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.FpsDisplayUpdate_Original), s_bindingFlags)
        );

        // 假延迟相关Hook
        CreateMonoHook(typeof(BattleServer), "SendUdpData",
            typeof(HookManager).GetMethod(nameof(SendUdpData_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.SendUdpData_Original), s_bindingFlags)
        );

        // 击中反馈相关Hook
        CreateMonoHook(typeof(HitPlayerHandler), "Handle",
            typeof(HookManager).GetMethod(nameof(HitPlayerHandler_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.HitPlayerHandler_Original), s_bindingFlags)
        );

        // 闪光弹相关Hook
        CreateMonoHook(typeof(FlashExplosionModel), "SetData",
            typeof(HookManager).GetMethod(nameof(FlashExplosion_SetData_Hook), s_bindingFlags),
            typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.FlashExplosionSetData_Original), s_bindingFlags)
        );

        // 聊天消息相关Hook
        //CreateMonoHook(typeof(UnityProxyHandler), "PackChatMsg", typeof(HookManager).GetMethod(nameof(PackChatMsg_Hook), s_bindingFlags),typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.PackChatMsg_Original), s_bindingFlags));

        // 控制台相关Hook
        //CreateMonoHook(typeof(QuickRuntimeConsole), "Update",typeof(HookManager).GetMethod(nameof(QuickRuntimeConsole_Update_Hook), s_bindingFlags),typeof(OriginalProxies).GetMethod(nameof(OriginalProxies.QuickRuntimeConsoleUpdate_Original), s_bindingFlags));
    }
    #endregion

    #region 反截图相关Hook

    private static int _screenshotBlockCount = 0;

    // 基础截图
    public static void ScreenNow_Hook(AbstractCaptureSnapshot self)
    {
        _screenshotBlockCount++;
        // #if Debug_Log
        // global::System.Console.WriteLine($"[反截图] 拦截基础截图请求 (AbstractCaptureSnapshot) - 已拦截 {_screenshotBlockCount} 次");
        // #endif
        return;
    }

    // 全屏模式截图
    public static void UpdateScreen_Hook1(ExclusiveCaptureSnapshot self)
    {
        _screenshotBlockCount++;
        // #if Debug_Log
        // global::System.Console.WriteLine($"[反截图] 拦截全屏模式截图 (ExclusiveCaptureSnapshot) - 已拦截 {_screenshotBlockCount} 次");
        // #endif
        return;
    }

    // 窗口模式截图 (GDI)
    public static void UpdateScreen_Hook2(WindowCaptureSnapshot self)
    {
        _screenshotBlockCount++;
        // #if Debug_Log
        // global::System.Console.WriteLine($"[反截图] 拦截窗口GDI截图 (WindowCaptureSnapshot) - 已拦截 {_screenshotBlockCount} 次");
        // #endif
        return;
    }

    // 底层窗口截图 (HDC/DllImport)
    public static void UpdateScreen_Hook3(WindowHdcCaptureSnapshot self)
    {
        _screenshotBlockCount++;
        // #if Debug_Log
        // global::System.Console.WriteLine($"[反截图] 拦截底层HDC截图 (WindowHdcCaptureSnapshot) - 已拦截 {_screenshotBlockCount} 次");
        // #endif
        return;
    }

    // 防透视检测
    public static IEnumerator CaptureCamera_Hook(CaptureCameraManager self)
    {
        _screenshotBlockCount++;
        // #if Debug_Log
        // global::System.Console.WriteLine($"[反截图] 拦截透视染色检测 (CaptureCameraManager) - 已拦截 {_screenshotBlockCount} 次");
        // #endif
        yield break;
    }
    #endregion

    #region 截图上传相关Hook
    public static void Send_Hook(NetEaseCloudManager instance, byte[] bytes, int methodId)
    {
        try
        {
            // 发送假截图数据
            SendFakeScreenshot(methodId);
        }
        catch (Exception ex)
        {
            // #if Debug_Log
            // global::System.Console.WriteLine($"[反截图] 发送假数据失败: {ex.Message}");
            // #endif
        }
    }

    // 发送假的空白截图数据
    private static void SendFakeScreenshot(int methodId)
    {
        try
        {
            // 创建带有效内容的假截图 (非全零: 渐变+噪声, 熵值接近真实画面)
            byte[] blankScreenshot = CreatePlausibleScreenshot();

            // 获取必要的配置数据
            var roomData = Contexts.sharedInstance?.battleRoom?.roomData?.Data;
            var gameBootConfig = TplManager.Instance?.GameBootConfig;

            if (roomData == null || gameBootConfig == null)
            {
                // #if Debug_Log
                // global::System.Console.WriteLine("[反截图] 无法获取配置数据，跳过发送");
                // #endif
                return;
            }

            // 构建请求字符串
            string requestString = string.Concat(new object[]
            {
            "&platform=", gameBootConfig.Platform,
            "&serverId=", gameBootConfig.ServerId,
            "&uid=", gameBootConfig.UserId,
            "&charId=", gameBootConfig.CharId,
            "&ruleType=", 1,
            "&gamePlugFlag=", 1,
            "&raceType=", roomData.RaceType,
            "&sceneId=", roomData.SceneId
            });

            // 计算 MD5 哈希
            string md5Hash = Md5Utility.GetMD5HashFromFile(
                Encoding.Default.GetBytes(requestString + "adf35b91c956e63f7de79c5513f5823e")
            );

            // 构建数据包
            BinaryDataWriter writer = new BinaryDataWriter();
            WriteString(writer, gameBootConfig.ExternalUrl);
            WriteString(writer, requestString);
            WriteString(writer, md5Hash);
            writer.WriteByteArray(blankScreenshot, 0, blankScreenshot.Length);

            byte[] finalData = writer.GetBytes();

            // 压缩数据
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (ZOutputStream zOutputStream = new ZOutputStream(memoryStream, -1))
                {
                    zOutputStream.Write(finalData, 0, finalData.Length);
                    zOutputStream.finish();
                }

                // 发送到服务器
                Contexts.sharedInstance.battleServer.battleServer.Server.SendTcpMessage(
                    methodId,
                    new ImgData
                    {
                        Content = memoryStream.GetBuffer(),
                        Type = MonoBehaviourSingleton<ScreenShotManager>.Instance.ScreenImageReason
                    }
                );
            }

            // #if Debug_Log
            // global::System.Console.WriteLine($"[反截图] 已发送假数据 (MethodId: {methodId})");
            // #endif
        }
        catch (Exception ex)
        {
            // #if Debug_Log
            // global::System.Console.WriteLine($"[反截图] SendFakeScreenshot 异常: {ex}");
            // #endif
        }
    }

    // 写入字符串到 BinaryDataWriter
    private static void WriteString(BinaryDataWriter writer, string data)
    {
        writer.WriteShort((short)data.Length);
        writer.WriteUtf(data, 0);
    }

    // 生成带有效内容的假截图: 1024x1024x4 RGBA
    // 渐变背景 + 随机色块 + 高频噪声, 熵值接近真实游戏画面 (服务器图像分析无法判定为纯黑/纯白)
    private static byte[] CreatePlausibleScreenshot()
    {
        const int W = 1024, H = 1024;
        byte[] data = new byte[W * H * 4];
        int seed = Environment.TickCount ^ (int)DateTime.UtcNow.Ticks;
        int rng = seed == 0 ? 0x1234567 : seed;

        // 伪随机 (xorshift32)
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int idx = (y * W + x) * 4;

                // 垂直渐变基调 (模拟天空/场景明暗变化)
                float grad = 24f + (y / (float)H) * 60f;

                // 若干随机"物体"色块 (模拟玩家/物体, 低频结构)
                float obj = 0f;
                if ((x % 251) < 3 && (y % 199) < 3) obj = 120f;      // 细网格
                if ((x / 64 + y / 64) % 2 == 0) obj += 8f;           // 棋盘
                if (((x - W / 2) * (x - W / 2) + (y - H / 2) * (y - H / 2)) < 4000)
                    obj += 40f;                                       // 中心光斑

                // 高频噪声
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float noise = (rng & 0xFF) * 0.08f;

                float v = grad + obj + noise;
                if (v > 255f) v = 255f;

                data[idx + 0] = (byte)(v * 0.9f);       // R
                data[idx + 1] = (byte)(v * 0.95f);      // G
                data[idx + 2] = (byte)(v * 0.85f);      // B
                data[idx + 3] = 255;                    // A
            }
        }
        return data;
    }
    #endregion

    #region 本地化相关Hook
    public delegate void SetStartupLanguageDelegate();

    public static void SetStartupLanguage_Hook()
    {
        string savedLanguage = PlayerPrefs.GetString("I2 Language", string.Empty);
        string defaultLanguage = "ChineseSimplified";

        if (defaultLanguage == "ChineseSimplified")
        {
            defaultLanguage = "Chinese (Simplified)";
        }
        else if (defaultLanguage == "ChineseTraditional")
        {
            defaultLanguage = "Chinese (Traditional)";
        }

        if (LocalizationManager.HasLanguage(savedLanguage, true, false))
        {
            LocalizationManager.CurrentLanguage = savedLanguage;
            return;
        }

        string supportedLanguage = LocalizationManager.GetSupportedLanguage(defaultLanguage);
        if (!string.IsNullOrEmpty(supportedLanguage))
        {
            string languageCode = LocalizationManager.GetLanguageCode(supportedLanguage);
            LocalizationManager.SetLanguageAndCode(supportedLanguage, languageCode, false, false);
            return;
        }

        foreach (var source in LocalizationManager.Sources)
        {
            if (source.mLanguages.Count > 0)
            {
                var firstLanguage = source.mLanguages[0];
                LocalizationManager.SetLanguageAndCode(firstLanguage.Name, firstLanguage.Code, false, false);
                return;
            }
        }

        #if Debug_Log

        global::System.Console.WriteLine("在本地化管理器中未找到可用语言");

        #endif
    }
    #endregion

    #region 玩家移动相关Hook
    public static int GetPlayerSpeed_Hook(IPyPlayerMove playerMove, IPyUserCmd userCmd)
    {
        int speed = OriginalProxies.GetPlayerSpeed_Original(playerMove, userCmd);
        PlayerEntity cameraOwner = Contexts.sharedInstance?.player?.cameraOwnerEntity;
        if (cameraOwner?.move?.PyPlayerMove != null &&
            ReferenceEquals(playerMove, cameraOwner.move.PyPlayerMove))
        {
            BlinkMovement.NotifyBaseMoveSpeed(speed);
        }
        return speed;
    }

    public static void InterceptNew_Hook(PostProcessUserCommandSystem commandSystem, UserCmd userCommand)
    {
        if (BlinkMovement.CaptureAndSuppressSoulCommand(userCommand))
            return;

        // Aura/Evi only clear these flags for GhostStep. Bit 2 is also used by
        // the native jump command, so clearing the group for bhop breaks the
        // input-to-command transition and produces an invalid button pattern.
        if (Config.GhostStep)
        {
            if ((userCommand.Buttons & 1) > 0)
            {
                userCommand.CleanButtonFlag(1);
            }
            if ((userCommand.Buttons & 2) > 0)
            {
                userCommand.CleanButtonFlag(2);
            }
            if ((userCommand.Buttons & 4) > 0)
            {
                userCommand.CleanButtonFlag(4);
            }
            if ((userCommand.Buttons & 8) > 0)
            {
                userCommand.CleanButtonFlag(8);
            }
        }
    }
    #endregion

    #region 相机逻辑相关Hook
    public static void AfterPrediction_Hook(CameraLogicToTransformSystem cameraSystem)
    {
        OriginalProxies.OnAfterPredication_Original(cameraSystem);

        WorldCameraContext cameraContext = GetHookField<WorldCameraContext>(cameraSystem, "_worldCameraContext", null);
        if (cameraContext?.cameraTransform != null)
        {
            Vector3 cameraPosition = BlinkMovement.ResolveSoulCameraPosition(
                cameraContext.cameraTransform.position);
            cameraContext.cameraTransform.position = cameraPosition;
        }

        if (!Config.LensCustom)
        {
            return;
        }

        if (cameraContext?.cameraTransform != null)
        {
            cameraContext.cameraTransform.Fov = Config.LensFov > 0f ? Config.LensFov : 90f;
        }
    }

    public static float GetCurrentCmdYaw_Hook(ICameraLogic cameraLogic)
    {
        PlayerEntity localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer != null && !localPlayer.IsDead() && ShouldUseCustomCameraAngles())
        {
            return Contexts.sharedInstance.worldCamera.cameraTransform.Yaw;
        }
        return OriginalProxies.GetCurrentCmdYaw_Original(cameraLogic);
    }

    public static float GetCurrentCmdPitch_Hook(ICameraLogic cameraLogic)
    {
        PlayerEntity localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer != null && !localPlayer.IsDead() && ShouldUseCustomCameraAngles())
        {
            return Contexts.sharedInstance.worldCamera.cameraTransform.Pitch;
        }
        return OriginalProxies.GetCurrentCmdPitch_Original(cameraLogic);
    }

    public static bool IsActive_Hook(TpsCameraLogic cameraLogic)
    {
        bool isThirdPerson = IsThirdPersonEnabled();
        try
        {
            OriginalProxies.IsActive_Original(cameraLogic);
            CameraDataComponent cameraData = Contexts.sharedInstance.worldCamera.cameraData;
            if (cameraData == null)
                return isThirdPerson;

            FovComponent fovComponent = null;
            try
            {
                fovComponent = Contexts.sharedInstance.player.cameraOwnerEntity?.fov;
            }
            catch
            {
            }

            bool isZoomed = fovComponent?.IsZoom() ?? false;
            if (isThirdPerson)
            {
                int orbitFov = Config.OrbitFov > 0 ? Config.OrbitFov : 90;
                cameraData.Fov = isZoomed && fovComponent != null
                    ? fovComponent.Fov
                    : orbitFov;
                cameraData.CameraYawAddValue = GetHookField(cameraLogic, "_yaw", 0f);
                cameraData.CameraPitchAddValue = GetHookField(cameraLogic, "_pitch", 0f) - 5f;

                int frameInterval = Contexts.sharedInstance.time.time.FrameInterval;
                cameraData.TransTime = Mathf.Max(230, cameraData.TransTime + frameInterval);
            }
            else if (Config.LensCustom && !isZoomed)
            {
                cameraData.Fov = Mathf.RoundToInt(Config.LensFov);
            }

            cameraData.IsTps = isThirdPerson;
            return isThirdPerson;
        }
        catch
        {
            return isThirdPerson;
        }
    }

    public static void TpsCameraUpdate_Hook(TpsCameraLogic cameraLogic)
    {
        FovComponent fovComponent = null;
        int originalFov = 90;
        int originalDelayFov = 90;
        bool restoreCustomFov = false;
        try
        {
            Contexts contexts = GetHookField(cameraLogic, "Contexts", Contexts.sharedInstance);
            PlayerEntity myPlayerEntity = contexts?.player?.myPlayerEntity;
            if (myPlayerEntity != null)
            {
                try
                {
                    fovComponent = myPlayerEntity.fov;
                    originalFov = fovComponent.Fov;
                    originalDelayFov = fovComponent.DelayFov;
                    restoreCustomFov = Config.LensCustom && !fovComponent.IsZoom();
                }
                catch
                {
                }
            }

            OriginalProxies.TpsCameraUpdate_Original(cameraLogic);
            PlayerEntity localPlayer = contexts?.player?.myPlayerEntity;
            if (localPlayer is null || localPlayer.IsDead())
            {
                return;
            }

            if (contexts?.worldCamera?.cameraData is null)
            {
                return;
            }

            CameraDataComponent cameraData = contexts.worldCamera.cameraData;
            Vector3 viewOriginPosition = GetHookField(cameraLogic, "_viewOrgPosition", Vector3.zero);
            float cameraYaw = GetHookField(cameraLogic, "_yaw", 0f);
            float cameraPitch = GetHookField(cameraLogic, "_pitch", 0f);
            float cameraDistance = GetHookField(cameraLogic, "_distance", 0f);
            Vector3 cameraEndPos = default;

            if (cameraData.IsTps)
            {
                cameraEndPos = cameraLogic.GetCalculateCameraEndPos(
                    viewOriginPosition,
                    cameraData.CameraYawAddValue,
                    cameraData.CameraPitchAddValue,
                    cameraDistance,
                    10f
                );

                Vector3D forward = new Vector3D();
                Vector3D right = new Vector3D();
                Vector3D up = new Vector3D();

                AngleUtility.AnglesToVectors2(
                    cameraYaw,
                    cameraPitch,
                    forward, right, up
                );

                forward.Normalize();
                right.Normalize();
                up.Normalize();

                right.ScaleBy(50f);

                cameraEndPos = cameraLogic.GetCalculateCameraEndPos(
                    cameraEndPos,
                    cameraData.CameraYawAddValue,
                    0f,
                    50f,
                    10f
                );

                if (myPlayerEntity != null && myPlayerEntity.fov.Fov != cameraData.Fov)
                {
                    myPlayerEntity.fov.Fov = cameraData.Fov;
                    myPlayerEntity.fov.DelayFov = cameraData.Fov;
                }
            }

            if (cameraData.TransTime != 0)
            {
                cameraLogic.InterpolateCamareDeadEndPos(viewOriginPosition, cameraEndPos, cameraData.TransTime);
            }
        }
        catch
        {
        }
        finally
        {
            if (restoreCustomFov && fovComponent != null)
            {
                fovComponent.Fov = originalFov;
                fovComponent.DelayFov = originalDelayFov;
            }
        }
    }

    public delegate float CameraYawDelegate(Func<float> originalMethod);

    public static float GetCameraOwnerYaw_Hook(Func<float> originalMethod)
    {
        PlayerEntity localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer != null && !localPlayer.IsDead() && ShouldUseCustomCameraAngles())
        {
            return Contexts.sharedInstance.worldCamera.cameraTransform.Yaw;
        }

        return originalMethod();
    }

    public static float GetControlEntityYaw_Hook(Func<float> originalMethod)
    {
        PlayerEntity localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer != null && !localPlayer.IsDead() && ShouldUseCustomCameraAngles())
        {
            return UiIEventCondition.Get_cameraOwnerData_Yaw();
        }

        return originalMethod();
    }
    #endregion

    #region 命令组件相关Hook
    public static short LastCameraYaw_Hook(CommandsComponent commandsComponent)
    {
        return GetCameraAngleValue(
            commandsComponent,
            () => Contexts.sharedInstance.worldCamera.cameraTransform.Yaw,
            OriginalProxies.LastCameraYaw_Original
        );
    }

    public static short LastCameraPitch_Hook(CommandsComponent commandsComponent)
    {
        return GetCameraAngleValue(
            commandsComponent,
            () => Contexts.sharedInstance.worldCamera.cameraTransform.Pitch,
            OriginalProxies.LastCameraPitch_Original
        );
    }

    private static short GetCameraAngleValue(
        CommandsComponent commandsComponent,
        Func<float> angleGetter,
        Func<CommandsComponent, short> originalMethod
    )
    {
        PlayerEntity myPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (myPlayer is null || myPlayer.IsDead() || !ShouldUseCustomCameraAngles())
        {
            return originalMethod(commandsComponent);
        }

        float angleValue = angleGetter();
        return (short)(angleValue * 100f);
    }

    public static void MakeCommand_Hook(ComputeUserCommandSystem system, UserCmd command, PlayerEntity player)
    {
        OriginalProxies.MakeCommand_Original(system, command, player);
    }

    private const int MaxBagRetryCommands = 8;
    private static int s_pendingBagId;
    private static int s_remainingBagCommands;

    public static void ChangeBag_Hook(ComputeUserCommandSystem system, UserCmd command, PlayerEntity player)
    {
        OriginalProxies.ChangeBag_Original(system, command, player);
        if (command == null)
            return;

        try
        {
            int bagId = command.BagId;
            if (bagId > 0)
            {
                BlinkMovement.RequestActionPassThrough();
                s_pendingBagId = bagId;
                s_remainingBagCommands = MaxBagRetryCommands;
            }

            if (s_pendingBagId <= 0)
            {
                ClearPendingBagChange();
                return;
            }

            if (s_remainingBagCommands <= 0)
            {
                ClearPendingBagChange();
                return;
            }

            if (player != null &&
                player.hasBasicInfo &&
                player.basicInfo.Current != null &&
                player.basicInfo.Current.CurrentBagId == s_pendingBagId)
            {
                ClearPendingBagChange();
                return;
            }

            command.Weapon = 0;
            command.BagId = s_pendingBagId;
            NotifyPendingBagPacket();
            s_remainingBagCommands--;
        }
        catch
        {
            ClearPendingBagChange();
        }
    }

    private static void ClearPendingBagChange()
    {
        s_pendingBagId = 0;
        s_remainingBagCommands = 0;
    }

    private static bool s_pendingBagPacket;

    private static void NotifyPendingBagPacket()
    {
        s_pendingBagPacket = true;
    }

    private static bool ConsumePendingBagPacket()
    {
        if (!s_pendingBagPacket)
            return false;

        s_pendingBagPacket = false;
        return true;
    }
    #endregion
    
    #region 玩家朝向相关Hook
    public static void OnPlayback_Hook(PlayerOrientationPlabackSystem system)
    {
        OriginalProxies.OnPlayback_Original(system);

        if (!ShouldOverrideLocalOrientation())
            return;

        PlayerEntity localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer is null || localPlayer.IsDead() ||
            Contexts.sharedInstance is null ||
            Contexts.sharedInstance.player.cameraOwnerEntity is null)
            return;

        PlayerEntity cameraOwner = Contexts.sharedInstance.player.cameraOwnerEntity;
        if (cameraOwner is null ||
            cameraOwner.orientation is null ||
            cameraOwner.basicInfo is null ||
            cameraOwner.punchOrientation is null)
            return;

        float visibleYaw = GetVisibleCameraYaw();
        float visiblePitch = GetVisibleCameraPitch();

        cameraOwner.orientation.Pitch = visiblePitch;
        cameraOwner.orientation.Yaw = visibleYaw;
        cameraOwner.orientation.MoveYaw = visibleYaw;
        cameraOwner.orientation.ActThirdMoveInterYaw = visibleYaw;

        if (cameraOwner.basicInfo.Next == null)
            return;

        PlayerEntityData playerData = cameraOwner.basicInfo.Next;
        cameraOwner.punchOrientation.PunchPitch = playerData.PunchPitch;
        cameraOwner.punchOrientation.PunchYaw = playerData.PunchYaw;
    }

    public static void OnPredicate_Hook(PlayerOrientationPredicationSystem predictionSystem, PlayerEntity targetPlayer, IUserCmd userCommand)
    {
        var context = Contexts.sharedInstance;
        if (context?.player?.cameraOwnerEntity?.orientation is null)
        {
            OriginalProxies.OnPredicate_Original(predictionSystem, targetPlayer, userCommand);
            return;
        }

        var localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer != null && !localPlayer.IsDead() && ShouldOverrideLocalOrientation())
        {
            context.player.cameraOwnerEntity.orientation.Pitch = GetVisibleCameraPitch();
            context.player.cameraOwnerEntity.orientation.Yaw = GetVisibleCameraYaw();
        }

        OriginalProxies.OnPredicate_Original(predictionSystem, targetPlayer, userCommand);
    }

    private static bool ShouldOverrideLocalOrientation()
    {
        return IsThirdPersonEnabled();
    }

    private static bool ShouldUseCustomCameraAngles()
    {
        return IsThirdPersonEnabled() || Config.Desync || Desync.IsSilentAiming;
    }

    private static bool IsThirdPersonEnabled()
    {
        return Config.OrbitCam && Menu.forceThirdPerson;
    }

    private static float GetVisibleCameraYaw()
    {
        if (Config.Desync || Desync.IsSilentAiming)
        {
            return Desync.SharedYaw;
        }

        return Contexts.sharedInstance.worldCamera.cameraTransform.Yaw;
    }

    private static float GetVisibleCameraPitch()
    {
        if (Config.Desync || Desync.IsSilentAiming)
        {
            return Desync.SharedPitch;
        }

        return Contexts.sharedInstance.worldCamera.cameraTransform.Pitch;
    }

    private static T GetHookField<T>(object source, string fieldName, T fallback)
    {
        if (source is null)
        {
            return fallback;
        }

        FieldInfo field = source.GetType().GetField(fieldName, s_bindingFlags);
        if (field is null)
        {
            return fallback;
        }

        object value = field.GetValue(source);
        return value is T typedValue ? typedValue : fallback;
    }

    public static void PredictCameraCommand_Hook(PlayerOrientationPredicationSystem predictionSystem, PlayerEntity targetPlayer, IUserCmd userCommand)
    {
        var localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
        if (localPlayer is null || localPlayer.IsDead() || !ShouldUseCustomCameraAngles())
        {
            OriginalProxies.PredictCmdOnCamera_Original(predictionSystem, targetPlayer, userCommand);
        }
    }
    #endregion

    #region 用户命令发送相关Hook
    private static readonly BinaryDataWriter s_commandWriter = new BinaryDataWriter();
    private static readonly BinaryDataWriter s_fastCommandWriter = new BinaryDataWriter();
    private static float s_currentPitch;

    public static void SaveUserCmd_Hook(SendUserCommandSystem sendSystem, CommandsComponent commands, UserCmd command)
    {
        try
        {
            if (BlinkMovement.IsHolding)
            {
                if (SpeedBoost.IsFastMode)
                    SpeedBoost.Cancel("Blink");
                OriginalProxies.SaveUserCmd_Original(sendSystem, commands, command);
            }
            else if (commands == null || command == null || !SpeedBoost.IsActive)
            {
                OriginalProxies.SaveUserCmd_Original(sendSystem, commands, command);
            }
            else
            {
                SpeedBoost.ApplyFastSaveFields(commands, command);
                SpeedBoost.SaveIntoCommandLists(commands, command);
            }
        }
        catch
        {
            OriginalProxies.SaveUserCmd_Original(sendSystem, commands, command);
        }
        finally
        {
            ProtocolProbe.RecordCommand(command);
        }
    }

    public static void SendUserCommands_Hook(SendUserCommandSystem sendSystem, CommandsComponent commands, SnapshotsComponent snapshots)
    {
        try
        {
            if (TryHandleBlinkCommands(sendSystem, commands, snapshots))
                return;
        }
        catch
        {
        }

        OriginalProxies.SendUserCommands_Original(sendSystem, commands, snapshots);
    }

    private static bool TryHandleBlinkCommands(
        SendUserCommandSystem sendSystem,
        CommandsComponent commands,
        SnapshotsComponent snapshots)
    {
        bool hasPending = BlinkMovement.QueuedCommands > 0;
        if (!hasPending)
            return false;

        if (commands?.CommandToSendList == null || snapshots == null)
            return false;

        LinkedList<UserCmd> currentCommands = commands.CommandToSendList;
        BattleServerContext transportContext = GetHookField<BattleServerContext>(
            sendSystem,
            "_battleServerContext",
            null);
        var transportServer = transportContext?.battleServer?.Server;

        if (!hasPending && (transportServer == null || !transportServer.IsDefaultUseTcp))
            return false;

        if (SpeedBoost.IsFastMode)
            SpeedBoost.Cancel("Blink");

        bool urgentAction = HasUrgentBlinkCommand(currentCommands);
        if (urgentAction)
            BlinkMovement.RequestActionPassThrough();

        if (!hasPending)
        {
            return false;
        }

        LinkedList<UserCmd> commandBatch = BlinkMovement.TakeCommands(currentCommands);
        currentCommands.Clear();
        if (commandBatch.Count == 0)
            return false;

        int totalCount = commandBatch.Count;
        bool sent = SendBlinkCommandBatch(sendSystem, commandBatch, snapshots, out bool useTcp);
        int sentCount = totalCount - commandBatch.Count;
        if (commandBatch.Count > 0)
            BlinkMovement.CaptureCommands(commandBatch);
        BlinkMovement.NotifyCommandsSent(sentCount, sent, useTcp);
        return true;
    }

    private static int QueueBlinkUdpCommandPackets(
        SendUserCommandSystem sendSystem,
        BattleServer server,
        LinkedList<UserCmd> commands,
        SnapshotsComponent snapshots)
    {
        if (sendSystem == null || server == null || commands == null || snapshots == null)
            return BlinkChokedPacketCount;

        while (commands.Count > 0)
        {
            var packetCommands = new LinkedList<UserCmd>();
            var node = commands.First;
            for (int i = 0; i < 5 && node != null; i++)
            {
                packetCommands.AddLast(node.Value);
                node = node.Next;
            }

            byte[] data = GetUserCmdBytes_Hook(
                sendSystem,
                packetCommands,
                snapshots,
                out int dataLength,
                false);
            if (data == null || dataLength <= 0)
                break;

            UdpPacket packet = UdpPacket.CreateUdpPacket(server.ConnectionId, 2, data);
            lock (s_chokedPacketsLock)
            {
                if (chokedPackets.Count > 0 && !s_chokedPacketsOwnedByBlink)
                    break;

                s_lastUdpServer = server;
                s_chokedPacketsOwnedByBlink = true;
                chokedPackets.Add(packet);
            }
            NetByteFactory.Instance.AddNormal(data);

            for (int i = 0; i < packetCommands.Count; i++)
                commands.RemoveFirst();
        }

        int count = BlinkChokedPacketCount;
        BlinkMovement.NotifyQueueChanged(count);
        return count;
    }

    private static bool IsBlinkMovementCommand(UserCmd command)
    {
        if (command != null &&
            (command.IsMove ||
             Mathf.Abs(command.MoveForward) > 0.01f ||
             Mathf.Abs(command.MoveRight) > 0.01f))
        {
            return true;
        }

        PlayerEntity cameraOwner = Contexts.sharedInstance?.player?.cameraOwnerEntity;
        if (cameraOwner?.move == null)
            return false;

        return MathUtility.CalculateHorizontalSpeed(cameraOwner.move.Velocity) > 0.1f;
    }

    private static bool SendBlinkCommandBatch(
        SendUserCommandSystem sendSystem,
        LinkedList<UserCmd> commands,
        SnapshotsComponent snapshots,
        out bool useTcp)
    {
        useTcp = false;
        if (sendSystem == null || commands == null || commands.Count == 0 || snapshots == null)
            return false;

        BattleServerContext battleServerContext = GetHookField<BattleServerContext>(
            sendSystem,
            "_battleServerContext",
            null);
        var server = battleServerContext?.battleServer?.Server;
        if (server == null)
            return false;

        // Match the room's active transport. UDP rooms do not consume command packets sent on TCP.
        useTcp = server.IsDefaultUseTcp;
        s_sendingBlinkCommandBatch = true;
        try
        {
            int packetBudget = useTcp
                ? int.MaxValue
                : Mathf.Clamp(Config.BlinkSyncPacketsPerFrame, 2, 32);
            int sentPackets = 0;
            while (commands.Count > 0 && sentPackets < packetBudget)
            {
                var packetCommands = new LinkedList<UserCmd>();
                var node = commands.First;
                for (int i = 0; i < 5 && node != null; i++)
                {
                    packetCommands.AddLast(node.Value);
                    node = node.Next;
                }

                byte[] packet = GetUserCmdBytes_Hook(
                    sendSystem,
                    packetCommands,
                    snapshots,
                    out int dataLength,
                    useTcp);
                if (packet == null || dataLength <= 0)
                    return false;

                if (useTcp)
                    server.SendTcpData(31, packet);
                else
                    server.SendUdpData(2, packet);
                NetByteFactory.Instance.AddNormal(packet);
                sentPackets++;

                for (int i = 0; i < packetCommands.Count; i++)
                    commands.RemoveFirst();
            }
            return sentPackets > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            s_sendingBlinkCommandBatch = false;
        }
    }

    private static bool SendFastCommands(SendUserCommandSystem sendSystem, CommandsComponent commands, SnapshotsComponent snapshots)
    {
        if (sendSystem == null || commands?.CommandToSendList == null || snapshots == null)
            return false;

        BattleServerContext battleServerContext = GetHookField<BattleServerContext>(sendSystem, "_battleServerContext", null);
        var server = battleServerContext?.battleServer?.Server;
        if (server == null)
            return false;

        int sentCount = 0;
        int maxSeq = SpeedBoost.MaxSentSeq;
        for (var node = commands.CommandToSendList.First; node != null; node = node.Next)
        {
            UserCmd command = node.Value;
            if (command == null || command.Seq <= SpeedBoost.MaxSentSeq)
                continue;

            var singleCommand = new LinkedList<UserCmd>();
            singleCommand.AddLast(command);
            byte[] packet = BuildFastCommandPacket(singleCommand, snapshots, out int dataLength);
            if (packet == null || dataLength <= 0)
                continue;

            server.SendTcpData(31, packet);
            NetByteFactory.Instance.AddNormal(packet);
            sentCount++;
            if (command.Seq > maxSeq)
                maxSeq = command.Seq;
        }

        SpeedBoost.OnFastSendCompleted(commands, sentCount, maxSeq);
        return sentCount > 0;
    }

    private static byte[] BuildFastCommandPacket(LinkedList<UserCmd> userCommands, SnapshotsComponent snapshots, out int outputLength)
    {
        outputLength = 0;
        if (userCommands == null || userCommands.Count <= 0 || userCommands.First?.Value == null)
            return null;

        UserCmd command = userCommands.First.Value;
        command.FrameInterval = Mathf.Clamp(Config.SpeedBoostFrameInterval, 1, 255);
        if (Config.SpeedBoostRewriteRenderTime && command.RenderTime <= 0)
            command.RenderTime = Environment.TickCount & 0x7FFFFFFF;

        SpeedBoost.ResolveMoveInput(command, out float forward, out float right);
        command.MoveForward = forward;
        command.MoveRight = right;
        AutoHop(ref command);

        float yaw = command.CameraYaw / 100f;
        float pitch = command.CameraPitch / 100f;
        float moveForward = command.MoveForward;
        float moveRight = command.MoveRight;
        int buttons = command.Buttons;
        bool antiAimApplied = false;
        Desync.ExecuteDesync(
            ref s_currentPitch, command, ref pitch,
            ref yaw, ref moveForward, ref moveRight, ref buttons,
            ref antiAimApplied
        );

        int renderTime = command.RenderTime;

        moveForward = Mathf.Clamp(moveForward, -100f, 100f);
        moveRight = Mathf.Clamp(moveRight, -100f, 100f);

        s_fastCommandWriter.Reset();
        s_fastCommandWriter.WriteByte(0);
        s_fastCommandWriter.WriteByte(0);
        s_fastCommandWriter.WriteInt(command.Seq);
        s_fastCommandWriter.WriteInt(renderTime);
        s_fastCommandWriter.WriteInt(0);
        s_fastCommandWriter.WriteByte(4);
        s_fastCommandWriter.WriteByte(0);
        s_fastCommandWriter.WriteByte((byte)(int)moveForward);
        s_fastCommandWriter.WriteByte((byte)(int)moveRight);
        s_fastCommandWriter.WriteInt(buttons);
        int equipmentData = (command.BagId << 4) | command.Weapon;
        s_fastCommandWriter.WriteByte((byte)equipmentData);
        s_fastCommandWriter.WriteShort((short)(yaw * 100f));
        s_fastCommandWriter.WriteShort((short)(pitch * 100f));
        s_fastCommandWriter.WriteShort(4508);
        s_fastCommandWriter.WriteInt(4452);
        s_fastCommandWriter.WriteShort((short)(yaw * 100f));
        s_fastCommandWriter.WriteInt(0);

        byte[] bytes = s_fastCommandWriter.GetBytes();
        outputLength = bytes != null ? bytes.Length : s_fastCommandWriter.Length;
        return bytes;
    }

    public static byte[] GetUserCmdBytes_Hook(
      SendUserCommandSystem sendSystem,
      LinkedList<UserCmd> userCommands,
      SnapshotsComponent snapshots,
      out int outputLength,
      bool isFirstCommand)
    {
        outputLength = 0;

        bool isDead = Contexts.sharedInstance.player.myPlayerEntity?.IsDead() ?? true;
        if (isDead)
        {
            return OriginalProxies.GetUserCmdBytes_Original(sendSystem, userCommands, snapshots, out outputLength, isFirstCommand);
        }

        if (userCommands.Count == 0) return null;
        if (HasUrgentBlinkCommand(userCommands))
        {
            BlinkMovement.RequestActionPassThrough();
        }

        if (HasBagSwitchCommand(userCommands))
        {
            return OriginalProxies.GetUserCmdBytes_Original(sendSystem, userCommands, snapshots, out outputLength, isFirstCommand);
        }

        var firstCmd = userCommands.First.Value;
        float yaw = 0f, pitch = 0f;
        float moveForward = 0f, moveRight = 0f;
        int buttons = 0;
        bool isSelfMoving = SendUserCommandSystem.Record?.IsSelfMove() ?? true;
        bool antiAimApplied = false;
        Desync.SetPitchAngle(ref pitch);
        AutoHop(ref firstCmd);
        Desync.ExecuteDesync(
                    ref s_currentPitch, firstCmd, ref pitch,
                    ref yaw, ref moveForward, ref moveRight, ref buttons,
                    ref antiAimApplied
                );
        s_commandWriter.Reset();
        if (isFirstCommand)
        {
            s_commandWriter.WriteByte(31);
        }

        bool speedActive = SpeedBoost.IsActive;
        int latency = Math.Min(snapshots.ReceiveSnapshotLatency, 255);
        s_commandWriter.WriteByte((byte)latency);
        s_commandWriter.WriteInt(firstCmd.Seq);
        int renderTime = firstCmd.RenderTime;
        s_commandWriter.WriteInt(renderTime);
        s_commandWriter.WriteInt(snapshots.LatestSnapshotSeqId);

        const int baseFlags = 0x0F | 0x20 | 0x10 | 0x80;
        s_commandWriter.WriteByte(baseFlags);
        int firstFrameInterval = speedActive
            ? SpeedBoost.NextFrameInterval()
            : firstCmd.FrameInterval;
        s_commandWriter.WriteByte((byte)firstFrameInterval);

        s_commandWriter.WriteByte((byte)(isSelfMoving ? moveForward : 0));
        s_commandWriter.WriteByte((byte)(isSelfMoving ? moveRight : 0));


        s_commandWriter.WriteInt(buttons);

        int equipmentData = (firstCmd.BagId << 4) | firstCmd.Weapon;
        s_commandWriter.WriteByte((byte)equipmentData);

        s_commandWriter.WriteShort((short)(yaw * 100f));
        s_commandWriter.WriteShort((short)(pitch * 100f));

        var currentNode = userCommands.First.Next;
        while (currentNode != null)
        {

            var cmd = currentNode.Value;
            AutoHop(ref cmd);
            Desync.ExecuteDesync(
                ref s_currentPitch, cmd, ref pitch,
                ref yaw, ref moveForward, ref moveRight, ref buttons,
                ref antiAimApplied
            );

            int positionMarker = s_commandWriter.Position;
            s_commandWriter.WriteByte(0);

            int finalFlags = speedActive ? 0x3F : 0xBF;
            int commandRenderTime = cmd.RenderTime;
            int commandFrameInterval = speedActive
                ? SpeedBoost.NextFrameInterval()
                : cmd.FrameInterval;
            s_commandWriter.WriteByte((byte)commandFrameInterval);
            s_commandWriter.WriteByte((byte)moveForward);
            s_commandWriter.WriteByte((byte)moveRight);
            s_commandWriter.WriteInt(buttons);

            equipmentData = (cmd.BagId << 4) | cmd.Weapon;
            s_commandWriter.WriteByte((byte)equipmentData);

            s_commandWriter.WriteShort((short)(yaw * 100f));
            s_commandWriter.WriteShort((short)(pitch * 100f));
            if (!speedActive)
                s_commandWriter.WriteInt(commandRenderTime);
            int endPosition = s_commandWriter.Position;
            s_commandWriter.Position = positionMarker;
            s_commandWriter.WriteByte((byte)finalFlags);
            s_commandWriter.Position = endPosition;

            currentNode = currentNode.Next;
        }

        if (speedActive)
        {
            int extraCommands = SpeedBoost.ExtraCommandCount;
            for (int i = 0; i < extraCommands; i++)
            {
                s_commandWriter.WriteByte(0x3F);
                s_commandWriter.WriteByte((byte)firstFrameInterval);
                s_commandWriter.WriteByte((byte)moveForward);
                s_commandWriter.WriteByte((byte)moveRight);
                s_commandWriter.WriteInt(buttons);
                s_commandWriter.WriteByte((byte)equipmentData);
                s_commandWriter.WriteShort((short)(yaw * 100f));
                s_commandWriter.WriteShort((short)(pitch * 100f));
            }
        }

        byte[] resultBuffer = NetByteFactory.Instance.GetOrCreateNormalByte(
            s_commandWriter.Length,
            true
        );
        s_commandWriter.SetBytes(resultBuffer);
        outputLength = resultBuffer.Length;

        return resultBuffer;
    }

    private static bool HasBagSwitchCommand(LinkedList<UserCmd> userCommands)
    {
        if (userCommands == null)
            return false;

        for (var node = userCommands.First; node != null; node = node.Next)
        {
            UserCmd command = node.Value;
            if (command != null && command.BagId > 0)
            {
                NotifyPendingBagPacket();
                return true;
            }
        }

        return false;
    }

    private static bool HasUrgentBlinkCommand(LinkedList<UserCmd> userCommands)
    {
        if (userCommands == null)
            return false;

        for (var node = userCommands.First; node != null; node = node.Next)
        {
            UserCmd command = node.Value;
            if (command != null &&
                (command.IsAttackOn || command.IsSecondaryAttackOn || command.BagId > 0))
            {
                return true;
            }
        }

        return false;
    }

    public static TraceResult Fire_Hook(
        PlayerEntity player,
        WeaponEntity weapon,
        int randomSeed,
        float range,
        float[] spreadX,
        float[] spreadY,
        bool knife = false)
    {
        var weaponInfo = weapon.basicInfo.Info;
        float yaw = player.orientation.Yaw + player.GetPunchYaw() * 2f;
        float pitch = player.orientation.Pitch + player.GetPunchPitch() * 2f;
        if (player.move.ActThirdMove)
        {
            yaw = (float)player.basicInfo.MoveYaw;
        }

        double shotsFiredSpread = FireUtility.CalShotsFiredSpread(
            weaponInfo.ShotsFiredSpreadMin,
            weaponInfo.ShotsFiredSpreadMax,
            weaponInfo.ShotsFiredSpreadTime,
            weapon.attack.ShotsFired,
            weaponInfo.AttackInterval);
        Vector3D forward = ShootingDirUtils.CalculateShotingDir(
            randomSeed,
            yaw,
            pitch,
            weapon.spread.Spread,
            weapon.spread.SpreadScaleY,
            shotsFiredSpread);
        TraceResult result = FireUtility.BulletTrace(
            Contexts.sharedInstance.battleRoom.pyEngine.PyEngine,
            player,
            Contexts.sharedInstance.player,
            range,
            forward,
            spreadX,
            spreadY,
            knife);

        return result;
    }

    #endregion

    #region 帧率显示相关Hook
    private static bool _isFpsTextModified = false;
    private static string _cachedOriginalFpsText = "";

    // 修改帧率显示文本
    public static void FpsDisplay_Update_Hook(FpsDisplay instance)
    {
        // 先调用原始方法
        OriginalProxies.FpsDisplayUpdate_Original(instance);

        // 获取Text组件
        Text textComponent = instance.GetFieldValue<Text>("_text");
        if (textComponent == null) return;

        // 确保只修改一次文本属性
        if (!_isFpsTextModified)
        {
            textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            _isFpsTextModified = true;
        }

        // 检查是否启用显示扩展信息
        bool isDisplayEnabled = instance.GetFieldValue<bool>("flag");
        if (!isDisplayEnabled)
        {
            _cachedOriginalFpsText = "";
            return;
        }

        //缓存原始FPS文本
        string currentText = textComponent.text;
        if (string.IsNullOrEmpty(currentText)) return;

        int customIndex = currentText.IndexOf("\nLuo - ");
        if (customIndex != -1)
        {
            _cachedOriginalFpsText = currentText.Substring(0, customIndex);
        }
        else
        {
            _cachedOriginalFpsText = currentText;
        }

        // 获取地图信息
        string mapInfoText = "";
        try
        {
            var battleRoom = Contexts.sharedInstance?.battleRoom;
            if (battleRoom != null && battleRoom.hasRoomData && battleRoom.roomData.Data != null)
            {
                var roomData = battleRoom.roomData.Data;

                // 主模式ID
                string raceType = roomData.RaceType.ToString();

                // 子模式ID
                string subRaceType = roomData.SubRaceTypeSpecified ? roomData.SubRaceType.ToString() : "未知";

                // 地图ID
                string sceneId = roomData.SceneIdSpecified ? roomData.SceneId.ToString() : "未知";

                // 胜利规则
                string winCondition = roomData.WinConditionSpecified ? roomData.WinCondition.ToString() : "未知";

                // 胜利规则值
                string winScore = roomData.WinScoreSpecified ? roomData.WinScore.ToString() : "未知";

                // 回合时长（不转换）
                string sectionTime = roomData.SectionTimeSpecified ? roomData.SectionTime.ToString() : "未知";

                // 复活时长（不转换）
                string reLiveTime = roomData.ReLiveTimeSpecified ? roomData.ReLiveTime.ToString() : "未知";

                // 房间最大人数
                string playerNum = roomData.PlayerNumSpecified ? roomData.PlayerNum.ToString() : "未知";

                // 是否为匹配模式
                string isMatch = roomData.IsMatchSpecified ? (roomData.IsMatch ? "是" : "否") : "未知";

                // Pve难度
                string pveLevel = roomData.PveLevelSpecified ? roomData.PveLevel.ToString() : "未知";

                // AI难度
                string botLevel = roomData.BotLevelSpecified ? roomData.BotLevel.ToString() : "未知";

                // 房间标识
                string roomId = roomData.RoomIdSpecified ? roomData.RoomId : "未知";

                mapInfoText = $"主模式ID:{raceType} | 子模式ID:{subRaceType} | 地图ID:{sceneId} | 胜利规则:{winCondition} | 胜利规则值:{winScore} | 回合时长:{sectionTime} | 复活时长:{reLiveTime} | 最大人数:{playerNum} | 匹配模式:{isMatch} | Pve难度:{pveLevel} | AI难度:{botLevel} | 房间ID:{roomId}";
            }
        }
        catch { }

        //获取系统时间
        string currentTime = DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss");

        //获取玩家速度和坐标
        string speedText = "未知";
        string gameCoordText = "未知";
        string unityCoordText = "未知";

        try
        {
            var player = Contexts.sharedInstance?.player?.myPlayerEntity;
            if (player != null && player.hasMove)
            {
                // 计算速度
                Vector3 velocity = player.move.Velocity;
                float speed = MathUtility.CalculateHorizontalSpeed(velocity);
                speedText = Mathf.Floor(speed).ToString();

                // 获取游戏坐标
                if (PlayerUpdate.LocalEntity != null)
                {
                    Vector3 gameCoord = PlayerUpdate.LocalEntity.Position;
                    gameCoordText = $"X:{gameCoord.x:F1} Y:{gameCoord.y:F1} Z:{gameCoord.z:F1}";

                    // 转换为Unity坐标
                    Vector3 unityCoord = VectorCoordConverter.SsjjToUnity(gameCoord);
                    unityCoordText = $"X:{unityCoord.x:F1} Y:{unityCoord.y:F1} Z:{unityCoord.z:F1}";
                }
            }
        }
        catch { }

        // 获取武器信息
        string weaponText = "未知";
        try
        {
            if (PlayerUpdate.LocalEntity != null)
            {
                string weaponName = PlayerUpdate.LocalEntity.Weapon;
                string weaponId = PlayerUpdate.LocalEntity.CurrentWeaponName;
                weaponText = $"{weaponName}（{weaponId}）";
            }
        }
        catch{ }

        // Anti-Aim 调试信息
        string aaDebugText = "";
        try
        {
            if (PlayerUpdate.LocalEntity != null)
            {
                // 1. 设置的角度 (Input)
                float setAngle = Config.DesyncPitch;

                // 2. 下发的角度 (Visual - 别人看到的)
                float serverAngle = PlayerUpdate.LocalEntity.ViewPitch;

                // --- 算法推导部分 ---

                // A. 模拟网络层 (Short 溢出)
                int packedInt = (int)(setAngle * 100f);
                short packedShort = (short)packedInt;
                float rawPitch = packedShort / 100f; // 服务器收到的原始值

                // B. 模拟视觉层 (预期下发值)
                float predictedVisual = rawPitch;
                while (predictedVisual > 180f) predictedVisual -= 360f;
                while (predictedVisual < -180f) predictedVisual += 360f;

                // C. 模拟物理层 (Hitbox - 真实判定值)
                // 核心规则：原始值绝对值 > 180 则反转，否则保持
                float predictedHitbox;
                if (Mathf.Abs(rawPitch) > 180f)
                {
                    predictedHitbox = -predictedVisual;
                }
                else
                {
                    predictedHitbox = predictedVisual;
                }
                // 物理引擎钳制
                predictedHitbox = Mathf.Clamp(predictedHitbox, -89f, 89f);

                // D. 判断是否去同步 (Desync)
                bool isDesync = Mathf.Sign(predictedVisual) != Mathf.Sign(predictedHitbox)
                                && Mathf.Abs(predictedVisual - predictedHitbox) > 10f;
                string status = isDesync ? " [去同步√]" : " [同步]";

                aaDebugText = $"设置的角度:{setAngle} | 预期的角度:{predictedVisual} | 下发的角度:{serverAngle} | 判定的角度:{predictedHitbox}{status}";
            }
        }
        catch { }

        //每帧都重新组合文本
        textComponent.text = $"{_cachedOriginalFpsText}\nLuo - {currentTime}\n\n\n\n\n\n\n\n\n\n\n\n\n\n速度：{speedText} | 坐标：{gameCoordText} | 武器：{weaponText}\n{mapInfoText}\n{aaDebugText}";
    }

    #endregion

    #region 假卡相关Hook
    public static void SendUdpData_Hook(BattleServer server, int packetId, byte[] data = null)
    {
        try
        {
            if (s_sendingBlinkCommandBatch)
            {
                OriginalProxies.SendUdpData_Original(server, packetId, data);
                return;
            }

            lock (s_chokedPacketsLock)
                s_lastUdpServer = server;

            PlayerEntity localPlayer = Contexts.sharedInstance.player.myPlayerEntity;
            PlayerEntity cameraOwner = Contexts.sharedInstance.player.cameraOwnerEntity;
            if (localPlayer == null || cameraOwner == null || localPlayer.IsDead())
            {
                if (localPlayer != null && localPlayer.IsDead())
                {
                    BlinkMovement.Reset(true);
                }
                else
                {
                    FlushChokedPackets(server);
                }

                OriginalProxies.SendUdpData_Original(server, packetId, data);
                return;
            }

            bool bagPacket = packetId == 2 && ConsumePendingBagPacket();
            bool passThrough = bagPacket ||
                               BlinkMovement.ShouldPassThroughCurrentFrame();
            if (passThrough || IsChokedPacketFlushRequested())
            {
                FlushChokedPackets(server);
                OriginalProxies.SendUdpData_Original(server, packetId, data);
                return;
            }

            bool blinkDraining = IsBlinkPacketDrainRequested;
            if (blinkDraining && packetId != 2)
            {
                OriginalProxies.SendUdpData_Original(server, packetId, data);
                return;
            }

            bool holdForBlink = blinkDraining && packetId == 2;
            if (!holdForBlink)
            {
                FlushChokedPackets(server);
                OriginalProxies.SendUdpData_Original(server, packetId, data);
                return;
            }

            if (IsSendingChokedPackets())
            {
                OriginalProxies.SendUdpData_Original(server, packetId, data);
                return;
            }

            bool ownerChanged;
            lock (s_chokedPacketsLock)
            {
                ownerChanged = chokedPackets.Count > 0 && s_chokedPacketsOwnedByBlink != holdForBlink;
            }
            if (ownerChanged)
                FlushChokedPackets(server);

            UdpPacket udpPacket = UdpPacket.CreateUdpPacket(server.ConnectionId, packetId, data);
            int queuedCount;
            lock (s_chokedPacketsLock)
            {
                if (chokedPackets.Count == 0)
                    s_chokedPacketsOwnedByBlink = holdForBlink;
                chokedPackets.Add(udpPacket);
                queuedCount = chokedPackets.Count;
            }
            BlinkMovement.NotifyQueueChanged(holdForBlink ? queuedCount : 0);

            if (queuedCount >= BlinkMovement.MaxPackets)
            {
                RequestBlinkPacketDrain();
            }
        }
        catch
        {
            FlushChokedPackets(server);
            OriginalProxies.SendUdpData_Original(server, packetId, data);
        }
    }

    public static void RequestChokedPacketFlush()
    {
        BattleServer server;
        lock (s_chokedPacketsLock)
        {
            s_flushChokedRequested = true;
            s_blinkDrainRequested = false;
            server = s_lastUdpServer;
        }

        if (server != null)
            FlushChokedPackets(server);
    }

    public static void RequestBlinkPacketDrain()
    {
        lock (s_chokedPacketsLock)
        {
            s_blinkDrainRequested = s_chokedPacketsOwnedByBlink && chokedPackets.Count > 0;
        }
    }

    public static void DrainBlinkPackets(int requestedCount)
    {
        BattleServer server;
        lock (s_chokedPacketsLock)
        {
            if (!s_blinkDrainRequested || !s_chokedPacketsOwnedByBlink || chokedPackets.Count == 0)
            {
                s_blinkDrainRequested = false;
                return;
            }
            server = s_lastUdpServer;
        }

        if (server != null)
            SendChokedPacketPrefix(server, Mathf.Clamp(requestedCount, 1, 64));
    }

    public static void ClearChokedPackets()
    {
        lock (s_chokedPacketsLock)
        {
            chokedPackets.Clear();
            s_flushChokedRequested = false;
            isSendingChoked = false;
            s_chokedPacketsOwnedByBlink = false;
            s_blinkDrainRequested = false;
        }
        BlinkMovement.NotifyQueueChanged(0);
    }

    private static bool IsChokedPacketFlushRequested()
    {
        lock (s_chokedPacketsLock)
            return s_flushChokedRequested;
    }

    private static bool IsSendingChokedPackets()
    {
        lock (s_chokedPacketsLock)
            return isSendingChoked;
    }

    private static void FlushChokedPackets(BattleServer server)
    {
        if (server == null || server.UdpSocket == null)
            return;

        lock (s_chokedPacketsLock)
        {
            if (chokedPackets.Count == 0)
            {
                s_flushChokedRequested = false;
                isSendingChoked = false;
                s_chokedPacketsOwnedByBlink = false;
                s_blinkDrainRequested = false;
                return;
            }

            isSendingChoked = true;
            try
            {
                for (int i = 0; i < chokedPackets.Count; i++)
                {
                    UdpPacket packet = chokedPackets[i];
                    if (packet != null && packet.FinalData != null)
                    {
                        try
                        {
                            server.UdpSocket.Send(packet.FinalData, packet.FinalLength);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            finally
            {
                chokedPackets.Clear();
                s_flushChokedRequested = false;
                isSendingChoked = false;
                s_chokedPacketsOwnedByBlink = false;
                s_blinkDrainRequested = false;
            }
        }

        BlinkMovement.NotifyQueueChanged(0);
    }

    public static void SendChokedPacketPrefix(BattleServer server, int requestedCount)
    {
        if (server == null || server.UdpSocket == null || requestedCount <= 0)
            return;

        int remaining;
        lock (s_chokedPacketsLock)
        {
            if (!s_chokedPacketsOwnedByBlink)
            {
                s_blinkDrainRequested = false;
                return;
            }

            int count = Mathf.Clamp(requestedCount, 0, chokedPackets.Count);
            for (int i = 0; i < count; i++)
            {
                UdpPacket packet = chokedPackets[i];
                if (packet == null || packet.FinalData == null)
                    continue;

                try
                {
                    server.UdpSocket.Send(packet.FinalData, packet.FinalLength);
                }
                catch
                {
                }
            }

            if (count > 0)
                chokedPackets.RemoveRange(0, count);
            remaining = chokedPackets.Count;
            if (remaining == 0)
            {
                s_flushChokedRequested = false;
                s_chokedPacketsOwnedByBlink = false;
                s_blinkDrainRequested = false;
            }
        }

        BlinkMovement.NotifyQueueChanged(remaining);
    }
    #endregion

    #region 击中反馈相关Hook
    public static void HitPlayerHandler_Hook(HitPlayerHandler self, GameServerSetupData data)
    {
        OriginalProxies.HitPlayerHandler_Original(self, data);

        // 广播事件
        GlobalEvents.InvokePlayerHit(data);
    }
    #endregion

    #region 无视闪光弹相关Hook
    public static void FlashExplosion_SetData_Hook(FlashExplosionModel self, GrenadeExplosionEventEntityData ov, GrenadeExplosionEventEntityData data)
    {
        // 如果没开启无视闪光弹，正常执行
        if (!Config.AntiFlash)
        {
            OriginalProxies.FlashExplosionSetData_Original(self, ov, data);
            return;
        }

        // 开启无视闪光弹时，强制隐藏闪光效果
        try
        {
            // 获取 _viewModel 私有字段
            Type modelType = typeof(FlashExplosionModel);
            FieldInfo viewModelField = modelType.GetField("_viewModel", BindingFlags.NonPublic | BindingFlags.Instance);

            if (viewModelField != null)
            {
                var viewModel = (FlashexplosionViewModel)viewModelField.GetValue(self);
                if (viewModel != null)
                {
                    // 强制隐藏闪光效果
                    viewModel.ShowRootshow = false;
                }
            }
        }
        catch (Exception ex)
        {
            #if Debug_Log
            global::System.Console.WriteLine($"[无视闪光弹] Hook执行失败: {ex.Message}");
            #endif
            // 出错时调用原始方法
            OriginalProxies.FlashExplosionSetData_Original(self, ov, data);
        }
    }
    #endregion

    //#region 聊天消息相关Hook
    //public static void PackChatMsg_Hook(UnityProxyHandler handler, UnityProxyData data, string message)
    //{
    //    // 先调用原始方法
    //    OriginalProxies.PackChatMsg_Original(handler, data, message);

    //    // 处理聊天消息
    //    if (!string.IsNullOrEmpty(message) && !message.StartsWith("Logger"))
    //    {
    //        try
    //        {
    //            // 解析消息
    //            string sender = ExtractXmlAttribute(message, "from");
    //            string msgType = ExtractXmlAttribute(message, "type");
    //            string content = ExtractXmlContent(message, "body");

    //            // 只处理全体和队伍频道
    //            if (msgType == "battle_all" || msgType == "battle_team")
    //            {
    //                string typeText = GetMessageTypeText(msgType);
    //                ////global::System.Console.WriteLine($"[聊天] [{typeText}] {sender}: {content}");

    //                // 自动查找并传递给 AI 处理
    //                var aiChatBot = UnityEngine.Object.FindObjectOfType<AIChatBot>();
    //                if (aiChatBot != null)
    //                {
    //                    aiChatBot.ProcessChatMessage(sender, content, msgType);
    //                }
    //            }
    //        }
    //        catch (System.Exception ex)
    //        {
    //            ////global::System.Console.WriteLine($"[聊天] 解析失败: {ex.Message}");
    //        }
    //    }
    //}

    //// 提取XML属性值
    //private static string ExtractXmlAttribute(string xml, string attributeName)
    //{
    //    string pattern = $"{attributeName}=\"";
    //    int startIndex = xml.IndexOf(pattern);
    //    if (startIndex == -1) return "";

    //    startIndex += pattern.Length;
    //    int endIndex = xml.IndexOf("\"", startIndex);
    //    if (endIndex == -1) return "";

    //    return xml.Substring(startIndex, endIndex - startIndex);
    //}

    //// 提取XML标签内容
    //private static string ExtractXmlContent(string xml, string tagName)
    //{
    //    string startTag = $"<{tagName}>";
    //    string endTag = $"</{tagName}>";

    //    int startIndex = xml.IndexOf(startTag);
    //    if (startIndex == -1) return "";

    //    startIndex += startTag.Length;
    //    int endIndex = xml.IndexOf(endTag, startIndex);
    //    if (endIndex == -1) return "";

    //    return xml.Substring(startIndex, endIndex - startIndex);
    //}

    //// 消息类型转换
    //private static string GetMessageTypeText(string msgType)
    //{
    //    switch (msgType)
    //    {
    //        case "battle_all": return "全体";
    //        case "battle_team": return "队伍";
    //        case "team": return "小队";
    //        case "personal": return "私聊";
    //        case "system": return "系统";
    //        case "tacticsSound": return "战术";
    //        default: return msgType;
    //    }
    //}
    //#endregion

    #region 控制台相关Hook
    // 改为只设置 _startConsole，不自动打开
    public static void QuickRuntimeConsole_Update_Hook(QuickRuntimeConsole console)
    {
        // 先调用原始方法
        OriginalProxies.QuickRuntimeConsoleUpdate_Original(console);

        try
        {
            // 只强制设置 _startConsole 为 true，允许输入 [cmd]
            FieldInfo startConsoleField = typeof(QuickRuntimeConsole).GetField(
                "_startConsole",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (startConsoleField != null)
            {
                startConsoleField.SetValue(console, true);
            }
        }
        catch (Exception ex)
        {
            #if Debug_Log
            global::System.Console.WriteLine($"快速运行时控制台钩子错误: {ex.Message}");
            #endif
        }
    }
    #endregion

    #region 连跳
    private static bool s_autoStrafeActive;
    private static bool s_alternateStrafeSide;
    private static int s_bhopLandingCount;
    private static int s_bhopGroundFrames;
    private static bool s_bhopWasGrounded;
    private static bool s_spaceHoldMasked;
    private static bool s_spaceMaskNeedsRefresh;
    private static bool s_bhopToggleActive;
    private static bool s_bhopRuntimeActive;

    public static void UpdateBhopInput()
    {
        if (!Config.Bhop8Dir || Config.BhopKey == KeyCode.None)
        {
            ResetAutoHopState();
            return;
        }

        Config.BhopActivationMode = Mathf.Clamp(Config.BhopActivationMode, 0, 1);
        if (Config.BhopActivationMode == 0)
        {
            s_bhopToggleActive = false;
            s_bhopRuntimeActive = IsHeld(Config.BhopKey);
        }
        else
        {
            if (Input.GetKeyDown(Config.BhopKey))
                s_bhopToggleActive = !s_bhopToggleActive;
            s_bhopRuntimeActive = s_bhopToggleActive;
        }

        var local = PlayerUpdate.LocalEntity;
        if (!s_bhopRuntimeActive || local == null || local._entity == null || local.IsDead)
        {
            ResetAutoHopState();
            return;
        }

        bool onGround;
        try
        {
            onGround = local._entity.OnGround();
        }
        catch
        {
            ResetAutoHopState();
            return;
        }

        s_autoStrafeActive = true;
        bool maskHeldSpace = Config.BhopKey == KeyCode.Space;
        if (maskHeldSpace)
            EnsureSpaceHoldMasked();
        else
            ReleaseSpaceHoldMask();

        if (!onGround)
        {
            if (!maskHeldSpace)
                MouseSimulator.ForceKey(KeyCode.Space, MouseSimulator.InputState.None);
            s_bhopGroundFrames = 0;
            if (s_bhopLandingCount >= 3)
                s_bhopLandingCount = 0;
            s_bhopWasGrounded = false;
            return;
        }

        if (Config.Airglide)
        {
            if (!maskHeldSpace)
                MouseSimulator.ForceKey(KeyCode.Space, MouseSimulator.InputState.None);
            s_bhopWasGrounded = true;
            return;
        }

        if (!s_bhopWasGrounded)
        {
            s_bhopLandingCount++;
            s_bhopGroundFrames = 0;
        }

        s_bhopGroundFrames++;
        float frameRate = Time.deltaTime > 0.00001f ? 1f / Time.deltaTime : 0f;
        int landingDelayFrames = frameRate > 200f ? 2 : 1;
        if (s_bhopLandingCount <= 2 || s_bhopGroundFrames > landingDelayFrames)
        {
            MouseSimulator.ForceKey(KeyCode.Space, MouseSimulator.InputState.TrueOnce);
            if (maskHeldSpace)
                s_spaceMaskNeedsRefresh = true;
        }

        s_bhopWasGrounded = true;
    }

    private static void AutoHop(ref UserCmd userCmd)
    {
        var local = PlayerUpdate.LocalEntity;
        if (local == null || local._entity == null || local.IsDead)
        {
            ResetAutoHopState();
            return;
        }

        if (!Config.Bhop8Dir || !s_bhopRuntimeActive)
        {
            s_autoStrafeActive = false;
            return;
        }

        s_autoStrafeActive = true;
        ApplyAuraAirStrafe(local._entity, ref userCmd);
    }

    private static bool IsHeld(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKey(key);
    }

    private static void ResetAutoHopState()
    {
        s_autoStrafeActive = false;
        s_alternateStrafeSide = false;
        s_bhopLandingCount = 0;
        s_bhopGroundFrames = 0;
        s_bhopWasGrounded = false;
        s_spaceMaskNeedsRefresh = false;
        s_spaceHoldMasked = false;
        s_bhopToggleActive = false;
        s_bhopRuntimeActive = false;
        MouseSimulator.ForceKey(KeyCode.Space, MouseSimulator.InputState.None);
    }

    private static void EnsureSpaceHoldMasked()
    {
        if (s_spaceHoldMasked && !s_spaceMaskNeedsRefresh)
            return;

        MouseSimulator.ForceKey(KeyCode.Space, MouseSimulator.InputState.FalseKeep);
        s_spaceHoldMasked = true;
        s_spaceMaskNeedsRefresh = false;
    }

    private static void ReleaseSpaceHoldMask()
    {
        if (!s_spaceHoldMasked)
            return;

        MouseSimulator.ForceKey(KeyCode.Space, MouseSimulator.InputState.None);
        s_spaceHoldMasked = false;
        s_spaceMaskNeedsRefresh = false;
    }

    private static void ApplyAuraAirStrafe(PlayerEntity player, ref UserCmd command)
    {
        if (!s_autoStrafeActive || player == null || player.move == null)
            return;

        try
        {
            if (player.OnGround())
                return;

            Vector3 velocity = player.move.Velocity;
            velocity.z = 0f;
            if (!IsFinite(velocity.x) || !IsFinite(velocity.y))
                return;

            float forwardInput = 0f;
            float sideInput = 0f;
            if (Input.GetKey(KeyCode.W))
                forwardInput += 100f;
            if (Input.GetKey(KeyCode.S))
                forwardInput -= 100f;
            if (Input.GetKey(KeyCode.D))
                sideInput += 100f;
            if (Input.GetKey(KeyCode.A))
                sideInput -= 100f;

            float cameraYaw = command.CameraYaw / 100f;
            float desiredYaw = cameraYaw;
            if (forwardInput != 0f || sideInput != 0f)
            {
                desiredYaw += Mathf.Atan2(-sideInput, forwardInput) * Mathf.Rad2Deg;
            }

            command.MoveForward = 0f;
            command.MoveRight = 0f;

            float velocityYaw = VectorYaw(velocity);
            float yawDelta = NormalizeAngle(desiredYaw - velocityYaw);
            float absoluteDelta = Mathf.Abs(yawDelta);
            float acceleration = Mathf.Lerp(60f, 30f, Mathf.Clamp01(absoluteDelta / 90f));
            float horizontalSpeed = HorizontalSpeed(velocity);
            float idealStrafeAngle = horizontalSpeed < 15f
                ? 90f
                : Mathf.Clamp(Mathf.Atan(acceleration / horizontalSpeed) * Mathf.Rad2Deg, 0f, 90f);

            if (absoluteDelta > idealStrafeAngle && horizontalSpeed > 15f)
            {
                desiredYaw = velocityYaw - Mathf.Sign(yawDelta) * idealStrafeAngle;
                command.MoveRight = yawDelta > 0f ? -100f : 100f;
            }
            else
            {
                s_alternateStrafeSide = !s_alternateStrafeSide;
                float direction = s_alternateStrafeSide ? 1f : -1f;
                desiredYaw = velocityYaw + idealStrafeAngle * direction;
                command.MoveRight = 100f * direction;
            }

            float movementYaw = VectorYaw(new Vector3(command.MoveForward, command.MoveRight, 0f));
            float correctedYaw = NormalizeAngle(cameraYaw - desiredYaw + movementYaw) * Mathf.Deg2Rad;
            command.MoveForward = Mathf.Cos(correctedYaw) * 100f;
            command.MoveRight = Mathf.Sin(correctedYaw) * 100f;
        }
        catch
        {
        }
    }

    private static float HorizontalSpeed(Vector3 velocity)
    {
        return Mathf.Sqrt(velocity.x * velocity.x + velocity.y * velocity.y);
    }

    private static float VectorYaw(Vector3 vector)
    {
        if (vector.x == 0f && vector.y == 0f)
            return 0f;

        float yaw = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
        return yaw < 0f ? yaw + 360f : yaw;
    }

    private static float NormalizeAngle(float angle)
    {
        if (!IsFinite(angle))
            return 0f;

        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;
        return angle;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    #endregion
}
