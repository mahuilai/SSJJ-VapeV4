#define RVA(Instr, InstrSize) ((DWORD64)Instr + InstrSize + *(LONG*)((DWORD64)Instr + (InstrSize - sizeof(LONG))))
#if defined(__clang__)
#define __stosb __mstosb
__forceinline void __mstosb(PBYTE dest, BYTE value, size_t count) {
    asm volatile (
        "rep stosb"
        : "+D"(dest), "+c"(count)    // 输出操作数：更新指针和计数器
        : "a"(value)                 // 输入操作数：AL寄存器的值
        : "memory"                   // 告诉编译器内存被修改了
        );
}
#endif

__forceinline HLOCAL SecureAlloc(SIZE_T Bytes) {
    return LI_FN(LocalAlloc)(LPTR, Bytes);
}

__forceinline HLOCAL SecureFree(HLOCAL Ptr) {
    __stosb((PBYTE)Ptr, 0, LI_FN(LocalSize)(Ptr));
    return LI_FN(LocalFree)(Ptr);
}

__declspec(noinline) wchar_t* AnsiToWide(const char* ansiStr) {
    if (ansiStr == nullptr) return nullptr;

    int numChars = LI_FN(MultiByteToWideChar)(CP_ACP, 0, ansiStr, -1, nullptr, 0);
    if (!numChars) return nullptr;

    wchar_t* wideStr = (wchar_t*)SecureAlloc(numChars * sizeof(wchar_t));
    if (!wideStr) return nullptr;

    if (LI_FN(MultiByteToWideChar)(CP_ACP, 0, ansiStr, -1, wideStr, numChars) == 0) {
        SecureFree(wideStr);
        return nullptr;
    }

    return wideStr;
}

__declspec(noinline) bool IsRunningAsAdmin() {
    BOOL   fRet = FALSE;
    HANDLE hToken = NULL;
    if (LI_FN(OpenProcessToken).in(LI_MODULE("ADVAPI32.dll").get())(LI_FN(GetCurrentProcess)(), TOKEN_QUERY, &hToken)) {
        TOKEN_ELEVATION Elevation;
        DWORD           cbSize = sizeof(TOKEN_ELEVATION);

        if (LI_FN(GetTokenInformation)
            .in(LI_MODULE("ADVAPI32.dll").get())(hToken, TokenElevation, &Elevation, sizeof(Elevation), &cbSize)) {
            fRet = Elevation.TokenIsElevated;
        }
    }
    if (hToken) {
        LI_FN(CloseHandle)(hToken);
    }
    return fRet;
}

__declspec(noinline) bool ChangePrivilege(bool enable) {
    HANDLE TokenHandle;
    if (!LI_FN(OpenProcessToken)
        .in(LI_MODULE("ADVAPI32.dll").get())(LI_FN(GetCurrentProcess)(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
            &TokenHandle)) {
        return FALSE;
    }

    LUID luid;
    if (!LI_FN(LookupPrivilegeValueW)
        .in(LI_MODULE("ADVAPI32.dll").get())(NULL, xorstr_(L"SeLoadDriverPrivilege"), &luid)) {
        LI_FN(CloseHandle)(TokenHandle);
        return FALSE;
    }

    TOKEN_PRIVILEGES tp;
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Luid = luid;
    tp.Privileges[0].Attributes = (enable ? SE_PRIVILEGE_ENABLED : 0);

    BOOL result =
        LI_FN(AdjustTokenPrivileges)
        .in(LI_MODULE("ADVAPI32.dll").get())(TokenHandle, FALSE, &tp, sizeof(TOKEN_PRIVILEGES), NULL, NULL);
    LI_FN(CloseHandle)(TokenHandle);

    return result && LI_FN(GetLastError)() == ERROR_SUCCESS;
}

__declspec(noinline) bool verifyDataSource(const char* data, const char* verify_data) {
    if (LI_FN(lstrlenA)(data) < 8)
        return false;
    if (LI_FN(RtlCompareMemory)(data, verify_data, 8) == 8)
        return true;
    return false;
}

__declspec(noinline) int str_to_int(const char* str) {
    if (str == nullptr || *str == '\0')
        return 0;

    int result = 0;
    while (*str >= '0' && *str <= '9') {
        result = result * 10 + (*str - '0');
        str++;
    }

    return result;
}
__declspec(noinline) unsigned __int64 str_to_ull(const char* str) {
    if (str == nullptr || *str == '\0')
        return 0;

    unsigned __int64 result = 0;
    while (*str >= '0' && *str <= '9') {
        result = result * 10 + (*str - '0');
        str++;
    }

    return result;
}

__declspec(noinline) char* GetMachineCode(bool Mutante) {
    char* buffer = (char*)SecureAlloc(1024);
    int offset = 0;

    // CPUID
    int cpuInfo[4];
    __stosb((PBYTE)cpuInfo, 0, sizeof(cpuInfo));
    __cpuid(cpuInfo, 1);
    offset += LI_FN(wsprintfA)(buffer + offset, xorstr_("%08X%08X"), cpuInfo[3], cpuInfo[0]);

    // 硬盘序列号
    DWORD serialNumber;
    __stosb((PBYTE)&serialNumber, 0, sizeof(serialNumber));
    LI_FN(GetVolumeInformationA)(xorstr_("C:\\"), nullptr, 0, &serialNumber, nullptr, nullptr, nullptr, 0);
    offset += LI_FN(wsprintfA)(buffer + offset, xorstr_("%08X"), serialNumber);

    // BIOS 序列号
    char biosSerialNumber[256];
    __stosb((PBYTE)biosSerialNumber, 0, sizeof(biosSerialNumber));
    DWORD biosSerialNumberSize = sizeof(biosSerialNumber);
    LI_FN(GetSystemFirmwareTable)('RSMB', 0, biosSerialNumber, biosSerialNumberSize);
    offset += LI_FN(wsprintfA)(buffer + offset, xorstr_("%s"), biosSerialNumber);

    // 简单的数据摘要算法
    char* digest = (char*)SecureAlloc(18);
    int sum = 0;
    for (int i = 0; i < offset; i++) {
        sum += buffer[i];
        if (!Mutante) digest[i % 17] += buffer[i];
        else digest[i % 17] ^= buffer[i];
    }
    for (int i = 0; i < 17; i++) {
        digest[i] = (digest[i] + sum) % 36;
        if (digest[i] < 10)
            digest[i] += '0';
        else
            digest[i] += 'A' - 10;
    }
    digest[17] = 0;

    SecureFree(buffer);
    return digest;
}

__declspec(noinline) char* GetSystem32Path() {
    char* buffer = (char*)SecureAlloc(MAX_PATH);
    if (LI_FN(GetSystemDirectoryA)(buffer, MAX_PATH) != 0)
        return buffer;
    else {
        __movsb((PBYTE)buffer, (BYTE*)xorstr_("C:\\Windows\\system32"), 20);
        return buffer;
    };
}

__declspec(noinline) char* GetSystemTempPath() {
    char* buffer = (char*)SecureAlloc(MAX_PATH);
    if (LI_FN(GetTempPathA)(MAX_PATH, buffer) != 0)
        return buffer;
    else {
        __movsb((PBYTE)buffer, (BYTE*)xorstr_("C:\\Temp\\"), 9);
        return buffer;
    };
}

__declspec(noinline) char* GenerateRandomString(int length) {
    const char* CHARACTERS = xorstr_("0x89abcdfghjstuv17no25w6pqrelm34yz");
    const int CHARACTERS_LENGTH = 35;

    char* random_string = (char*)SecureAlloc(length + 1);
    for (int i = 0; i < length; ++i) {
        
        unsigned __int64 rdtsc = __rdtsc();
        int rdtsc_int = LOWORD(rdtsc);
        int random_index = rdtsc_int % CHARACTERS_LENGTH;
        random_string[i] = CHARACTERS[random_index];
    }

    return random_string;
}

typedef struct _TABLE {
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
}TABLE, * PTABLE;

__declspec(noinline) bool WriteVolatileBinaryToRegistry(const char* regPath, const char* valueName, unsigned char* buffer, int size) {
    HKEY hKey;
    bool bSuccess = false;

    LONG lResult = LI_FN(RegCreateKeyExA).in(LI_MODULE("ADVAPI32.dll").get())(HKEY_LOCAL_MACHINE, regPath, 0, NULL, REG_OPTION_VOLATILE, KEY_WRITE | KEY_WOW64_64KEY, NULL, &hKey, NULL);
    if (lResult == ERROR_SUCCESS) {
        lResult = LI_FN(RegSetValueExA).in(LI_MODULE("ADVAPI32.dll").get())(hKey, valueName, 0, REG_BINARY, (const BYTE*)buffer, size);
        if (lResult == ERROR_SUCCESS) bSuccess = true;
        LI_FN(RegCloseKey).in(LI_MODULE("ADVAPI32.dll").get())(hKey);
    }

    return bSuccess;
}
__declspec(noinline) bool WriteVolatileQWORDToRegistry(const char* regPath, const char* valueName, DWORD64 value) {
    HKEY hKey;
    bool bSuccess = false;

    LONG lResult = LI_FN(RegCreateKeyExA).in(LI_MODULE("ADVAPI32.dll").get())(HKEY_LOCAL_MACHINE, regPath, 0, NULL, REG_OPTION_VOLATILE, KEY_WRITE | KEY_WOW64_64KEY, NULL, &hKey, NULL);
    if (lResult == ERROR_SUCCESS) {
        lResult = LI_FN(RegSetValueExA).in(LI_MODULE("ADVAPI32.dll").get())(hKey, valueName, 0, REG_QWORD, (const BYTE*)&value, sizeof(DWORD64));
        if (lResult == ERROR_SUCCESS) bSuccess = true;
        LI_FN(RegCloseKey).in(LI_MODULE("ADVAPI32.dll").get())(hKey);
    }

    return bSuccess;
}

__declspec(noinline) bool WriteFileFromBuffer(const char* path, char* buffer, int size) {
    DWORD written;
    HANDLE hFile = LI_FN(CreateFileA).forwarded()(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) return false;
    BOOL writeResult = LI_FN(WriteFile).forwarded()(hFile, buffer, size, &written, NULL);
    LI_FN(CloseHandle)(hFile);
    return writeResult && (written == (DWORD)size);
}

__declspec(noinline) bool ReadFileToBuffer(const char* path, char** buffer, int* size) {
    HANDLE hFile = LI_FN(CreateFileA).forwarded()(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) return false;

    DWORD fileSize = LI_FN(GetFileSize).forwarded()(hFile, NULL);
    if (fileSize == INVALID_FILE_SIZE) {
        LI_FN(CloseHandle)(hFile);
        return false;
    }

    *buffer = (char*)SecureAlloc(fileSize);
    if (*buffer == NULL) {
        LI_FN(CloseHandle)(hFile);
        return false;
    }

    DWORD bytesRead;
    BOOL readResult = LI_FN(ReadFile).forwarded()(hFile, *buffer, fileSize, &bytesRead, NULL);
    *size = bytesRead; // 设置读取的大小
    LI_FN(CloseHandle)(hFile);

    return readResult && (bytesRead == fileSize);
}

__declspec(noinline) int FileAddressToRVA(PVOID hModule, int fileAddress) {
    if (fileAddress <= 0) return 0;
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    PIMAGE_NT_HEADERS64 pNtHeaders = (PIMAGE_NT_HEADERS64)((LPBYTE)hModule + pDosHeader->e_lfanew);

    PIMAGE_SECTION_HEADER pSectionHeaders = (PIMAGE_SECTION_HEADER)((LPBYTE)pNtHeaders + sizeof(IMAGE_NT_HEADERS64));
    for (int i = 0; i < pNtHeaders->FileHeader.NumberOfSections; i++) {
        int sectionStartFileAddress = pSectionHeaders[i].PointerToRawData;
        int sectionEndFileAddress = sectionStartFileAddress + pSectionHeaders[i].SizeOfRawData;

        if (fileAddress >= sectionStartFileAddress && fileAddress < sectionEndFileAddress) {
            int offsetInSection = fileAddress - sectionStartFileAddress;
            return pSectionHeaders[i].VirtualAddress + offsetInSection;
        }
    }

    return -1;
}

__declspec(noinline) DWORD RVAToFileAddress(PVOID hModule, DWORD rva) {
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    PIMAGE_NT_HEADERS64 pNtHeaders = (PIMAGE_NT_HEADERS64)((LPBYTE)hModule + pDosHeader->e_lfanew);

    PIMAGE_SECTION_HEADER pSectionHeaders = (PIMAGE_SECTION_HEADER)((LPBYTE)pNtHeaders + sizeof(IMAGE_NT_HEADERS64));
    for (int i = 0; i < pNtHeaders->FileHeader.NumberOfSections; i++) {
        DWORD sectionStartRVA = pSectionHeaders[i].VirtualAddress;
        DWORD sectionEndRVA = sectionStartRVA + pSectionHeaders[i].Misc.VirtualSize;

        if (rva >= sectionStartRVA && rva < sectionEndRVA) {
            DWORD offsetInSection = rva - sectionStartRVA;
            return pSectionHeaders[i].PointerToRawData + offsetInSection;
        }
    }

    return 0;
}

__forceinline ULONG64 Get64PEImageBase(PVOID hModule) {
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    PIMAGE_NT_HEADERS64 pNtHeaders = (PIMAGE_NT_HEADERS64)((LPBYTE)hModule + pDosHeader->e_lfanew);

    return pNtHeaders->OptionalHeader.ImageBase;
}

__declspec(noinline) DWORD GetProcAddressCustomFromFile(PVOID hModule, LPCSTR lpProcName) {
    if (!hModule) return NULL;
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    PIMAGE_NT_HEADERS64 pNtHeaders = (PIMAGE_NT_HEADERS64)((LPBYTE)hModule + pDosHeader->e_lfanew);
    if (!pNtHeaders->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT].VirtualAddress) return NULL;
    PIMAGE_EXPORT_DIRECTORY pExportDirectory = (PIMAGE_EXPORT_DIRECTORY)((LPBYTE)hModule + RVAToFileAddress(hModule, pNtHeaders->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT].VirtualAddress));
    PDWORD pNameRVAs = (PDWORD)((LPBYTE)hModule + RVAToFileAddress(hModule, pExportDirectory->AddressOfNames));
    for (DWORD i = 0; i < pExportDirectory->NumberOfNames; i++) {
        LPCSTR functionName = (LPCSTR)((LPBYTE)hModule + RVAToFileAddress(hModule, pNameRVAs[i]));
        if (!LI_FN(_stricmp)(functionName, lpProcName)) {
            WORD ordinal = ((PWORD)((LPBYTE)hModule + RVAToFileAddress(hModule, pExportDirectory->AddressOfNameOrdinals)))[i];
            DWORD functionRVA = ((PDWORD)((LPBYTE)hModule + RVAToFileAddress(hModule, pExportDirectory->AddressOfFunctions)))[ordinal];
            return functionRVA;
        }
    }
    return NULL;
}

__declspec(noinline) DWORD GetProcAddressCustomFromMemory(PVOID hModule, LPCSTR lpProcName) {
    if (!hModule) return NULL;
    PIMAGE_DOS_HEADER pDosHeader = (PIMAGE_DOS_HEADER)hModule;
    PIMAGE_NT_HEADERS64 pNtHeaders = (PIMAGE_NT_HEADERS64)((LPBYTE)hModule + pDosHeader->e_lfanew);
    if (!pNtHeaders->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT].VirtualAddress) return NULL;
    PIMAGE_EXPORT_DIRECTORY pExportDirectory = (PIMAGE_EXPORT_DIRECTORY)((LPBYTE)hModule + pNtHeaders->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT].VirtualAddress);
    PDWORD pNameRVAs = (PDWORD)((LPBYTE)hModule + pExportDirectory->AddressOfNames);
    for (DWORD i = 0; i < pExportDirectory->NumberOfNames; i++) {
        LPCSTR functionName = (LPCSTR)((LPBYTE)hModule + pNameRVAs[i]);
        if (!LI_FN(_stricmp)(functionName, lpProcName)) {
            WORD ordinal = ((PWORD)((LPBYTE)hModule + pExportDirectory->AddressOfNameOrdinals))[i];
            DWORD functionRVA = ((PDWORD)((LPBYTE)hModule + pExportDirectory->AddressOfFunctions))[ordinal];
            return functionRVA;
        }
    }
    return NULL;
}

__declspec(noinline) void ParsePattern(const char* pattern, unsigned char* bytes, bool* mask, int& patternSize) {
    const char* p = pattern;
    patternSize = 0;
    while (*p) {
        if (*p == ' ') {
            ++p;
            continue;
        }
        if (*p == '?') {
            bytes[patternSize] = 0;
            mask[patternSize] = false;
            if (*(p + 1) == '?') {
                ++p;
            }
        }
        else {
            bytes[patternSize] = static_cast<unsigned char>(LI_FN(strtoul)(p, nullptr, 16));
            mask[patternSize] = true;
        }
        patternSize++;
        p += 2;
    }
}

// 搜索函数：在数据中搜索特征码的第一次出现
__declspec(noinline) int FindPattern(const char* data, int size, const char* pattern) {
    unsigned char* bytes = (unsigned char*)SecureAlloc(256); // 假设特征码长度不会超过256字节
    bool* mask = (bool*)SecureAlloc(256);
    int patternSize = 0;

    ParsePattern(pattern, bytes, mask, patternSize);

    for (int i = 0; i <= size - patternSize; ++i) {
        bool found = true;
        for (int j = 0; j < patternSize; ++j) {
            if (mask[j] && static_cast<unsigned char>(data[i + j]) != bytes[j]) {
                found = false;
                break;
            }
        }
        if (found) {
            SecureFree(bytes);
            SecureFree(mask);
            return i;
        }
    }
    SecureFree(bytes);
    SecureFree(mask);
    return 0; // 没有找到特征码
}

__forceinline int FindULONG64(const char* data, int size, ULONG64 dst) {
    // 迭代数据，每次比较一个 ULONG64
    for (int i = 0; i <= size - sizeof(ULONG64); ++i) {
        if (*(ULONG64*)(data + i) == dst) return i;
    }

    return 0; // 未找到
}

__forceinline void LoadAndImportDlls() {
    LI_FN(LoadLibraryA)(xorstr_("kernel32.dll"));
    LI_FN(LoadLibraryA)(xorstr_("KernelBase.dll"));
    LI_FN(LoadLibraryA)(xorstr_("msvcrt.dll"));
    LI_FN(LoadLibraryA)(xorstr_("ADVAPI32.dll"));
    LI_FN(LoadLibraryA)(xorstr_("shlwapi.dll"));
    LI_FN(LoadLibraryA)(xorstr_("WINHTTP.dll"));
    LI_FN(LoadLibraryA)(xorstr_("wininet.dll"));
    LI_FN(LoadLibraryA)(xorstr_("user32.dll"));
    LI_FN(LoadLibraryA)(xorstr_("ntdll.dll"));
    LI_FN(LoadLibraryA)(xorstr_("Ws2_32.dll"));
    LI_FN(LoadLibraryA)(xorstr_("Shell32.dll"));
}

__forceinline char* m_strchr(const char* str, int ch) {
    while (*str) {
        if (*str == (char)ch) return (char*)str;
        str++;
    }
    if (ch == '\0') return (char*)str;
    return NULL;
}

template<typename T>
class LocalPtr {
public:
    __forceinline LocalPtr(int size = 0) noexcept {
        size_ = size;
        if (size_) {
            size_ = size;
            ptr_ = (T*)SecureAlloc(size_);
        }
    };
    __forceinline LocalPtr(const LocalPtr& other) noexcept {
        size_ = other.size_;
        ptr_ = (T*)SecureAlloc(size_);
        __movsb((PBYTE)ptr_, (const PBYTE)other.ptr_, size_);
    }
    __forceinline LocalPtr& operator=(const LocalPtr& other) noexcept {
        size_ = other.size_;
        ptr_ = (T*)SecureAlloc(size_);
        __movsb((PBYTE)ptr_, (const PBYTE)other.ptr_, size_);
        return *this;
    }
    __forceinline T* operator->() const noexcept {
        return ptr_;
    }
    __forceinline T operator&() const noexcept {
        return (T)ptr_;
    }
    __forceinline ~LocalPtr() noexcept {
        if (ptr_ && size_) {
            __stosb((PBYTE)ptr_, 0, size_);
            SecureFree(ptr_);
        }
    }
private:
    T* ptr_;
    SIZE_T size_;
};

__forceinline bool IsPeb64(__int64 Peb) {
    if (Peb <= 0xFFFFFFFF) return false;
    return true;
}

__declspec(noinline) __int64 GetModuleAddress(_Globals* Globals, const char* ModuleName, __int64 pPeb, __int64 OperateMemFunction, bool Is64Bit) {
    if (!pPeb) return 0;
    wchar_t* wModuleName = AnsiToWide(ModuleName);

    if (Is64Bit) {
        LocalPtr<PPEB64> Peb(sizeof(PEB64));
        LocalPtr<PPEB_LDR_DATA64> Ldr(sizeof(PEB_LDR_DATA64));

        PLDR_DATA_TABLE_ENTRY64 LdrDataEntry = NULL;

        ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)(Globals, pPeb, &Peb, sizeof(PEB64), MemOperateType::Read);
        ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)(Globals, (&Peb)->Ldr, &Ldr, sizeof(PEB_LDR_DATA64), MemOperateType::Read);

        for (__int64 pCur = (&Ldr)->InMemoryOrderModuleList.Flink; pCur != (&Peb)->Ldr + offsetof(PEB_LDR_DATA64, InMemoryOrderModuleList);) {
            LocalPtr<PLDR_DATA_TABLE_ENTRY64> curData(sizeof(LDR_DATA_TABLE_ENTRY64));
            ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)(Globals, pCur, &curData, sizeof(LDR_DATA_TABLE_ENTRY64), MemOperateType::Read);
            LdrDataEntry = CONTAINING_RECORD(&curData, LDR_DATA_TABLE_ENTRY64, InMemoryOrderLinks);
            pCur = LdrDataEntry->InMemoryOrderLinks.Flink;
            if (LdrDataEntry->BaseDllName.Buffer) {
                LocalPtr<wchar_t*> wszBuff(520);
                ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)
                    (Globals, LdrDataEntry->BaseDllName.Buffer, &wszBuff, LdrDataEntry->BaseDllName.Length + 2, MemOperateType::Read);

                int result = LI_FN(_wcsicmp)(wModuleName, &wszBuff);
                if (!result) {
                    SecureFree(wModuleName);
                    return LdrDataEntry->DllBase;
                }
            }
        }
    }
    else {
        LocalPtr<PPEB32> Peb(sizeof(PEB32));
        LocalPtr<PPEB_LDR_DATA32> Ldr(sizeof(PEB_LDR_DATA32));

        PLDR_DATA_TABLE_ENTRY32 LdrDataEntry = NULL;

        ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)(Globals, pPeb, &Peb, sizeof(PEB32), MemOperateType::Read);
        ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)(Globals, (&Peb)->Ldr, &Ldr, sizeof(PEB_LDR_DATA32), MemOperateType::Read);

        for (int pCur = (&Ldr)->InMemoryOrderModuleList.Flink; pCur != (&Peb)->Ldr + offsetof(PEB_LDR_DATA32, InMemoryOrderModuleList);) {
            LocalPtr<PLDR_DATA_TABLE_ENTRY32> curData(sizeof(LDR_DATA_TABLE_ENTRY32));
            ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)(Globals, pCur, &curData, sizeof(LDR_DATA_TABLE_ENTRY32), MemOperateType::Read);
            LdrDataEntry = CONTAINING_RECORD(&curData, LDR_DATA_TABLE_ENTRY32, InMemoryOrderLinks);
            pCur = LdrDataEntry->InMemoryOrderLinks.Flink;
            if (LdrDataEntry->BaseDllName.Buffer) {
                LocalPtr<wchar_t*> wszBuff(520);
                ((bool(*)(_Globals*, __int64, void*, long, int))OperateMemFunction)
                    (Globals, LdrDataEntry->BaseDllName.Buffer, &wszBuff, LdrDataEntry->BaseDllName.Length + 2, MemOperateType::Read);

                int result = LI_FN(_wcsicmp)(wModuleName, &wszBuff);
                if (!result) {
                    SecureFree(wModuleName);
                    return LdrDataEntry->DllBase;
                }
            }
        }
    }

    SecureFree(wModuleName);
    return 0;
}