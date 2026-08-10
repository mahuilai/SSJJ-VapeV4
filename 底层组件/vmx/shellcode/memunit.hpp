
__forceinline void SwapPhysicalMemoryRanges(PPHYSICAL_MEMORY_RANGE a, PPHYSICAL_MEMORY_RANGE b) {
	PHYSICAL_MEMORY_RANGE temp = *a;
	*a = *b;
	*b = temp;
}


__declspec(noinline) void HeapifyIterative(PPHYSICAL_MEMORY_RANGE ranges, ULONG64 n, ULONG64 i) {
	ULONG64 largest = i;

	while (TRUE) {
		ULONG64 left = 2 * largest + 1;  
		ULONG64 right = 2 * largest + 2; 


		if (left < n && ranges[left].BaseAddress.QuadPart > ranges[largest].BaseAddress.QuadPart) {
			largest = left;
		}


		if (right < n && ranges[right].BaseAddress.QuadPart > ranges[largest].BaseAddress.QuadPart) {
			largest = right;
		}


		if (largest != i) {
			SwapPhysicalMemoryRanges(&ranges[i], &ranges[largest]);
			i = largest;
		}
		else {
			break; 
		}
	}
}


__declspec(noinline) void HeapSortPhysicalMemoryRanges(PPHYSICAL_MEMORY_RANGE ranges, ULONG64 n) {

	for (LONG64 i = (n / 2) - 1; i >= 0; i--) {
		HeapifyIterative(ranges, n, i);
	}


	for (ULONG64 i = n - 1; i > 0; i--) {

		SwapPhysicalMemoryRanges(&ranges[0], &ranges[i]);


		HeapifyIterative(ranges, i, 0);
	}
}


__forceinline void PreprocessPhysicalMemoryRanges(PPHYSICAL_MEMORY_RANGE PhysicalMemoryRanges, ULONG64 PhysicalMemoryRangesCount) {
	if (PhysicalMemoryRangesCount > 1) {
		HeapSortPhysicalMemoryRanges(PhysicalMemoryRanges, PhysicalMemoryRangesCount);
	}
}

__declspec(noinline) bool IsValidPhysicalAddress(PPHYSICAL_MEMORY_RANGE PhysicalMemoryRanges, ULONG64 PhysicalRangeCount, ULONG64 PhysicalAddress, ULONG64 Size) {
	ULONG64 Left = 0;
	ULONG64 Right = PhysicalRangeCount - 1;
	ULONG64 EndAddress = PhysicalAddress + Size;

	while (Left <= Right) {
		ULONG64 Mid = Left + ((Right - Left) / 2);
		ULONG64 Start = PhysicalMemoryRanges[Mid].BaseAddress.QuadPart;
		ULONG64 End = Start + PhysicalMemoryRanges[Mid].NumberOfBytes.QuadPart;

		if (PhysicalAddress >= Start && EndAddress <= End) return true;

		if (PhysicalAddress < Start) {
			if (!Mid) break;
			Right = Mid - 1;
		}
		else {
			Left = Mid + 1;
		}
	}

	return false;
}


typedef struct {
	ULONG64 KeInvalidateRangeAllCaches;
	ULONG64 Page;
	ULONG64* PagePteAddress;
	ULONG64 OriginalPte;
	PPHYSICAL_MEMORY_RANGE PhysicalMemoryRanges;
	ULONG64 PhysicalMemoryRangesCount;
	ULONG64 PageOffset;
	__int64 RemainSize;
} InvlpgCopyMemoryParameters;

__declspec(noinline) bool InvlpgCopyPhysicalAddress(InvlpgCopyMemoryParameters* Parameters, ULONG64 Address, PVOID Buffer, ULONG64 Size, ULONG64 Write) {
	*(Parameters->PagePteAddress) = ((Address >> 12) << 12) | (Write == MemOperateType::ForceWrite ? 0b111101011 : 0b111100011) & (~(1 << 9));

	__invlpg((void*)Parameters->Page);

	unsigned char* Target = (unsigned char*)(Parameters->Page + (Address & 0xFFF));
	if (!Buffer || !Target) return false;

	if (!Write) __movsb((unsigned char*)Buffer, Target, Size);
	else __movsb(Target, (unsigned char*)Buffer, Size);
	return true;
}

__declspec(noinline) unsigned __int64 TranslateLinearAddress(InvlpgCopyMemoryParameters* Parameters, ULONG64 Cr3, ULONG64 VirtualAddress, ULONG64* PxeAddress) {
	ULONG64 pml4 = 0;
	ULONG64 pml4_address = (Cr3 & 0xFFFFFFFFFF000) + 8 * ((VirtualAddress >> 39) & 0x1FF);

	if (!InvlpgCopyPhysicalAddress(Parameters, pml4_address, &pml4, 8, 0)) return 0;

	if (!(pml4 & 1)) return 0;
	if (((pml4 >> 12) & 0xFFFFFFFFFF) == ((Cr3 >> 12) & 0xFFFFFFFFFF)) return 0;

	ULONG64 pdpte = 0;
	ULONG64 pdpte_address = (pml4 & 0xFFFFFFFFFF000) + 8 * ((VirtualAddress >> 30) & 0x1FF);

	if (!InvlpgCopyPhysicalAddress(Parameters, pdpte_address, &pdpte, 8, 0)) return 0;

	if (!(pdpte & 1)) return 0;
	if (((pdpte >> 7) & 1)) {
		if (!(pdpte & 0b10)) *PxeAddress = pdpte_address;
		return (pdpte & 0xFFFFFC0000000) + (VirtualAddress & 0x3FFFFFFF);
	}

	ULONG64 pde = 0;
	ULONG64 pde_address = (pdpte & 0xFFFFFFFFFF000) + 8 * ((VirtualAddress >> 21) & 0x1FF);

	if (!InvlpgCopyPhysicalAddress(Parameters, pde_address, &pde, 8, 0)) return 0;

	if (!(pde & 1)) return 0;
	if (((pde >> 7) & 1)) {
		if (!(pde & 0b10)) *PxeAddress = pde_address;
		return (pde & 0xFFFFFFFE00000) + (VirtualAddress & 0x1FFFFF);
	}

	ULONG64 pte = 0;
	ULONG64 pte_address = (pde & 0xFFFFFFFFFF000) + 8 * ((VirtualAddress >> 12) & 0x1FF);

	if (!InvlpgCopyPhysicalAddress(Parameters, pte_address, &pte, 8, 0)) return 0;

	if (!(pte & 1)) return 0;

	if (!(pte & 0b10)) *PxeAddress = pte_address;

	if (pte & 0x200000000000) return (pte & 0xFFFFFFFF000) + (VirtualAddress & 0xFFF);

	return (pte & 0xFFFFFFFFFF000) + (VirtualAddress & 0xFFF);
}