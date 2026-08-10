# SSJJ Native Injector

面向 **生死狙击微端（Unity/Mono）** 的自包含原生注入器，架构对标 Vape 4.21
（`D:\项目\vapev4.21\native\`）：先做原生 DLL 注入，再由 DLL 在进程内附着
Mono 运行时，加载内嵌的托管载荷。

## 组成

| 文件 | 说明 |
| --- | --- |
| `Vape.exe` | **自包含注入器**：内嵌 SSJJNative.dll（RCDATA 422）与产品图标，运行时释放到 %TEMP% 再**驱动级注入** |
| `SSJJDrv.sys` | **内核驱动**（签名，与 Vape.exe 同目录）：Zw* 注入 + ObRegisterCallbacks 进程句柄保护（阻止 GameGuard 附加/注入），注入完成启用保护后保持加载 |
| `SSJJNative.dll` | 引导 DLL（内嵌于注入器；单独保留便于开发调试）：等 mono → 附着根域 → 加载内嵌 Vape.dll → 调入口 |

> 交付需 `Vape.exe` + 签名后的 `SSJJDrv.sys` 两个文件（同目录）。
> 开发调试时也可显式指定外部 DLL：`Vape.exe <pid> <SSJJNative.dll>`。

## 引导流程

```
Vape.exe (注入器, 已提权)
  ├─ 释放 SSJJNative.dll 到 %TEMP%\SSJJVape\tmp-<rand>.dll (L3 解密)
  ├─ NtLoadDriver 加载 SSJJDrv.sys (临时服务键, 无 SCM 记录)
  │    └─ DeviceIoControl(IOCTL_INJECT, PID + DLL路径)
  │         └─ 驱动: ZwOpenProcess → ZwAllocateVirtualMemory →
  │              ZwWriteVirtualMemory → ZwCreateThreadEx(LoadLibraryW)
  ├─ 注入完成 → DeviceIoControl(IOCTL_PROTECT) 启用进程保护
  │    └─ 驱动: ObRegisterCallbacks 拦截进程句柄
  │         未授权 OpenProcess(VM_WRITE/CREATE_THREAD...) 被降权
  │         注入器 PID 记为受信 → 自身操作不受影响
  ├─ 驱动保持加载（保护持续），按任意键退出 → 关闭保护 + 卸载
  └─ SSJJNative.dll
       ├─ 等 mono 运行时（mono-2.0-bdwgc.dll → sgen → mono.dll，≤60s）
       ├─ mono_get_root_domain → mono_thread_attach
       ├─ 内嵌 Vape.dll (RCDATA 421) 落地 %TEMP%\SSJJVape\tmp-<rand>.dll
       ├─ 内存加载优先（mono_image_open_from_data_with_name + load_from_full）
       │    磁盘回退（mono_domain_assembly_open）
       ├─ 入口：Vape.Loader.Load()（回退 t.u.i()）
       └─ mono_runtime_invoke → F12 菜单
```

> 注入全程不走 `OpenProcess`/`WriteProcessMemory`/`CreateRemoteThread`——
> GameGuard 用户态 hook 完全无效，跨进程操作全部在内核 SYSTEM 上下文完成。
>
> **进程保护**：注入完成后驱动通过 `ObRegisterCallbacks` 拦截对游戏进程的
> 句柄创建/复制，未授权进程拿不到 `VM_WRITE/VM_OPERATION/CREATE_THREAD/`
> `SUSPEND_RESUME/TERMINATE/VM_READ` 权限 → GameGuard 无法再附加/注入。
> 保留 `QUERY_INFORMATION`（能看见但碰不到）。受信 PID（注入器）不受影响。

## 构建

前置：Visual Studio 2022（x64 C++）、CMake ≥ 3.21、**WDK**（驱动编译需要 km 头文件）、
先构建好 `bin\x64\Release\Vape.dll`。

```powershell
# 一键（含驱动构建 + 签名）
.\build_native.ps1 -BuildDriver -CertPfx "D:\cert\vape.pfx" -CertPassword "xxx"

# 仅用户态
.\build_native.ps1

# 仅驱动（需 WDK，可选签名）
.\build_driver.ps1 -CertPfx "D:\cert\vape.pfx" -CertPassword "xxx"
.\build_driver.ps1 -CertThumbprint "AA..FF"

# 或手动
cmake -S . -B build -A x64 -DVAPE_PAYLOAD="..\bin\x64\Release\Vape.dll"
cmake --build build --config Release
```

产物在 `build/dist/`：`Vape.exe` + `SSJJDrv.sys`（+ VMProtect 运行库）。

## 使用（双击即用）

1. 启动游戏 `SSJJ_BattleClient_Unity.exe`，进入大厅/战斗场景。
2. **双击 `Vape.exe`** → UAC 提权 → 自动检测游戏进程并注入。
   - 需要签名后的 `SSJJDrv.sys` 与 `Vape.exe` 同目录。
   - 未签名/测试签名驱动：Win10/11 x64 默认拒绝加载（需 EV 签名或 attestation 签名）。
3. 若未检测到游戏进程，显示"未检测到游戏进程，请启动游戏并进入地图后加载..."，**按任意键关闭程序**。
4. 注入成功后，游戏内按 **F12** 开关菜单；窗口会保持到按任意键关闭。

其他模式：

```powershell
# 手动选择进程（交互选择器，带 Logo 菜单）
.\Vape.exe --manual

# 脚本注入（开发模式，显式指定外部 DLL）
.\Vape.exe 12345 .\SSJJNative.dll

# 帮助
.\Vape.exe --help
```

## 加密流水线（托管载荷混淆）

交付版 `Vape.exe` 内嵌的托管 `Vape.dll` 默认经过 **ConfuserEx v1.6.0** 混淆
（字符串加密 + 控制流）。

> ⚠️ **重命名（rename）已禁用**：与 Unity Mono 不兼容——
> ① MonoBehaviour 的 `Awake/Update/OnGUI` 等魔法方法是 Unity 按方法名反射调用的，
> 改名后 Unity 不会调用，组件全部失效；② `HookManager` 用 `nameof()`+`GetMethod`
> 反射自己的 private 方法，改名后 Hook 静默失败。故只保留 constants + ctrl flow。

一键命令（改完 C# 代码后跑这个，自动混淆→验证→打包）：

```powershell
.\protect_payload.ps1
# 或指定托管 DLL
.\protect_payload.ps1 -ManagedDll "D:\path\to\Vape.dll"
```

流程：复制依赖 → ConfuserEx（配置 `D:\加密\ConfuserEx\vape.crproj`）→
`verify2.exe` 验证（入口保留 + ldstr 已加密，失败即中止）→ 原生打包。

已知（可接受）：
- 2 个 `const` 字符串字段（`"vp_bootstrap"` / `"vp_runtime_root"`，仅 GameObject 名）
  在元数据 Constant 表以明文存在，ConfuserEx 不覆盖，低风险。

### L3 注入器资源加密（已实现）

- 构建期：`ssjj_encrypt.exe`（CMake 自动编译）用随机 32 字节密钥（`build/ssjj_key.h`，
  首次配置生成并持久化）对 `SSJJNative.dll` 做流密码加密 → 密文作为 RCDATA 422 嵌入
  `Vape.exe`。磁盘上无明文 DLL。
- 运行期：`injector.c` 释放前解密 → 校验 MZ → 写 `%TEMP%\SSJJVape\` → 注入 → 删除。
- 算法：keyed xorshift128+ 流密码（`ssjj_crypto.h`），加密==解密。
- 验证：RCDATA 422 头字节非 MZ；解密后与 SSJJNative.dll SHA256 一致。

后续层：~~L4~~ ✅ 已完成（见下）。

### L4 VMProtect 加壳 + 机器锁（已实现）

- `Vape.exe` 经 VMProtect 3.5 加壳：`lock_check` 标记虚拟化（Virtualization）+
  全模块 Mutation + 内置反调试/反虚拟机检查。
- 机器锁：启动即检测 调试器 / 虚拟机，命中弹 **"有锁机!"** 并退出。
  - VMP SDK：`VMProtectIsDebuggerPresent` / `VMProtectIsVirtualMachinePresent`
  - 自研兜底：IsDebuggerPresent + NtQueryInformationProcess(调试端口/标志/句柄)、
    CPUID 虚拟机位、BIOS/厂商字符串、VM 驱动文件、VM 注册表键
- 交付需要 **3 个文件**：`Vape.exe` + `VMProtect_Ext64.dll` + `VMProtectSDK64.dll`（同目录）。
- 一键流程已含 VMP 阶段：`protect_payload.ps1` 5/5 自动加壳。
- 注：`SSJJNative.dll` 未加 VMP（它被注入游戏进程，VMP 特征可能被 GameGuard 扫到；
  其保护由 L3 资源加密 + 内部托管载荷的 L1 ConfuserEx 承担）。

## 退出码 / 错误码

### Vape.exe（注入器）

| 码 | 含义 |
| --- | --- |
| 0 | 注入成功 + 进程保护已激活（驱动保持加载；按任意键退出时解除并卸载） |
| 1 | 用法错误 / 无进程 / 内嵌 DLL 缺失 / 注入成功但保护启用失败（驱动已卸载） |
| 2 | DLL 已加载（幂等返回） |
| 5 | 注入失败（驱动加载 / 驱动 IOCTL / 未映射） |

### SSJJNative.dll bootstrap（见 ssjj-native.log）

| 码 | 含义 |
| --- | --- |
| 0 | 成功，`Vape.Loader.Load` 已执行 |
| 4 | mono 运行时 60s 内未加载 |
| 5 | mono 导出解析失败 |
| 6 | `mono_get_root_domain` 返回 NULL |
| 7 | `mono_thread_attach` 失败 |
| 8 | 内嵌 Vape.dll 资源缺失/损坏 |
| 9 | 载荷落地写盘失败 |
| 10 | 程序集加载失败（内存与磁盘均失败） |
| 11 | 入口类未找到（Vape.Loader / t.u） |
| 12 | 入口方法未找到（Load / i） |
| 13 | `mono_runtime_invoke` 抛出托管异常 |

## 目标

- 进程：`SSJJ_BattleClient_Unity.exe`（兼容 SSJJ* 名称与窗口标题兜底）
- 运行时：Unity 2019+ → `MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll`（自动探测）
- 载荷：`Vape.dll`（.NET Framework 4.8 / x64）
