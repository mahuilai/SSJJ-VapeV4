# SSJJ_Vape 原生注入器设计文档

> 对标 `D:\项目\vapev4.21\native\`（Vape 4.21 注入架构），为 **生死狙击微端（Unity/Mono）** 重建一套
> 自包含原生注入方案，替代 SharpMonoInjector。
>
> 状态：**设计稿 v1（待确认后实现）**

---

## 1. 背景与目标

### 1.1 现状

| 项 | 现状 |
| --- | --- |
| 托管载荷 | `Vape.dll`（.NET Framework 4.8 / x64，`bin\x64\Release\`） |
| 入口 | `Vape.Loader.Load()`（静态方法，MonoBehaviour 挂载），兼容短入口 `t.u.i()` |
| 注入方式 | SharpMonoInjector 2.7（TheHolyOneZ Edition）——托管 shellcode 远程线程注入 |
| 反作弊 | nProtect GameGuard（`GameMon64.des.exe`，x64，VM 加壳）+ 游戏内置 RuntimeCheatGuard |

### 1.2 目标

对标 Vape 4.21 的**两段式原生注入**架构：

```
SSJJInjector.exe  (原生 C, x64)
   └─ CreateRemoteThread + LoadLibraryW
        └─ SSJJNative.dll  (原生 C, x64)
             ├─ 等待 mono 运行时加载
             ├─ 附着 Mono 根域
             ├─ 内嵌 Vape.dll (RCDATA 资源) 落地到临时目录
             ├─ 加载程序集 → 定位入口 → mono_runtime_invoke
             └─ 完成引导，日志落盘
```

### 1.3 收益（相对 SharpMonoInjector）

1. **自包含**：一个 exe + 一个 dll 全搞定，不再依赖外部注入工具。
2. **载荷内嵌**：`Vape.dll` 打进 DLL 资源，无需单独放置 payload 文件。
3. **原生层能力**：GameGuard 对抗（IAT hook、窗口欺骗）可下沉到原生层，比托管层隐蔽。
4. **时序可控**：原生层能精确等待 `mono.dll` 加载、`Assembly-CSharp` 就绪，避免"注入太早"崩溃。
5. **可扩展**：原生桥可以加 MonoMod/Harmony 前置、native hook、输入钩子。

---

## 2. 目标环境确认（依据工作区证据）

| 项 | 结论 | 证据 |
| --- | --- | --- |
| 游戏 | 生死狙击 4399 微端 | `_diag.txt`：`WDlauncher.exe`（x86 启动器） |
| 引擎 | Unity **2019.x**（模块化程序集） | `依赖/` 有 `UnityEngine.CoreModule.dll`、`UnityEngine.InputLegacyModule.dll`（2019+ 才拆分 InputLegacyModule） |
| 运行时 | **`MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll`** | Unity 2018+ 标准命名；需运行时以 `mono.dll` 兜底（Unity ≤2017） |
| 架构 | x64 | `Vape.dll` 为 x64 构建；`GameMon64.des.exe` 为 x64 |
| 反作弊 | GameGuard（`npggNT64.des` / `GameMon64.des.exe`）+ 内置 RuntimeCheatGuard | `AntiCheatBypass.cs`、`_diag2.txt` |
| 注入目标 | 游戏主进程（Unity 游戏 exe，窗口标题含 "生死狙击"/"SSJJ"） | 待实机确认进程名，注入器按窗口标题 + mono 模块双条件筛选 |

> ⚠️ **待确认项**：游戏主进程 exe 名称、游戏窗口标题。设计按"可配置进程名 + 窗口标题匹配 + mono 模块存在"三重过滤，不写死单一 exe 名。

---

## 3. 文件结构（新增 `底层组件/` 目录）

```text
SSJJ_Vape/
├── Vape.csproj                 # 托管载荷（现有）
└── 底层组件/                   # 新增：原生注入
    ├── CMakeLists.txt          # MSVC x64 构建
    ├── injector.c              # SSJJInjector.exe
    ├── dllmain.c               # SSJJNative.dll 入口 + bootstrap 线程
    ├── mono_bridge.c           # Mono API 动态解析与封装
    ├── mono_bridge.h
    ├── payload.rc.in           # 内嵌 Vape.dll (RCDATA, ID=421)
    ├── build_native.ps1        # 一键构建脚本（CMake + 资源嵌入）
    └── README.md
```

构建产物（`底层组件/build/dist/`）：

```text
SSJJInjector.exe
SSJJNative.dll
```

---

## 4. 详细设计

### 4.1 注入器 `SSJJInjector.exe`（对标 `injector.c`）

**流程**（与 Vape 4.21 `inject_library()` 对齐）：

```
1. 枚举候选进程
   - CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)
   - 过滤：进程名 ∈ {游戏exe, "WDGame.exe", "WDClient.exe", 待确认}
         或 可见窗口标题 匹配 "生死狙击"/"SSJJ"（EnumWindows 回填标题）
   - 每 750ms 刷新选择器（Up/Down 选择，Enter 注入，Esc 退出）
2. OpenProcess(PROCESS_CREATE_THREAD|QUERY_INFORMATION|VM_OPERATION|VM_WRITE|VM_READ)
3. require_x64_target()：IsWow64Process2 拒绝非 x64
4. VirtualAllocEx + WriteProcessMemory 写入 DLL 绝对路径
5. 解析远端 LoadLibraryW：
   - 本地 GetModuleHandleW("kernel32.dll") + GetProcAddress("LoadLibraryW")
   - 本地偏移 offset = LoadLibraryW - kernel32_base
   - 远端 base = 目标进程 kernel32 基址（Toolhelp32Snapshot SNAPMODULE）
   - remote_LoadLibraryW = remote_kernel32_base + offset
6. CreateRemoteThread(remote_LoadLibraryW, remote_path)
7. WaitForSingleObject(30s) → 轮询 DLL 是否映射（最多 100×50ms）
8. 返回码：0 失败 / 1 成功 / 2 已加载
```

**与 Vape 4.21 的差异**：
- 进程过滤从 `java.exe/javaw.exe` → 游戏进程名/窗口标题。
- 保留偏移式 `LoadLibraryW` 解析（避免硬编码地址，兼容系统更新）。
- 可选追加 `NtCreateThreadEx` 模式（避开部分 GG 对 CRT 的监控），默认仍用 CRT。

### 4.2 原生引导 DLL `SSJJNative.dll`（对标 `dllmain.c`）

**DllMain**：

```c
BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = inst;
        DisableThreadLibraryCalls(inst);
        HANDLE t = CreateThread(NULL, 0, bootstrap_thread, inst, 0, NULL);
        if (t) CloseHandle(t);          // 不在 LoaderLock 内做事
    }
    return TRUE;
}
```

**bootstrap_thread 时序**：

```
[0ms]    Sleep(150) 沉降
[150ms]  轮询 mono 模块（每 100ms，最多 60s）：
         mono-2.0-bdwgc.dll → mono-2.0-sgen.dll → mono.dll 依次尝试
         （GetModuleHandleW + GetModuleFileNameW 校验路径含 Mono 字样）
[就绪]   mono_bridge 初始化：
         - GetProcAddress 解析全部 mono 导出（见 4.3）
         - mono_get_root_domain()           → 失败 = 域未就绪（重试）
         - mono_thread_attach(domain)       → 失败 = 错误码 3
[域就绪] 落地内嵌 Vape.dll：
         - FindResourceW(RCDATA, ID=421) → LockResource
         - %TEMP%\SSJJVape\vape-%pid%.dll（Vape 4.21 同款临时目录方案）
[落地]   mono_assembly_load_from_full(内存加载) 或 mono_domain_assembly_open(路径)
         → mono_assembly_get_image
         → mono_class_from_name(image, "Vape", "Loader")
         → mono_class_get_method_from_name(klass, "Load", 0)
         → mono_runtime_invoke(method, NULL, NULL, &exc)
         → 若失败，尝试短入口 ("t", "u", "i")
[完成]   pin 模块（GET_MODULE_HANDLE_EX_FLAG_PIN），写 ssjj-native.log
```

**关键设计点**（对齐 Vape 4.21）：

1. **等待 mono 而非轮询 JVM**：`GetModuleHandleW` 轮询 `mono-2.0-bdwgc.dll`，等价于 Vape 等 `jvm.dll`。
2. **线程附着**：`mono_thread_attach` 等价于 `AttachCurrentThreadAsDaemon`——Mono 要求每个调用运行时 API 的线程注册 GC。
3. **载荷落地**：从 RCDATA 资源写出到 `%TEMP%`（Vape 4.21 的 `materialize_embedded_product_jar()` 同款逻辑，带 PID 后缀防多开冲突）。
4. **内存加载优先**：`mono_image_open_from_data_with_name(need_copy=TRUE)` + `mono_assembly_load_from_full` 可从内存直接加载，不落盘更隐蔽；失败时回退 `mono_domain_assembly_open` 走磁盘。
5. **双重入口尝试**：主入口 `Vape.Loader.Load` 失败 → 短入口 `t.u.i`（兼容现有 Assembly 两种入口）。
6. **幂等**：DLL 已加载（module path 存在）则直接返回码 2，不二次引导。
7. **JNI_OnLoad 等价路径（可选）**：Vape 支持 `-agentpath` 加载；Unity 对应的是把 DLL 放进游戏 `Plugins/x86_64/` 由 Unity 原生插件机制加载，并导出 `UnityPluginLoad`。作为可选增强，主路径仍走注入器。

### 4.3 Mono API 桥 `mono_bridge.c`（对标 `native_bridge.c`）

**动态解析**（不链接 mono 导入库，运行时 GetProcAddress，与 mono-rt 思路一致）：

```c
// 从已加载的 mono 模块解析导出；全部失败则视为"非 Mono 进程"
typedef MonoDomain*   (*fn_mono_get_root_domain)(void);
typedef MonoThread*   (*fn_mono_thread_attach)(MonoDomain*);
typedef MonoImage*    (*fn_mono_image_open_from_data_with_name)(
        char*, guint32, gboolean, MonoImageOpenStatus*, const char*);
typedef MonoAssembly* (*fn_mono_assembly_load_from_full)(
        MonoImage*, const char*, MonoImageOpenStatus*, gboolean);
typedef MonoAssembly* (*fn_mono_domain_assembly_open)(MonoDomain*, const char*);
typedef MonoImage*    (*fn_mono_assembly_get_image)(MonoAssembly*);
typedef MonoClass*    (*fn_mono_class_from_name)(MonoImage*, const char*, const char*);
typedef MonoMethod*   (*fn_mono_class_get_method_from_name)(MonoClass*, const char*, int);
typedef MonoObject*   (*fn_mono_runtime_invoke)(MonoMethod*, void*, void**, MonoObject**);
typedef MonoString*   (*fn_mono_string_new)(MonoDomain*, const char*);
```

**类型定义**（自建最小兼容头，不依赖完整 mono 头）：

```c
typedef struct _MonoDomain MonoDomain;
typedef struct _MonoAssembly MonoAssembly;
typedef struct _MonoImage MonoImage;
typedef struct _MonoClass MonoClass;
typedef struct _MonoMethod MonoMethod;
typedef struct _MonoObject MonoObject;
typedef struct _MonoThread MonoThread;
typedef struct _MonoString MonoString;
typedef int MonoImageOpenStatus;   // MONO_IMAGE_OK=0
typedef int gboolean;              // TRUE=1
typedef unsigned int guint32;
```

> Unity 的 `mono-2.0-bdwgc.dll` 保留了标准 Mono 嵌入式 API 导出（SharpMonoInjector/MonoJabber 等均依赖这些导出，已被证明可用）。

### 4.4 错误码表（对齐 Vape 4.21 设计风格）

| 码 | 含义 |
| --- | --- |
| 0 | 注入成功，`NativeBridge` 已启动 |
| 1 | 进程打开失败 / 权限不足 |
| 2 | DLL 已加载（幂等返回） |
| 3 | 目标非 x64，拒绝注入 |
| 4 | mono 运行时 60s 内未加载 |
| 5 | mono 导出解析失败（非标准 Mono 或导出被裁剪） |
| 6 | `mono_get_root_domain()` 返回 NULL / 域未就绪 |
| 7 | `mono_thread_attach` 失败 |
| 8 | 内嵌 Vape.dll 资源缺失/损坏（非 PK 魔数） |
| 9 | 载荷落地写盘失败 |
| 10 | 程序集加载失败（image open / assembly load） |
| 11 | 入口类未找到（`Vape.Loader` 与 `t.u` 均失败） |
| 12 | 入口方法未找到（`Load` / `i`） |
| 13 | `mono_runtime_invoke` 抛出托管异常（记入日志） |

错误码通过：
- **返回码**：注入器进程退出码（0 成功，非 0 见上表）
- **日志**：DLL 同目录 `ssjj-native.log`（`vape_log` 同款，带时间戳 UTF-8）

### 4.5 GameGuard 对抗设计（`AntiCheatBypass.cs` 下沉或协同）

| 层 | 现方案（托管） | 新方案（原生优先） |
| --- | --- | --- |
| 窗口欺骗 | `FindWindowA/ExA → NULL`（托管 P/Invoke IAT hook） | **原生层**：hook 更早生效，不依赖托管运行时已就绪 |
| 调试器检测 | `IsDebuggerPresent → FALSE` | 原生层 + 注入器自身 `CheckRemoteDebuggerPresent` 规避 |
| 进程枚举 | `Process32FirstW/NextW` 过滤 | 原生层同样过滤 |
| 内置检测 | RuntimeCheatGuard 特征码 patch | 保留托管层（运行时补丁），原生层负责早启动 |
| 注入时机 | 手动 | **等待游戏窗口出现 + mono 就绪 + Assembly-CSharp 稳定 4s**（ZModManager 同款门控） |

> 原则：**原生层负责"活着进场"（隐藏 + 注入），托管层负责"活着运行"（运行时 patch 游戏内置检测）**。
> GG IAT hook 在原生 DLL 里做，比在 `Vape.dll` 里做早一个阶段，且不受托管层失败影响。

### 4.6 时序图

```text
 用户                   注入器              游戏进程(GG)             mono运行时
  │                       │                    │                      │
  │ 启动微端               │                    │                      │
  │───────────────────────▶│                    │                      │
  │                       │              GameMon64.des 加载            │
  │                       │                    │                      │
  │ 进大厅/战斗            │                    │                      │
  │───────────────────────▶│                    │                      │
  │                       │ 枚举窗口标题匹配      │                      │
  │ 选择进程+Enter         │                    │                      │
  │───────────────────────▶│                    │                      │
  │                       │ OpenProcess(VM_*)   │                      │
  │                       │ VirtualAllocEx+WPM  │                      │
  │                       │ CreateRemoteThread  │                      │
  │                       │──────LoadLibraryW──▶│                      │
  │                       │                    │ DllMain→bootstrap线程   │
  │                       │                    │   │                   │
  │                       │                    │   ├─等 mono(≤60s)────▶│
  │                       │                    │   ├─get_root_domain    │
  │                       │                    │   ├─thread_attach      │
  │                       │                    │   ├─落地Vape.dll        │
  │                       │                    │   ├─assembly_load       │
  │                       │                    │   ├─class+method        │
  │                       │                    │   └─runtime_invoke Load()│
  │                       │                    │        │               │
  │                       │                    │   GameObject+Main 挂载  │
  │                       │                    │   F12 菜单可用          │
```

### 4.7 构建系统（对标 Vape 4.21 的 CMake）

```powershell
# 底层组件/ 下
cmake -S . -B build -A x64 -DVAPE_PAYLOAD="..\bin\x64\Release\Vape.dll"
cmake --build build --config Release
# 输出: build/dist/SSJJInjector.exe + SSJJNative.dll
```

`CMakeLists.txt` 要点：
- `configure_file` 生成 `payload.rc`，把 `Vape.dll` 作为 `RCDATA 421` 嵌入。
- 依赖仅 `kernel32`、`user32`、`advapi32`、`ws2_32`（预留 loader socket 通信），**不链接 mono 导入库**。
- MSVC `/O2`、`x64`、无 CRT 依赖项（`/MT`）。

`build_native.ps1` 一键脚本：检查 `bin\x64\Release\Vape.dll` 存在 → CMake 配置/构建 → 汇总 bundle。

### 4.8 与 SharpMonoInjector 方案对比

| 维度 | SharpMonoInjector（现） | 本设计（Vape 4.21 架构） |
| --- | --- | --- |
| 注入载荷 | 托管 shellcode（动态生成 ASM） | 原生 DLL + LoadLibraryW |
| 载荷载体 | 独立 Vape.dll 文件 | 内嵌 DLL 资源 |
| 工具依赖 | 需单独运行注入器 GUI | 自带 exe 选择器 |
| mono 交互 | shellcode 调 mono API | DLL 内部原生调用 |
| 反作弊暴露面 | CreateRemoteThread 模式固定 | 可换 NtCreateThreadEx / 可加原生 GG 对抗 |
| 二次开发 | 有限 | 原生桥可扩展（hook、输入、通信） |
| 复杂度 | 低 | 中（需维护原生工程） |

---

## 5. 风险与注意事项

1. **CreateRemoteThread 被 GG 监控**：默认路径保留，必要时切换到 `NtCreateThreadEx` 或注入前暂停 GG 线程（Skill 已有流程）。
2. **mono 导出裁剪**：个别 Unity 构建裁剪导出表；本设计用 `GetModuleFileNameW` 校验模块路径 + 导出解析失败码 5 暴露问题。实测目标（Unity 2019 微端）SharpMonoInjector 可用，证明导出完整。
3. **从内存加载 vs 落地**：内存加载（`mono_image_open_from_data_with_name`）无文件痕迹，但对含 `Assembly.Location` 依赖的代码不友好；本设计内存优先、磁盘回退。
4. **加载时机**：必须在 mono 根域就绪且 `Assembly-CSharp` 已加载后注入（Vape 载荷引用 UnityEngine 与 SSJJ* 程序集，需已在游戏域中）。
5. **GG IAT hook 在原生层**：原生 hook 需在 `Vape.dll` 的 `AntiCheatBypass` 之前建立，二者通过"谁先到谁生效 + watchdog 互认"避免冲突（详见 4.5）。
6. **多开/重复注入**：DLL 已加载则返回码 2 幂等；`Loader.Load()` 本身有 `_hookObject` 幂等保护。
7. **法律与合规**：仅供授权/隔离环境研究测试。

---

## 6. 实施步骤（确认后进入编码）

- [ ] **S1 实机确认**：游戏主进程 exe 名、窗口标题、mono DLL 实际文件名（跑一次 `GetModuleFileName` 或任务管理器确认）。
- [ ] **S2** 新建 `底层组件/` 工程骨架：CMakeLists + payload.rc.in + build_native.ps1。
- [ ] **S3** 实现 `mono_bridge.c/h`：导出解析 + 最小 mono 类型头。
- [ ] **S4** 实现 `dllmain.c`：bootstrap 线程 + 错误码 + 日志。
- [ ] **S5** 实现 `injector.c`：进程/窗口枚举 + LoadLibraryW 注入（含 NtCreateThreadEx 备选）。
- [ ] **S6** 构建 + 在授权隔离环境实机测试；GG 在场/不在场两种模式验证。
- [ ] **S7**（可选）GG 原生层 IAT hook 下沉 + 注入时机门控完善。
