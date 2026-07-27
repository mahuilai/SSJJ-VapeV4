// ============================================================
// Vape runtime shield
// single-module inject
// ============================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Vape
{
    public class AntiCheatBypass : MonoBehaviour
    {
        private static bool _initialized = false;
        private static float _timer = 0f;

        // RuntimeCheatGuard 字符串特征码
        private static readonly byte[] RCG_SIG = Encoding.ASCII.GetBytes("RuntimeCheatGuard");
        private static readonly byte[] SUS_SIG = Encoding.ASCII.GetBytes("Suspicious");

        // GameGuard 进程名单
        private static readonly string[] GG_PROCESSES = {
            "GameGuard", "GameMon64", "GameMon",
            "npggNT64", "npggNT", "ggscan",
            "npgmup", "npsc64", "nplpdb", "ggexp", "ggerror"
        };

        // GameGuard 服务
        private static readonly string[] GG_SERVICES = {
            "npggsvc", "npgmup"
        };

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll")]
        private static extern void CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandleA(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("advapi32.dll")]
        private static extern IntPtr OpenSCManagerA(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll")]
        private static extern IntPtr OpenServiceA(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll")]
        private static extern bool ControlService(IntPtr hService, uint dwControl, IntPtr lpServiceStatus);

        [DllImport("advapi32.dll")]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("advapi32.dll")]
        private static extern bool ChangeServiceConfigA(IntPtr hService, uint dwServiceType,
            uint dwStartType, uint dwErrorControl, string lpBinaryPathName,
            string lpLoadOrderGroup, IntPtr lpdwTagId, string lpDependencies,
            string lpServiceStartName, string lpPassword, string lpDisplayName);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 1. 杀 GameGuard 进程
            KillGGProcesses();

            // 2. 停 GameGuard 服务  
            StopGGServices();

            // 3. Patch RuntimeCheatGuard 内存
            PatchRuntimeCheatGuard();

            // 4. 启动持续防护协程
            if (GameObject.Find("vp_shield") == null)
            {
                var go = new GameObject("vp_shield");
                DontDestroyOnLoad(go);
                go.AddComponent<AntiCheatBypass>();
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer > 2.0f) // 每2秒执行一次
            {
                _timer = 0f;
                KillGGProcesses();
                StopGGServices();
                PatchRuntimeCheatGuard();
            }
        }

        private static void KillGGProcesses()
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName.ToLower();
                    foreach (var target in GG_PROCESSES)
                    {
                        if (name.Contains(target.ToLower()) ||
                            name.Contains(target.ToLower().Replace(".des", "")))
                        {
                            proc.Kill();
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        private static void StopGGServices()
        {
            foreach (var svc in GG_SERVICES)
            {
                try
                {
                    var p = new Process();
                    p.StartInfo.FileName = "sc.exe";
                    p.StartInfo.Arguments = $"stop {svc}";
                    p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();
                    p.WaitForExit(1000);
                }
                catch { }
            }
        }

        private static void PatchRuntimeCheatGuard()
        {
            try
            {
                // 获取 Assembly-CSharp.dll 模块
                IntPtr hMod = GetModuleHandleA("Assembly-CSharp.dll");
                if (hMod == IntPtr.Zero) return;

                // 搜索 RuntimeCheatGuard 字符串
                IntPtr strAddr = SearchPattern(hMod, RCG_SIG);
                if (strAddr == IntPtr.Zero)
                    strAddr = SearchPattern(hMod, SUS_SIG);
                if (strAddr == IntPtr.Zero) return;

                // 在字符串附近搜索 CALL/JMP 指令并 NOP 掉
                long searchStart = strAddr.ToInt64() - 0x800;
                long searchEnd = strAddr.ToInt64() + 0x800;
                long hMod64 = hMod.ToInt64();
                long strAddr64 = strAddr.ToInt64();
                if (searchStart < hMod64)
                    searchStart = hMod64;

                int patched = 0;

                for (long offset = 0; offset < 0x1000; offset += 1)
                {
                    long addr = searchStart + offset;
                    if (addr >= searchEnd) break;

                    byte[] code = new byte[6];
                    Marshal.Copy(new IntPtr(addr), code, 0, 6);

                    // CALL rel32 (E8 xx xx xx xx)
                    if (code[0] == 0xE8)
                    {
                        int target = (int)(addr - hMod64) + 5 + BitConverter.ToInt32(code, 1);
                        long targetPtr = hMod64 + target;
                        if (targetPtr >= strAddr64 - 0x400 && targetPtr <= strAddr64 + 0x400)
                        {
                            uint old;
                            VirtualProtect(new IntPtr(addr), 5, 0x40, out old);
                            Marshal.Copy(new byte[5] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 0, new IntPtr(addr), 5);
                            VirtualProtect(new IntPtr(addr), 5, old, out old);
                            patched++;
                        }
                    }
                    // JMP rel32 (E9 xx xx xx xx)
                    else if (code[0] == 0xE9)
                    {
                        int target = (int)(addr - hMod64) + 5 + BitConverter.ToInt32(code, 1);
                        long targetPtr = hMod64 + target;
                        if (targetPtr >= strAddr64 - 0x400 && targetPtr <= strAddr64 + 0x400)
                        {
                            uint old;
                            VirtualProtect(new IntPtr(addr), 5, 0x40, out old);
                            Marshal.Copy(new byte[5] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 0, new IntPtr(addr), 5);
                            VirtualProtect(new IntPtr(addr), 5, old, out old);
                            patched++;
                        }
                    }
                    // Jcc rel32 (0F 8x xx xx xx xx)
                    else if (code[0] == 0x0F && (code[1] & 0xF0) == 0x80)
                    {
                        int target = (int)(addr - hMod64) + 6 + BitConverter.ToInt32(code, 2);
                        long targetPtr = hMod64 + target;
                        if (targetPtr >= strAddr64 - 0x400 && targetPtr <= strAddr64 + 0x400)
                        {
                            uint old;
                            VirtualProtect(new IntPtr(addr), 6, 0x40, out old);
                            Marshal.Copy(new byte[6] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }, 0, new IntPtr(addr), 6);
                            VirtualProtect(new IntPtr(addr), 6, old, out old);
                            patched++;
                        }
                    }
                }
            }
            catch { }
        }

        private static IntPtr SearchPattern(IntPtr moduleBase, byte[] pattern)
        {
            try
            {
                // 读取模块内存搜索特征码
                uint size = 0x400000; // 4MB 搜索范围
                byte[] buffer = new byte[size];
                Marshal.Copy(moduleBase, buffer, 0, buffer.Length);

                for (int i = 0; i < buffer.Length - pattern.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (buffer[i + j] != pattern[j])
                        {
                            found = false;
                            break;
                        }
                    }
                    if (found)
                        return moduleBase + i;
                }
            }
            catch { }
            return IntPtr.Zero;
        }
    }
}
