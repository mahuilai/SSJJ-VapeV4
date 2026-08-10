__declspec(noinline) NTSTATUS CopyVirtualMemory(PMap Map, PDriverControl Control) {
	ULONG64 Copied = 0;
	if (Control->Write == MemOperateType::Write) {
		return ((NTSTATUS(*)(PVOID, ULONG64, PVOID, ULONG64, ULONG64, KPROCESSOR_MODE, PULONG64))Map->ImportTable.MmCopyVirtualMemory)(
			((PVOID(*)())Map->ImportTable.IoGetCurrentProcess)(),
			Control->Buffer,
			Map->Process.CurrentProcess,
			Control->Address,
			Control->Size,
			KernelMode,
			&Copied
			);
	}
	else {
		return ((NTSTATUS(*)(PVOID, ULONG64, PVOID, ULONG64, ULONG64, KPROCESSOR_MODE, PULONG64))Map->ImportTable.MmCopyVirtualMemory)(
			Map->Process.CurrentProcess,
			Control->Address,
			((PVOID(*)())Map->ImportTable.IoGetCurrentProcess)(),
			Control->Buffer,
			Control->Size,
			KernelMode,
			&Copied
			);
	}
}

__declspec(noinline) NTSTATUS CopyKernelVirtualMemory(PMap Map, PDriverControl Control) {
	if (!((BOOLEAN(*)(ULONG64))Map->ImportTable.MmIsAddressValid)(Control->Address)) return STATUS_UNSUCCESSFUL;
	if (Control->Write == MemOperateType::Write) __movsb((PUCHAR)Control->Address, (PUCHAR)Control->Buffer, Control->Size);
	else __movsb((PUCHAR)Control->Buffer, (PUCHAR)Control->Address, Control->Size);

	return STATUS_SUCCESS;
}

__declspec(noinline) ULONG64 GetKernelModuleAddress(PMap Map, PDriverControl Control) {
	PLDR_DATA_TABLE_ENTRY pCurEntry = Map->System.NtoskrnlLdr;

	do
	{
		if (pCurEntry->BaseDllName.Buffer != NULL && (ULONG64)pCurEntry->BaseDllName.Buffer > KERNEL_SPACE_START && (ULONG64)pCurEntry->BaseDllName.Buffer < KERNEL_SPACE_END)
			if (_wcsstr(pCurEntry->BaseDllName.Buffer, (wchar_t*)Control->Buffer)) return (ULONG64)pCurEntry->DllBase;
		pCurEntry = (PLDR_DATA_TABLE_ENTRY)pCurEntry->InLoadOrderLinks.Flink;
	} while (pCurEntry != Map->System.NtoskrnlLdr);

	return 0;
}