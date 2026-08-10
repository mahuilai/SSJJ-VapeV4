#include <ntifs.h>
#include <ntimage.h>

#include "kli.hpp"
#include "utils.hpp"
NTSTATUS Init(PDRIVER_OBJECT pDriverObject) {
    PHYSICAL_ADDRESS haa{};
    haa.QuadPart = 0xFFFFFFFFFFFFFFFF;
    UNICODE_STRING RoutineName;
    NTSTATUS Status = STATUS_SUCCESS;
    wchar_t text_vmm_[] = {
        L'\\', L'R', L'e', L'g', L'i', L's', L't', L'r', L'y',
        L'\\', L'M', L'a', L'c', L'h', L'i', L'n', L'e',
        L'\\', L'S', L'O', L'F', L'T', L'W', L'A', L'R', L'E',
        L'\\', L'v', L'm', L'm', L'_', L'\0'
    };
    wchar_t text_ShellcodeBinary[] = {
        L'S', L'h', L'e', L'l', L'l', L'c', L'o', L'd', L'e', L'B', L'i', L'n', L'a', L'r', L'y', L'\0'
    };
    wchar_t text_ImportTable[] = {
        L'I', L'm', L'p', L'o', L'r', L't', L'T', L'a', L'b', L'l', L'e', L'\0'
    };
    wchar_t text_FunctionPtr[] = {
        L'F', L'u', L'n', L'c', L't', L'i', L'o', L'n', L'P', L't', L'r', L'\0'
    };
    wchar_t text_MouseBinary[] = {
        L'M', L'o', L'u', L's', L'e', L'B', L'i', L'n', L'a', L'r', L'y', L'\0'
    };
    wchar_t text_MousePtr[] = {
        L'M', L'o', L'u', L's', L'e', L'P', L't', L'r', L'\0'
    };
    wchar_t text_DriverPath[] = {
       L'D', L'r', L'i', L'v', L'e', L'r', L'P', L'a', L't', L'h', L'\0'
    };
    wchar_t text_MmGetVirtualForPhysical[] = {
       L'M', L'm', L'G', L'e', L't', L'V', L'i', L'r', L't', L'u', L'a', L'l', L'F', L'o', L'r', L'P', L'h', L'y', L's', L'i', L'c', L'a', L'l', L'\0'
    };
    wchar_t text_IoFileObjectType[] = {
        L'I', L'o', L'F', L'i', L'l', L'e', L'O', L'b', L'j', L'e', L'c', L't', L'T', L'y', L'p', L'e', L'\0'
    };
    wchar_t text_429621[] = {
        L'4', L'2', L'9', L'6', L'2', L'1', L'\0'
    };
    wchar_t text_mouclass[] = {
        L'\\', L'D', L'r', L'i', L'v', L'e', L'r',
        L'\\', L'M', L'o', L'u', L'C', L'l', L'a', L's', L's', L'\0'
    };
    wchar_t text_ntoskrnl_exe[] = {
        L'n', L't', L'o', L's', L'k', L'r', L'n', L'l', L'.', L'e', L'x', L'e', L'\0'
    };
    wchar_t text_mouclass_sys[] = {
        L'm', L'o', L'u', L'c', L'l', L'a', L's', L's', L'.', L's', L'y', L's', L'\0'
    };
    wchar_t text_win32kbase[] = {
        L'w', L'i', L'n', L'3', L'2', L'k', L'b', L'a', L's', L'e', L'.', L's', L'y', L's', L'\0'
    };
    wchar_t text_ObReferenceObjectByName[] = {
        L'O', L'b', L'R', L'e', L'f', L'e', L'r', L'e', L'n', L'c', L'e', L'O', L'b', L'j', L'e', L'c', L't', L'B', L'y', L'N', L'a', L'm', L'e', L'\0'
    };
    wchar_t text_IoDriverObjectType[] = {
        L'I', L'o', L'D', L'r', L'i', L'v', L'e', L'r', L'O', L'b', L'j', L'e', L'c', L't', L'T', L'y', L'p', L'e', L'\0'
    };

    // 寻找基地址
    PLDR_DATA_TABLE_ENTRY NtoskrnlLdr = 0;
    ULONG64 _kernel_base = (ULONG64)FindBase(pDriverObject, text_ntoskrnl_exe, &NtoskrnlLdr);
    PVOID _mouclass_sys_base = FindBase(pDriverObject, text_mouclass_sys, 0);
    PVOID _win32kbase_sys_base = FindBase(pDriverObject, text_win32kbase, 0);

    // 初始化要用到的函数地址
    KLI_FN(_kernel_base, RtlInitUnicodeString)(&RoutineName, text_MmGetVirtualForPhysical);
    PVOID _MmGetVirtualForPhysical = KLI_FN(_kernel_base, MmGetSystemRoutineAddress)(&RoutineName);
    KLI_FN(_kernel_base, RtlInitUnicodeString)(&RoutineName, text_ObReferenceObjectByName);
    PVOID _ObReferenceObjectByName = KLI_FN(_kernel_base, MmGetSystemRoutineAddress)(&RoutineName);
    KLI_FN(_kernel_base, RtlInitUnicodeString)(&RoutineName, text_IoFileObjectType);
    PVOID _IoFileObjectType = KLI_FN(_kernel_base, MmGetSystemRoutineAddress)(&RoutineName);
    KLI_FN(_kernel_base, RtlInitUnicodeString)(&RoutineName, text_IoDriverObjectType);
    PVOID _IoDriverObjectType = KLI_FN(_kernel_base, MmGetSystemRoutineAddress)(&RoutineName);

    // 自删除
    PLDR_DATA_TABLE_ENTRY section = (PLDR_DATA_TABLE_ENTRY)pDriverObject->DriverSection;
    DeleteDriverFile(_kernel_base, _IoFileObjectType, &section->FullDllName);

    // 简单清理加载记录
    section->BaseDllName.Length = 0;
    unsigned char* Shellcode_Buffer = 0; int Shellcode_Size = 0;
    Status = ReadRegistryBinary(_kernel_base, text_vmm_, text_ShellcodeBinary, &Shellcode_Buffer, &Shellcode_Size);
    if (!NT_SUCCESS(Status)) return STATUS_DEVICE_NOT_READY;

    unsigned char* ImportTable = 0; int ImportTable_Size = 0;
    Status = ReadRegistryBinary(_kernel_base, text_vmm_, text_ImportTable, &ImportTable, &ImportTable_Size);
    if (!NT_SUCCESS(Status)) return STATUS_DEVICE_NOT_READY;

    unsigned __int64 _VerifierRtlHashUnicodeString = 0;
    Status = ReadRegistryQword(_kernel_base, text_vmm_, text_FunctionPtr, &_VerifierRtlHashUnicodeString);
    if (!NT_SUCCESS(Status)) return STATUS_DEVICE_NOT_READY;
    unsigned __int64 VerifierRtlHashUnicodeString = _kernel_base + _VerifierRtlHashUnicodeString;

    unsigned __int64 _SubRsp70RVA = 0;
    Status = ReadRegistryQword(_kernel_base, text_vmm_, text_MousePtr, &_SubRsp70RVA);
    if (!NT_SUCCESS(Status)) return STATUS_DEVICE_NOT_READY;

    unsigned char* MouseBinary = 0; int MouseBinary_Size = 0;
    if (_SubRsp70RVA) {
        unsigned __int64 SubRsp70Address = (ULONG64)_mouclass_sys_base + _SubRsp70RVA;

        Status = ReadRegistryBinary(_kernel_base, text_vmm_, text_MouseBinary, &MouseBinary, &MouseBinary_Size);
        if (!NT_SUCCESS(Status)) return STATUS_DEVICE_NOT_READY;
        *(ULONG64*)(MouseBinary + MouseBinary_Size - 8) = SubRsp70Address;
    }

    DeleteRegistryPath(_kernel_base, text_vmm_);

    PMap pMap = (PMap)KLI_FN(_kernel_base, MmAllocateContiguousMemory)(sizeof(_Map), haa);
    KLI_FN(_kernel_base, memset)(pMap, 0, sizeof(_Map));

    KLI_FN(_kernel_base, memcpy)(pMap, ImportTable, ImportTable_Size);
    KLI_FN(_kernel_base, ExFreePoolWithTag)(ImportTable, 'abcd');

    RelocateBaseValue((unsigned char*)(pMap->ImportTable.Ntoskrnl), sizeof(pMap->ImportTable.Ntoskrnl), _kernel_base);
    RelocateBaseValue((unsigned char*)(pMap->ImportTable.win32kbase), sizeof(pMap->ImportTable.win32kbase), (ULONG64)_win32kbase_sys_base);

    pMap->System.PteBase = *(ULONG64*)((ULONG64)_MmGetVirtualForPhysical + 0x22);

    ULONG64 MappedPteAddress = (ULONG64)KLI_FN(_kernel_base, MmAllocateMappingAddress)(512 * PAGE_SIZE, 1);
    ULONG64 MappedPte = (ULONG64)MiGetSystemPteAddress(MappedPteAddress);
    pMap->System.Ptes = (ULONG64)MiGetVirtualAddressMappedByPte(pMap->System.PteBase, MappedPte);

    ULONG64 system_cr3 = __readcr3();
    PHYSICAL_ADDRESS physical_cr3{};
    physical_cr3.QuadPart = system_cr3;
    ULONG64 virtual_cr3 = (ULONG64)KLI_FN(_kernel_base, MmGetVirtualForPhysical)(physical_cr3);

    pMap->System.SystemPteAddress = (ULONG64)MiGetPteAddress(pMap->System.PteBase, virtual_cr3);
    pMap->System.MmPfnDataBase = *(ULONG64*)((ULONG64)_MmGetVirtualForPhysical + 0x10) - 8;

    if (_SubRsp70RVA) {
        KLI_FN(_kernel_base, RtlInitUnicodeString)(&RoutineName, text_mouclass);
        PVOID MouseClassServiceCallback_0 = KLI_FN(_kernel_base, MmAllocateContiguousMemory)(MouseBinary_Size, haa);
        KLI_FN(_kernel_base, memcpy)(MouseClassServiceCallback_0, MouseBinary, MouseBinary_Size);
        KLI_FN(_kernel_base, ExFreePoolWithTag)(MouseBinary, 'abcd');

        pMap->System.MouseClassServiceCallback = (ULONG64)MouseClassServiceCallback_0;
        pMap->System.MouseDevice = Get_PointerClass0_DeviceObjectPointer(_kernel_base, RoutineName, _ObReferenceObjectByName, _IoDriverObjectType);
    }

    pMap->System.NtoskrnlLdr = NtoskrnlLdr;

    PVOID TEXT_0 = KLI_FN(_kernel_base, MmAllocateContiguousMemory)(Shellcode_Size + 0x8, haa);
    KLI_FN(_kernel_base, memset)(TEXT_0, 0, Shellcode_Size + 0x8);
    *(PVOID*)TEXT_0 = pMap;
    KLI_FN(_kernel_base, memcpy)((PVOID)((ULONG64)TEXT_0 + 0x8), Shellcode_Buffer, Shellcode_Size);

    ULONG64 pXdvRtlHashUnicodeString = VerifierRtlHashUnicodeString + 0x4;
    ULONG64 deref = (ULONG64)pXdvRtlHashUnicodeString + *(int*)((unsigned char*)pXdvRtlHashUnicodeString + 3) + 7;
    _InterlockedExchangePointer((PVOID*)deref, (PVOID)((ULONG64)TEXT_0 + 0x8));

    LARGE_INTEGER regcookie = { 0 };
    UNICODE_STRING altitude = { 0 };
    KLI_FN(_kernel_base, RtlInitUnicodeString)(&altitude, text_429621);
    Status = KLI_FN(_kernel_base, CmRegisterCallbackEx)((PEX_CALLBACK_FUNCTION)VerifierRtlHashUnicodeString, &altitude, &altitude, 0, &regcookie, nullptr);
    if (!NT_SUCCESS(Status) && Status != STATUS_FLT_INSTANCE_ALTITUDE_COLLISION) return STATUS_DEVICE_NOT_READY;

    return STATUS_UNSUCCESSFUL;
}

NTSTATUS DriverEntry(PDRIVER_OBJECT pDriverObject, PUNICODE_STRING RegistryPath) {
    return Init(pDriverObject);
}