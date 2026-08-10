// ============================================================
// Vape runtime shield  v2  (替换 AntiCheatBypass.cs 使用)
// ------------------------------------------------------------
// 核心改动 (相对 v1):
//  1) 删除 KillGGProcesses() —— 杀 GG 进程会触发用户态守护反制
//  2) 新增 GG IAT hook —— 让 npggNT64.des "活着但瞎掉":
//     FindWindowA / FindWindowExA  -> 永远返回 NULL (找不到 CE/x64dbg 窗口)
//     IsDebuggerPresent            -> 永远 FALSE
//     NtQueryInformationProcess    -> ProcessDebugPort 返回 0 (无调试端口)
//     原理: 改写 GG 模块 IAT 槽, 无论调用来自 .text 还是 .vlizer(VM) 都被拦
//  3) 保留 RuntimeCheatGuard patch (游戏内置检测)
//  4) Update() 每 2 秒 watchdog: GG 复原 IAT 就重新 patch
// ============================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Vape
{
    public class AntiCheatBypass : MonoBehaviour
    {
        private static bool _initialized = false;
        private static float _timer = 0f;

        // ---- GG IAT hook 状态 (懒初始化) ----
        private static readonly List<GCHandle> _hookHandles = new List<GCHandle>();
        private static IntPtr _apiFindWindowA, _apiFindWindowExA, _apiIsDebuggerPresent, _apiNtQIP;
        private static IntPtr _apiGetProcAddress, _apiProcess32FirstW, _apiProcess32NextW;
        private static IntPtr _hookFindWindowA, _hookFindWindowExA, _hookIsDebuggerPresent, _hookNtQIP;
        private static IntPtr _hookGetProcAddress, _hookProcess32FirstW, _hookProcess32NextW;

        // RuntimeCheatGuard 字符串特征码 (游戏内置检测)
        private static readonly byte[] RCG_SIG = Encoding.ASCII.GetBytes("RuntimeCheatGuard");
        private static readonly byte[] SUS_SIG = Encoding.ASCII.GetBytes("Suspicious");

        // ================= P/Invoke =================
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandleA(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MODULEENTRY32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
        }

        // PROCESSENTRY32W (x64)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID; // ULONG_PTR (x64 自动 8 对齐)
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        // ================= GG IAT hook 委托 (x64: stdcall = 默认约定) =================
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr DFindWindowA(IntPtr cls, IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr DFindWindowExA(IntPtr parent, IntPtr child, IntPtr cls, IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DIsDebuggerPresent();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DNtQIP(IntPtr h, uint cls, IntPtr info, uint len, IntPtr retLen);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr DGetProcAddress(IntPtr hModule, IntPtr lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DProcess32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DProcess32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        private static DNtQIP _realNtQIP;
        private static DGetProcAddress _realGetProcAddress;
        private static DProcess32FirstW _realProcess32FirstW;
        private static DProcess32NextW _realProcess32NextW;

        // ---- hook 实现 ----
        private static IntPtr HookFindWindowA(IntPtr cls, IntPtr name) { return IntPtr.Zero; }
        private static IntPtr HookFindWindowExA(IntPtr p, IntPtr c, IntPtr cls, IntPtr n) { return IntPtr.Zero; }
        private static int HookIsDebuggerPresent() { return 0; }

        // 反调试: 拦截 DebugPort(7)/DebugObjectHandle(0x1E)/DebugFlags(0x1F)
        // 全部返回"无调试器"状态; 其余类转发原函数
        private const uint PROCESSINFOCLASS_DebugPort          = 7;
        private const uint PROCESSINFOCLASS_DebugObjectHandle  = 0x1E;
        private const uint PROCESSINFOCLASS_DebugFlags         = 0x1F;

        private static int HookNtQIP(IntPtr h, uint cls, IntPtr info, uint len, IntPtr retLen)
        {
            if (cls == PROCESSINFOCLASS_DebugPort ||
                cls == PROCESSINFOCLASS_DebugObjectHandle) // 调试端口 / 调试对象 -> 空
            {
                if (info != IntPtr.Zero && len >= (uint)IntPtr.Size) Marshal.WriteIntPtr(info, IntPtr.Zero);
                if (retLen != IntPtr.Zero) Marshal.WriteInt32(retLen, 0, IntPtr.Size);
                return 0; // STATUS_SUCCESS
            }
            if (cls == PROCESSINFOCLASS_DebugFlags) // 调试标志: 1 = 未被调试
            {
                if (info != IntPtr.Zero && len >= 4) Marshal.WriteInt32(info, 0, 1);
                if (retLen != IntPtr.Zero) Marshal.WriteInt32(retLen, 0, 4);
                return 0;
            }
            return _realNtQIP != null ? _realNtQIP(h, cls, info, len, retLen) : -1;
        }

        // ---- 工具进程黑名单 (过滤 GG 的进程枚举, 让 GG 枚举不到 CE/调试器) ----
        private static readonly string[] ToolProcessNames = new string[]
        {
            "cheatengine", "x64dbg", "x32dbg", "ollydbg", "windbg",
            "ida", "ghidra", "procexp", "processhacker", "pe-bear"
        };

        private static bool IsToolProcess(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            string n = exe.ToLowerInvariant();
            for (int i = 0; i < ToolProcessNames.Length; i++)
                if (n.IndexOf(ToolProcessNames[i], StringComparison.Ordinal) == 0) return true;
            return false;
        }

        // GetProcAddress: 拦截 GG 动态解析目标 API (防运行时 GetProcAddress 绕 IAT)
        private static IntPtr HookGetProcAddress(IntPtr hModule, IntPtr lpProcName)
        {
            long v = lpProcName.ToInt64();
            if ((v >> 16) != 0) // 名称字符串 (非序号)
            {
                string name = Marshal.PtrToStringAnsi(lpProcName);
                if (name != null)
                {
                    if (name == "FindWindowA")               return _hookFindWindowA;
                    if (name == "FindWindowExA")             return _hookFindWindowExA;
                    if (name == "IsDebuggerPresent")         return _hookIsDebuggerPresent;
                    if (name == "NtQueryInformationProcess") return _hookNtQIP;
                }
            }
            return _realGetProcAddress != null ? _realGetProcAddress(hModule, lpProcName) : IntPtr.Zero;
        }

        // 进程枚举: 跳过工具进程
        private static int HookProcess32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry)
        {
            int r = _realProcess32FirstW(snapshot, ref entry);
            if (r == 0) return 0;
            while (r != 0 && IsToolProcess(entry.szExeFile))
                r = _realProcess32NextW(snapshot, ref entry);
            return r;
        }

        private static int HookProcess32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry)
        {
            int r = _realProcess32NextW(snapshot, ref entry);
            if (r == 0) return 0;
            while (r != 0 && IsToolProcess(entry.szExeFile))
                r = _realProcess32NextW(snapshot, ref entry);
            return r;
        }

        // ================= 入口 =================
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            PatchGgIat();
            PatchRuntimeCheatGuard();

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
            if (_timer > 2.0f) // 每 2 秒 watchdog
            {
                _timer = 0f;
                PatchGgIat();          // GG 复原 IAT 就重新 patch (幂等)
                PatchRuntimeCheatGuard();
            }
        }

        // ================= GG IAT hook =================
        private static void PatchGgIat()
        {
            // 懒初始化 (只做一次)
            if (_apiFindWindowA == IntPtr.Zero)
            {
                IntPtr u32 = GetModuleHandleA("user32.dll");
                _apiFindWindowA   = u32 != IntPtr.Zero ? GetProcAddress(u32, "FindWindowA")   : IntPtr.Zero;
                _apiFindWindowExA = u32 != IntPtr.Zero ? GetProcAddress(u32, "FindWindowExA") : IntPtr.Zero;

                IntPtr k32 = GetModuleHandleA("kernel32.dll");
                _apiIsDebuggerPresent = k32 != IntPtr.Zero ? GetProcAddress(k32, "IsDebuggerPresent") : IntPtr.Zero;

                IntPtr nt = GetModuleHandleA("ntdll.dll");
                _apiNtQIP = nt != IntPtr.Zero ? GetProcAddress(nt, "NtQueryInformationProcess") : IntPtr.Zero;

                _apiGetProcAddress   = k32 != IntPtr.Zero ? GetProcAddress(k32, "GetProcAddress")   : IntPtr.Zero;
                _apiProcess32FirstW  = k32 != IntPtr.Zero ? GetProcAddress(k32, "Process32FirstW")  : IntPtr.Zero;
                _apiProcess32NextW   = k32 != IntPtr.Zero ? GetProcAddress(k32, "Process32NextW")   : IntPtr.Zero;

                _hookFindWindowA       = MakeHook(new DFindWindowA(HookFindWindowA));
                _hookFindWindowExA     = MakeHook(new DFindWindowExA(HookFindWindowExA));
                _hookIsDebuggerPresent = MakeHook(new DIsDebuggerPresent(HookIsDebuggerPresent));
                _hookNtQIP             = MakeHook(new DNtQIP(HookNtQIP));
                _hookGetProcAddress    = MakeHook(new DGetProcAddress(HookGetProcAddress));
                _hookProcess32FirstW   = MakeHook(new DProcess32FirstW(HookProcess32FirstW));
                _hookProcess32NextW    = MakeHook(new DProcess32NextW(HookProcess32NextW));

                if (_apiNtQIP != IntPtr.Zero)
                    _realNtQIP = (DNtQIP)Marshal.GetDelegateForFunctionPointer(_apiNtQIP, typeof(DNtQIP));
                if (_apiGetProcAddress != IntPtr.Zero)
                    _realGetProcAddress = (DGetProcAddress)Marshal.GetDelegateForFunctionPointer(_apiGetProcAddress, typeof(DGetProcAddress));
                if (_apiProcess32FirstW != IntPtr.Zero)
                    _realProcess32FirstW = (DProcess32FirstW)Marshal.GetDelegateForFunctionPointer(_apiProcess32FirstW, typeof(DProcess32FirstW));
                if (_apiProcess32NextW != IntPtr.Zero)
                    _realProcess32NextW = (DProcess32NextW)Marshal.GetDelegateForFunctionPointer(_apiProcess32NextW, typeof(DProcess32NextW));
            }

            // 定位 GG 模块: 优先按名字, 兜底遍历快照
            IntPtr gg = GetModuleHandleA("npggNT64.des");
            if (gg == IntPtr.Zero) gg = FindGgModuleBySnapshot();
            if (gg == IntPtr.Zero) return; // GG 未加载 (前置清理成功) 则无需处理

            PatchTarget(gg, _apiFindWindowA,       _hookFindWindowA);
            PatchTarget(gg, _apiFindWindowExA,     _hookFindWindowExA);
            PatchTarget(gg, _apiIsDebuggerPresent, _hookIsDebuggerPresent);
            PatchTarget(gg, _apiNtQIP,             _hookNtQIP);
            PatchTarget(gg, _apiGetProcAddress,    _hookGetProcAddress);
            PatchTarget(gg, _apiProcess32FirstW,   _hookProcess32FirstW);
            PatchTarget(gg, _apiProcess32NextW,    _hookProcess32NextW);
        }

        private static IntPtr MakeHook(Delegate d)
        {
            _hookHandles.Add(GCHandle.Alloc(d)); // 固定, 防 GC 回收/移动
            return Marshal.GetFunctionPointerForDelegate(d);
        }

        // 改一个 IAT 槽 (幂等: 已 patch 则跳过)
        private static void PatchTarget(IntPtr gg, IntPtr api, IntPtr hook)
        {
            if (api == IntPtr.Zero || hook == IntPtr.Zero) return;
            IntPtr slot = FindIatSlot(gg, api);
            if (slot == IntPtr.Zero) return;
            if (Marshal.ReadIntPtr(slot) == hook) return; // 已是我们的 hook
            uint old;
            if (VirtualProtect(slot, (UIntPtr)IntPtr.Size, 0x40, out old)) // PAGE_EXECUTE_READWRITE
            {
                Marshal.WriteIntPtr(slot, hook);
                VirtualProtect(slot, (UIntPtr)IntPtr.Size, old, out old);
            }
        }

        // 解析模块 PE 导入表, 找到指向 api 的 IAT 槽地址 (x64)
        private static IntPtr FindIatSlot(IntPtr modBase, IntPtr api)
        {
            try
            {
                int e_lfanew = Marshal.ReadInt32(modBase, 0x3C);
                IntPtr nt = modBase + e_lfanew;
                if (Marshal.ReadInt32(nt) != 0x4550) return IntPtr.Zero;
                // OptionalHeader @ nt+0x18, 数据目录 @ opt+0x70, Import = dir[1] -> +8
                int impRva = Marshal.ReadInt32(nt, 0x18 + 0x70 + 8);
                if (impRva == 0) return IntPtr.Zero;

                IntPtr desc = modBase + impRva;
                for (int i = 0; i < 128; i++) // 最多 128 个导入描述符
                {
                    IntPtr d = desc + i * 20; // IMAGE_IMPORT_DESCRIPTOR = 20 字节
                    int firstThunkRva = Marshal.ReadInt32(d, 16);
                    if (firstThunkRva == 0) break;

                    IntPtr thunk = modBase + firstThunkRva; // IAT 槽数组 (x64: 8 字节/项)
                    for (int j = 0; j < 8192; j++)
                    {
                        IntPtr val = Marshal.ReadIntPtr(thunk, j * IntPtr.Size);
                        if (val == IntPtr.Zero) break;
                        if (val == api) return thunk + j * IntPtr.Size;
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        // 兜底: 遍历模块快照找 GG (文件名含 npgg / GameMon)
        private static IntPtr FindGgModuleBySnapshot()
        {
            try
            {
                const uint TH32CS_SNAPMODULE = 0x8, TH32CS_SNAPMODULE32 = 0x10;
                IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, 0);
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return IntPtr.Zero;

                MODULEENTRY32W me = new MODULEENTRY32W();
                me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                IntPtr found = IntPtr.Zero;

                if (Module32FirstW(snap, ref me))
                {
                    do
                    {
                        if (me.szModule.IndexOf("npgg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            me.szModule.IndexOf("GameMon", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = me.modBaseAddr;
                            break;
                        }
                    } while (Module32NextW(snap, ref me));
                }
                CloseHandle(snap);
                return found;
            }
            catch { return IntPtr.Zero; }
        }

        // ================= RuntimeCheatGuard patch (游戏内置检测, 保留) =================
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
                            VirtualProtect(new IntPtr(addr), (UIntPtr)5, 0x40, out old);
                            Marshal.Copy(new byte[5] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 0, new IntPtr(addr), 5);
                            VirtualProtect(new IntPtr(addr), (UIntPtr)5, old, out old);
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
                            VirtualProtect(new IntPtr(addr), (UIntPtr)5, 0x40, out old);
                            Marshal.Copy(new byte[5] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 0, new IntPtr(addr), 5);
                            VirtualProtect(new IntPtr(addr), (UIntPtr)5, old, out old);
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
                            VirtualProtect(new IntPtr(addr), (UIntPtr)6, 0x40, out old);
                            Marshal.Copy(new byte[6] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }, 0, new IntPtr(addr), 6);
                            VirtualProtect(new IntPtr(addr), (UIntPtr)6, old, out old);
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
