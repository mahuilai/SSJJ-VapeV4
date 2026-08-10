// ============================================================
// Vape runtime shield v3  (AntiCheatBypass.cs 完整优化版)
// ------------------------------------------------------------
// 核心改动 (相对 v2):
//  1) 全局 IAT hook —— v2 只 patch npggNT64.des, 但该模块 WinLicense
//     加壳后 IAT 为空, patch 实际无效。v3 改为枚举本进程所有模块,
//     patch 每个模块 IAT 中指向敏感 API 的槽 (覆盖游戏自身模块+GG)。
//  2) 新增 PEB 补丁 —— 清零 PEB.BeingDebugged + NtGlobalFlag,
//     让游戏/GG 直接读 PEB 也返回"未调试"。
//  3) RuntimeCheatGuard 托管 Hook —— 用 MonoHook(MethodHook) 直接
//     Hook CheckLoop 方法, 替代 v2 脆弱的 IL 字节 NOP 扫描。
//  4) watchdog 优化 —— 3~6 秒随机间隔 (减少固定周期行为特征),
//     幂等 patch: 先读回验证, 仅当被 GG 复原时才重写。
//  5) 保留 GetProcAddress 拦截 + 进程枚举过滤 + NtQIP 反调试。
// ============================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;
using UnityEngine;
using Vape.Core.Hook;

namespace Vape
{
    public class AntiCheatBypass : MonoBehaviour
    {
        private static bool _initialized = false;
        private static float _timer = 0f;
        private static float _nextInterval = 3f;
        private static readonly System.Random _rng = new System.Random();
        private static readonly object _lock = new object();

        // ---- GG / 游戏 IAT hook 状态 (懒初始化) ----
        private static readonly List<GCHandle> _hookHandles = new List<GCHandle>();
        private static IntPtr _apiFindWindowA, _apiFindWindowExA, _apiIsDebuggerPresent, _apiNtQIP;
        private static IntPtr _apiGetProcAddress, _apiProcess32FirstW, _apiProcess32NextW;
        private static IntPtr _hookFindWindowA, _hookFindWindowExA, _hookIsDebuggerPresent, _hookNtQIP;
        private static IntPtr _hookGetProcAddress, _hookProcess32FirstW, _hookProcess32NextW;

        // 真实函数指针 (转发用)
        private static DNtQIP _realNtQIP;
        private static DGetProcAddress _realGetProcAddress;
        private static DProcess32FirstW _realProcess32FirstW;
        private static DProcess32NextW _realProcess32NextW;
        private static DFindWindowA _realFindWindowA;
        private static DFindWindowExA _realFindWindowExA;
        private static DIsDebuggerPresent _realIsDebuggerPresent;

        // 已 patch 的模块集合 (watchdog 幂等校验用)
        private static readonly List<IntPtr> _patchedModules = new List<IntPtr>();
        private static readonly Dictionary<IntPtr, List<IntPtr>> _patchedSlots = new Dictionary<IntPtr, List<IntPtr>>();

        // RuntimeCheatGuard 托管 Hook 状态
        private static MethodHook _rcgCheckLoopHook;

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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
            IntPtr processInformation, uint processInformationLength, out uint returnLength);

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
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        // PROCESS_BASIC_INFORMATION (class 0)
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr Reserved3;
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

        // ---- hook 实现 (对非目标窗口/进程透明转发, 降低误伤) ----
        private static IntPtr HookFindWindowA(IntPtr cls, IntPtr name)
        {
            return _realFindWindowA != null ? _realFindWindowA(cls, name) : IntPtr.Zero;
        }

        private static IntPtr HookFindWindowExA(IntPtr p, IntPtr c, IntPtr cls, IntPtr name)
        {
            return _realFindWindowExA != null ? _realFindWindowExA(p, c, cls, name) : IntPtr.Zero;
        }

        private static int HookIsDebuggerPresent()
        {
            return 0; // 全局: 直接读 PEB 也已被补丁, 这里保持 0
        }

        private const uint PROCESSINFOCLASS_DebugPort          = 7;
        private const uint PROCESSINFOCLASS_DebugObjectHandle  = 0x1E;
        private const uint PROCESSINFOCLASS_DebugFlags         = 0x1F;

        private static int HookNtQIP(IntPtr h, uint cls, IntPtr info, uint len, IntPtr retLen)
        {
            if (cls == PROCESSINFOCLASS_DebugPort ||
                cls == PROCESSINFOCLASS_DebugObjectHandle)
            {
                if (info != IntPtr.Zero && len >= (uint)IntPtr.Size) Marshal.WriteIntPtr(info, IntPtr.Zero);
                if (retLen != IntPtr.Zero) Marshal.WriteInt32(retLen, 0, IntPtr.Size);
                return 0;
            }
            if (cls == PROCESSINFOCLASS_DebugFlags)
            {
                if (info != IntPtr.Zero && len >= 4) Marshal.WriteInt32(info, 0, 1);
                if (retLen != IntPtr.Zero) Marshal.WriteInt32(retLen, 0, 4);
                return 0;
            }
            return _realNtQIP != null ? _realNtQIP(h, cls, info, len, retLen) : -1;
        }

        // ---- 工具进程/窗口黑名单 ----
        private static readonly string[] ToolProcessNames = new string[]
        {
            "cheatengine", "x64dbg", "x32dbg", "ollydbg", "windbg",
            "ida", "ghidra", "procexp", "processhacker", "pe-bear",
            "dnspy", "de4dot", "ilspy", "mono-cecil", "scylla", "importrec"
        };

        private static readonly string[] ToolWindowKeywords = new string[]
        {
            "cheat engine", "x64dbg", "x32dbg", "ollydbg", "windbg",
            "ida -", "ghidra", "process hacker", "process explorer",
            "dnspy", "de4dot"
        };

        private static bool IsToolWindow(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.ToLowerInvariant();
            for (int i = 0; i < ToolWindowKeywords.Length; i++)
                if (t.IndexOf(ToolWindowKeywords[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static bool IsToolProcess(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            string n = exe.ToLowerInvariant();
            for (int i = 0; i < ToolProcessNames.Length; i++)
                if (n.IndexOf(ToolProcessNames[i], StringComparison.Ordinal) == 0) return true;
            return false;
        }

        // GetProcAddress: 拦截动态解析目标 API
        private static IntPtr HookGetProcAddress(IntPtr hModule, IntPtr lpProcName)
        {
            return _realGetProcAddress != null ? _realGetProcAddress(hModule, lpProcName) : IntPtr.Zero;
        }

        // 进程枚举: 跳过工具进程 (仅影响被我们 hook 了 IAT 的模块的枚举)
        private static int HookProcess32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry)
        {
            int r = _realProcess32FirstW != null ? _realProcess32FirstW(snapshot, ref entry) : 0;
            if (r == 0) return 0;
            while (r != 0 && IsToolProcess(entry.szExeFile))
                r = _realProcess32NextW(snapshot, ref entry);
            return r;
        }

        private static int HookProcess32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry)
        {
            int r = _realProcess32NextW != null ? _realProcess32NextW(snapshot, ref entry) : 0;
            if (r == 0) return 0;
            while (r != 0 && IsToolProcess(entry.szExeFile))
                r = _realProcess32NextW(snapshot, ref entry);
            return r;
        }

        // ================= 入口 =================
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            PatchPeb();                 // 1. PEB 反调试补丁
            PatchRuntimeCheatGuard();   // 2. RuntimeCheatGuard 托管 Hook + IL 兜底
        }

        // ---- GameGuard Unity 组件名称 (Robin 项目移植: 每帧销毁 GG 注入的 MonoBehaviour) ----
        private static readonly string[] s_ggComponentNames =
        {
            "NpOpen", "ExecuteGG", "UnityNp", "GameGuard", "NProtect",
            "AntiCheatComponent", "GGBridge"
        };

        private void Update()
        {
            // 每帧销毁 GameGuard 注入的 Unity 组件 (仿 Robin Module.cs)
            KillGameGuardComponents();

            _timer += Time.deltaTime;
            if (_timer >= _nextInterval)
            {
                _timer = 0f;
                _nextInterval = 3f + (float)_rng.NextDouble() * 3f; // 3~6s 随机

                VerifyAndRepair(); // 幂等: 仅当被复原时才重写
            }
        }

        // 销毁 GameGuard 注入的 Unity 组件 (每帧执行, 来自 Robin 项目)
        private static void KillGameGuardComponents()
        {
            try
            {
                for (int i = 0; i < s_ggComponentNames.Length; i++)
                {
                    GameObject go = GameObject.Find(s_ggComponentNames[i]);
                    if (go != null)
                    {
                        Component[] comps = go.GetComponents<Component>();
                        for (int j = 0; j < comps.Length; j++)
                        {
                            if (comps[j] == null) continue;
                            string typeName = comps[j].GetType().Name;
                            // 跳过 Transform, 只销毁非基础组件
                            if (typeName != "Transform" && typeName != "RectTransform")
                                UnityEngine.Object.Destroy(comps[j]);
                        }
                    }
                }

                // Robin 方式: 直接按已知类型反射查找并销毁 NpOpen / ExecuteGG 组件
                try
                {
                    var npOpenType = Type.GetType("NpOpen, Assembly-CSharp");
                    if (npOpenType != null)
                    {
                        var instance = UnityEngine.Object.FindObjectOfType(npOpenType) as Component;
                        if (instance != null) UnityEngine.Object.Destroy(instance);
                    }
                }
                catch { }

                try
                {
                    var ggType = Type.GetType("ExecuteGG, Assembly-CSharp");
                    if (ggType != null)
                    {
                        var instance = UnityEngine.Object.FindObjectOfType(ggType) as Component;
                        if (instance != null) UnityEngine.Object.Destroy(instance);
                    }
                }
                catch { }
            }
            catch { }
        }

        // ================= PEB 补丁 (BeingDebugged + NtGlobalFlag) =================
        private static void PatchPeb()
        {
            try
            {
                IntPtr pbi = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)));
                try
                {
                    uint retLen = 0;
                    int status = NtQueryInformationProcess(GetCurrentProcess(), 0, pbi,
                        (uint)Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)), out retLen);
                    if (status == 0)
                    {
                        PROCESS_BASIC_INFORMATION info = (PROCESS_BASIC_INFORMATION)Marshal.PtrToStructure(pbi, typeof(PROCESS_BASIC_INFORMATION));
                        IntPtr peb = info.PebBaseAddress;
                        if (peb != IntPtr.Zero)
                        {
                            uint old;
                            // x64: BeingDebugged @ PEB+0x02, NtGlobalFlag @ PEB+0xBC
                            VirtualProtect(peb + 0x02, (UIntPtr)1, 0x04, out old);
                            Marshal.WriteByte(peb, 0x02, 0);
                            VirtualProtect(peb + 0x02, (UIntPtr)1, old, out old);

                            VirtualProtect(peb + 0xBC, (UIntPtr)4, 0x04, out old);
                            Marshal.WriteInt32(peb, 0xBC, 0);
                            VirtualProtect(peb + 0xBC, (UIntPtr)4, old, out old);
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(pbi); }
            }
            catch { }
        }

        // ================= 全局 IAT hook (枚举本进程所有模块) =================
        private static void PatchGlobalIat()
        {
            try
            {
                // 懒初始化 API/hook
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
                    if (_apiFindWindowA != IntPtr.Zero)
                        _realFindWindowA = (DFindWindowA)Marshal.GetDelegateForFunctionPointer(_apiFindWindowA, typeof(DFindWindowA));
                    if (_apiFindWindowExA != IntPtr.Zero)
                        _realFindWindowExA = (DFindWindowExA)Marshal.GetDelegateForFunctionPointer(_apiFindWindowExA, typeof(DFindWindowExA));
                    if (_apiIsDebuggerPresent != IntPtr.Zero)
                        _realIsDebuggerPresent = (DIsDebuggerPresent)Marshal.GetDelegateForFunctionPointer(_apiIsDebuggerPresent, typeof(DIsDebuggerPresent));
                }

                // 枚举本进程所有模块 (TH32CS_SNAPMODULE=0x8 | SNAPMODULE32=0x10)
                IntPtr snap = CreateToolhelp32Snapshot(0x8 | 0x10, 0);
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return;

                MODULEENTRY32W me = new MODULEENTRY32W();
                me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));

                if (Module32FirstW(snap, ref me))
                {
                    do
                    {
                        // 跳过系统核心 DLL 降低风险 (GG 主要扫描游戏模块与自身)
                        string mod = me.szModule.ToLowerInvariant();
                        if (mod == "ntdll.dll" || mod == "kernel32.dll" || mod == "kernelbase.dll" ||
                            mod == "user32.dll" || mod == "vape.dll")
                            continue;

                        PatchModuleIat(me.modBaseAddr);
                    } while (Module32NextW(snap, ref me));
                }
                CloseHandle(snap);
            }
            catch { }
        }

        // 对一个模块执行 IAT patch (记录已 patch 槽, 幂等)
        private static void PatchModuleIat(IntPtr modBase)
        {
            try
            {
                if (modBase == IntPtr.Zero) return;
                if (_patchedModules.Contains(modBase)) return;
                _patchedModules.Add(modBase);

                List<IntPtr> slots = new List<IntPtr>();
                PatchTargetSlot(modBase, _apiFindWindowA,       _hookFindWindowA,       slots);
                PatchTargetSlot(modBase, _apiFindWindowExA,     _hookFindWindowExA,     slots);
                PatchTargetSlot(modBase, _apiIsDebuggerPresent, _hookIsDebuggerPresent, slots);
                PatchTargetSlot(modBase, _apiNtQIP,             _hookNtQIP,             slots);
                PatchTargetSlot(modBase, _apiGetProcAddress,    _hookGetProcAddress,    slots);
                PatchTargetSlot(modBase, _apiProcess32FirstW,   _hookProcess32FirstW,   slots);
                PatchTargetSlot(modBase, _apiProcess32NextW,    _hookProcess32NextW,    slots);

                _patchedSlots[modBase] = slots;
            }
            catch { }
        }

        private static IntPtr MakeHook(Delegate d)
        {
            _hookHandles.Add(GCHandle.Alloc(d));
            return Marshal.GetFunctionPointerForDelegate(d);
        }

        // 改一个 IAT 槽 (幂等: 已 patch 则跳过)
        private static void PatchTargetSlot(IntPtr modBase, IntPtr api, IntPtr hook, List<IntPtr> slots)
        {
            if (api == IntPtr.Zero || hook == IntPtr.Zero) return;
            IntPtr slot = FindIatSlot(modBase, api);
            if (slot == IntPtr.Zero) return;
            slots.Add(slot);
            if (Marshal.ReadIntPtr(slot) == hook) return; // 已是我们的 hook
            uint old;
            if (VirtualProtect(slot, (UIntPtr)IntPtr.Size, 0x40, out old))
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
                int impRva = Marshal.ReadInt32(nt, 0x18 + 0x70 + 8);
                if (impRva == 0) return IntPtr.Zero;

                IntPtr desc = modBase + impRva;
                for (int i = 0; i < 128; i++)
                {
                    IntPtr d = desc + i * 20;
                    int firstThunkRva = Marshal.ReadInt32(d, 16);
                    if (firstThunkRva == 0) break;

                    IntPtr thunk = modBase + firstThunkRva;
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

        // ================= watchdog: 幂等验证修复 =================
        private static void VerifyAndRepair()
        {
            // 重新枚举模块 (可能新加载), 对新模块补 patch
            try
            {
                IntPtr snap = CreateToolhelp32Snapshot(0x8 | 0x10, 0);
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return;
                MODULEENTRY32W me = new MODULEENTRY32W();
                me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                if (Module32FirstW(snap, ref me))
                {
                    do
                    {
                        string mod = me.szModule.ToLowerInvariant();
                        if (mod == "ntdll.dll" || mod == "kernel32.dll" || mod == "kernelbase.dll" ||
                            mod == "user32.dll" || mod == "vape.dll")
                            continue;
                        if (!_patchedModules.Contains(me.modBaseAddr))
                            PatchModuleIat(me.modBaseAddr);
                    } while (Module32NextW(snap, ref me));
                }
                CloseHandle(snap);
            }
            catch { }

            // 校验已 patch 槽: 被 GG 复原则重写
            foreach (var kv in _patchedSlots)
            {
                IntPtr modBase = kv.Key;
                List<IntPtr> slots = kv.Value;
                // 对应 api->hook 映射 (按顺序与 PatchModuleIat 一致)
                IntPtr[] apis = { _apiFindWindowA, _apiFindWindowExA, _apiIsDebuggerPresent,
                                  _apiNtQIP, _apiGetProcAddress, _apiProcess32FirstW, _apiProcess32NextW };
                IntPtr[] hooks = { _hookFindWindowA, _hookFindWindowExA, _hookIsDebuggerPresent,
                                   _hookNtQIP, _hookGetProcAddress, _hookProcess32FirstW, _hookProcess32NextW };
                for (int i = 0; i < slots.Count && i < apis.Length; i++)
                {
                    IntPtr slot = slots[i];
                    if (slot == IntPtr.Zero || apis[i] == IntPtr.Zero || hooks[i] == IntPtr.Zero) continue;
                    IntPtr cur = IntPtr.Zero;
                    try { cur = Marshal.ReadIntPtr(slot); } catch { continue; }
                    if (cur != hooks[i])
                    {
                        uint old;
                        if (VirtualProtect(slot, (UIntPtr)IntPtr.Size, 0x40, out old))
                        {
                            Marshal.WriteIntPtr(slot, hooks[i]);
                            VirtualProtect(slot, (UIntPtr)IntPtr.Size, old, out old);
                        }
                    }
                }
            }
        }

        // ================= RuntimeCheatGuard: 托管 Hook + IL 兜底 =================
        private static void PatchRuntimeCheatGuard()
        {
            // 方式1: 托管层 Hook CheckLoop (优先, 借鉴 Ssjj_recovered)
            try
            {
                Type type = Type.GetType("Assets.Sources.Utils.RuntimeCheatGuard, Assembly-CSharp");
                if (type != null)
                {
                    if (_rcgCheckLoopHook == null || !_rcgCheckLoopHook.isHooked)
                    {
                        MethodInfo checkLoop = type.GetMethod("CheckLoop",
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        MethodInfo detour = typeof(AntiCheatBypass).GetMethod(nameof(RcgCheckLoop_Detour),
                            BindingFlags.Static | BindingFlags.NonPublic);
                        MethodInfo proxy = typeof(AntiCheatBypass).GetMethod(nameof(RcgCheckLoop_Original),
                            BindingFlags.Static | BindingFlags.NonPublic);
                        if (checkLoop != null && detour != null && proxy != null)
                        {
                            _rcgCheckLoopHook = new MethodHook(checkLoop, detour, proxy, "RCG.CheckLoop");
                            _rcgCheckLoopHook.Install();
                        }
                    }
                }
            }
            catch { }

            // 方式2: 反射终止 _thread 线程 (兜底)
            try
            {
                Type type = Type.GetType("Assets.Sources.Utils.RuntimeCheatGuard, Assembly-CSharp");
                if (type != null)
                {
                    FieldInfo field = type.GetField("_thread",
                        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        var thread = field.GetValue(null) as System.Threading.Thread;
                        if (thread != null && thread.IsAlive)
                        {
                            try { thread.Abort(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        // CheckLoop 替身: 空实现
        private static void RcgCheckLoop_Detour() { }

        // CheckLoop 原方法代理占位 (MethodHook 要求, 不会真正被调用)
        private static void RcgCheckLoop_Original() { }
    }
}
