/* ====================================================================
 * mapper.h - 内核驱动手动映射器（kdmapper 风格，x64）
 *
 * 职责：通过 provider（intel_driver，iqvw64e.sys 漏洞驱动）的 R/W
 * 原语，把任意未签名 .sys 手动映射进内核并调用其 DriverEntry。
 *
 * 核心流程：
 *   读 PE → 分配内核内存 → 拷贝 headers/sections → 重定位 →
 *   导入解析（kernel import table）→ 构造 PDRIVER_OBJECT →
 *   调用 DriverEntry → （可选）调用 DriverUnload 卸载。
 *
 * 注意：本模块只做"通用 PE 加载"（中性技术）。provider 的加载
 * 与漏洞驱动二进制来源见 provider/ 与 README.md。
 * ==================================================================== */
#ifndef SSJJ_MAPPER_H
#define SSJJ_MAPPER_H

#include <Windows.h>
#include <cstdint>
#include <string>

/* ------------------------------------------------------------------ */
/* x64 DRIVER_OBJECT / DRIVER_EXTENSION 自声明（用户态无完整定义）     */
/* ------------------------------------------------------------------ */
#define IO_TYPE_DRIVER 0x104

/* 与 WDK 的 _DRIVER_OBJECT 布局一致（x64）。 */
typedef struct _SSJJ_DRIVER_OBJECT {
    SHORT  Type;                 /* 0x000 IO_TYPE_DRIVER */
    SHORT  Size;                 /* 0x002 sizeof */
    /* 0x004 padding */
    PVOID  DeviceObject;         /* 0x008 */
    ULONG  Flags;                /* 0x010 */
    /* 0x014 padding */
    PVOID  DriverStart;          /* 0x018 */
    ULONG  DriverSize;           /* 0x020 */
    /* 0x024 padding */
    PVOID  DriverSection;        /* 0x028 */
    PVOID  DriverExtension;      /* 0x030 */
    UNICODE_STRING DriverName;   /* 0x038 */
    PVOID  HardwareDatabase;     /* 0x048 */
    PVOID  FastIoDispatch;       /* 0x050 */
    PVOID  DriverInit;           /* 0x058 */
    PVOID  DriverStartIo;        /* 0x060 */
    PVOID  DriverUnload;         /* 0x068 */
    PVOID  MajorFunction[28];    /* 0x070 */
} SSJJ_DRIVER_OBJECT;
#define SSJJ_DRIVER_OBJECT_SIZE 0x150

typedef struct _SSJJ_DRIVER_EXTENSION {
    PVOID  DriverObject;         /* 0x000 */
    PVOID  AddDevice;            /* 0x008 */
    ULONG  Count;                /* 0x010 */
    /* 0x014 padding */
    UNICODE_STRING ServiceKeyName; /* 0x018 */
    /* 其余字段映射驱动用不到 */
} SSJJ_DRIVER_EXTENSION;
#define SSJJ_DRIVER_EXTENSION_SIZE 0x28

/* 映射结果 */
struct SSJJ_MAPPED_DRIVER {
    uint64_t base = 0;             /* 内核映射基址 */
    uint64_t driver_object = 0;    /* 内核 DRIVER_OBJECT 地址 */
    uint64_t entry_point = 0;      /* DriverEntry 内核地址 */
    uint64_t unload_routine = 0;   /* DriverUnload 内核地址（可 0） */
    uint32_t image_size = 0;       /* SizeOfImage */
};

namespace ssjj_mapper {

/* 把 driver_path 指定的 .sys 手动映射进内核并调用 DriverEntry。
 * device: provider 设备句柄（intel_driver::Load 的返回值）。
 * 成功返回 true 并填充 out；失败返回 false。 */
bool MapDriver(HANDLE device, const std::wstring& driver_path,
               SSJJ_MAPPED_DRIVER& out);

/* 卸载映射的驱动：调用 DriverUnload（注销 Ob 回调/删设备）→
 * 释放 DRIVER_OBJECT → 释放驱动代码页。 */
bool UnmapDriver(HANDLE device, const SSJJ_MAPPED_DRIVER& drv);

} /* namespace ssjj_mapper */

#endif /* SSJJ_MAPPER_H */
