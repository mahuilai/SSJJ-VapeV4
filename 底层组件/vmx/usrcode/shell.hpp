#define Cracker_TRUE 0x1FBD1DF5
#define Cracker_FALSE 0x5F3759DF
#define Cracker_MAGICNUMBER 0x2A517D3C

namespace VMShell {
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

    typedef struct _DriverParameter {
        ULONG64 VMKey;
        ULONG64 Control;
        ULONG64 OutputBuffer;
    }DriverParameter, * PDriverParameter;

    __forceinline unsigned __int64 GetVerifyCode(_Globals* Globals) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.Flag = 'ACE';
        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer;
    }

    __declspec(noinline) DWORD WINAPI HeartBeat(LPVOID lpParam) {
#ifdef _AMD64_
        PVOID FunctionAddress = HeartBeat;
#else
        PVOID FunctionAddress = (PVOID)((int)_ReturnAddress() + *(int*)((int)_ReturnAddress() - 4));
#endif
        _Globals* Globals = (_Globals*)lpParam;
        if (!Globals->VM.thread) {
            ULONG64 verifycode = GetVerifyCode(Globals);
            if (!verifycode) return 0;

            char* get_verify_response = 0;
            int get_verify_response_size = 0;

            char* request_buffer = (char*)SecureAlloc(MAX_PATH);
            LI_FN(sprintf)(request_buffer, xorstr_("p1v5*%s*Cracker*%s*%s*%llu\n"), Globals->SecretKey, Globals->hwid, Globals->ip, verifycode);

            if (!tcpRequest(Globals->console_server, Globals->console_port, request_buffer, &get_verify_response, &get_verify_response_size)) {
                SecureFree(request_buffer);
                return StatusCode::QuestVerifyForVMDriverTCPError;
            }

            SecureFree(request_buffer);
            Globals->VM.checkcode = str_to_ull(get_verify_response);
            Globals->VM.thread = true;
            LI_FN(CreateThread).forwarded()(NULL, 0, (LPTHREAD_START_ROUTINE)FunctionAddress, Globals, 0, NULL);
            return StatusCode::Success;
        }

        while (true) {
            LI_FN(Sleep)(1800000);

            ULONG64 verifycode = GetVerifyCode(Globals);
            if (!verifycode) continue;

            char* get_verify_response = 0;
            int get_verify_response_size = 0;

            char* request_buffer = (char*)SecureAlloc(MAX_PATH);
            LI_FN(sprintf)(request_buffer, xorstr_("p1v5*%s*Cracker*%s*%s*%llu\n"), Globals->SecretKey, Globals->hwid, Globals->ip, verifycode);

            if (!tcpRequest(Globals->console_server, Globals->console_port, request_buffer, &get_verify_response, &get_verify_response_size)) {
                SecureFree(request_buffer);
                continue;
            }

            SecureFree(request_buffer);
            Globals->VM.checkcode = str_to_ull(get_verify_response);
            SecureFree(get_verify_response);
        }
    }

    __forceinline bool SetProcess(_Globals* Globals, unsigned long ProcessId, bool DecryptCr3) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1167';
        control.ProcessId = ProcessId;
        control.Write = DecryptCr3;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
    __declspec(noinline) bool OperatePhysicalMemory(_Globals* Globals, __int64 address, void* buffer, long size, int OperateType) {
        if (address <= 0 || address > 0x7FFFFFFFFFFF || !buffer || size <= 0) return FALSE;

        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1169';
        control.Address = address;
        control.Buffer = (ULONG64)buffer;
        control.Size = size;
        control.Write = OperateType; // Read

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
    __declspec(noinline) bool OperateVirtualMemory(_Globals* Globals, __int64 address, void* buffer, long size, int OperateType) {
        if (address <= 0 || address > 0x7FFFFFFFFFFF || !buffer || size <= 0) return FALSE;

        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1175';
        control.Address = address;
        control.Buffer = (ULONG64)buffer;
        control.Size = size;
        control.Write = OperateType; // Read

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
    __declspec(noinline) bool OperatePagefulMemory(_Globals* Globals, __int64 address, void* buffer, long size, int OperateType) {
        if (address <= 0 || address > 0x7FFFFFFFFFFF || !buffer || size <= 0) return FALSE;

        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1173';
        control.Address = address;
        control.Buffer = (ULONG64)buffer;
        control.Size = size;
        control.Write = OperateType; // Read

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline bool OperateKernelVirtualMemory(_Globals* Globals, ULONG64 address, void* buffer, long size, int OperateType) {
        if (address <= 0 || address > 0xFFFFFFFFFFFFFFFF || !buffer || size <= 0) return FALSE;

        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1170';
        control.Address = address;
        control.Buffer = (ULONG64)buffer;
        control.Size = size;
        control.Write = OperateType; // Read

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline ULONG64 GetKernelModuleAddress(_Globals* Globals, const char* ModuleName) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1171';
        control.Buffer = (ULONG64)AnsiToWide(ModuleName);

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        SecureFree((HLOCAL)control.Buffer);

        return outputbuffer;
    }
    typedef struct _UNICODE_STRING64 {
        USHORT Length;
        USHORT MaximumLength;
        DWORD64 Buffer;
    } UNICODE_STRING64, * PUNICODE_STRING64;
    __forceinline bool KernelDeleteFile(_Globals* Globals, const char* FilePath) {
        char* str = (char*)SecureAlloc(MAX_PATH);
        LI_FN(sprintf)(str, xorstr_("\\??\\%s"), FilePath);
        int len = LI_FN(strlen)(str);

        wchar_t* wstr = AnsiToWide(str);

        UNICODE_STRING64 ustr;
        ustr.Length = len * sizeof(wchar_t);
        ustr.MaximumLength = (len + 1) * sizeof(wchar_t);
        ustr.Buffer = (DWORD64)wstr;

        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1172';
        control.Buffer = (ULONG64)&ustr;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        SecureFree((HLOCAL)str);
        SecureFree((HLOCAL)wstr);

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline __int64 GetMainModuleBase(_Globals* Globals) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1176';

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer;
    }
    __forceinline bool AllocateMemory(_Globals* Globals, ULONG64* BaseAddress, ULONG64* RegionSize, ULONG AllocationType, ULONG Protect) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1177';
        control.Address = *BaseAddress;
        control.Size = *RegionSize;
        control.ProcessId = AllocationType;
        control.Write = Protect;
        control.Buffer = 0;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        *BaseAddress = control.Address;
        *RegionSize = control.Size;

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline bool ProtectMemory(_Globals* Globals, ULONG64* BaseAddress, ULONG64* RegionSize, ULONG NewProtect, ULONG* OldProtect) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1177';
        control.Address = *BaseAddress;
        control.Size = *RegionSize;
        control.Write = NewProtect;
        control.Buffer = 1;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        *BaseAddress = control.Address;
        *RegionSize = control.Size;
        *OldProtect = control.ProcessId;

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline bool FreeMemory(_Globals* Globals, ULONG64* BaseAddress, ULONG64* RegionSize, ULONG FreeType) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1177';
        control.Address = *BaseAddress;
        control.Size = *RegionSize;
        control.Write = FreeType;
        control.Buffer = 2;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        *BaseAddress = control.Address;
        *RegionSize = control.Size;

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline bool CreateRemoteThread(_Globals* Globals, ULONG64 StartAddress, ULONG64 StartParameter) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1177';
        control.Address = StartAddress;
        control.Write = StartParameter;
        control.Buffer = 3;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
    __forceinline __int64 GetProcessEnvironmentBlock(_Globals* Globals) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1168';

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer;
    }
    __forceinline bool MouseEvent(_Globals* Globals, void* InputData) {
        DriverParameter parameter;
        DriverControl control;
        ULONG64 outputbuffer = 0;

        control.CheckCode = Globals->VM.checkcode;
        control.Flag = '1174';
        control.InputData = *(MOUSE_INPUT_DATA*)InputData;

        parameter.VMKey = Cracker_MAGICNUMBER;
        parameter.Control = (ULONG64)&control;
        parameter.OutputBuffer = (ULONG64)&outputbuffer;

        ((LSTATUS(__stdcall*)(HKEY, LPCSTR, DWORD, DWORD, BYTE*, DWORD))Globals->VM.RegSetValueExA)
            (HKEY_CURRENT_USER, NULL, NULL, REG_BINARY, (BYTE*)&parameter, sizeof(parameter));

        return outputbuffer == Cracker_TRUE;
    }
}