// SetProcess 设置进程
__forceinline NTSTATUS SetProcess(PMap Map, ULONG64 ProcessId, ULONG64 DecryptCr3) {
	Map->Process.CurrentCr3 = 0; Map->Process.CurrentPEB = 0; Map->Process.CurrentProcess = 0; Map->Process.PhysicalMemoryRangesCount = 0;
	if (Map->Process.PhysicalMemoryRanges) {
		((void(*)(PVOID, ULONG))Map->ImportTable.ExFreePoolWithTag)(Map->Process.PhysicalMemoryRanges, 0);
		Map->Process.PhysicalMemoryRanges = 0;
	}

	if (!ProcessId) return STATUS_UNSUCCESSFUL;

	if (DecryptCr3) {
		PPHYSICAL_MEMORY_RANGE PhysicalMemoryRanges = ((PPHYSICAL_MEMORY_RANGE(*)(PVOID))Map->ImportTable.MmGetPhysicalMemoryRangesEx)(0);
		if (!PhysicalMemoryRanges) return STATUS_UNSUCCESSFUL;

		for (ULONG64 i = 0; i < 0x100; i++) {
			if (!PhysicalMemoryRanges[i].BaseAddress.QuadPart && !PhysicalMemoryRanges[i].NumberOfBytes.QuadPart) break;

			ULONG64 start = PhysicalMemoryRanges[i].BaseAddress.QuadPart >> 12;
			ULONG64 end = start + (PhysicalMemoryRanges[i].NumberOfBytes.QuadPart >> 12);

			for (ULONG64 j = start; j < end; j++) {
				PMMPFN pfn = (PMMPFN)(Map->System.MmPfnDataBase + sizeof(MMPFN) * j);
				if (!((BOOLEAN(*)(PVOID))Map->ImportTable.MmIsAddressValid)((PVOID)pfn)) continue;
				if (pfn->Flink == 0 || pfn->Flink == 1 || pfn->PteAddress != Map->System.SystemPteAddress) continue;

				PVOID Process;
				if (!Map->ImportTable.MiGetPageTablePfnBuddyRaw)
					Process = (PVOID)((pfn->Flink >> 0xD) & 0x7FFFFFFFFFF0 | 0xFFFF800000000000);
				else
					Process = ((PVOID(*)(PMMPFN))Map->ImportTable.MiGetPageTablePfnBuddyRaw)(pfn);

				if (((BOOLEAN(*)(PVOID))Map->ImportTable.MmIsAddressValid)(Process) && ((ULONG64(*)(PVOID))Map->ImportTable.PsGetProcessId)(Process) == ProcessId) {
					Map->Process.CurrentCr3 = (ULONG64)(j << 12);

					Map->Process.CurrentPEB = ((ULONG64(*)(PVOID))Map->ImportTable.PsGetProcessWow64Process)(Process);
					if (!Map->Process.CurrentPEB) Map->Process.CurrentPEB = ((ULONG64(*)(PVOID))Map->ImportTable.PsGetProcessPeb)(Process);

					Map->Process.CurrentProcess = Process;

					Map->Process.PhysicalMemoryRanges = PhysicalMemoryRanges;
					while (
						PhysicalMemoryRanges[Map->Process.PhysicalMemoryRangesCount].BaseAddress.QuadPart ||
						PhysicalMemoryRanges[Map->Process.PhysicalMemoryRangesCount].NumberOfBytes.QuadPart
						)
					{
						Map->Process.PhysicalMemoryRangesCount++;
					}
					PreprocessPhysicalMemoryRanges(Map->Process.PhysicalMemoryRanges, Map->Process.PhysicalMemoryRangesCount);

					return STATUS_SUCCESS;
				}
			}
		}

		((void(*)(PVOID, ULONG))Map->ImportTable.ExFreePoolWithTag)(PhysicalMemoryRanges, 0);
		return STATUS_UNSUCCESSFUL;
	}
	else {
		PEPROCESS Process = 0;
		NTSTATUS Status = ((decltype(PsLookupProcessByProcessId)*)Map->ImportTable.PsLookupProcessByProcessId)((HANDLE)ProcessId, &Process);
		if (!NT_SUCCESS(Status)) return STATUS_UNSUCCESSFUL;

		RTL_OSVERSIONINFOW ver;
		((decltype(RtlGetVersion)*)Map->ImportTable.RtlGetVersion)(&ver);

		ULONG64 Dtb = *(ULONG64*)((ULONG64)Process + 0x28);
		if (!Dtb) {
			switch (ver.dwBuildNumber)
			{
			case 17134:
			case 17763:
				Dtb = *(ULONG64*)((ULONG64)Process + 0x0278);
				break;
			case 18362:
			case 18363:
				Dtb = *(ULONG64*)((ULONG64)Process + 0x0280);
				break;
			default:
				Dtb = *(ULONG64*)((ULONG64)Process + 0x0388);
				break;
			}
		}

		Map->Process.CurrentCr3 = Dtb;
		Map->Process.CurrentPEB = ((ULONG64(*)(PVOID))Map->ImportTable.PsGetProcessWow64Process)(Process);
		if (!Map->Process.CurrentPEB) Map->Process.CurrentPEB = ((ULONG64(*)(PVOID))Map->ImportTable.PsGetProcessPeb)(Process);

		Map->Process.CurrentProcess = Process;

		((decltype(ObfDereferenceObject)*)Map->ImportTable.ObfDereferenceObject)(Process);
		return STATUS_SUCCESS;
	}
}

NTSTATUS CopyPhysicalMemory(PMap pMap, PDriverControl pControl) {
	if (!pControl->Address || !pControl->Buffer || !pControl->Size || !pMap->Process.CurrentCr3) return STATUS_UNSUCCESSFUL;

	int threadIndex = GetThreadUniqueIndexFromMap(pMap, pControl->Size + sizeof(DriverControl) + sizeof(InvlpgCopyMemoryParameters) + 8);
	if (threadIndex == -1) return STATUS_UNSUCCESSFUL;

	PDriverControl pControlNonPaged = (PDriverControl)pMap->Thread.threadDataBuffer[threadIndex];
	*pControlNonPaged = *pControl;

	if (!pControlNonPaged->Buffer) {
		_InterlockedExchange8(&pMap->Thread.threadUseMap[threadIndex], false);
		return STATUS_UNSUCCESSFUL;
	}

	InvlpgCopyMemoryParameters* pParametersNonPaged = (InvlpgCopyMemoryParameters*)(pMap->Thread.threadDataBuffer[threadIndex] + sizeof(DriverControl));
	ULONG64 pBufferNonPaged = (ULONG64)(pMap->Thread.threadDataBuffer[threadIndex] + sizeof(DriverControl) + sizeof(InvlpgCopyMemoryParameters));

	pParametersNonPaged->KeInvalidateRangeAllCaches = pMap->ImportTable.KeInvalidateRangeAllCaches;
	pParametersNonPaged->Page = pMap->System.Ptes + threadIndex * PAGE_SIZE;
	pParametersNonPaged->PagePteAddress = MiGetPteAddress(pMap->System.PteBase, pParametersNonPaged->Page);
	pParametersNonPaged->OriginalPte = *pParametersNonPaged->PagePteAddress;
	pParametersNonPaged->PhysicalMemoryRanges = pMap->Process.PhysicalMemoryRanges;
	pParametersNonPaged->PhysicalMemoryRangesCount = pMap->Process.PhysicalMemoryRangesCount;
	pParametersNonPaged->PageOffset = 0;
	pParametersNonPaged->RemainSize = pControlNonPaged->Size;

	if (pControlNonPaged->Write) __movsb((UCHAR*)pBufferNonPaged, (UCHAR*)pControlNonPaged->Buffer, pControlNonPaged->Size);

	_disable();
	KIRQL kirql = RaiseIrql(DISPATCH_LEVEL);

	// 如果需要解密Cr3
	ULONG64 Cr3 = pMap->Process.CurrentCr3;

	while (true) {
		if (pParametersNonPaged->RemainSize <= 0) break;

		ULONG64 PxeAddress = 0;
		ULONG64 PhysicalAddress = TranslateLinearAddress(pParametersNonPaged, Cr3, pControlNonPaged->Address + pParametersNonPaged->PageOffset, &PxeAddress);
		if (!PhysicalAddress) break;

		if (PxeAddress && (pControlNonPaged->Write == MemOperateType::Write)) break;

		ULONG64 BytesToCopy = min(PAGE_SIZE - (PhysicalAddress & 0xFFF), pParametersNonPaged->RemainSize);
		if (!BytesToCopy) break;

		if (!InvlpgCopyPhysicalAddress(pParametersNonPaged, PhysicalAddress, (PVOID)(pBufferNonPaged + pParametersNonPaged->PageOffset), BytesToCopy, pControlNonPaged->Write)) break;

		pParametersNonPaged->RemainSize -= BytesToCopy;
		pParametersNonPaged->PageOffset += BytesToCopy;
	}

	*(pParametersNonPaged->PagePteAddress) = pParametersNonPaged->OriginalPte;
	__invlpg((void*)pParametersNonPaged->Page);

	LowerIrql(kirql);
	_enable();

	if (pControlNonPaged->Write == MemOperateType::Read) {
		__movsb((UCHAR*)pControlNonPaged->Buffer, (UCHAR*)pBufferNonPaged, pControlNonPaged->Size);
		__stosb((UCHAR*)pBufferNonPaged, 0, pControlNonPaged->Size);
	}
	_InterlockedExchange8(&pMap->Thread.threadUseMap[threadIndex], false);

	return -(LONG)pParametersNonPaged->RemainSize;
}