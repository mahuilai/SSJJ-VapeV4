__declspec(noinline) int Cracker_Install(ULONG_PTR Key, int SignId) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    if (Globals->source != 0x91A) return StatusCode::AppKeyInvalid;
    if (Globals->VM.active == true) return StatusCode::Success;

    int PreVerifyStatus = Cracker_PreVerifySecretKeyForProduct(xorstr_("Cracker"), Globals);
    if (PreVerifyStatus != StatusCode::Success) return PreVerifyStatus;

    Globals->VM.RegSetValueExA = (__int64)LI_FN(GetProcAddress)(LI_FN(GetModuleHandleA)(xorstr_("ADVAPI32.dll")), xorstr_("RegSetValueExA"));

    unsigned __int64 VerifyCode = VMShell::GetVerifyCode(Globals);
#ifndef _DEBUG
    if (VerifyCode) {
#else
    if (false) {
#endif
        int HeartBeatStatus = VMShell::HeartBeat(Globals);
        if (HeartBeatStatus == StatusCode::Success) {
            VMShell::SetProcess(Globals, 0, false);
            return StatusCode::Success;
        }
    }

    PVOID FsRedirection = 0;
    LI_FN(Wow64DisableWow64FsRedirection)(&FsRedirection);

    char* System32Path = GetSystem32Path();

    LocalPtr<char*> filepath_buffer(MAX_PATH);
    LI_FN(sprintf)(&filepath_buffer, xorstr_("%s\\ntoskrnl.exe"), System32Path);

    char* ntoskrnl_file_buffer = 0; int ntoskrnl_file_size = 0;
    if (!ReadFileToBuffer(&filepath_buffer, &ntoskrnl_file_buffer, &ntoskrnl_file_size)) {
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::Read_Ntoskrnl_ToMem_Error;
    }

    __stosb((PBYTE)&filepath_buffer, 0, MAX_PATH);
    LI_FN(sprintf)(&filepath_buffer, xorstr_("%s\\win32kbase.sys"), System32Path);

    char* win32kbase_file_buffer = 0; int win32kbase_file_size = 0;
    if (!ReadFileToBuffer(&filepath_buffer, &win32kbase_file_buffer, &win32kbase_file_size)) {
        SecureFree(ntoskrnl_file_buffer);
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::Read_Win32kBase_ToMem_Error;
    }

    __stosb((PBYTE)&filepath_buffer, 0, MAX_PATH);
    LI_FN(sprintf)(&filepath_buffer, xorstr_("%s\\drivers\\mouclass.sys"), System32Path);

    char* mouclass_file_buffer = 0; int mouclass_file_size = 0;
    if (!ReadFileToBuffer(&filepath_buffer, &mouclass_file_buffer, &mouclass_file_size)) {
        SecureFree(ntoskrnl_file_buffer);
        SecureFree(win32kbase_file_buffer);
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::Read_MouClass_ToMem_Error;
    }

    __int64 ntoskrnl_imagebase = Get64PEImageBase(ntoskrnl_file_buffer);
  
    int MiGetPageTablePfnBuddyRaw = FileAddressToRVA(ntoskrnl_file_buffer, FindPattern(ntoskrnl_file_buffer, ntoskrnl_file_size,
        xorstr_("\x00\x00\xFF\x03\x48\xD1\xEA")));

    if (MiGetPageTablePfnBuddyRaw != 0) {
        char* MiGetBuffer = (char*)SecureAlloc(100);
        if (MiGetBuffer) {
            size_t offset = MiGetPageTablePfnBuddyRaw - 100;
            if (offset < 0) {
                offset = 0;
            }
            memcpy(MiGetBuffer, ntoskrnl_file_buffer + offset, 100);

            bool find = false;
            for (int i = 99; i >= 0; i--) {
                if (static_cast<unsigned char>(MiGetBuffer[i]) == 0x48 &&
                    static_cast<unsigned char>(MiGetBuffer[i + 1]) == 0x8B) {
                    MiGetPageTablePfnBuddyRaw = offset + i;
                    find = true;
                    break;
                }
            }

            SecureFree(MiGetBuffer);

            if (!find) {
                MiGetPageTablePfnBuddyRaw = 0;
            }
        }
        else {
            MiGetPageTablePfnBuddyRaw = 0;
        }
    }

    int aRtlhashunicodeFA = FindPattern(ntoskrnl_file_buffer, ntoskrnl_file_size,
        xorstr_("00 00 52 74 6C 48 61 73 68 55 6E 69 63 6F 64 65 53 74 72 69 6E 67 00 00")) + 2;
    if (!aRtlhashunicodeFA) {
        SecureFree(ntoskrnl_file_buffer);
        SecureFree(win32kbase_file_buffer);
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::aRtlhashunicodeInvalid;
    }
    int aRtlhashunicodeRVA = FileAddressToRVA(ntoskrnl_file_buffer, aRtlhashunicodeFA);
    int aRtlhashunicodeRVArefRVA = FindULONG64(ntoskrnl_file_buffer, ntoskrnl_file_size, ntoskrnl_imagebase + aRtlhashunicodeRVA);
    if (!aRtlhashunicodeFA) {
        SecureFree(ntoskrnl_file_buffer);
        SecureFree(win32kbase_file_buffer);
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::aRtlhashunicodeRVArefRVAInvalid;
    }
    int VerifierRtlHashUnicodeString = *(ULONG64*)(ntoskrnl_file_buffer + aRtlhashunicodeRVArefRVA + 8) - ntoskrnl_imagebase;

    char* MouseClassServiceCallbackHeaderContent = 0;
    int MouseClassServiceCallbackHeaderContentLength = 0;
    int SubRsp70RVA = 0;
    do {
        int MouseClassServiceCallbackIoCode = FindPattern(mouclass_file_buffer, mouclass_file_size, xorstr_("B9 03 02 0F 00"));
        if (!MouseClassServiceCallbackIoCode) break;

        int leaToMouseClassServiceCallbackFA = MouseClassServiceCallbackIoCode + 0x5;
        int MouseClassServiceCallbackDataptr = *(int*)(mouclass_file_buffer + leaToMouseClassServiceCallbackFA + 3);
        if (!MouseClassServiceCallbackDataptr) break;

        int leaToMouseClassServiceCallbackRVA = FileAddressToRVA(mouclass_file_buffer, leaToMouseClassServiceCallbackFA);
        if (!leaToMouseClassServiceCallbackRVA) break;

        int MouseClassServiceCallbackRVA = leaToMouseClassServiceCallbackRVA + 7 + MouseClassServiceCallbackDataptr;
        if (!MouseClassServiceCallbackRVA) break;

        int MouseClassServiceCallbackFA = RVAToFileAddress(mouclass_file_buffer, MouseClassServiceCallbackRVA);
        if (!MouseClassServiceCallbackFA) break;

        int SubRsp70RA = FindPattern(mouclass_file_buffer + MouseClassServiceCallbackFA, 100, xorstr_("48 83 EC 70"));
        if (!SubRsp70RA) break;

        int SubRsp70FA = MouseClassServiceCallbackFA + SubRsp70RA;
        SubRsp70RVA = FileAddressToRVA(mouclass_file_buffer, SubRsp70FA);
        if (!SubRsp70RVA) break;

        int MouseClassServiceCallbackHeaderLength = SubRsp70FA - MouseClassServiceCallbackFA;
        MouseClassServiceCallbackHeaderContentLength = MouseClassServiceCallbackHeaderLength + 14;
        MouseClassServiceCallbackHeaderContent = (char*)SecureAlloc(MouseClassServiceCallbackHeaderContentLength);

        __movsb((PBYTE)MouseClassServiceCallbackHeaderContent, (PBYTE)(mouclass_file_buffer + MouseClassServiceCallbackFA), MouseClassServiceCallbackHeaderLength);
        __movsb(
            (PBYTE)(MouseClassServiceCallbackHeaderContent + MouseClassServiceCallbackHeaderLength),
            (PBYTE)xorstr_("\xFF\x25\x00\x00\x00\x00\xCC\xCC\xCC\xCC\xCC\xCC\xCC\xCC"), 14);
    } while (false);
    SecureFree(mouclass_file_buffer);

    PTABLE shellcode_table = (PTABLE)SecureAlloc(sizeof(TABLE));

    shellcode_table->MmIsAddressValid = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmIsAddressValid"));
    shellcode_table->PsGetProcessWow64Process = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("PsGetProcessWow64Process"));
    shellcode_table->PsGetProcessPeb = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("PsGetProcessPeb"));
    shellcode_table->MmGetPhysicalMemoryRangesEx = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmGetPhysicalMemoryRangesEx"));
    shellcode_table->ExAllocatePoolWithTag = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ExAllocatePoolWithTag"));
    shellcode_table->ExFreePoolWithTag = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ExFreePoolWithTag"));

    shellcode_table->IoGetCurrentProcess = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("IoGetCurrentProcess"));
    shellcode_table->MmCopyMemory = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmCopyMemory"));
    shellcode_table->MmMapIoSpaceEx = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmMapIoSpaceEx"));
    shellcode_table->MmUnmapIoSpace = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmUnmapIoSpace"));
    shellcode_table->MmCopyVirtualMemory = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmCopyVirtualMemory"));
    shellcode_table->PsGetProcessSectionBaseAddress = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("PsGetProcessSectionBaseAddress"));
    shellcode_table->PsGetProcessId = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("PsGetProcessId"));

    shellcode_table->RtlHashUnicodeString = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("RtlHashUnicodeString"));
    shellcode_table->MiGetPageTablePfnBuddyRaw = MiGetPageTablePfnBuddyRaw;

    shellcode_table->KeAcquireSpinLockAtDpcLevel = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("KeAcquireSpinLockAtDpcLevel"));
    shellcode_table->KeReleaseSpinLockFromDpcLevel = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("KeReleaseSpinLockFromDpcLevel"));
    shellcode_table->IofCompleteRequest = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("IofCompleteRequest"));
    shellcode_table->IoReleaseRemoveLockEx = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("IoReleaseRemoveLockEx"));

    shellcode_table->ValidateHwnd = GetProcAddressCustomFromFile(win32kbase_file_buffer, xorstr_("ValidateHwnd"));
    shellcode_table->KeInvalidateRangeAllCaches = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("KeInvalidateRangeAllCaches"));
    shellcode_table->memmove = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("memmove"));

    shellcode_table->IoCreateFileEx = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("IoCreateFileEx"));
    shellcode_table->ObReferenceObjectByHandleWithTag = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ObReferenceObjectByHandleWithTag"));
    shellcode_table->ObfDereferenceObject = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ObfDereferenceObject"));
    shellcode_table->ObCloseHandle = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ObCloseHandle"));
    shellcode_table->ZwDeleteFile = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ZwDeleteFile"));
    shellcode_table->MmFlushImageSection = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("MmFlushImageSection"));
    shellcode_table->IoFileObjectType = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("IoFileObjectType"));
    shellcode_table->KeGetCurrentProcessorNumberEx = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("KeGetCurrentProcessorNumberEx"));
    shellcode_table->PsLookupProcessByProcessId = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("PsLookupProcessByProcessId"));
    shellcode_table->RtlGetVersion = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("RtlGetVersion"));

    shellcode_table->ObOpenObjectByPointer = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ObOpenObjectByPointer"));
    shellcode_table->ZwAllocateVirtualMemory = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ZwAllocateVirtualMemory"));
    shellcode_table->ZwProtectVirtualMemory = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ZwProtectVirtualMemory"));
    shellcode_table->ZwFreeVirtualMemory = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ZwFreeVirtualMemory"));
    shellcode_table->ZwClose = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ZwClose"));
    shellcode_table->PsProcessType = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("PsProcessType"));
    shellcode_table->RtlCreateUserThread = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("RtlCreateUserThread"));
    shellcode_table->ZwWaitForSingleObject = GetProcAddressCustomFromFile(ntoskrnl_file_buffer, xorstr_("ZwWaitForSingleObject"));

    SecureFree(ntoskrnl_file_buffer);
    SecureFree(win32kbase_file_buffer);

    LocalPtr<char*> request_buffer(MAX_PATH);
    LI_FN(sprintf)(&request_buffer, xorstr_("p1v3*%s*Cracker*%s*%s\n"), Globals->SecretKey, Globals->hwid, Globals->ip);

    char* get_shellcode_response = 0;
    int get_shellcode_response_size = 0;

    if (!tcpRequest(Globals->console_server, Globals->console_port, &request_buffer, &get_shellcode_response, &get_shellcode_response_size)) {
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::QuestKernelShellcodeFromVMDriverServerTCPError;
    }

    if (!verifyDataSource(get_shellcode_response, xorstr_("VMXFILEX"))) {
        SecureFree(System32Path);
        SecureFree(get_shellcode_response);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::QuestKernelShellcodeFromVMDriverServerTCPError;
    }

    WriteVolatileBinaryToRegistry(xorstr_("SOFTWARE\\vmm_"), xorstr_("ImportTable"), (unsigned char*)shellcode_table, sizeof(TABLE));
    WriteVolatileBinaryToRegistry(xorstr_("SOFTWARE\\vmm_"), xorstr_("ShellcodeBinary"), (unsigned char*)(get_shellcode_response + 8), get_shellcode_response_size - 8);
    WriteVolatileQWORDToRegistry(xorstr_("SOFTWARE\\vmm_"), xorstr_("MousePtr"), SubRsp70RVA);
    WriteVolatileBinaryToRegistry(xorstr_("SOFTWARE\\vmm_"), xorstr_("MouseBinary"), (unsigned char*)MouseClassServiceCallbackHeaderContent, MouseClassServiceCallbackHeaderContentLength);
    WriteVolatileQWORDToRegistry(xorstr_("SOFTWARE\\vmm_"), xorstr_("FunctionPtr"), VerifierRtlHashUnicodeString);
    SecureFree(MouseClassServiceCallbackHeaderContent);
    SecureFree(get_shellcode_response);

    __stosb((PBYTE)&request_buffer, 0, MAX_PATH);
    if (SignId == 0) {
        LI_FN(sprintf)(&request_buffer, xorstr_("p1v4*%s*Cracker*%s*%s\n"), Globals->SecretKey, Globals->hwid, Globals->ip);
    }
    else if (SignId == 1) {
        LI_FN(sprintf)(&request_buffer, xorstr_("p1v6*%s*Cracker*%s*%s\n"), Globals->SecretKey, Globals->hwid, Globals->ip);
    }
    else if (SignId == 2) {
        LI_FN(sprintf)(&request_buffer, xorstr_("p1v7*%s*Cracker*%s*%s\n"), Globals->SecretKey, Globals->hwid, Globals->ip);
    }

    char* get_mapper_response = 0;
    int get_mapper_response_size = 0;

    if (!tcpRequest(Globals->console_server, Globals->console_port, &request_buffer, &get_mapper_response, &get_mapper_response_size)) {
        SecureFree(System32Path);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::QuestKernelMapperFromVMDriverServerTCPError;
    }

    if (!verifyDataSource(get_mapper_response, xorstr_("VMXFILEX"))) {
        SecureFree(System32Path);
        SecureFree(get_mapper_response);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::QuestKernelMapperFromVMDriverServerTCPError;
    }

    char* AgentDriverName = (char*)SecureAlloc(MAX_PATH);
    char* RandomString = GenerateRandomString(8);
    LI_FN(lstrcatA)(AgentDriverName, RandomString);
    SecureFree(RandomString);
    
    char* AgentDriverPath = (char*)SecureAlloc(MAX_PATH);
    LI_FN(sprintf)(AgentDriverPath, xorstr_("%s\\drivers\\%s.sys"), System32Path, AgentDriverName);

    SecureFree(System32Path);

    WriteFileFromBuffer(AgentDriverPath, get_mapper_response + 8, get_mapper_response_size - 8);

    SecureFree(get_mapper_response);

    if (!CreateServiceEx(AgentDriverPath, AgentDriverName)) {
        SecureFree(AgentDriverName);
        LI_FN(DeleteFileA)(AgentDriverPath);
        SecureFree(AgentDriverPath);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::CreateServiceExError;
    }

    NTSTATUS AgentDriverStatus = StartServiceEx(AgentDriverName);
    if (AgentDriverStatus == 0xC00000A3) {
        DeleteServiceEx(AgentDriverName);
        SecureFree(AgentDriverName);
        LI_FN(DeleteFileA)(AgentDriverPath);
        SecureFree(AgentDriverPath);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::DriverServiceLaunchStatusError;
    }
    if (AgentDriverStatus != 0xC0000001) {
        DeleteServiceEx(AgentDriverName);
        SecureFree(AgentDriverName);
        LI_FN(DeleteFileA)(AgentDriverPath);
        SecureFree(AgentDriverPath);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
        return StatusCode::StartServiceExError;
    }

    DeleteServiceEx(AgentDriverName);
    SecureFree(AgentDriverName);
    SecureFree(AgentDriverPath);
    LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);

    bool ShellStatus = true;
    do {
        unsigned __int64 VerifyCode = VMShell::GetVerifyCode(Globals);
        ShellStatus = ShellStatus && VerifyCode;
        int HeartBeatStatus = VMShell::HeartBeat(Globals);
        ShellStatus = ShellStatus && HeartBeatStatus == StatusCode::Success;
        if (!ShellStatus) break;
    } while (false);

    if (!ShellStatus) return StatusCode::VMDriverShellStatusError;

    VMShell::SetProcess(Globals, 0, false);

    return StatusCode::Success;
    }

__declspec(noinline) bool Cracker_SetProcess(ULONG_PTR Key, unsigned long ProcessId, bool DecryptCr3) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::SetProcess(Globals, ProcessId, DecryptCr3);
}
__declspec(noinline) __int64 Cracker_GetProcessEnvironmentBlock(ULONG_PTR Key) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::GetProcessEnvironmentBlock(Globals);
}
__declspec(noinline) __int64 Cracker_GetModuleAddress(ULONG_PTR Key, const char* ModuleName, int Method, bool Is64Bit) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);

    switch (Method) {
    case 0:
        return GetModuleAddress(Globals, ModuleName, VMShell::GetProcessEnvironmentBlock(Globals), (__int64)VMShell::OperatePagefulMemory, Is64Bit);
    case 1:
        return GetModuleAddress(Globals, ModuleName, VMShell::GetProcessEnvironmentBlock(Globals), (__int64)VMShell::OperateVirtualMemory, Is64Bit);
    case 2:
        return GetModuleAddress(Globals, ModuleName, VMShell::GetProcessEnvironmentBlock(Globals), (__int64)VMShell::OperatePhysicalMemory, Is64Bit);
    }
}
__declspec(noinline) bool Cracker_OperatePhysicalMemory(ULONG_PTR Key, __int64 address, void* buffer, long size, int OperateType) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);

    return VMShell::OperatePhysicalMemory(Globals, address, buffer, size, OperateType);
}
__declspec(noinline) bool Cracker_OperateKernelVirtualMemory(ULONG_PTR Key, ULONG64 address, void* buffer, long size, int OperateType) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::OperateKernelVirtualMemory(Globals, address, buffer, size, OperateType);
}
__declspec(noinline) bool Cracker_OperateVirtualMemory(ULONG_PTR Key, __int64 address, void* buffer, long size, int OperateType) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::OperateVirtualMemory(Globals, address, buffer, size, OperateType);
}
__declspec(noinline) bool Cracker_OperatePagefulMemory(ULONG_PTR Key, __int64 address, void* buffer, long size, int OperateType) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::OperatePagefulMemory(Globals, address, buffer, size, OperateType);
}
__declspec(noinline) ULONG64 Cracker_GetKernelModuleAddress(ULONG_PTR Key, const char* ModuleName) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::GetKernelModuleAddress(Globals, ModuleName);
}
__declspec(noinline) bool Cracker_KernelDeleteFile(ULONG_PTR Key, const char* FilePath) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::KernelDeleteFile(Globals, FilePath);
}
__declspec(noinline) __int64 Cracker_GetMainModuleBase(ULONG_PTR Key) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::GetMainModuleBase(Globals);
}
__declspec(noinline) __int64 Cracker_AllocateMemory(ULONG_PTR Key, ULONG64* BaseAddress, ULONG64* RegionSize, ULONG AllocationType, ULONG Protect) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::AllocateMemory(Globals, BaseAddress, RegionSize, AllocationType, Protect);
}
__declspec(noinline) __int64 Cracker_ProtectMemory(ULONG_PTR Key, ULONG64* BaseAddress, ULONG64* RegionSize, ULONG NewProtect, ULONG* OldProtect) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::ProtectMemory(Globals, BaseAddress, RegionSize, NewProtect, OldProtect);
}
__declspec(noinline) __int64 Cracker_FreeMemory(ULONG_PTR Key, ULONG64* BaseAddress, ULONG64* RegionSize, ULONG FreeType) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::FreeMemory(Globals, BaseAddress, RegionSize, FreeType);
}
__declspec(noinline) bool Cracker_MouseEvent(ULONG_PTR Key, void* InputData) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::MouseEvent(Globals, InputData);
}
__declspec(noinline) bool Cracker_CreateRemoteThread(ULONG_PTR Key, ULONG64 StartAddress, ULONG64 StartParameter) {
    _Globals* Globals = (_Globals*)(Key ^ (ULONG_PTR)0xFE4A5956A9F18671);
    return VMShell::CreateRemoteThread(Globals, StartAddress, StartParameter);
}

