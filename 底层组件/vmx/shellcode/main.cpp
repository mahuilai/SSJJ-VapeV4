#include <ntifs.h>
#include <intrin.h>
#include <ntddmou.h>
#include "structs.h"
#include "tools.hpp"
#include "memunit.hpp"
#include "mem_rw_invlpg.hpp"
#include "mem_rw_cr3.hpp"
#include "mem_api.hpp"
#include "mouse.hpp"
#include "deletefile.hpp"

__declspec(noinline) NTSTATUS A00_RegistryCallback(PVOID a1, PVOID a2, PVOID a3) {
	volatile PMap Map = (PMap) * (PVOID*)((ULONG64)&A00_RegistryCallback - 0x8);

	if (a1) return ((NTSTATUS(*)(PVOID, PVOID, PVOID))Map->ImportTable.RtlHashUnicodeString)(a1, a2, a3);
	if (!a2 || !a3) return STATUS_SUCCESS;
	REG_NOTIFY_CLASS Notify = (REG_NOTIFY_CLASS)(ULONG64)a2;
	switch (Notify) {
	case RegNtSetValueKey: {
		PREG_SET_VALUE_KEY_INFORMATION RegInfo = (PREG_SET_VALUE_KEY_INFORMATION)a3;
		if (RegInfo->Type == REG_BINARY && RegInfo->DataSize == sizeof(DriverParameter)) {
			PDriverParameter Parameter = (PDriverParameter)RegInfo->Data;
			if (Parameter->VMKey != VM_MAGICNUMBER) return STATUS_SUCCESS;

			PDriverControl Control = Parameter->Control;
			if (Control->Flag == 'ACE') {
				*(ULONG64*)Parameter->OutputBuffer = 1;
				return STATUS_UNSUCCESSFUL;
			}

			switch (Control->Flag) {
			case '1167': { 
				NT_SUCCESS(SetProcess(Map, Control->ProcessId, Control->Write)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1168': { 
				ULONG64 PEB = Map->Process.CurrentPEB;
				if (PEB) *(ULONG64*)Parameter->OutputBuffer = PEB;
				return STATUS_UNSUCCESSFUL;
			}
			case '1169': { 
				NT_SUCCESS(CopyPhysicalMemory(Map, Control)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1170': {
				NT_SUCCESS(CopyKernelVirtualMemory(Map, Control)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1171': {
				*(ULONG64*)Parameter->OutputBuffer = GetKernelModuleAddress(Map, Control);
				return STATUS_UNSUCCESSFUL;
			}
			case '1172': {
				NT_SUCCESS(KernelDeleteFile(Map, Control)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1173': {
				NT_SUCCESS(CopyPagefulMemory(Map, Control)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1174': {
				NT_SUCCESS(MouseEvent(Map, Control)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1175': {
				NT_SUCCESS(CopyVirtualMemory(Map, Control)) ?
					*(ULONG64*)Parameter->OutputBuffer = VM_TRUE :
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
				return STATUS_UNSUCCESSFUL;
			}
			case '1176': {
				if (Map->Process.CurrentProcess) {
					ULONG64 MainModuleBase = ((ULONG64(*)(PVOID))Map->ImportTable.PsGetProcessSectionBaseAddress)(Map->Process.CurrentProcess);
					if (MainModuleBase) *(ULONG64*)Parameter->OutputBuffer = MainModuleBase;
				}
				return STATUS_UNSUCCESSFUL;
			}
			case '1177': {
				HANDLE ProcessHandle = 0;
				NTSTATUS Status = ((decltype(ObOpenObjectByPointer)*)Map->ImportTable.ObOpenObjectByPointer)
					(Map->Process.CurrentProcess, OBJ_KERNEL_HANDLE, 0, PROCESS_ALL_ACCESS, *(POBJECT_TYPE*)Map->ImportTable.PsProcessType, KernelMode, &ProcessHandle);
				if (!NT_SUCCESS(Status)) {
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
					return STATUS_UNSUCCESSFUL;
				}

				if (Control->Buffer == 0) 
				{
					Status = ((decltype(ZwAllocateVirtualMemory)*)Map->ImportTable.ZwAllocateVirtualMemory)
						(ProcessHandle, (PVOID*)&Control->Address, 0, (PSIZE_T)&Control->Size, Control->ProcessId, Control->Write);
				}
				else if (Control->Buffer == 1) { 
					Status = ((decltype(ZwProtectVirtualMemory)*)Map->ImportTable.ZwProtectVirtualMemory)
						(ProcessHandle, (PVOID*)&Control->Address, (PSIZE_T)&Control->Size, Control->Write, (PULONG)&Control->ProcessId);
				}
				else if (Control->Buffer == 2) { 
					Status = ((decltype(ZwFreeVirtualMemory)*)Map->ImportTable.ZwFreeVirtualMemory)
						(ProcessHandle, (PVOID*)&Control->Address, (PSIZE_T)&Control->Size, Control->Write);
				}
				else if (Control->Buffer == 3) {
					HANDLE ThreadHandle;
					Status = ((decltype(RtlCreateUserThread)*)Map->ImportTable.RtlCreateUserThread)
						(ProcessHandle, 0, 0, 0, 0, 0, (PVOID)Control->Address, (PVOID)Control->Write, &ThreadHandle, 0);

					if (NT_SUCCESS(Status)) {
						LARGE_INTEGER Time;
						Time.QuadPart = -(60ll * 10 * 1000 * 100);
						((decltype(ZwWaitForSingleObject)*)Map->ImportTable.ZwWaitForSingleObject)(ThreadHandle, TRUE, &Time);

						((decltype(ZwClose)*)Map->ImportTable.ZwClose)(ThreadHandle);
					}
				}

				if (!NT_SUCCESS(Status)) {
					((decltype(ZwClose)*)Map->ImportTable.ZwClose)(ProcessHandle);
					*(ULONG64*)Parameter->OutputBuffer = VM_FALSE;
					return STATUS_UNSUCCESSFUL;
				}

				*(ULONG64*)Parameter->OutputBuffer = VM_TRUE;
				return STATUS_UNSUCCESSFUL;
			}
			}
		}
		break;
	}
	}
	return STATUS_SUCCESS;
}