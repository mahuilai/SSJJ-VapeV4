typedef struct _PEB_LDR_DATA32 {
    ULONG Length;
    BOOLEAN Initialized;
    ULONG SsHandle;
    LIST_ENTRY32 InLoadOrderModuleList;
    LIST_ENTRY32 InMemoryOrderModuleList;
} PEB_LDR_DATA32, * PPEB_LDR_DATA32;
typedef struct _PEB_LDR_DATA64 {
    ULONG Length;
    BOOLEAN Initialized;
    ULONG64 SsHandle;
    LIST_ENTRY64 InLoadOrderModuleList;
    LIST_ENTRY64 InMemoryOrderModuleList;
} PEB_LDR_DATA64, * PPEB_LDR_DATA64;
typedef struct _PEB32 {
    BYTE Reserved1[2];
    BYTE BeingDebugged;
    BYTE Reserved2[1];
    DWORD Reserved3[2];
    DWORD Ldr;
} PEB32, * PPEB32;
typedef struct _PEB64 {
    BYTE Reserved1[2];
    BYTE BeingDebugged;
    BYTE Reserved2[1];
    DWORD64 Reserved3[2];
    DWORD64 Ldr;
} PEB64, * PPEB64;
typedef struct _STRING32 {
    USHORT   Length;
    USHORT   MaximumLength;
    ULONG  Buffer;
} STRING32;
typedef struct _STRING64 {
    USHORT   Length;
    USHORT   MaximumLength;
    ULONG64  Buffer;
} STRING64;
typedef struct _LDR_DATA_TABLE_ENTRY32 {
    LIST_ENTRY32 InLoadOrderLinks;
    LIST_ENTRY32 InMemoryOrderLinks;
    LIST_ENTRY32 InInitializationOrderLinks;
    ULONG DllBase;
    ULONG EntryPoint;
    ULONG SizeOfImage;
    STRING32 FullDllName;
    STRING32 BaseDllName;
} LDR_DATA_TABLE_ENTRY32, * PLDR_DATA_TABLE_ENTRY32;
typedef struct _LDR_DATA_TABLE_ENTRY64 {
    LIST_ENTRY64 InLoadOrderLinks;
    LIST_ENTRY64 InMemoryOrderLinks;
    union
    {
        LIST_ENTRY64 InInitializationOrderLinks;
        LIST_ENTRY64 InProgressLinks;
    };
    DWORD64 DllBase;
    DWORD64 EntryPoint;
    ULONG SizeOfImage;
    STRING64 FullDllName;
    STRING64 BaseDllName;
} LDR_DATA_TABLE_ENTRY64, * PLDR_DATA_TABLE_ENTRY64;

typedef struct _MOUSE_INPUT_DATA {
    USHORT UnitId;
    USHORT Flags;
    union {
        ULONG Buttons;
        struct {
            USHORT  ButtonFlags;
            USHORT  ButtonData;
        };
    };
    ULONG RawButtons;
    LONG LastX;
    LONG LastY;
    ULONG ExtraInformation;
} MOUSE_INPUT_DATA, * PMOUSE_INPUT_DATA;

enum MemOperateType {
    Read = 0,
    Write = 1,
    ForceWrite = 2
};

typedef struct _PROCESS_INFO {
    unsigned __int64 ProcessID;
    unsigned __int64 ProcessCr3;
    unsigned __int64 ProcessPeb;
    unsigned __int64 Process;
} PROCESS_INFO, * PPROCESS_INFO;

struct _Globals {
    int source;
    char* console_server;
    int console_port;
    char SecretKey[17];
    char hwid[18];
    char* ip;

    struct _VM {
        bool active;

        bool thread;
        __int64 RegSetValueExA;
        ULONG64 checkcode;

        bool DutyDeclaration;
        int JmpRcxInNtdll;
    } VM;

    struct _Yukino {
        bool active;

        bool thread;
        __int64 VmxVmcall;
        PROCESS_INFO ProcessInfo;

    } Yukino;
};

template<typename T = VOID, typename... Arg> __forceinline T Call(PVOID address, Arg... args) {
    return ((T(*)(Arg...))address)(args...);
}
template<typename T = VOID, typename... Arg> __forceinline T Call(__int64 address, Arg... args) {
    return ((T(*)(Arg...))address)(args...);
}

template<typename T = VOID, typename... Arg> __forceinline T stdCall(PVOID address, Arg... args) {
    return ((T(__stdcall*)(Arg...))address)(args...);
}
template<typename T = VOID, typename... Arg> __forceinline T stdCall(__int64 address, Arg... args) {
    return ((T(__stdcall*)(Arg...))address)(args...);
}