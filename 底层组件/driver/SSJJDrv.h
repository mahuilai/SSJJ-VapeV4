/* ====================================================================
 * SSJJDrv.h - shared definitions between SSJJDrv.sys (kernel) and
 *             Vape.exe (user-mode injector).
 *
 * The injector never touches OpenProcess/WriteProcessMemory/
 * CreateRemoteThread anymore; all cross-process work happens in the
 * driver via Zw* APIs, which GameGuard's user-mode hooks cannot see.
 *
 * Driver is loaded with NtLoadDriver (no SCM service record) and
 * unloaded immediately after the injection completes to minimize the
 * kernel exposure window.
 * ==================================================================== */
#ifndef SSJJ_DRV_H
#define SSJJ_DRV_H

/* CTL_CODE / FILE_DEVICE_UNKNOWN / METHOD_BUFFERED / FILE_ANY_ACCESS are
 * provided by wdm.h (kernel build) or winioctl.h via windows.h (user
 * build). Do NOT include winioctl.h here: the WDK layout may pull a
 * different SDK version into the kernel compile. */

#ifdef __cplusplus
extern "C" {
#endif

/* Device / symbolic link names */
#define SSJJ_DEVICE_NAME    L"\\Device\\SSJJDrv"
#define SSJJ_SYMLINK_NAME   L"\\??\\SSJJDrv"

/* Registry key used for NtLoadDriver (deleted after load/unload). */
#define SSJJ_DRV_SERVICE_KEY L"\\Registry\\Machine\\System\\CurrentControlSet\\Services\\SSJJDrv"
#define SSJJ_DRV_SERVICE_NAME L"SSJJDrv"

/* IOCTL codes (METHOD_BUFFERED) */
#define SSJJ_IOCTL_BASE      0x9000
#define SSJJ_IOCTL_QUERY_X64 CTL_CODE(FILE_DEVICE_UNKNOWN, SSJJ_IOCTL_BASE + 0, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define SSJJ_IOCTL_INJECT    CTL_CODE(FILE_DEVICE_UNKNOWN, SSJJ_IOCTL_BASE + 1, METHOD_BUFFERED, FILE_ANY_ACCESS)

/* Injected DLL path size (wide chars, including terminator). */
#define SSJJ_MAX_DLL_PATH 520

/* Request for SSJJ_IOCTL_INJECT.
 * The driver resolves kernel32!LoadLibraryW itself by walking the target
 * PEB loader table; LoadLibraryOffset is passed only as a sanity hint
 * (GetProcAddress(kernel32, LoadLibraryW) - kernel32 base, local copy). */
typedef struct _SSJJ_INJECT_REQ {
    ULONG   ProcessId;          /* target PID */
    ULONGLONG LoadLibraryOffset;/* local offset hint (optional, 0 = ignore) */
    WCHAR   DllPath[SSJJ_MAX_DLL_PATH];
} SSJJ_INJECT_REQ;

/* Result of SSJJ_IOCTL_INJECT (ULONG output). */
#define SSJJ_INJ_OK                0
#define SSJJ_INJ_ERR_OPEN         1   /* ZwOpenProcess failed */
#define SSJJ_INJ_ERR_ALLOC        2   /* ZwAllocateVirtualMemory failed */
#define SSJJ_INJ_ERR_WRITE        3   /* ZwWriteVirtualMemory failed */
#define SSJJ_INJ_ERR_RESOLVE      4   /* kernel32!LoadLibraryW not found */
#define SSJJ_INJ_ERR_CREATE       5   /* ZwCreateThreadEx failed */
#define SSJJ_INJ_ERR_WAIT         6   /* thread did not finish */
#define SSJJ_INJ_ERR_BADPID       7   /* process not found */
#define SSJJ_INJ_ERR_LOAD         8   /* LoadLibraryW returned 0 */

/* SSJJ_IOCTL_QUERY_X64: input ULONG PID, output BOOLEAN (1 = native x64) */
#define SSJJ_ARCH_ERR_NO_PROC     0xFFFFFFFF  /* output value when lookup fails */

#ifdef __cplusplus
}
#endif

#endif /* SSJJ_DRV_H */
