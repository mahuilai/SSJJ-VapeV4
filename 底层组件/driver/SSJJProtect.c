/* ====================================================================
 * SSJJProtect.c - 基于 ObRegisterCallbacks 的进程句柄保护。
 *
 * 原理：用户态 OpenProcess/NtOpenProcess 或 DuplicateHandle 在真正
 * 创建/复制进程句柄前会进入本模块的 pre-operation 回调。
 * 对未授权请求清除写内存、分配内存、创建线程、挂起、终止和
 * 读内存权限，但保留 PROCESS_QUERY_INFORMATION。
 *
 * 本文件不修改 EPROCESS，不使用任何硬编码内核结构偏移，因此
 * 不触发 ActiveProcessLinks/SSDT 类 PatchGuard 风险。
 * ==================================================================== */
#include <ntddk.h>

#include "SSJJProtect.h"

/* ------------------------------------------------------------------ */
/* 对精简/旧 WDK 的 Ob 回调定义兼容层。                       */
/* 新版 wdm.h/ntddk.h 已自带这些定义，下面代码不会重复声明。 */
/* ------------------------------------------------------------------ */
#ifndef OB_FLT_REGISTRATION_VERSION
#define OB_FLT_REGISTRATION_VERSION_0100 0x0100
#define OB_FLT_REGISTRATION_VERSION OB_FLT_REGISTRATION_VERSION_0100

typedef ULONG OB_OPERATION;
#define OB_OPERATION_HANDLE_CREATE    0x00000001
#define OB_OPERATION_HANDLE_DUPLICATE 0x00000002

typedef struct _OB_PRE_CREATE_HANDLE_INFORMATION {
    ACCESS_MASK DesiredAccess;
    ACCESS_MASK OriginalDesiredAccess;
} OB_PRE_CREATE_HANDLE_INFORMATION, *POB_PRE_CREATE_HANDLE_INFORMATION;

typedef struct _OB_PRE_DUPLICATE_HANDLE_INFORMATION {
    ACCESS_MASK DesiredAccess;
    ACCESS_MASK OriginalDesiredAccess;
    PVOID SourceProcess;
    PVOID TargetProcess;
} OB_PRE_DUPLICATE_HANDLE_INFORMATION,
        *POB_PRE_DUPLICATE_HANDLE_INFORMATION;

typedef union _OB_PRE_OPERATION_PARAMETERS {
    OB_PRE_CREATE_HANDLE_INFORMATION CreateHandleInformation;
    OB_PRE_DUPLICATE_HANDLE_INFORMATION DuplicateHandleInformation;
} OB_PRE_OPERATION_PARAMETERS, *POB_PRE_OPERATION_PARAMETERS;

typedef struct _OB_PRE_OPERATION_INFORMATION {
    OB_OPERATION Operation;
    union {
        ULONG Flags;
        struct {
            ULONG KernelHandle : 1;
            ULONG Reserved : 31;
        };
    };
    PVOID Object;
    POBJECT_TYPE ObjectType;
    PVOID CallContext;
    POB_PRE_OPERATION_PARAMETERS Parameters;
} OB_PRE_OPERATION_INFORMATION, *POB_PRE_OPERATION_INFORMATION;

typedef enum _OB_PREOP_CALLBACK_STATUS {
    OB_PREOP_SUCCESS
} OB_PREOP_CALLBACK_STATUS;

typedef OB_PREOP_CALLBACK_STATUS
(*POB_PRE_OPERATION_CALLBACK)(
        PVOID RegistrationContext,
        POB_PRE_OPERATION_INFORMATION OperationInformation);

typedef VOID
(*POB_POST_OPERATION_CALLBACK)(PVOID RegistrationContext, PVOID Information);

typedef struct _OB_OPERATION_REGISTRATION {
    POBJECT_TYPE *ObjectType;
    OB_OPERATION Operations;
    POB_PRE_OPERATION_CALLBACK PreOperation;
    POB_POST_OPERATION_CALLBACK PostOperation;
} OB_OPERATION_REGISTRATION, *POB_OPERATION_REGISTRATION;

typedef struct _OB_CALLBACK_REGISTRATION {
    USHORT Version;
    USHORT OperationRegistrationCount;
    UNICODE_STRING Altitude;
    PVOID RegistrationContext;
    POB_OPERATION_REGISTRATION OperationRegistration;
} OB_CALLBACK_REGISTRATION, *POB_CALLBACK_REGISTRATION;

NTKERNELAPI NTSTATUS ObRegisterCallbacks(
        POB_CALLBACK_REGISTRATION CallbackRegistration,
        PVOID *RegistrationHandle);
NTKERNELAPI VOID ObUnRegisterCallbacks(PVOID RegistrationHandle);
#endif

/* PsGetProcessImageFileName 是 ntoskrnl 导出，但部分 WDK 未在公开
 * 头文件中声明。它返回 ANSI PCHAR，并且只保留约 15 个字节。 */
NTKERNELAPI PCHAR NTAPI PsGetProcessImageFileName(
        _In_ PEPROCESS Process);
NTKERNELAPI NTSTATUS NTAPI PsLookupProcessByProcessId(
        _In_ HANDLE ProcessId,
        _Outptr_ PEPROCESS *Process);

/* /Zl 不自动引入 CRT 库；_stricmp 由 ntoskrnl.lib 导出。 */
int __cdecl _stricmp(_In_z_ const char *left, _In_z_ const char *right);

/* ------------------------------------------------------------------ */
/* 访问权限和固定参数。                                           */
/* ------------------------------------------------------------------ */
#ifndef PROCESS_TERMINATE
#define PROCESS_TERMINATE      0x0001
#endif
#ifndef PROCESS_CREATE_THREAD
#define PROCESS_CREATE_THREAD  0x0002
#endif
#ifndef PROCESS_VM_OPERATION
#define PROCESS_VM_OPERATION   0x0008
#endif
#ifndef PROCESS_VM_READ
#define PROCESS_VM_READ        0x0010
#endif
#ifndef PROCESS_VM_WRITE
#define PROCESS_VM_WRITE       0x0020
#endif
#ifndef PROCESS_SUSPEND_RESUME
#define PROCESS_SUSPEND_RESUME 0x0800
#endif

#define SSJJ_DANGEROUS_PROCESS_ACCESS ((ACCESS_MASK)(                 \
        PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_CREATE_THREAD | \
        PROCESS_SUSPEND_RESUME | PROCESS_TERMINATE | PROCESS_VM_READ))

/* EPROCESS.ImageFileName/PsGetProcessImageFileName 的可比较上限。
 * 这里的 16 是 15 字节名字 + '\0'，不是硬编码 EPROCESS 偏移。 */
#define SSJJ_PROCESS_IMAGE_BYTES 16
#define SSJJ_SYSTEM_PID 4UL
#define SSJJ_CALLBACK_ALTITUDE L"399999"

typedef struct _SSJJ_PROTECT_STATE {
    /* 只有 IOCTL/卸载控制路径获取此锁；Ob 回调从不获取它。 */
    FAST_MUTEX ControlMutex;
    volatile LONG Initialized;
    volatile LONG FilterEnabled;
    volatile LONG Active;

    PVOID CallbackHandle;
    BOOLEAN ImageNotifyRegistered;

    ULONG GamePid;
    ULONG TrustedPid;
    ULONG OwnerPid;
    CHAR ImageName[SSJJ_PROCESS_IMAGE_BYTES];

    /* 引用进程对象而不只比较白名单 PID，防止 PID 复用造成误放行。 */
    PEPROCESS TrustedProcess;
    PEPROCESS OwnerProcess;
} SSJJ_PROTECT_STATE;

static SSJJ_PROTECT_STATE g_SSJJProtect;

/* x64 /Zp8 下协议必须保持 12 + 64*2 = 140 字节。 */
C_ASSERT(sizeof(SSJJ_PROTECT_REQ) == 140);

/* ------------------------------------------------------------------ */
/* 小型辅助函数。                                                   */
/* ------------------------------------------------------------------ */

/* 只做原子读，回调内不获取可等待锁。 */
static LONG SSJJReadLong(_In_ volatile LONG *value)
{
    return InterlockedCompareExchange((volatile LONG *)value, 0, 0);
}

/* 将用户的 WCHAR[64] 基本名转为回调可直接比较的 ANSI。
 * 转换发生在 PASSIVE_LEVEL IOCTL 路径，而非 Ob 回调热路径。 */
static NTSTATUS SSJJMakeAnsiImageName(
        _In_reads_(SSJJ_PROTECT_IMAGE_CHARS) const WCHAR *wideName,
        _Out_writes_(SSJJ_PROCESS_IMAGE_BYTES) CHAR *ansiName)
{
    const WCHAR *baseName = wideName;
    ULONG i;
    ULONG length = 0;
    UNICODE_STRING source;
    ANSI_STRING destination;
    NTSTATUS status;

    RtlZeroMemory(ansiName, SSJJ_PROCESS_IMAGE_BYTES);

    /* 同时容忍控制程序传入完整路径，只取最后的文件名。 */
    for (i = 0; i < SSJJ_PROTECT_IMAGE_CHARS; ++i) {
        WCHAR ch = wideName[i];
        if (ch == L'\0') {
            break;
        }
        if (ch == L'\\' || ch == L'/') {
            baseName = &wideName[i + 1];
        }
    }

    if (i == SSJJ_PROTECT_IMAGE_CHARS) {
        return STATUS_INVALID_PARAMETER; /* 输入未以 NUL 结尾 */
    }

    while (baseName[length] != L'\0' &&
           length < (SSJJ_PROCESS_IMAGE_BYTES - 1)) {
        ++length;
    }
    if (length == 0) {
        return STATUS_SUCCESS; /* 空名字表示使用 PID 匹配 */
    }

    source.Buffer = (PWCHAR)baseName;
    source.Length = (USHORT)(length * sizeof(WCHAR));
    source.MaximumLength = source.Length;

    destination.Buffer = ansiName;
    destination.Length = 0;
    destination.MaximumLength = SSJJ_PROCESS_IMAGE_BYTES;
    status = RtlUnicodeStringToAnsiString(&destination, &source, FALSE);
    if (!NT_SUCCESS(status)) {
        RtlZeroMemory(ansiName, SSJJ_PROCESS_IMAGE_BYTES);
        return status;
    }

    /* 无论 Rtl 实现是否追加 NUL，都强制保证 _stricmp 输入结尾。 */
    ansiName[SSJJ_PROCESS_IMAGE_BYTES - 1] = '\0';
    return STATUS_SUCCESS;
}

/* 名字非空时名字优先；只有名字为空时才回退到 PID。 */
static BOOLEAN SSJJIsProtectedProcess(_In_ PEPROCESS process)
{
    if (g_SSJJProtect.ImageName[0] != '\0') {
        CHAR currentName[SSJJ_PROCESS_IMAGE_BYTES];
        PCHAR rawName = PsGetProcessImageFileName(process);

        if (rawName == NULL) {
            return FALSE;
        }

        /* 不直接把 rawName 传给 _stricmp：极限长度时内核字段
         * 不一定包含 NUL，先复制 15 字节再强制终止更安全。 */
        RtlCopyMemory(currentName, rawName, SSJJ_PROCESS_IMAGE_BYTES - 1);
        currentName[SSJJ_PROCESS_IMAGE_BYTES - 1] = '\0';
        return (_stricmp(currentName, g_SSJJProtect.ImageName) == 0);
    }

    return HandleToULong(PsGetProcessId(process)) == g_SSJJProtect.GamePid;
}

static BOOLEAN SSJJIsTrustedCaller(VOID)
{
    PEPROCESS currentProcess = PsGetCurrentProcess();
    ULONG currentPid = HandleToULong(PsGetCurrentProcessId());

    if (currentPid == SSJJ_SYSTEM_PID) {
        return TRUE;
    }

    /* 按对象指针比较，进程退出后 PID 复用不会继承信任。 */
    return currentProcess == g_SSJJProtect.TrustedProcess ||
           currentProcess == g_SSJJProtect.OwnerProcess;
}

/* ------------------------------------------------------------------ */
/* Ob pre-operation 回调。                                           */
/* ------------------------------------------------------------------ */
static OB_PREOP_CALLBACK_STATUS SSJJProtectPreOperation(
        _In_ PVOID registrationContext,
        _Inout_ POB_PRE_OPERATION_INFORMATION operationInformation)
{
    ACCESS_MASK *desiredAccess = NULL;

    UNREFERENCED_PARAMETER(registrationContext);

    if (operationInformation == NULL ||
        SSJJReadLong(&g_SSJJProtect.FilterEnabled) == 0) {
        return OB_PREOP_SUCCESS;
    }

    /* 硬性要求：OBJ_KERNEL_HANDLE 对应的内核句柄始终放行。 */
    if (operationInformation->KernelHandle) {
        return OB_PREOP_SUCCESS;
    }

    if (operationInformation->ObjectType != *PsProcessType ||
        operationInformation->Object == NULL ||
        operationInformation->Parameters == NULL) {
        return OB_PREOP_SUCCESS;
    }

    if (SSJJIsTrustedCaller()) {
        return OB_PREOP_SUCCESS;
    }

    if (!SSJJIsProtectedProcess((PEPROCESS)operationInformation->Object)) {
        return OB_PREOP_SUCCESS;
    }

    /* CREATE 和 DUPLICATE 的 DesiredAccess 在联合体中是两个不同字段。 */
    if (operationInformation->Operation == OB_OPERATION_HANDLE_CREATE) {
        desiredAccess =
            &operationInformation->Parameters->CreateHandleInformation.DesiredAccess;
    } else if (operationInformation->Operation ==
               OB_OPERATION_HANDLE_DUPLICATE) {
        desiredAccess =
            &operationInformation->Parameters->DuplicateHandleInformation.DesiredAccess;
    }

    if (desiredAccess != NULL) {
        *desiredAccess &= ~SSJJ_DANGEROUS_PROCESS_ACCESS;
    }

    /* Ob pre callback 不直接返回拒绝状态；通过降权后继续创建句柄。 */
    return OB_PREOP_SUCCESS;
}

/* ------------------------------------------------------------------ */
/* 可选的映像加载审计回调：只记录，不在此路径阻塞 DLL。       */
/* ------------------------------------------------------------------ */
static BOOLEAN SSJJBaseNameEquals(
        _In_ PUNICODE_STRING fullName,
        _In_z_ PCWSTR expectedBaseName)
{
    UNICODE_STRING actual;
    UNICODE_STRING expected;
    USHORT chars;
    USHORT start;

    if (fullName == NULL || fullName->Buffer == NULL ||
        fullName->Length == 0) {
        return FALSE;
    }

    chars = (USHORT)(fullName->Length / sizeof(WCHAR));
    start = chars;
    while (start != 0) {
        WCHAR ch = fullName->Buffer[start - 1];
        if (ch == L'\\' || ch == L'/') {
            break;
        }
        --start;
    }

    actual.Buffer = &fullName->Buffer[start];
    actual.Length = (USHORT)((chars - start) * sizeof(WCHAR));
    actual.MaximumLength = actual.Length;
    RtlInitUnicodeString(&expected, expectedBaseName);
    return RtlEqualUnicodeString(&actual, &expected, TRUE);
}

static VOID SSJJProtectImageLoadNotify(
        _In_opt_ PUNICODE_STRING fullImageName,
        _In_ HANDLE processId,
        _In_ PIMAGE_INFO imageInfo)
{
    PEPROCESS process = NULL;
    BOOLEAN target;

    UNREFERENCED_PARAMETER(imageInfo);

    if (SSJJReadLong(&g_SSJJProtect.FilterEnabled) == 0 ||
        processId == NULL || fullImageName == NULL) {
        return;
    }

    if (!NT_SUCCESS(PsLookupProcessByProcessId(processId, &process)) ||
        process == NULL) {
        return;
    }

    target = SSJJIsProtectedProcess(process);
    ObDereferenceObject(process);
    if (!target) {
        return;
    }

    /* 在此表追加自定义黑名单项即可；通知回调只用于审计。 */
    if (SSJJBaseNameEquals(fullImageName, L"npgg64.dll")) {
        DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                "SSJJProtect: suspicious image in protected PID %lu: %wZ\n",
                HandleToULong(processId), fullImageName);
    }
}

/* ------------------------------------------------------------------ */
/* 启停路径：只能在 PASSIVE_LEVEL 且已持有 ControlMutex 时调用。 */
/* ------------------------------------------------------------------ */
static VOID SSJJClearConfiguration(VOID)
{
    PEPROCESS trustedProcess = g_SSJJProtect.TrustedProcess;
    PEPROCESS ownerProcess = g_SSJJProtect.OwnerProcess;

    g_SSJJProtect.TrustedProcess = NULL;
    g_SSJJProtect.OwnerProcess = NULL;
    g_SSJJProtect.GamePid = 0;
    g_SSJJProtect.TrustedPid = 0;
    g_SSJJProtect.OwnerPid = 0;
    RtlZeroMemory(g_SSJJProtect.ImageName,
            sizeof(g_SSJJProtect.ImageName));

    if (trustedProcess != NULL) {
        ObDereferenceObject(trustedProcess);
    }
    if (ownerProcess != NULL) {
        ObDereferenceObject(ownerProcess);
    }
}

static VOID SSJJStopProtectionLocked(VOID)
{
    PVOID callbackHandle = g_SSJJProtect.CallbackHandle;
    BOOLEAN imageNotifyRegistered =
            g_SSJJProtect.ImageNotifyRegistered;

    /* Active 先反映给控制端；FilterEnabled 保持到回调真正注销
     * 完成，使注销过程中已进入的请求仍按旧配置受保护。 */
    InterlockedExchange(&g_SSJJProtect.Active, 0);
    g_SSJJProtect.CallbackHandle = NULL;
    g_SSJJProtect.ImageNotifyRegistered = FALSE;

    if (callbackHandle != NULL) {
        ObUnRegisterCallbacks(callbackHandle);
    }

    if (imageNotifyRegistered) {
        NTSTATUS status =
                PsRemoveLoadImageNotifyRoutine(SSJJProtectImageLoadNotify);
        if (!NT_SUCCESS(status)) {
            DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                    "SSJJProtect: PsRemoveLoadImageNotifyRoutine failed: 0x%08X\n",
                    status);
        }
    }

    /* ObUnRegisterCallbacks/PsRemove... 返回后不再有回调读取这些对象。 */
    InterlockedExchange(&g_SSJJProtect.FilterEnabled, 0);
    SSJJClearConfiguration();
}

/* ------------------------------------------------------------------ */
/* 对外接口。                                                       */
/* ------------------------------------------------------------------ */
NTSTATUS SSJJProtectInit(VOID)
{
    /* DriverEntry 串行调用；保留幂等检查便于嵌入现有驱动。 */
    if (InterlockedCompareExchange(&g_SSJJProtect.Initialized, 1, 0) != 0) {
        return STATUS_SUCCESS;
    }

    ExInitializeFastMutex(&g_SSJJProtect.ControlMutex);
    InterlockedExchange(&g_SSJJProtect.FilterEnabled, 0);
    InterlockedExchange(&g_SSJJProtect.Active, 0);
    return STATUS_SUCCESS;
}

NTSTATUS SSJJProtectHandleIoctl(
        _In_reads_bytes_(inputLen) PVOID input,
        _In_ ULONG inputLen,
        _Out_writes_bytes_opt_(outputLen) PVOID output,
        _In_ ULONG outputLen)
{
    SSJJ_PROTECT_REQ request;
    CHAR ansiImageName[SSJJ_PROCESS_IMAGE_BYTES];
    PEPROCESS trustedProcess = NULL;
    PEPROCESS ownerProcess = NULL;
    ULONG trustedPid;
    ULONG ownerPid;
    NTSTATUS status = STATUS_SUCCESS;

    PAGED_CODE();

    if (KeGetCurrentIrql() != PASSIVE_LEVEL) {
        return STATUS_INVALID_DEVICE_STATE;
    }
    if (SSJJReadLong(&g_SSJJProtect.Initialized) == 0) {
        return STATUS_DEVICE_NOT_READY;
    }
    if (input == NULL || inputLen < sizeof(SSJJ_PROTECT_REQ)) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    /* METHOD_BUFFERED 的 input/output 可能指向同一 SystemBuffer，先整体复制。 */
    RtlCopyMemory(&request, input, sizeof(request));

    if (request.Action != SSJJ_PROTECT_ENABLE &&
        request.Action != SSJJ_PROTECT_DISABLE) {
        status = STATUS_INVALID_PARAMETER;
        goto write_output;
    }

    if (request.Action == SSJJ_PROTECT_DISABLE) {
        ExAcquireFastMutex(&g_SSJJProtect.ControlMutex);
        SSJJStopProtectionLocked();
        ExReleaseFastMutex(&g_SSJJProtect.ControlMutex);
        goto write_output;
    }

    status = SSJJMakeAnsiImageName(request.ImageName, ansiImageName);
    if (!NT_SUCCESS(status)) {
        goto write_output;
    }
    if (request.GamePid == 0 && ansiImageName[0] == '\0') {
        status = STATUS_INVALID_PARAMETER;
        goto write_output;
    }

    ownerPid = HandleToULong(PsGetCurrentProcessId());
    trustedPid = request.TrustedPid == 0 ? ownerPid : request.TrustedPid;

    /* PsLookupProcessByProcessId 返回已引用对象，一直持有到保护关闭。 */
    status = PsLookupProcessByProcessId(
            ULongToHandle(trustedPid), &trustedProcess);
    if (!NT_SUCCESS(status) || trustedProcess == NULL) {
        trustedProcess = NULL;
        goto write_output;
    }

    ownerProcess = PsGetCurrentProcess();
    ObReferenceObject(ownerProcess);

    ExAcquireFastMutex(&g_SSJJProtect.ControlMutex);

    /* 硬性约定：已注册时先注销，然后用新配置重新注册。 */
    SSJJStopProtectionLocked();

    g_SSJJProtect.GamePid = request.GamePid;
    g_SSJJProtect.TrustedPid = trustedPid;
    g_SSJJProtect.OwnerPid = ownerPid;
    RtlCopyMemory(g_SSJJProtect.ImageName, ansiImageName,
            sizeof(g_SSJJProtect.ImageName));
    g_SSJJProtect.TrustedProcess = trustedProcess;
    g_SSJJProtect.OwnerProcess = ownerProcess;
    trustedProcess = NULL; /* 引用权已转移到全局状态 */
    ownerProcess = NULL;

    {
        OB_OPERATION_REGISTRATION operation;
        OB_CALLBACK_REGISTRATION registration;
        UNICODE_STRING altitude;

        RtlZeroMemory(&operation, sizeof(operation));
        RtlZeroMemory(&registration, sizeof(registration));
        RtlInitUnicodeString(&altitude, SSJJ_CALLBACK_ALTITUDE);

        operation.ObjectType = PsProcessType;
        operation.Operations = OB_OPERATION_HANDLE_CREATE |
                               OB_OPERATION_HANDLE_DUPLICATE;
        operation.PreOperation = SSJJProtectPreOperation;
        operation.PostOperation = NULL;

        registration.Version = OB_FLT_REGISTRATION_VERSION;
        registration.OperationRegistrationCount = 1;
        registration.Altitude = altitude;
        registration.RegistrationContext = NULL;
        registration.OperationRegistration = &operation;

        /* 先准备好不变状态，以便 ObRegisterCallbacks 内部一旦公布
         * 回调，立即开始正确过滤。Active 仍在注册成功后才置 1。 */
        InterlockedExchange(&g_SSJJProtect.FilterEnabled, 1);
        status = ObRegisterCallbacks(&registration,
                &g_SSJJProtect.CallbackHandle);
    }

    if (!NT_SUCCESS(status)) {
        InterlockedExchange(&g_SSJJProtect.FilterEnabled, 0);
        g_SSJJProtect.CallbackHandle = NULL;
        SSJJClearConfiguration();
        ExReleaseFastMutex(&g_SSJJProtect.ControlMutex);
        goto write_output;
    }

    /* 映像通知是审计加分项；注册失败不影响 Ob 句柄保护主功能。 */
    status = PsSetLoadImageNotifyRoutine(SSJJProtectImageLoadNotify);
    if (NT_SUCCESS(status)) {
        g_SSJJProtect.ImageNotifyRegistered = TRUE;
    } else {
        DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                "SSJJProtect: image notify unavailable: 0x%08X\n", status);
        status = STATUS_SUCCESS;
    }

    InterlockedExchange(&g_SSJJProtect.Active, 1);
    ExReleaseFastMutex(&g_SSJJProtect.ControlMutex);

write_output:
    if (trustedProcess != NULL) {
        ObDereferenceObject(trustedProcess);
    }
    if (ownerProcess != NULL) {
        ObDereferenceObject(ownerProcess);
    }

    /* 可选输出：ULONG 0/1，便于用户态立即确认最终状态。 */
    if (output != NULL && outputLen >= sizeof(ULONG)) {
        *(ULONG *)output = SSJJProtectIsActive() ? 1UL : 0UL;
    }
    return status;
}

VOID SSJJProtectCleanup(VOID)
{
    PAGED_CODE();

    if (SSJJReadLong(&g_SSJJProtect.Initialized) == 0) {
        return;
    }

    ExAcquireFastMutex(&g_SSJJProtect.ControlMutex);
    SSJJStopProtectionLocked();
    ExReleaseFastMutex(&g_SSJJProtect.ControlMutex);
}

BOOLEAN SSJJProtectIsActive(VOID)
{
    return SSJJReadLong(&g_SSJJProtect.Active) != 0 ? TRUE : FALSE;
}
