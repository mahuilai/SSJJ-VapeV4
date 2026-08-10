#pragma once
#include <ntifs.h>
#include <ntddmou.h>

#define IsLoad 0x1000000
#define VMCall 0x2000000

#define VM_TRUE 0x1FBD1DF5
#define VM_FALSE 0x5F3759DF
#define VM_MAGICNUMBER 0x2A517D3C

#define MI_PFN_PRIORITY_BITS    3
typedef ULONG WSLE_NUMBER, * PWSLE_NUMBER;

typedef struct _MMPFNENTRY {
	USHORT Modified : 1;
	USHORT ReadInProgress : 1;
	USHORT WriteInProgress : 1;
	USHORT PrototypePte : 1;
	USHORT PageColor : 4;
	USHORT PageLocation : 3;
	USHORT RemovalRequested : 1;
	USHORT CacheAttribute : 2;
	USHORT Rom : 1;
	USHORT ParityError : 1;
} MMPFNENTRY;
typedef struct _MMPFN {
	ULONG64 Flink;
	ULONG64 PteAddress;
	ULONG64 Blink;
	union {
		struct {
			USHORT ReferenceCount;
			MMPFNENTRY e1;
		};
		struct {
			USHORT ReferenceCount;
			USHORT ShortFlags;
		} e2;
	} u3;
#if defined (_WIN64)
	ULONG UsedPageTableEntries;
#endif
	LONG AweReferenceCount;
	union {
		ULONG_PTR EntireFrame;
		struct {
#if defined (_WIN64)
			ULONG_PTR PteFrame : 57;
#else
			ULONG_PTR PteFrame : 25;
#endif
			ULONG_PTR InPageError : 1;
			ULONG_PTR VerifierAllocation : 1;
			ULONG_PTR AweAllocation : 1;
			ULONG_PTR Priority : MI_PFN_PRIORITY_BITS;
			ULONG_PTR MustBeCached : 1;
		};
	} u4;

} MMPFN, * PMMPFN;

typedef struct _LDR_DATA_TABLE_ENTRY
{
	LIST_ENTRY InLoadOrderLinks;
	LIST_ENTRY InMemoryOrderLinks;
	LIST_ENTRY InInitializationOrderLinks;
	PVOID DllBase;
	PVOID EntryPoint;
	ULONG SizeOfImage;
	UNICODE_STRING FullDllName;
	UNICODE_STRING BaseDllName;
	ULONG Flags;
	short LoadCount;
	short TlsIndex;
	union
	{
		LIST_ENTRY HashLinks;
		struct
		{
			PVOID SectionPointer;
			ULONG CheckSum;
		};
	};
	union
	{
		ULONG TimeDateStamp;
		PVOID LoadedImports;
	};
	PVOID* EntryPointActivationContext;
	PVOID PatchInformation;
	LIST_ENTRY ForwarderLinks;
	LIST_ENTRY ServiceTagLinks;
	LIST_ENTRY StaticLinks;
} LDR_DATA_TABLE_ENTRY, * PLDR_DATA_TABLE_ENTRY;

typedef struct _DriverControl {
	ULONG64 CheckCode;
	ULONG64 Flag;
	union {
		struct {
			ULONG64 ProcessId;
			ULONG64 Address;
			ULONG64 Buffer;
		};
		MOUSE_INPUT_DATA InputData;
	};
	ULONG64 Size;
	ULONG64 Write;
}DriverControl, * PDriverControl;

typedef struct _Map {
	struct {
		union {
			struct {
				union {
					struct {
						ULONG64 MmIsAddressValid;
						ULONG64 PsGetProcessWow64Process;
						ULONG64 PsGetProcessPeb;
						ULONG64 MmGetPhysicalMemoryRangesEx;
						ULONG64 ExAllocatePoolWithTag;
						ULONG64 ExFreePoolWithTag;

						ULONG64 MmCopyMemory;
						ULONG64 MmMapIoSpaceEx;
						ULONG64 MmUnmapIoSpace;
						ULONG64 MmCopyVirtualMemory;
						ULONG64 IoGetCurrentProcess;
						ULONG64 PsGetProcessSectionBaseAddress;
						ULONG64 PsGetProcessId;

						ULONG64 RtlHashUnicodeString;
						ULONG64 MiGetPageTablePfnBuddyRaw;

						ULONG64 KeAcquireSpinLockAtDpcLevel;
						ULONG64 KeReleaseSpinLockFromDpcLevel;
						ULONG64 IofCompleteRequest;
						ULONG64 IoReleaseRemoveLockEx;

						ULONG64 KeInvalidateRangeAllCaches;
						ULONG64 memmove;

						ULONG64 IoCreateFileEx;
						ULONG64 ObReferenceObjectByHandleWithTag;
						ULONG64 ObfDereferenceObject;
						ULONG64 ObCloseHandle;

						ULONG64 ZwDeleteFile;
						ULONG64 MmFlushImageSection;
						ULONG64 IoFileObjectType;

						ULONG64 KeGetCurrentProcessorNumberEx;
						ULONG64 PsLookupProcessByProcessId;
						ULONG64 RtlGetVersion;

						ULONG64 ObOpenObjectByPointer;
						ULONG64 ZwAllocateVirtualMemory;
						ULONG64 ZwProtectVirtualMemory;
						ULONG64 ZwFreeVirtualMemory;
						ULONG64 ZwClose;
						ULONG64 PsProcessType;
						ULONG64 RtlCreateUserThread;
						ULONG64 ZwWaitForSingleObject;
					};

					ULONG64 Ntoskrnl[512];
				};

				union {
					struct {
						ULONG64 ValidateHwnd;
					};

					ULONG64 win32kbase[8];
				};
			};

			ULONG64 _blank[1024];
		};
	} ImportTable;

	struct {
		ULONG64 Ptes;
		ULONG64 PteBase;
		ULONG64 SystemPteAddress;
		ULONG64 MmPfnDataBase;
		ULONG64 MouseClassServiceCallback;
		ULONG64 MouseDevice;
		PLDR_DATA_TABLE_ENTRY NtoskrnlLdr;
	} System;

	struct {
		ULONG64 CurrentCr3;
		ULONG64 CurrentPEB;
		PVOID CurrentProcess;

		PPHYSICAL_MEMORY_RANGE PhysicalMemoryRanges;
		ULONG64 PhysicalMemoryRangesCount;

		ULONG64 DecryptCr3;
	} Process;

	struct {
		ULONG64 TimeStamp;
		ULONG64 Prev;
		ULONG64 Next;
	} Verify;

	struct {
		CHAR threadUseMap[256];
		ULONG64 threadDataBuffer[256];
		ULONG64 threadDataBufferSize[256];
	} Thread;
}Map, * PMap;

typedef union __cr3
{
	ULONG64 flags;
	struct
	{
		ULONG64 reserved1 : 3;
		ULONG64 page_level_write_through : 1;
		ULONG64 page_level_cache_disable : 1;
		ULONG64 reserved2 : 7;
		ULONG64 dirbase : 36;
		ULONG64 reserved3 : 16;
	};
} _cr3;

typedef union __pml4
{
	ULONG64 value;
	struct
	{
		ULONG64 present : 1; //0
		ULONG64 ReadWrite : 1; // 1
		ULONG64 user_supervisor : 1; // 2
		ULONG64 PageWriteThrough : 1; // 3
		ULONG64 page_cache : 1; // 4
		ULONG64 accessed : 1; // 5
		ULONG64 Ignored1 : 1; // 6
		ULONG64 page_size : 1; // 7
		ULONG64 Ignored2 : 4; // 8
		ULONG64 pfn : 36; // 12
		ULONG64 Reserved : 4;
		ULONG64 Ignored3 : 11;
		ULONG64 nx : 1;
	};
} _pml4, * _ppml4;

typedef union __pdpte
{
	ULONG64 value;
	struct
	{
		ULONG64 present : 1;
		ULONG64 ReadWrite : 1;
		ULONG64 user_supervisor : 1;
		ULONG64 PageWriteThrough : 1;
		ULONG64 page_cache : 1;
		ULONG64 accessed : 1;
		ULONG64 Ignored1 : 1;
		ULONG64 page_size : 1;
		ULONG64 Ignored2 : 4;
		ULONG64 pfn : 36;
		ULONG64 Reserved : 4;
		ULONG64 Ignored3 : 11;
		ULONG64 nx : 1;
	};
} _pdpte, * _ppdpte;

typedef union __pde
{
	ULONG64 value;
	struct
	{
		ULONG64 present : 1;
		ULONG64 ReadWrite : 1;
		ULONG64 user_supervisor : 1;
		ULONG64 PageWriteThrough : 1;
		ULONG64 page_cache : 1;
		ULONG64 accessed : 1;
		ULONG64 Ignored1 : 1;
		ULONG64 page_size : 1;
		ULONG64 Ignored2 : 4;
		ULONG64 pfn : 36;
		ULONG64 Reserved : 4;
		ULONG64 Ignored3 : 11;
		ULONG64 nx : 1;
	};
} _pde, * _ppde;

typedef union __pte
{
	ULONG64 value;
	struct
	{
		ULONG64 present : 1;
		ULONG64 ReadWrite : 1;
		ULONG64 user_supervisor : 1;
		ULONG64 PageWriteThrough : 1;
		ULONG64 page_cache : 1;
		ULONG64 accessed : 1;
		ULONG64 Dirty : 1;
		ULONG64 PageAccessType : 1;
		ULONG64 Global : 1;
		ULONG64 Ignored2 : 3;
		ULONG64 pfn : 36;
		ULONG64 Reserved : 4;
		ULONG64 Ignored3 : 7;
		ULONG64 ProtectionKey : 4;
		ULONG64 nx : 1;
	};
} _pte, * _ppte;

typedef struct _DriverParameter {
	ULONG64 VMKey;
	PDriverControl Control;
	PVOID OutputBuffer;
}DriverParameter, * PDriverParameter;
namespace MemOperateType {
	enum {
		Read = 0,
		Write = 1,
		ForceWrite = 2,
		ForceWrite2 = 3
	};
}

#define KERNEL_SPACE_START 0xFFFF080000000000
#define KERNEL_SPACE_END 0xFFFFFFFFFFFFFFFF

typedef struct
{
	UINT8  Type;
	UINT8  Length;
	UINT8  Handle[2];
} SMBIOS_HEADER, * PSMBIOS_HEADER;

typedef UINT8 SMBIOS_STRING;

typedef struct
{
	SMBIOS_HEADER   Hdr;
	SMBIOS_STRING   Vendor;
	SMBIOS_STRING   BiosVersion;
	UINT8           BiosSegment[2];
	SMBIOS_STRING   BiosReleaseDate;
	UINT8           BiosSize;
	UINT8           BiosCharacteristics[8];
} SMBIOS_TYPE0;

typedef struct
{
	SMBIOS_HEADER   Hdr;
	SMBIOS_STRING   Manufacturer;
	SMBIOS_STRING   ProductName;
	SMBIOS_STRING   Version;
	SMBIOS_STRING   SerialNumber;
	GUID			Uuid; // EFI_GUID == GUID?
	UINT8           WakeUpType;
} SMBIOS_TYPE1;

typedef struct
{
	SMBIOS_HEADER   Hdr;
	SMBIOS_STRING   Manufacturer;
	SMBIOS_STRING   ProductName;
	SMBIOS_STRING   Version;
	SMBIOS_STRING   SerialNumber;
} SMBIOS_TYPE2;

typedef struct
{
	SMBIOS_HEADER   Hdr;
	SMBIOS_STRING   Manufacturer;
	UINT8           Type;
	SMBIOS_STRING   Version;
	SMBIOS_STRING   SerialNumber;
	SMBIOS_STRING   AssetTag;
	UINT8           BootupState;
	UINT8           PowerSupplyState;
	UINT8           ThermalState;
	UINT8           SecurityStatus;
	UINT8           OemDefined[4];
} SMBIOS_TYPE3;

//CPU
typedef struct {
	UINT32    ProcessorSteppingId : 4;
	UINT32    ProcessorModel : 4;
	UINT32    ProcessorFamily : 4;
	UINT32    ProcessorType : 2;
	UINT32    ProcessorReserved1 : 2;
	UINT32    ProcessorXModel : 4;
	UINT32    ProcessorXFamily : 8;
	UINT32    ProcessorReserved2 : 4;
} PROCESSOR_SIGNATURE;

typedef struct {
	UINT8    ProcessorVoltageCapability5V : 1;
	UINT8    ProcessorVoltageCapability3_3V : 1;
	UINT8    ProcessorVoltageCapability2_9V : 1;
	UINT8    ProcessorVoltageCapabilityReserved : 1; ///< Bit 3, must be zero.
	UINT8    ProcessorVoltageReserved : 3; ///< Bits 4-6, must be zero.
	UINT8    ProcessorVoltageIndicateLegacy : 1;
} PROCESSOR_VOLTAGE;

typedef struct {
	UINT32    ProcessorFpu : 1;
	UINT32    ProcessorVme : 1;
	UINT32    ProcessorDe : 1;
	UINT32    ProcessorPse : 1;
	UINT32    ProcessorTsc : 1;
	UINT32    ProcessorMsr : 1;
	UINT32    ProcessorPae : 1;
	UINT32    ProcessorMce : 1;
	UINT32    ProcessorCx8 : 1;
	UINT32    ProcessorApic : 1;
	UINT32    ProcessorReserved1 : 1;
	UINT32    ProcessorSep : 1;
	UINT32    ProcessorMtrr : 1;
	UINT32    ProcessorPge : 1;
	UINT32    ProcessorMca : 1;
	UINT32    ProcessorCmov : 1;
	UINT32    ProcessorPat : 1;
	UINT32    ProcessorPse36 : 1;
	UINT32    ProcessorPsn : 1;
	UINT32    ProcessorClfsh : 1;
	UINT32    ProcessorReserved2 : 1;
	UINT32    ProcessorDs : 1;
	UINT32    ProcessorAcpi : 1;
	UINT32    ProcessorMmx : 1;
	UINT32    ProcessorFxsr : 1;
	UINT32    ProcessorSse : 1;
	UINT32    ProcessorSse2 : 1;
	UINT32    ProcessorSs : 1;
	UINT32    ProcessorReserved3 : 1;
	UINT32    ProcessorTm : 1;
	UINT32    ProcessorReserved4 : 2;
} PROCESSOR_FEATURE_FLAGS;
typedef struct {
	PROCESSOR_SIGNATURE        Signature;
	PROCESSOR_FEATURE_FLAGS    FeatureFlags;
} PROCESSOR_ID_DATA;

//64
typedef struct {
	UINT8     AnchorString[5];
	UINT8     EntryPointStructureChecksum;
	UINT8     EntryPointLength;
	UINT8     MajorVersion;
	UINT8     MinorVersion;
	UINT8     DocRev;
	UINT8     EntryPointRevision;
	UINT8     Reserved;
	UINT32    TableMaximumSize;
	UINT64    TableAddress;
} SMBIOS_STRUCTURE_TABLE;

typedef struct _SMBIOS_TYPE4 {
	SMBIOS_HEADER    Hdr; //bios
	SMBIOS_STRING    SocketDesignation;
	UINT8                  ProcessorType;         ///< The enumeration value from PROCESSOR_TYPE_DATA.
	UINT8                  ProcessorFamily;       ///< The enumeration value from PROCESSOR_FAMILY_DATA.
	SMBIOS_STRING    ProcessorManufacturer;
	unsigned long long      ProcessorId;         //fix bios
	SMBIOS_STRING    ProcessorVersion;
	PROCESSOR_VOLTAGE      Voltage;
	UINT16                 ExternalClock;
	UINT16                 MaxSpeed;
	UINT16                 CurrentSpeed;
	UINT8                  Status;
	UINT8                  ProcessorUpgrade;     ///< The enumeration value from PROCESSOR_UPGRADE.
	UINT16                 L1CacheHandle;
	UINT16                 L2CacheHandle;
	UINT16                 L3CacheHandle;
	SMBIOS_STRING    SerialNumber;
	SMBIOS_STRING    AssetTag;
	SMBIOS_STRING    PartNumber;
	UINT8                  CoreCount;
	UINT8                  EnabledCoreCount;
	UINT8                  ThreadCount;
	UINT16                 ProcessorCharacteristics;
	UINT16                 ProcessorFamily2;
	UINT16                 CoreCount2;
	UINT16                 EnabledCoreCount2;
	UINT16                 ThreadCount2;
	UINT16                 ThreadEnabled;
} SMBIOS_TYPE4, * PSMBIOS_TYPE4;

typedef struct {
	UINT16    Reserved : 1;
	UINT16    Other : 1;
	UINT16    Unknown : 1;
	UINT16    FastPaged : 1;
	UINT16    StaticColumn : 1;
	UINT16    PseudoStatic : 1;
	UINT16    Rambus : 1;
	UINT16    Synchronous : 1;
	UINT16    Cmos : 1;
	UINT16    Edo : 1;
	UINT16    WindowDram : 1;
	UINT16    CacheDram : 1;
	UINT16    Nonvolatile : 1;
	UINT16    Registered : 1;
	UINT16    Unbuffered : 1;
	UINT16    LrDimm : 1;
} MEMORY_DEVICE_TYPE_DETAIL;

//À´×Ôedk2
typedef struct {
	SMBIOS_HEADER                           Hdr;
	UINT16                                     MemoryArrayHandle;
	UINT16                                     MemoryErrorInformationHandle;
	UINT16                                     TotalWidth;
	UINT16                                     DataWidth;
	UINT16                                     Size;
	UINT8                                      FormFactor;        ///< The enumeration value from MEMORY_FORM_FACTOR.
	UINT8                                      DeviceSet;
	SMBIOS_STRING                        DeviceLocator;
	SMBIOS_STRING                        BankLocator;
	UINT8                                      MemoryType;        ///< The enumeration value from MEMORY_DEVICE_TYPE.
	MEMORY_DEVICE_TYPE_DETAIL                  TypeDetail;
	UINT16                                     Speed;
	SMBIOS_STRING                        Manufacturer;
	SMBIOS_STRING                        SerialNumber;
	SMBIOS_STRING                        AssetTag;
	SMBIOS_STRING                        PartNumber;
} MEMORY_DEVICE_HEADER;

typedef struct _IOC_REQUEST {
	PVOID Buffer;
	ULONG BufferLength;
	PVOID OldContext;
	PIO_COMPLETION_ROUTINE OldRoutine;
} IOC_REQUEST, * PIOC_REQUEST;

extern "C" NTSTATUS NTAPI ZwProtectVirtualMemory(
	HANDLE ProcessHandle,
	PVOID* BaseAddress,
	PSIZE_T RegionSize,
	ULONG NewProtect,
	PULONG OldProtect
);

EXTERN_C NTKERNELAPI NTSTATUS RtlCreateUserThread(HANDLE ProcessHandle, PSECURITY_DESCRIPTOR SecurityDescriptor, BOOLEAN CreateSuspended, ULONG StackZeroBits, SIZE_T StackReserve, SIZE_T StackCommit, PVOID StartAddress, PVOID StartParameter, PHANDLE ThreadHandle, PVOID ClientID);