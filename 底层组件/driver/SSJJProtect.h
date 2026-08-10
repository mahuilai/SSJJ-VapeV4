/* ====================================================================
 * SSJJProtect.h - SSJJDrv.sys 进程句柄保护模块对外接口。
 *
 * 该头文件同时定义用户态/内核态共享的 METHOD_BUFFERED
 * IOCTL 协议。用户态工程应先包含 Windows.h；内核工程应先包含
 * ntddk.h，再包含本文件。
 * ==================================================================== */
#ifndef SSJJ_PROTECT_H
#define SSJJ_PROTECT_H

#include "SSJJDrv.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 与 SSJJDrv.h 中的 IOCTL 编号空间共用同一个基值。 */
#ifndef SSJJ_IOCTL_BASE
#define SSJJ_IOCTL_BASE 0x9000
#endif

#define SSJJ_IOCTL_PROTECT CTL_CODE(FILE_DEVICE_UNKNOWN,                  \
        SSJJ_IOCTL_BASE + 2, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define SSJJ_PROTECT_DISABLE 0UL
#define SSJJ_PROTECT_ENABLE  1UL
#define SSJJ_PROTECT_IMAGE_CHARS 64

/*
 * SSJJ_IOCTL_PROTECT 输入结构。
 *
 * GamePid    : 目标 PID；ImageName 非空时只作记录，匹配优先看名字。
 * TrustedPid : 受信任进程 PID；0 表示发起当前 IOCTL 的进程。
 * Action     : 1 启用，0 关闭。
 * ImageName  : 目标可执行文件的基本名，如 L"TargetApp.exe"。
 */
typedef struct _SSJJ_PROTECT_REQ {
    ULONG GamePid;
    ULONG TrustedPid;
    ULONG Action;
    WCHAR ImageName[SSJJ_PROTECT_IMAGE_CHARS];
} SSJJ_PROTECT_REQ, *PSSJJ_PROTECT_REQ;

/* 下列函数只在内核工程中可见，避免用户态头文件缺少 NTSTATUS。 */
#if defined(_NTDDK_) || defined(_WDMDDK_) || defined(_NTIFS_)
NTSTATUS SSJJProtectInit(VOID);
NTSTATUS SSJJProtectHandleIoctl(
        _In_reads_bytes_(inputLen) PVOID input,
        _In_ ULONG inputLen,
        _Out_writes_bytes_opt_(outputLen) PVOID output,
        _In_ ULONG outputLen);
VOID SSJJProtectCleanup(VOID);
BOOLEAN SSJJProtectIsActive(VOID);
#endif

#ifdef __cplusplus
}
#endif

#endif /* SSJJ_PROTECT_H */
