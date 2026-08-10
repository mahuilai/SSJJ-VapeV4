/* ====================================================================
 * SSJJDrv.sys - kernel-mode injector for the SSJJ Unity client.
 *
 * Why a driver?
 *   The old user-mode injector used OpenProcess / VirtualAllocEx /
 *   WriteProcessMemory / CreateRemoteThread. GameGuard hooks exactly
 *   those APIs, so the injection was trivially detectable and could be
 *   blocked. A kernel driver performs the same work with Zw* APIs from
 *   SYSTEM context, which no user-mode anti-cheat hook can intercept.
 *
 * Flow (IOCTL_SSJJ_INJECT):
 *   1. PsLookupProcessByProcessId -> EPROCESS (no user-mode handle)
 *   2. walk target PEB loader table -> kernel32.dll base
 *   3. ZwOpenProcess (kernel-mode open bypasses user-mode hooks)
 *   4. ZwAllocateVirtualMemory in target -> DLL path buffer
 *   5. ZwWriteVirtualMemory -> write the path
 *   6. ZwCreateThreadEx -> remote thread calls kernel32!LoadLibraryW
 *   7. ZwWaitForSingleObject + query exit status (module handle)
 *
 * The driver is loaded with NtLoadDriver (no SCM service record) and
 * unloaded right after the injection, minimizing the kernel exposure
 * window for GameGuard's npggsvc.sys.
 *
 * NOTE: this source is fully self-contained. It self-declares the NT
 * kernel APIs, access-right constants and x64 PEB/LDR offsets, so it
 * builds even against a minimal WDK install (missing ntpebteb.h,
 * ntldr.h, ntdef.h, ntifs.h).
 * ==================================================================== */
#include <ntddk.h>
#include <ntstrsafe.h>

#include "SSJJDrv.h"
#include "SSJJProtect.h"

/* ------------------------------------------------------------------ */
/* Minimal self-declared types (missing from the reduced WDK)          */
/* ------------------------------------------------------------------ */
#ifndef SSJJ_TEB_DEFINED
typedef struct _TEB *PTEB;
#define SSJJ_TEB_DEFINED
#endif

/* Access rights / allocation constants (from ntdef.h, normally) */
#ifndef PROCESS_VM_READ
#define PROCESS_VM_READ            (0x0010)
#define PROCESS_VM_WRITE           (0x0020)
#define PROCESS_VM_OPERATION       (0x0008)
#define PROCESS_CREATE_THREAD      (0x0002)
#define PROCESS_QUERY_INFORMATION  (0x0400)
#define PROCESS_TERMINATE          (0x0001)
#define MEM_COMMIT                 0x00001000
#define MEM_RESERVE                0x00002000
#define MEM_RELEASE                0x00008000
#define PAGE_READWRITE             0x04
#endif

/* THREAD_BASIC_INFORMATION (class 0) */
typedef struct _SSJJ_THREAD_BASIC_INFORMATION {
    NTSTATUS ExitStatus;
    PVOID    TebBaseAddress;
    CLIENT_ID ClientId;
    ULONG_PTR AffinityMask;
    LONG     Priority;
    LONG     BasePriority;
} SSJJ_THREAD_BASIC_INFORMATION;

/* ------------------------------------------------------------------ */
/* Self-declared NT APIs (exported by ntoskrnl.exe / ntoskrnl.lib)     */
/* ------------------------------------------------------------------ */
NTSYSAPI NTSTATUS NTAPI ZwOpenProcess(
        PHANDLE ProcessHandle, ACCESS_MASK DesiredAccess,
        POBJECT_ATTRIBUTES ObjectAttributes, PCLIENT_ID ClientId);
NTSYSAPI NTSTATUS NTAPI ZwAllocateVirtualMemory(
        HANDLE ProcessHandle, PVOID *BaseAddress, ULONG_PTR ZeroBits,
        PSIZE_T RegionSize, ULONG AllocationType, ULONG Protect);
NTSYSAPI NTSTATUS NTAPI ZwWriteVirtualMemory(
        HANDLE ProcessHandle, PVOID BaseAddress, PVOID Buffer,
        SIZE_T BufferSize, PSIZE_T NumberOfBytesWritten);
NTSYSAPI NTSTATUS NTAPI ZwFreeVirtualMemory(
        HANDLE ProcessHandle, PVOID *BaseAddress, PSIZE_T RegionSize,
        ULONG FreeType);
NTSYSAPI NTSTATUS NTAPI ZwWaitForSingleObject(
        HANDLE Handle, BOOLEAN Alertable, PLARGE_INTEGER Timeout);
NTSYSAPI NTSTATUS NTAPI ZwQueryInformationThread(
        HANDLE ThreadHandle, ULONG ThreadInformationClass,
        PVOID ThreadInformation, ULONG ThreadInformationLength,
        PULONG ReturnLength);
NTSYSAPI NTSTATUS NTAPI ZwClose(HANDLE Handle);

/* ------------------------------------------------------------------ */
/* Dynamically resolved APIs (not exported by WDK's ntoskrnl.lib, but  */
/* present in ntoskrnl.exe's export table).                            */
/* ------------------------------------------------------------------ */
static NTSTATUS (NTAPI *g_ZwWriteVirtualMemory)(
        HANDLE ProcessHandle, PVOID BaseAddress, PVOID Buffer,
        SIZE_T BufferSize, PSIZE_T NumberOfBytesWritten);
static NTSTATUS (NTAPI *g_ZwCreateThreadEx)(
        PHANDLE ThreadHandle, ACCESS_MASK DesiredAccess,
        POBJECT_ATTRIBUTES ObjectAttributes, HANDLE ProcessHandle,
        PVOID StartRoutine, PVOID Argument, ULONG CreateFlags,
        SIZE_T ZeroBits, SIZE_T StackSize, SIZE_T MaximumStackSize,
        PVOID AttributeList);
static VOID (NTAPI *g_PsGetProcessPebAndTeb)(
        PEPROCESS Process, PPEB *Peb, PTEB *Teb);

static NTSTATUS SSJJResolveApis(void)
{
    UNICODE_STRING name;

    RtlInitUnicodeString(&name, L"ZwWriteVirtualMemory");
    g_ZwWriteVirtualMemory = (NTSTATUS (NTAPI *)(HANDLE, PVOID, PVOID,
            SIZE_T, PSIZE_T))MmGetSystemRoutineAddress(&name);
    if (g_ZwWriteVirtualMemory == NULL) return STATUS_NOT_FOUND;

    RtlInitUnicodeString(&name, L"ZwCreateThreadEx");
    g_ZwCreateThreadEx = (NTSTATUS (NTAPI *)(PHANDLE, ACCESS_MASK,
            POBJECT_ATTRIBUTES, HANDLE, PVOID, PVOID, ULONG, SIZE_T,
            SIZE_T, SIZE_T, PVOID))MmGetSystemRoutineAddress(&name);
    if (g_ZwCreateThreadEx == NULL) return STATUS_NOT_FOUND;

    RtlInitUnicodeString(&name, L"PsGetProcessPebAndTeb");
    g_PsGetProcessPebAndTeb = (VOID (NTAPI *)(PEPROCESS, PPEB *, PTEB *))
            MmGetSystemRoutineAddress(&name);
    if (g_PsGetProcessPebAndTeb == NULL) return STATUS_NOT_FOUND;

    return STATUS_SUCCESS;
}

NTKERNELAPI NTSTATUS NTAPI PsLookupProcessByProcessId(
        HANDLE ProcessId, PEPROCESS *Process);
NTKERNELAPI PVOID NTAPI PsGetProcessWow64Process(PEPROCESS Process);
NTKERNELAPI NTSTATUS NTAPI MmCopyVirtualMemory(
        PEPROCESS SourceProcess, PVOID SourceAddress,
        PEPROCESS TargetProcess, PVOID TargetAddress,
        SIZE_T BufferSize, KPROCESSOR_MODE PreviousMode,
        PSIZE_T NumberOfBytesCopied);

/* x64 public offsets (stable across Win10/11 x64) */
#define SSJJ_PEB_LDR_OFFSET            0x18 /* PEB.Ldr */
#define SSJJ_LDR_IN_MEM_LIST_OFFSET    0x20 /* PEB_LDR_DATA.InMemoryOrderModuleList */
#define SSJJ_LDR_ENTRY_LINKS_OFFSET    0x10 /* LDR_DATA_TABLE_ENTRY.InMemoryOrderLinks */
#define SSJJ_LDR_ENTRY_DLLBASE_OFFSET  0x30 /* LDR_DATA_TABLE_ENTRY.DllBase */
#define SSJJ_LDR_ENTRY_BASENAME_OFFSET 0x58 /* LDR_DATA_TABLE_ENTRY.BaseDllName */

/* ------------------------------------------------------------------ */
/* Globals                                                             */
/* ------------------------------------------------------------------ */
static PDEVICE_OBJECT g_device_object = NULL;
static UNICODE_STRING g_device_name;
static UNICODE_STRING g_symlink_name;

/* Forward declarations */
DRIVER_INITIALIZE DriverEntry;
DRIVER_UNLOAD SSJJDrvUnload;
DRIVER_DISPATCH SSJJDrvCreateClose;
DRIVER_DISPATCH SSJJDrvDeviceControl;

/* Read a target-process buffer via MmCopyVirtualMemory. */
static BOOLEAN SSJJReadRemote(PEPROCESS process, PVOID address,
        PVOID local_buffer, SIZE_T size)
{
    SIZE_T copied = 0;
    NTSTATUS status = MmCopyVirtualMemory(process, address,
            PsGetCurrentProcess(), local_buffer, size, KernelMode, &copied);
    return NT_SUCCESS(status) && copied == size;
}

/* ------------------------------------------------------------------ */
/* PEB walking helper: find kernel32.dll base in a target process.     */
/* ------------------------------------------------------------------ */
static PVOID SSJJFindKernel32Base(PEPROCESS process)
{
    PPEB peb = NULL;
    PTEB teb = NULL;
    PVOID ldr_addr = NULL;
    LIST_ENTRY head, current;
    int i;

    g_PsGetProcessPebAndTeb(process, &peb, &teb);
    if (peb == NULL) return NULL;

    /* peb->Ldr */
    if (!SSJJReadRemote(process,
            (PVOID)((ULONG_PTR)peb + SSJJ_PEB_LDR_OFFSET),
            &ldr_addr, sizeof(ldr_addr)))
        return NULL;
    if (ldr_addr == NULL) return NULL;

    /* ldr->InMemoryOrderModuleList */
    if (!SSJJReadRemote(process,
            (PVOID)((ULONG_PTR)ldr_addr + SSJJ_LDR_IN_MEM_LIST_OFFSET),
            &head, sizeof(head)))
        return NULL;

    current = head;

    for (i = 0; i < 512; ++i) {
        PVOID next_link = current.Flink;
        ULONG_PTR entry_addr;
        ULONG_PTR dll_base = 0;
        UNICODE_STRING base_name;
        WCHAR name_buffer[64];
        SIZE_T name_bytes;

        if (next_link == NULL) break;

        if (!SSJJReadRemote(process, next_link, &current, sizeof(current)))
            break;

        /* entry = next_link - InMemoryOrderLinks offset */
        entry_addr = (ULONG_PTR)next_link - SSJJ_LDR_ENTRY_LINKS_OFFSET;

        /* DllBase */
        if (!SSJJReadRemote(process,
                (PVOID)(entry_addr + SSJJ_LDR_ENTRY_DLLBASE_OFFSET),
                &dll_base, sizeof(dll_base)))
            break;

        /* BaseDllName (UNICODE_STRING) */
        RtlZeroMemory(&base_name, sizeof(base_name));
        if (!SSJJReadRemote(process,
                (PVOID)(entry_addr + SSJJ_LDR_ENTRY_BASENAME_OFFSET),
                &base_name, sizeof(base_name)))
            break;

        if (base_name.Buffer == NULL || base_name.Length == 0)
            continue;

        name_bytes = base_name.Length;
        if (name_bytes > sizeof(name_buffer) - sizeof(WCHAR))
            name_bytes = sizeof(name_buffer) - sizeof(WCHAR);

        RtlZeroMemory(name_buffer, sizeof(name_buffer));
        if (!SSJJReadRemote(process, base_name.Buffer,
                name_buffer, name_bytes))
            break;

        if (_wcsicmp(name_buffer, L"kernel32.dll") == 0)
            return (PVOID)dll_base;
    }

    return NULL;
}

/* ------------------------------------------------------------------ */
/* Core injection (must run at PASSIVE_LEVEL).                         */
/* ------------------------------------------------------------------ */
static ULONG SSJJKernelInject(SSJJ_INJECT_REQ *request)
{
    PEPROCESS process = NULL;
    HANDLE process_handle = NULL;
    HANDLE thread_handle = NULL;
    PVOID kernel32_base = NULL;
    PVOID load_library_addr = NULL;
    PVOID remote_path = NULL;
    SIZE_T path_bytes;
    SIZE_T region_size = 0;
    SIZE_T written = 0;
    NTSTATUS status;
    ULONG result = SSJJ_INJ_ERR_BADPID;

    if (request == NULL || request->ProcessId == 0) return SSJJ_INJ_ERR_BADPID;

    status = PsLookupProcessByProcessId((HANDLE)(ULONG_PTR)request->ProcessId,
            &process);
    if (!NT_SUCCESS(status) || process == NULL) return SSJJ_INJ_ERR_BADPID;
    ObDereferenceObject(process);

    /* 1. Resolve kernel32!LoadLibraryW in the target */
    kernel32_base = SSJJFindKernel32Base(process);
    if (kernel32_base == NULL) return SSJJ_INJ_ERR_RESOLVE;

    load_library_addr = (PVOID)((ULONG_PTR)kernel32_base
            + (ULONG_PTR)request->LoadLibraryOffset);

    /* 2. Open the target (kernel-mode open bypasses user-mode hooks) */
    {
        OBJECT_ATTRIBUTES oa;
        CLIENT_ID client_id;
        InitializeObjectAttributes(&oa, NULL, 0, NULL, NULL);
        client_id.UniqueProcess = (HANDLE)(ULONG_PTR)request->ProcessId;
        client_id.UniqueThread = NULL;

        status = ZwOpenProcess(&process_handle,
                PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION
                | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION
                | PROCESS_TERMINATE,
                &oa, &client_id);
        if (!NT_SUCCESS(status)) return SSJJ_INJ_ERR_OPEN;
    }

    /* 3. Allocate the path buffer inside the target */
    path_bytes = (wcslen(request->DllPath) + 1) * sizeof(WCHAR);
    region_size = path_bytes;
    status = ZwAllocateVirtualMemory(process_handle, &remote_path, 0,
            &region_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!NT_SUCCESS(status) || remote_path == NULL) {
        result = SSJJ_INJ_ERR_ALLOC;
        goto cleanup;
    }

    /* 4. Write the DLL path */
    status = g_ZwWriteVirtualMemory(process_handle, remote_path,
            (PVOID)request->DllPath, path_bytes, &written);
    if (!NT_SUCCESS(status) || written != path_bytes) {
        result = SSJJ_INJ_ERR_WRITE;
        goto cleanup;
    }

    /* 5. Create a remote thread running LoadLibraryW */
    status = g_ZwCreateThreadEx(&thread_handle,
            THREAD_ALL_ACCESS, NULL, process_handle,
            load_library_addr, remote_path,
            0, 0, 0, 0, NULL);
    if (!NT_SUCCESS(status)) {
        result = SSJJ_INJ_ERR_CREATE;
        goto cleanup;
    }

    /* 6. Wait for LoadLibraryW to return */
    status = ZwWaitForSingleObject(thread_handle, FALSE, NULL);
    if (!NT_SUCCESS(status)) {
        result = SSJJ_INJ_ERR_WAIT;
        goto cleanup;
    }

    /* 7. Read the thread exit status (= LoadLibraryW return = module base) */
    {
        SSJJ_THREAD_BASIC_INFORMATION tbi;
        ULONG ret_len = 0;
        RtlZeroMemory(&tbi, sizeof(tbi));
        status = ZwQueryInformationThread(thread_handle, 0 /*ThreadBasicInformation*/,
                &tbi, sizeof(tbi), &ret_len);
        if (NT_SUCCESS(status) && tbi.ExitStatus != 0) {
            result = SSJJ_INJ_OK;
        } else {
            result = SSJJ_INJ_ERR_LOAD;
        }
    }

cleanup:
    if (thread_handle != NULL) ZwClose(thread_handle);
    if (remote_path != NULL && process_handle != NULL) {
        region_size = 0;
        ZwFreeVirtualMemory(process_handle, &remote_path, &region_size,
                MEM_RELEASE);
    }
    if (process_handle != NULL) ZwClose(process_handle);
    return result;
}

/* ------------------------------------------------------------------ */
/* IRP dispatch                                                        */
/* ------------------------------------------------------------------ */
static NTSTATUS SSJJDrvCreateClose(PDEVICE_OBJECT device, PIRP irp)
{
    UNREFERENCED_PARAMETER(device);
    irp->IoStatus.Status = STATUS_SUCCESS;
    irp->IoStatus.Information = 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

static NTSTATUS SSJJDrvDeviceControl(PDEVICE_OBJECT device, PIRP irp)
{
    UNREFERENCED_PARAMETER(device);
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
    ULONG control_code = stack->Parameters.DeviceIoControl.IoControlCode;
    PVOID input = irp->AssociatedIrp.SystemBuffer;
    ULONG input_len = stack->Parameters.DeviceIoControl.InputBufferLength;
    ULONG output_len = stack->Parameters.DeviceIoControl.OutputBufferLength;
    NTSTATUS status = STATUS_SUCCESS;
    ULONG info = 0;

    switch (control_code)
    {
    case SSJJ_IOCTL_QUERY_X64:
    {
        /* input: ULONG PID; output: ULONG (1 = x64, 0 = not, -1 = no proc) */
        ULONG pid = 0;
        PEPROCESS process = NULL;
        ULONG result = 0;

        if (input != NULL && input_len >= sizeof(ULONG))
            pid = *(ULONG *)input;

        if (pid == 0 || !NT_SUCCESS(PsLookupProcessByProcessId(
                (HANDLE)(ULONG_PTR)pid, &process))) {
            result = SSJJ_ARCH_ERR_NO_PROC;
        } else {
            /* x64 native process: Wow64Process == NULL */
            if (PsGetProcessWow64Process(process) == NULL)
                result = 1;
            else
                result = 0;
            ObDereferenceObject(process);
        }

        if (irp->AssociatedIrp.SystemBuffer != NULL
                && output_len >= sizeof(ULONG)) {
            *(ULONG *)irp->AssociatedIrp.SystemBuffer = result;
            info = sizeof(ULONG);
        }
        break;
    }

    case SSJJ_IOCTL_INJECT:
    {
        SSJJ_INJECT_REQ *request = (SSJJ_INJECT_REQ *)input;
        ULONG result = SSJJ_INJ_ERR_BADPID;

        if (request != NULL && input_len >= sizeof(SSJJ_INJECT_REQ)) {
            result = SSJJKernelInject(request);
        }
        if (irp->AssociatedIrp.SystemBuffer != NULL
                && output_len >= sizeof(ULONG)) {
            *(ULONG *)irp->AssociatedIrp.SystemBuffer = result;
            info = sizeof(ULONG);
        }
        break;
    }

    case SSJJ_IOCTL_PROTECT:
    {
        /* METHOD_BUFFERED 的输入/输出共用 SystemBuffer。保护模块会
         * 先复制请求，所以同一指针可安全地同时作为 input/output。 */
        status = SSJJProtectHandleIoctl(input, input_len,
                irp->AssociatedIrp.SystemBuffer, output_len);
        if (output_len >= sizeof(ULONG) &&
            irp->AssociatedIrp.SystemBuffer != NULL) {
            info = sizeof(ULONG); /* 返回当前 active(0/1) */
        }
        break;
    }

    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    irp->IoStatus.Status = status;
    irp->IoStatus.Information = info;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return status;
}

/* ------------------------------------------------------------------ */
/* Driver entry / unload                                               */
/* ------------------------------------------------------------------ */
NTSTATUS DriverEntry(PDRIVER_OBJECT driver_object, PUNICODE_STRING registry_path)
{
    NTSTATUS status;
    ULONG i;

    UNREFERENCED_PARAMETER(registry_path);

    /* Resolve undocumented APIs first; abort load if unavailable. */
    status = SSJJResolveApis();
    if (!NT_SUCCESS(status)) return status;

    status = SSJJProtectInit();
    if (!NT_SUCCESS(status)) return status;

    RtlInitUnicodeString(&g_device_name, SSJJ_DEVICE_NAME);
    RtlInitUnicodeString(&g_symlink_name, SSJJ_SYMLINK_NAME);

    for (i = 0; i <= IRP_MJ_MAXIMUM_FUNCTION; ++i)
        driver_object->MajorFunction[i] = SSJJDrvCreateClose;
    driver_object->MajorFunction[IRP_MJ_DEVICE_CONTROL] = SSJJDrvDeviceControl;
    driver_object->DriverUnload = SSJJDrvUnload;

    status = IoCreateDevice(driver_object, 0, &g_device_name,
            FILE_DEVICE_UNKNOWN, 0, FALSE, &g_device_object);
    if (!NT_SUCCESS(status)) {
        SSJJProtectCleanup();
        return status;
    }

    status = IoCreateSymbolicLink(&g_symlink_name, &g_device_name);
    if (!NT_SUCCESS(status)) {
        SSJJProtectCleanup();
        IoDeleteDevice(g_device_object);
        g_device_object = NULL;
        return status;
    }

    return STATUS_SUCCESS;
}

VOID SSJJDrvUnload(PDRIVER_OBJECT driver_object)
{
    UNREFERENCED_PARAMETER(driver_object);

    /* 必须先注销 Ob/映像回调，再删除符号链接和设备对象。 */
    SSJJProtectCleanup();

    if (g_device_object != NULL) {
        IoDeleteSymbolicLink(&g_symlink_name);
        IoDeleteDevice(g_device_object);
        g_device_object = NULL;
    }
}
