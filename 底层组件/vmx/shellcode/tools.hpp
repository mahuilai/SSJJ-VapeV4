#include "structs.h"

#define MiGetVirtualAddressMappedByPte(PteBase, PTE) (ULONG64)((LONG_PTR)(((LONG_PTR)(PTE) - PteBase) << 25L) >> 16)
#define MiGetPteAddress(PteBase, VirtualAddress) (ULONG64*)(((VirtualAddress >> 9) & 0x7FFFFFFFF8) + PteBase);

class SpoofFunction {
public:
	ULONG64 temp = 0;
	void* ret_addr_in_stack = 0;
	__forceinline SpoofFunction(PVOID addr, ULONG64 fakeaddr) :ret_addr_in_stack(addr) {
		temp = *(ULONG64*)ret_addr_in_stack;
		*(ULONG64*)ret_addr_in_stack = fakeaddr;
	}
	__forceinline ~SpoofFunction() {
		*(ULONG64*)ret_addr_in_stack = temp;
	}
};

__forceinline KIRQL RaiseIrql(KIRQL NewIrql)
{
	KIRQL result = (KIRQL)__readcr8();
	__writecr8(NewIrql);
	return result;
}

__forceinline void LowerIrql(KIRQL NewIrql)
{
	__writecr8(NewIrql);
}

__forceinline int GetThreadUniqueIndexFromMap(PMap Map, ULONG64 BufferSize) {
	if (BufferSize < PAGE_SIZE) BufferSize = PAGE_SIZE;

	int threadIndex = -1;

	for (int i = 0; i < 256; ++i) {
		if (!_InterlockedCompareExchange8(&Map->Thread.threadUseMap[i], true, false)) {
			threadIndex = i;
			break;
		}
	}

	if (threadIndex != -1) {
		if (Map->Thread.threadDataBufferSize[threadIndex] < BufferSize || !Map->Thread.threadDataBuffer[threadIndex]) {
			if (Map->Thread.threadDataBuffer[threadIndex]) {
				((void(*)(ULONG64, ULONG))Map->ImportTable.ExFreePoolWithTag)(Map->Thread.threadDataBuffer[threadIndex], 0x656E6F4Eu);
				Map->Thread.threadDataBuffer[threadIndex] = 0;
				Map->Thread.threadDataBufferSize[threadIndex] = 0;
			}
  
			Map->Thread.threadDataBuffer[threadIndex] =
				((ULONG64(*)(POOL_TYPE, SIZE_T, ULONG))Map->ImportTable.ExAllocatePoolWithTag)(NonPagedPoolNx, BufferSize, 0x656E6F4Eu);
			if (Map->Thread.threadDataBuffer[threadIndex]) {
				Map->Thread.threadDataBufferSize[threadIndex] = BufferSize;
			}
			else { 
				Map->Thread.threadDataBufferSize[threadIndex] = 0;
				_InterlockedExchange8(&Map->Thread.threadUseMap[threadIndex], 0);
				threadIndex = -1;
			}
		}
	}

	return threadIndex;
}

__declspec(noinline) wchar_t* _wcsstr(wchar_t* str, wchar_t* substr)
{
	if (str == NULL || substr == NULL) return NULL;

	if (*substr == L'\0') return str;
	wchar_t* p1 = str;
	wchar_t* p2 = substr;
	while (*p1 != L'\0')
	{
		if (*p1 == *p2)
		{
			wchar_t* p1_temp = p1;
			wchar_t* p2_temp = p2;
			while (*p1_temp != L'\0' && *p2_temp != L'\0' && *p1_temp == *p2_temp)
			{
				p1_temp++; p2_temp++;
			}
			if (*p2_temp == L'\0') return p1;
		}
		p1++;
	}
	return NULL;
}

__declspec(noinline) size_t _strlen(const char* str) {
	size_t len = 0;
	const char* ptr = str;
	while (*ptr++ != '\0') len++;
	return len;
}