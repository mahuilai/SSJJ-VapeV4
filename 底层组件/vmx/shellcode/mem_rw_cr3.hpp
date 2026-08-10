NTSTATUS ReadPagefulAddress(PMap Map, ULONG64 TargetAddress, PVOID lpBuffer, SIZE_T Size, SIZE_T* BytesRead)
{
	MM_COPY_ADDRESS AddrToRead;
	AddrToRead.PhysicalAddress.QuadPart = (ULONG64)TargetAddress;
	return ((decltype(MmCopyMemory)*)Map->ImportTable.MmCopyMemory)(lpBuffer, AddrToRead, Size, MM_COPY_MEMORY_PHYSICAL, BytesRead);
}
NTSTATUS WritePagefulAddress(PMap Map, PVOID TargetAddress, PVOID lpBuffer, SIZE_T Size, SIZE_T* BytesWritten)
{
	if (!TargetAddress) return STATUS_UNSUCCESSFUL;
	PHYSICAL_ADDRESS AddrToWrite;
	AddrToWrite.QuadPart = (ULONG64)TargetAddress;
	PVOID pmapped_mem = ((decltype(MmMapIoSpaceEx)*)Map->ImportTable.MmMapIoSpaceEx)(AddrToWrite, Size, PAGE_READWRITE);
	if (!pmapped_mem) return STATUS_UNSUCCESSFUL;
	__movsb((PUCHAR)pmapped_mem, (PUCHAR)lpBuffer, Size);
	*BytesWritten = Size;
	((decltype(MmUnmapIoSpace)*)Map->ImportTable.MmUnmapIoSpace)(pmapped_mem, Size);
	return STATUS_SUCCESS;
}

// ∑≠“Îµÿ÷∑
#define PAGE_OFFSET_SIZE 12
static const ULONG64 PMASK = (~0xfull << 8) & 0xfffffffffull;
__declspec(noinline) unsigned __int64 TranslatePagefulLinearAddress(PMap Map, ULONG64 Cr3, ULONG64 VirtualAddress) {
	Cr3 &= ~0xf;

	ULONG64 pageOffset = VirtualAddress & ~(~0ul << PAGE_OFFSET_SIZE);
	ULONG64 pte = ((VirtualAddress >> 12) & (0x1ffll));
	ULONG64 pt = ((VirtualAddress >> 21) & (0x1ffll));
	ULONG64 pd = ((VirtualAddress >> 30) & (0x1ffll));
	ULONG64 pdp = ((VirtualAddress >> 39) & (0x1ffll));

	SIZE_T readsize = 0;
	ULONG64 pdpe = 0;
	ReadPagefulAddress(Map, Cr3 + 8 * pdp, &pdpe, sizeof(pdpe), &readsize);
	if (~pdpe & 1)
		return 0;

	ULONG64 pde = 0;
	ReadPagefulAddress(Map, (pdpe & PMASK) + 8 * pd, &pde, sizeof(pde), &readsize);
	if (~pde & 1)
		return 0;

	/* 1GB large page, use pde's 12-34 bits */
	if (pde & 0x80)
		return (pde & (~0ull << 42 >> 12)) + (VirtualAddress & ~(~0ull << 30));

	ULONG64 pteAddr = 0;
	ReadPagefulAddress(Map, (pde & PMASK) + 8 * pt, &pteAddr, sizeof(pteAddr), &readsize);
	if (~pteAddr & 1)
		return 0;

	/* 2MB large page */
	if (pteAddr & 0x80)
		return (pteAddr & PMASK) + (VirtualAddress & ~(~0ull << 21));

	VirtualAddress = 0;
	ReadPagefulAddress(Map, (pteAddr & PMASK) + 8 * pte, &VirtualAddress, sizeof(VirtualAddress), &readsize);
	VirtualAddress &= PMASK;

	if (!VirtualAddress)
		return 0;

	return VirtualAddress + pageOffset;
}

NTSTATUS CopyPagefulMemory(PMap pMap, PDriverControl pControl) {
	if (!pControl->Address || !pControl->Buffer || !pControl->Size || !pMap->Process.CurrentCr3) return STATUS_UNSUCCESSFUL;

	NTSTATUS NtRet = STATUS_UNSUCCESSFUL;

	SIZE_T CurOffset = 0;
	SIZE_T TotalSize = pControl->Size;
	while (TotalSize)
	{

		ULONG64 CurPhysAddr = TranslatePagefulLinearAddress(pMap, pMap->Process.CurrentCr3, (ULONG64)pControl->Address + CurOffset);
		if (!CurPhysAddr) return STATUS_UNSUCCESSFUL;

		ULONG64 OpSize = min(PAGE_SIZE - (CurPhysAddr & 0xFFF), TotalSize);
		SIZE_T BytesOp = 0;
		NtRet = pControl->Write ? WritePagefulAddress(pMap, (PVOID)CurPhysAddr, (PVOID)((ULONG64)pControl->Buffer + CurOffset), OpSize, &BytesOp) :
			ReadPagefulAddress(pMap, CurPhysAddr, (PVOID)((ULONG64)pControl->Buffer + CurOffset), OpSize, &BytesOp);

		TotalSize -= BytesOp;
		CurOffset += BytesOp;
		if (NtRet != STATUS_SUCCESS) break;
		if (BytesOp == 0) break;
	}

	return NtRet;
}