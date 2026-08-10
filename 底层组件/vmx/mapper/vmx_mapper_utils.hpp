#define MiGetVirtualAddressMappedByPte(_PteBase, _Pte) (ULONG64)((LONG_PTR)(((LONG_PTR)(_Pte) - _PteBase) << 25L) >> 16)
#define MiGetPteAddress(_PteBase, _VirtualAddress) (ULONG64*)(((_VirtualAddress >> 9) & 0x7FFFFFFFF8) + _PteBase);
#define MiGetSystemPteAddress(_VirtualAddress) (ULONG64*)(((_VirtualAddress >> 9) & 0x7FFFFFFFF8));
#define RVA(Instr, InstrSize) ((DWORD64)Instr + InstrSize + *(LONG*)((DWORD64)Instr + (InstrSize - sizeof(LONG))))

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

typedef struct _Map {
    struct {
        union {
            struct {
                union {
                    struct {
                        ULONG64 e1;
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

void ReplaceValue(unsigned char* array, int size, ULONG64 value1, ULONG64 value2) {
    if (sizeof(ULONG64) > size) return;
    for (int i = 0; i <= size - sizeof(ULONG64); ++i) {
        ULONG64* current = reinterpret_cast<ULONG64*>(&array[i]);
        if (*current == value1) *current = value2;
    }
}

NTSTATUS ReadRegistryQword(ULONG64 kernel_base, PCWSTR registryPath, PCWSTR valueName, PULONGLONG dataOut) {
    UNICODE_STRING path;
    UNICODE_STRING value;
    OBJECT_ATTRIBUTES attributes;
    HANDLE hKey = NULL;
    NTSTATUS status = STATUS_UNSUCCESSFUL;
    ULONG resultLength;

    // 初始化Unicode字符串
    KLI_FN(kernel_base, RtlInitUnicodeString)(&path, registryPath);
    KLI_FN(kernel_base, RtlInitUnicodeString)(&value, valueName);

    // 初始化对象属性
    InitializeObjectAttributes(&attributes, &path, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);

    // 尝试打开注册表键
    status = KLI_FN(kernel_base, ZwOpenKey)(&hKey, KEY_READ, &attributes);
    if (!NT_SUCCESS(status)) return status;

    PKEY_VALUE_PARTIAL_INFORMATION keyValueInformation =
        (PKEY_VALUE_PARTIAL_INFORMATION)KLI_FN(kernel_base, ExAllocatePoolWithTag)(NonPagedPoolNx, sizeof(KEY_VALUE_PARTIAL_INFORMATION) + sizeof(ULONGLONG) - 1, 'abcd');

    if (!keyValueInformation) {
        KLI_FN(kernel_base, ZwClose)(hKey);
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    status = KLI_FN(kernel_base, ZwQueryValueKey)(hKey, &value, KeyValuePartialInformation, keyValueInformation, sizeof(KEY_VALUE_PARTIAL_INFORMATION) + sizeof(ULONGLONG) - 1, &resultLength);
    if (keyValueInformation->Type == REG_QWORD && keyValueInformation->DataLength == sizeof(ULONGLONG))
        *dataOut = *(PULONGLONG)(keyValueInformation->Data);
    else status = STATUS_INVALID_PARAMETER;

    KLI_FN(kernel_base, ExFreePoolWithTag)(keyValueInformation, 'abcd');
    KLI_FN(kernel_base, ZwClose)(hKey);

    return status;
}
NTSTATUS ReadRegistryBinary(ULONG64 kernel_base, PCWSTR registryPath, PCWSTR valueName, unsigned char** output, int* output_size) {
    UNICODE_STRING path;
    UNICODE_STRING value;
    OBJECT_ATTRIBUTES attributes;
    HANDLE hKey = NULL;
    NTSTATUS status = STATUS_UNSUCCESSFUL;
    ULONG resultLength;

    // 初始化Unicode字符串
    KLI_FN(kernel_base, RtlInitUnicodeString)(&path, registryPath);
    KLI_FN(kernel_base, RtlInitUnicodeString)(&value, valueName);

    // 初始化对象属性
    InitializeObjectAttributes(&attributes, &path, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);

    // 尝试打开注册表键
    status = KLI_FN(kernel_base, ZwOpenKey)(&hKey, KEY_READ, &attributes);
    if (!NT_SUCCESS(status)) return status;

    // 首先查询所需的数据大小
    status = KLI_FN(kernel_base, ZwQueryValueKey)(hKey, &value, KeyValuePartialInformation, NULL, 0, &resultLength);
    if (status != STATUS_BUFFER_TOO_SMALL) {
        KLI_FN(kernel_base, ZwClose)(hKey);
        return status == STATUS_SUCCESS ? STATUS_UNSUCCESSFUL : status;
    }

    // 分配内存以读取数据
    PKEY_VALUE_PARTIAL_INFORMATION keyValueInformation = (PKEY_VALUE_PARTIAL_INFORMATION)KLI_FN(kernel_base, ExAllocatePoolWithTag)(NonPagedPoolNx, resultLength, 'abcd');

    if (!keyValueInformation) {
        KLI_FN(kernel_base, ZwClose)(hKey);
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    // 实际查询数据
    status = KLI_FN(kernel_base, ZwQueryValueKey)(hKey, &value, KeyValuePartialInformation, keyValueInformation, resultLength, &resultLength);
    *output_size = keyValueInformation->DataLength;
    *output = (unsigned char*)KLI_FN(kernel_base, ExAllocatePoolWithTag)(NonPagedPoolNx, *output_size, 'abcd');
    if (*output) KLI_FN(kernel_base, memcpy)(*output, keyValueInformation->Data, *output_size);
    else status = STATUS_INSUFFICIENT_RESOURCES;

    KLI_FN(kernel_base, ExFreePoolWithTag)(keyValueInformation, 0);
    KLI_FN(kernel_base, ZwClose)(hKey);

    return status;
}
NTSTATUS DeleteRegistryPath(ULONG64 kernel_base, PCWSTR registryPath) {
    UNICODE_STRING path;
    OBJECT_ATTRIBUTES attributes;
    HANDLE hKey = NULL;
    NTSTATUS status = STATUS_UNSUCCESSFUL;
    KLI_FN(kernel_base, RtlInitUnicodeString)(&path, registryPath);
    InitializeObjectAttributes(&attributes, &path, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);
    status = KLI_FN(kernel_base, ZwOpenKey)(&hKey, DELETE, &attributes);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    status = KLI_FN(kernel_base, ZwDeleteKey)(hKey);
    KLI_FN(kernel_base, ZwClose)(hKey);
    return status;
}
BOOLEAN WriteKernelMemory(ULONG64 kernel_base, PVOID address, PVOID buffer, ULONG size) {
    auto mdl = KLI_FN(kernel_base, IoAllocateMdl)(address, size, FALSE, FALSE, NULL);
    if (!mdl) return FALSE;

    KLI_FN(kernel_base, MmProbeAndLockPages)(mdl, KernelMode, IoWriteAccess);

    auto map = KLI_FN(kernel_base, MmMapLockedPagesSpecifyCache)(mdl, KernelMode, MmNonCached, NULL, FALSE, NormalPagePriority);
    if (!map) {
        KLI_FN(kernel_base, MmUnlockPages)(mdl);
        KLI_FN(kernel_base, IoFreeMdl)(mdl);
        return FALSE;
    }

    if (!NT_SUCCESS(KLI_FN(kernel_base, MmProtectMdlSystemAddress)(mdl, PAGE_READWRITE))) {
        KLI_FN(kernel_base, MmUnmapLockedPages)(map, mdl);
        KLI_FN(kernel_base, MmUnlockPages)(mdl);
        KLI_FN(kernel_base, IoFreeMdl)(mdl);
        return FALSE;
    }
    __movsb((PUCHAR)map, (PUCHAR)buffer, size);

    KLI_FN(kernel_base, MmUnmapLockedPages)(map, mdl);
    KLI_FN(kernel_base, MmUnlockPages)(mdl);
    KLI_FN(kernel_base, IoFreeMdl)(mdl);
    return TRUE;
}

ULONG_PTR NTAPI InvalidateAllCachesOnProcessor(ULONG_PTR Argument) {
    __invlpg((void*)Argument);
    return 0;
}

NTSTATUS DeleteDriverFile(ULONG64 kernel_base, PVOID _IoFileObjectType, PUNICODE_STRING pUsDriverPath)
{
    IO_STATUS_BLOCK IoStatusBlock;
    HANDLE FileHandle;
    OBJECT_ATTRIBUTES ObjectAttributes;
    InitializeObjectAttributes(
        &ObjectAttributes,
        pUsDriverPath,
        OBJ_KERNEL_HANDLE | OBJ_CASE_INSENSITIVE,
        0,
        0);

    NTSTATUS Status = KLI_FN(kernel_base, IoCreateFileEx)(&FileHandle,
        SYNCHRONIZE | DELETE,
        &ObjectAttributes,
        &IoStatusBlock,
        nullptr,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_DELETE,
        FILE_OPEN,
        FILE_NON_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT,
        nullptr,
        0,
        CreateFileTypeNone,
        nullptr,
        IO_NO_PARAMETER_CHECKING,
        nullptr);

    if (!NT_SUCCESS(Status)) return Status;

    PFILE_OBJECT FileObject;
    Status = 
        KLI_FN(kernel_base, ObReferenceObjectByHandleWithTag)(FileHandle, SYNCHRONIZE | DELETE, *(POBJECT_TYPE*)_IoFileObjectType, KernelMode, 0, reinterpret_cast<PVOID*>(&FileObject), nullptr);
    if (!NT_SUCCESS(Status))
    {
        KLI_FN(kernel_base, ObCloseHandle)(FileHandle, KernelMode);
        return Status;
    }

    PSECTION_OBJECT_POINTERS SectionObjectPointer = FileObject->SectionObjectPointer;
    SectionObjectPointer->ImageSectionObject = nullptr;

    BOOLEAN ImageSectionFlushed = KLI_FN(kernel_base, MmFlushImageSection)(SectionObjectPointer, MmFlushForDelete);

    KLI_FN(kernel_base, ObfDereferenceObject)(FileObject);
    KLI_FN(kernel_base, ObCloseHandle)(FileHandle, KernelMode);

    if (ImageSectionFlushed)
    {
        Status = KLI_FN(kernel_base, ZwDeleteFile)(&ObjectAttributes);
        if (NT_SUCCESS(Status)) return Status;
    }

    return Status;
}

int my_strcmp(const char* s1, const char* s2) {
    while (*s1 && (*s1 == *s2)) {
        s1++;
        s2++;
    }
    return (unsigned char)*s1 - (unsigned char)*s2;
}

wchar_t* my_wcsstr(wchar_t* str, wchar_t* substr)
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
#define KERNEL_SPACE_START 0xFFFF080000000000 // 适用于 64 位系统的内核地址空间起始地址
#define KERNEL_SPACE_END 0xFFFFFFFFFFFFFFFF   // 适用于 64 位系统的内核地址空间结束地址
PVOID FindBase(PDRIVER_OBJECT pDriverObject, wchar_t* Name, PLDR_DATA_TABLE_ENTRY* LdrEntry)
{
    PLDR_DATA_TABLE_ENTRY pLdrEntry = (PLDR_DATA_TABLE_ENTRY)pDriverObject->DriverSection;
    PLDR_DATA_TABLE_ENTRY pCurEntry = pLdrEntry;

    do
    {
        if (pCurEntry->BaseDllName.Buffer != NULL && (ULONG64)pCurEntry->BaseDllName.Buffer > KERNEL_SPACE_START && (ULONG64)pCurEntry->BaseDllName.Buffer < KERNEL_SPACE_END)
        {
            if (my_wcsstr(pCurEntry->BaseDllName.Buffer, Name) != NULL) {
                if (LdrEntry) *LdrEntry = pCurEntry;
                return pCurEntry->DllBase;
            }
        }

        pCurEntry = (PLDR_DATA_TABLE_ENTRY)pCurEntry->InLoadOrderLinks.Flink;
    } while (pCurEntry != pLdrEntry);

    return NULL;
}

void RelocateBaseValue(unsigned char* data, int size, ULONG64 value) {
    if (size < sizeof(ULONG64)) return;
    for (int i = 0; i < size; i += sizeof(ULONG64)) {
        ULONG64* p = reinterpret_cast<ULONG64*>(data + i);
        if (*p) *p += value;
    }
}

typedef struct _PTE_PAGE {
    PVOID MappingAddress;
    PMDL Mdl;
    PVOID VirtualAddress;
} PTE_PAGE, * PPTE_PAGE;
ULONG64 Get_PointerClass0_DeviceObjectPointer(ULONG64 kernel_base, UNICODE_STRING MouClass, PVOID ObReferenceObjectByName, PVOID IoDriverObjectType) {
    PDEVICE_OBJECT deviceObject = NULL;
    NTSTATUS status = STATUS_NOT_FOUND;

    PDRIVER_OBJECT driverObject = NULL;
    status = ((NTSTATUS(*)(PUNICODE_STRING, ULONG, PACCESS_STATE, ACCESS_MASK, POBJECT_TYPE, KPROCESSOR_MODE, PVOID, PVOID*))ObReferenceObjectByName)
        (&MouClass, OBJ_CASE_INSENSITIVE, NULL, 0, *(POBJECT_TYPE*)IoDriverObjectType, KernelMode, NULL, (PVOID*)&driverObject);
    if (!NT_SUCCESS(status)) return status;

    deviceObject = driverObject->DeviceObject;
    while (deviceObject != NULL) {
        if (deviceObject->DeviceType == FILE_DEVICE_MOUSE && !deviceObject->NextDevice) {
            KLI_FN(kernel_base, ObfDereferenceObject)(driverObject);
            return (ULONG64)deviceObject;
        }
        deviceObject = deviceObject->NextDevice;
    }

    KLI_FN(kernel_base, ObfDereferenceObject)(driverObject);
    return 0;
}