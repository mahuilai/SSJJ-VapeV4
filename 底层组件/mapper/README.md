# ssjj_mapper - kdmapper 风格手动映射器

把未签名的 `SSJJDrv.sys` 免签名加载进内核，保留其全部功能
（Zw* 注入 + ObRegisterCallbacks 进程保护）。

## 架构

```
injector.c (Vape.exe)
  └─ loader_bridge.cpp (extern "C")
       ├─ intel_driver (provider): iqvw64e.sys 漏洞驱动
       │    随机驱动名 → %TEMP% 释放 → 服务注册 → NtLoadDriver
       │    → \\.\Nal 句柄 → R/W 原语 + 内核导出解析 + NtAddAtom
       │      hook 调用内核函数 → 清理痕迹(PiDDB/HashBucket/MmUnloaded)
       └─ mapper.cpp (PE 手动映射)
            读 .sys → AllocatePool → 拷贝 headers/sections →
            重定位 → 导入解析(ntoskrnl 导出) → 构造 DRIVER_OBJECT →
            CallKernelFunction(DriverEntry)
```

## 目录

| 文件 | 来源 | 职责 |
|------|------|------|
| `mapper.cpp/.h` | 新写 | PE 手动映射核心 |
| `loader_bridge.cpp/.h` | 新写 | extern "C" 桥 |
| `general.h` | meme-rw 裁剪 | 公共头（去掉 demo 依赖） |
| `provider/intel_driver.cpp/.h` | meme-rw | iqvw64e.sys provider + R/W 原语 |
| `provider/service.cpp/.h` | meme-rw | 驱动服务注册/加载 |
| `provider/utils.cpp/.h` | meme-rw | 工具（模块基址/文件 IO） |
| `provider/nt.h` | meme-rw | 内核结构定义 |
| `provider/driver_resource.h` | meme-rw | 内嵌 iqvw64e.sys 二进制 |

> provider 代码来自 [SamuelTulach/meme-rw](https://github.com/SamuelTulach/meme-rw)
> （kdmapper by z175 的 fork），MIT 生态。

## 部署（使用前提，重要）

1. **Windows 安全中心排除**：Defender 内置 LOLDrivers 检测，会拦截
   iqvw64e.sys。加载前需排除：
   ```
   设置 → 隐私和安全性 → Windows 安全中心 → 病毒和威胁防护 →
   管理设置 → 排除项 → 添加排除 → 文件 → iqvw64e.sys
   （或排除 Vape.exe 所在目录）
   ```
2. **系统要求**：HVCI/VBS 必须关闭（否则漏洞驱动被阻止列表拦截）。
   本机 Win11 26200 HVCI=0 满足。
3. **管理员权限**：Vape.exe 自动提权（manifest）。

## 使用

```powershell
# 默认：kdmapper 方式加载（免签名）
.\Vape.exe

# 回退：NtLoadDriver（需签名/测试模式）
.\Vape.exe --legacy-load
```

加载成功后 `\\.\SSJJDrv` 设备可用，注入 + 保护逻辑与原来完全一致。

## 验证 / 回滚

- 映射成功标志：`[loader] SSJJDrv.sys mapped` 输出 + `\\.\SSJJDrv` 可打开
- 保护验证：`OpenProcess(PROCESS_ALL_ACCESS)` 应失败(5)，
  `PROCESS_QUERY_INFORMATION` 应成功
- **蓝屏风险**：手动映射 + DriverEntry 执行属高危操作。
  首次务必在虚拟机/隔离环境测试，确认稳定后再用于实机。
- 回滚：重启系统即清除全部（映射驱动是内存态，无持久化）。

## 已知限制

- 映射驱动无正常卸载路径：`ssjj_loader_unload_driver()` 通过
  DriverUnload（注销 Ob 回调/删设备）后释放内存实现，但不受系统
  管理（重启最干净）。
- `CallKernelFunction` 用 NtAddAtom 短时 hook，多线程竞态理论上
  存在（一次性映射场景可接受）。
- iqvw64e.sys 若被微软黑名单更新覆盖（HVCI 未来开启），需更换
  provider（接口已解耦，替换 `intel_driver` 即可）。
