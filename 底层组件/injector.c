/* ====================================================================
 * SSJJInjector.exe - self-contained injector for the SSJJ Unity client.
 * Embeds SSJJNative.dll (RCDATA 422) and runs with auto-elevation.
 *
 *  Usage (double-click = auto):
 *    SSJJInjector.exe                          auto scan + inject (UAC elevated)
 *    SSJJInjector.exe --manual                interactive window selector
 *    SSJJInjector.exe <pid> <SSJJNative.dll>   scripted injection
 *    SSJJInjector.exe <SSJJNative.dll>         auto mode, custom DLL (dev)
 *    SSJJInjector.exe --help                  show usage
 *
 *  Injection techniques (in priority order):
 *    1. VMX path (default): vmx_install() maps VMShellcode into kernel via
 *       VMLiteMapper (CmRegisterCallbackEx hook), then vmx_create_remote_thread()
 *       performs kernel-level thread creation in the game process. Completely
 *       bypasses GameGuard user-mode hooks.
 *    2. SSJJDrv.sys path (--legacy-load): loaded via kdmapper or NtLoadDriver,
 *       uses Zw* syscalls from SYSTEM context. Fallback only.
 *
 *  Exit codes:
 *    0 success       2 already loaded      4 target not x64
 *    1 usage/abort   3 OpenProcess failed  5 injection failed
 *                                         6 module not mapped (timeout)
 * ==================================================================== */
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winioctl.h>
#include <tlhelp32.h>
#include <winreg.h>
#include <winsvc.h>

#include <conio.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <winternl.h>
#include <ntstatus.h>

/* L3: keyed stream cipher shared with the build-time encryptor. */
#include "ssjj_crypto.h"

/* L4: VMProtect SDK - lock check (debugger / VM detection). */
#include <stdbool.h>
#include "VMProtectSDK.h"

/* Kernel driver IOCTL protocol (shared headers). */
#include "driver/SSJJDrv.h"
#include "driver/SSJJProtect.h"

/* kdmapper-style loader (extern "C" bridge) */
#include "mapper/loader_bridge.h"

/* VMX 内核通信接口（过检测系统集成，无授权验证版本） */
#include "vmx/vmx_interface.h"

#define MAX_CANDIDATES 256
#define WINDOW_TITLE_CAPACITY 256
#define REFRESH_INTERVAL_MS 750

/* SSJJNative.dll is embedded into this executable as RCDATA 422. */
#define SSJJ_NATIVE_RESOURCE_ID   422
/* VMShellcode.bin is embedded as RCDATA 423 (used by vmx_install). */
#define VMX_SHELLCODE_RESOURCE_ID 423
/* VMLiteMapper.sys is embedded as RCDATA 424 (used by vmx_install). */
#define VMX_MAPPER_RESOURCE_ID    424

/* Primary target executable. */
#define TARGET_EXE L"SSJJ_BattleClient_Unity.exe"
/* Fallback: any executable whose name starts with this (SSJJ* variants). */
#define TARGET_EXE_PREFIX L"SSJJ"
/* Fallback: window title keywords (used when the exe name is unknown). */
static const wchar_t *const kTitleKeywords[] = {
    L"\u751f\u6b7b\u51fb", /* 生死狙击 */
    L"SSJJ",
    NULL
};

typedef struct process_candidate {
    DWORD process_id;
    wchar_t executable[MAX_PATH];
    wchar_t title[WINDOW_TITLE_CAPACITY];
} process_candidate;

typedef struct window_search_context {
    process_candidate *candidates;
    size_t count;
} window_search_context;

static void print_last_error(const wchar_t *operation) {
    DWORD error = GetLastError();
    wchar_t *message = NULL;
    FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER
                    | FORMAT_MESSAGE_FROM_SYSTEM
                    | FORMAT_MESSAGE_IGNORE_INSERTS,
            NULL, error, 0, (wchar_t *)&message, 0, NULL);
    fwprintf(stderr, L"%ls failed (%lu): %ls\n", operation,
            (unsigned long)error, message == NULL ? L"unknown error" : message);
    if (message != NULL) {
        LocalFree(message);
    }
}

static int absolute_existing_file(
        const wchar_t *input, wchar_t *output, DWORD capacity) {
    DWORD length = GetFullPathNameW(input, capacity, output, NULL);
    DWORD attributes;
    if (length == 0 || length >= capacity) {
        return 0;
    }
    attributes = GetFileAttributesW(output);
    return attributes != INVALID_FILE_ATTRIBUTES
            && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

/* ------------------------------------------------------------------ */
/* Extract the embedded (encrypted) SSJJNative.dll to %TEMP%\SSJJVape  */
/* L3: the RCDATA 422 resource is ciphertext; decrypt it here first.   */
/* ------------------------------------------------------------------ */
static int extract_embedded_dll(wchar_t *output, size_t capacity) {
    HMODULE self = GetModuleHandleW(NULL);
    HRSRC resource;
    HGLOBAL loaded;
    const unsigned char *bytes;
    unsigned char *plain = NULL;
    DWORD size;
    wchar_t temp_root[MAX_PATH];
    wchar_t temp_directory[MAX_PATH];
    HANDLE file = INVALID_HANDLE_VALUE;
    DWORD offset = 0;
    int result = 0;

    resource = FindResourceW(self, MAKEINTRESOURCEW(SSJJ_NATIVE_RESOURCE_ID),
            MAKEINTRESOURCEW(10)); /* RT_RCDATA */
    if (resource == NULL) {
        return 0;
    }
    size = SizeofResource(self, resource);
    loaded = LoadResource(self, resource);
    bytes = loaded == NULL ? NULL
            : (const unsigned char *)LockResource(loaded);
    if (bytes == NULL || size < 4) {
        return 0;
    }

    /* L3: copy to a heap buffer and decrypt (XOR stream, symmetric). */
    plain = (unsigned char *)malloc(size);
    if (plain == NULL) {
        return 0;
    }
    memcpy(plain, bytes, size);
    ssjj_crypto_init();
    ssjj_crypto_xor(plain, size);
    if (plain[0] != 'M' || plain[1] != 'Z') {
        free(plain);
        return 0;
    }

    if (GetTempPathW((DWORD)(sizeof(temp_root) / sizeof(temp_root[0])),
            temp_root) == 0) {
        free(plain);
        return 0;
    }
    _snwprintf_s(temp_directory,
            sizeof(temp_directory) / sizeof(temp_directory[0]), _TRUNCATE,
            L"%lsSSJJVape", temp_root);
    if (!CreateDirectoryW(temp_directory, NULL)
            && GetLastError() != ERROR_ALREADY_EXISTS) {
        free(plain);
        return 0;
    }
    /* Randomize extracted native DLL name: avoids predictable
     * ssjjnative-<pid>.dll that GameGuard could fingerprint on disk. */
    {
        DWORD tick = GetTickCount();
        DWORD seed = tick ^ GetCurrentProcessId();
        if (seed == 0) seed = 0x2545F491u;
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        if (_snwprintf_s(output, capacity, _TRUNCATE,
                L"%ls\\st-%08lx.dll", temp_directory, seed) < 0) {
            free(plain);
            return 0;
        }
    }
    file = CreateFileW(output, GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_DELETE, NULL, CREATE_ALWAYS,
            FILE_ATTRIBUTE_TEMPORARY, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        free(plain);
        return 0;
    }
    while (offset < size) {
        DWORD written = 0;
        DWORD remaining = size - offset;
        if (!WriteFile(file, plain + offset, remaining, &written, NULL)
                || written == 0) {
            goto cleanup;
        }
        offset += written;
    }
    result = FlushFileBuffers(file) ? 1 : 0;

cleanup:
    CloseHandle(file);
    free(plain);
    if (!result) {
        DeleteFileW(output);
    }
    return result;
}

/* ------------------------------------------------------------------ */
/* Colored console helpers                                             */
/* ------------------------------------------------------------------ */
typedef enum console_color {
    CC_DEFAULT = 7,
    CC_GRAY    = 8,
    CC_GREEN   = 10,
    CC_CYAN    = 11,
    CC_RED     = 12,
    CC_MAGENTA = 13,
    CC_YELLOW  = 14,
    CC_WHITE   = 15
} console_color;

static HANDLE g_console_out = NULL;

static void cc_init(void) {
    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
    /* If launched in a context without a console, attach to the parent or
     * allocate a fresh one so the UI is always visible. */
    if (output == NULL || output == INVALID_HANDLE_VALUE) {
        if (!AttachConsole(ATTACH_PARENT_PROCESS)) {
            AllocConsole();
        }
        freopen("CONOUT$", "w", stdout);
        freopen("CONIN$", "r", stdin);
        output = GetStdHandle(STD_OUTPUT_HANDLE);
    }
    g_console_out = output;
    if (g_console_out == INVALID_HANDLE_VALUE) {
        g_console_out = NULL;
    }
    /* unbuffered: every line shows immediately, even when redirected */
    setvbuf(stdout, NULL, _IONBF, 0);
}

static void cc_set(console_color color) {
    if (g_console_out != NULL) {
        SetConsoleTextAttribute(g_console_out, (WORD)color);
    }
}

static void cc_print(console_color color, const wchar_t *format, ...) {
    va_list args;
    wchar_t buffer[2048];
    va_start(args, format);
    _vsnwprintf_s(buffer, sizeof(buffer) / sizeof(buffer[0]), _TRUNCATE,
            format, args);
    va_end(args);
    cc_set(color);
    if (g_console_out != NULL) {
        DWORD written = 0;
        /* write straight to the console handle - immune to stdio issues */
        if (!WriteConsoleW(g_console_out, buffer,
                (DWORD)wcslen(buffer), &written, NULL)) {
            fputws(buffer, stdout);
        }
    } else {
        fputws(buffer, stdout);
    }
    cc_set(CC_DEFAULT);
}

/* ------------------------------------------------------------------ */
/* L4: 机器锁检查 - 检测到调试器则弹 "有锁机!" 并退出                   */
/* 整段代码由 VMProtect 的 lock_check 标记虚拟化加固。                 */
/* 注: 虚拟机检测已按用户要求移除(真机开 Hyper-V/VBS 会误报)。        */
/* ------------------------------------------------------------------ */
typedef LONG (NTAPI *nt_query_information_process_fn)(
        HANDLE, ULONG, PVOID, ULONG, PULONG);

static int detect_debugger_native(void) {
    nt_query_information_process_fn query;
    DWORD debug_port = 0;
    HANDLE debug_object = NULL;

    /* 1) IsDebuggerPresent (PEB.BeingDebugged) */
    if (IsDebuggerPresent()) return 1;

    /* 2) NtQueryInformationProcess: debug port + debug object handle.
     *    不查 ProcessDebugFlags: 部分反作弊/工具会把它置 0 导致误报。 */
    query = (nt_query_information_process_fn)GetProcAddress(
            GetModuleHandleW(L"ntdll.dll"), "NtQueryInformationProcess");
    if (query != NULL) {
        if (query(GetCurrentProcess(), 7 /*ProcessDebugPort*/,
                &debug_port, sizeof(debug_port), NULL) == 0 && debug_port != 0) {
            return 1;
        }
        if (query(GetCurrentProcess(), 0x1E /*ProcessDebugObjectHandle*/,
                &debug_object, sizeof(debug_object), NULL) == 0
                && debug_object != NULL) {
            return 1;
        }
    }
    return 0;
}

/* L4: 机器锁主检查 - 由 VMProtect 标记虚拟化 (vmp 项目内 lock_check) */
static void lock_check(void) {
    VMProtectBeginVirtualization("lock_check");
    if (VMProtectIsDebuggerPresent(FALSE)
            || detect_debugger_native()) {
        MessageBoxW(NULL, L"\u6709\u9501\u673a!", L"Vape",
                MB_OK | MB_ICONERROR);
        ExitProcess(1);
    }
    VMProtectEnd();
}

/* ------------------------------------------------------------------ */
/* Logo banner (VAPE, pure ASCII so any console font can render it)     */
/* ------------------------------------------------------------------ */
static void print_logo(void) {
    cc_print(CC_CYAN,
        L"__     __    _    ____  _____ \n"
        L"\\ \\   / /   / \\  |  _ \\| ____|\n"
        L" \\ \\ / /   / _ \\ | |_) |  _|  \n"
        L"  \\ V /   / ___ \\|  __/| |___ \n"
        L"   \\_/   /_/   \\_\\_|   |_____|\n");
    cc_print(CC_WHITE, L"   Vape for SSJJ");
    cc_print(CC_YELLOW, L"  v1.3");
    cc_print(CC_GRAY, L"   (Unity Mono \u00b7 \u81ea\u5305\u542b)\n");  /* (Unity Mono · 自包含) */
    cc_print(CC_CYAN, L"  ------------------------------------------------------------\n");
}

/* forward declaration (defined below with the other process helpers) */
static int is_primary_target(const wchar_t *executable);

/* Find the game process by exact executable name. Returns PID or 0. */
static DWORD find_game_process(void) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    PROCESSENTRY32W entry;
    DWORD pid = 0;
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }
    memset(&entry, 0, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (is_primary_target(entry.szExeFile)) {
                pid = entry.th32ProcessID;
                break;
            }
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return pid;
}

/* Auto mode: one-shot scan for the game process. Returns PID or 0. */
static DWORD auto_check_process(void) {
    DWORD pid;
    print_logo();
    cc_print(CC_GREEN, L"[+] \u6b63\u5728\u68c0\u6d4b\u6e38\u620f\u8fdb\u7a0b %ls ...\n", TARGET_EXE);
    pid = find_game_process();
    if (pid != 0) {
        cc_print(CC_GREEN, L"[+] \u5df2\u627e\u5230\uff1a%ls (PID %lu)\n", TARGET_EXE, (unsigned long)pid);
    }
    return pid;
}

/* Pause until a key is pressed (interactive runs only). */
static void press_any_key(void) {
    cc_print(CC_GRAY, L"\n\u6309\u4efb\u610f\u952e\u5173\u95ed\u7a0b\u5e8f...\n");  /* 按任意键关闭程序... */
    _getwch();
}

/* Exact match on the primary target exe. */
static int is_primary_target(const wchar_t *executable) {
    return _wcsicmp(executable, TARGET_EXE) == 0;
}

/* Prefix match for SSJJ* variants (e.g. SSJJ_BattleClient_Unity.exe). */
static int is_ssjj_named(const wchar_t *executable) {
    return _wcsnicmp(executable, TARGET_EXE_PREFIX,
            wcslen(TARGET_EXE_PREFIX)) == 0;
}

static int title_matches_target(const wchar_t *title) {
    int i;
    for (i = 0; kTitleKeywords[i] != NULL; ++i) {
        if (wcsstr(title, kTitleKeywords[i]) != NULL) {
            return 1;
        }
    }
    return 0;
}

static int is_target_process(const wchar_t *executable, const wchar_t *title) {
    if (is_primary_target(executable)) {
        return 1;
    }
    /* SSJJ* exe name + a visible window => likely the game client */
    if (is_ssjj_named(executable) && title != NULL && title[0] != L'\0') {
        return 1;
    }
    /* Unknown exe name but obvious window title */
    if (title != NULL && title_matches_target(title)) {
        return 1;
    }
    return 0;
}

static BOOL CALLBACK capture_window_title(HWND window, LPARAM parameter) {
    window_search_context *context = (window_search_context *)parameter;
    wchar_t title[WINDOW_TITLE_CAPACITY];
    DWORD process_id = 0;
    size_t index;
    if (!IsWindowVisible(window) || GetWindowTextLengthW(window) == 0) {
        return TRUE;
    }
    GetWindowThreadProcessId(window, &process_id);
    if (process_id == 0
            || GetWindowTextW(window, title, WINDOW_TITLE_CAPACITY) == 0) {
        return TRUE;
    }
    for (index = 0; index < context->count; ++index) {
        process_candidate *candidate = &context->candidates[index];
        if (candidate->process_id == process_id && candidate->title[0] == L'\0') {
            wcscpy(candidate->title, title);
            break;
        }
    }
    return TRUE;
}

static int compare_candidates(const void *left, const void *right) {
    const process_candidate *a = (const process_candidate *)left;
    const process_candidate *b = (const process_candidate *)right;
    if (a->process_id < b->process_id) return -1;
    if (a->process_id > b->process_id) return 1;
    return 0;
}

static size_t enumerate_candidates(
        process_candidate *candidates, size_t capacity) {
    HANDLE snapshot;
    PROCESSENTRY32W entry;
    size_t count = 0;
    size_t read_index;
    size_t write_index = 0;
    window_search_context context;

    snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }
    memset(&entry, 0, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (count < capacity && is_ssjj_named(entry.szExeFile)) {
                process_candidate *candidate = &candidates[count++];
                memset(candidate, 0, sizeof(*candidate));
                candidate->process_id = entry.th32ProcessID;
                wcsncpy(candidate->executable, entry.szExeFile, MAX_PATH - 1);
            }
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);

    context.candidates = candidates;
    context.count = count;
    EnumWindows(capture_window_title, (LPARAM)&context);

    /* Keep only candidates that are actually the game client. */
    for (read_index = 0; read_index < count; ++read_index) {
        if (is_target_process(candidates[read_index].executable,
                candidates[read_index].title)) {
            if (write_index != read_index) {
                candidates[write_index] = candidates[read_index];
            }
            ++write_index;
        }
    }
    qsort(candidates, write_index, sizeof(*candidates), compare_candidates);
    return write_index;
}

static void clear_console_rows(HANDLE output, SHORT rows) {
    CONSOLE_SCREEN_BUFFER_INFO info;
    COORD start = {0, 0};
    DWORD cells;
    DWORD written;
    if (!GetConsoleScreenBufferInfo(output, &info)) return;
    if (rows > info.dwSize.Y) rows = info.dwSize.Y;
    cells = (DWORD)info.dwSize.X * (DWORD)rows;
    FillConsoleOutputCharacterW(output, L' ', cells, start, &written);
    FillConsoleOutputAttribute(output, info.wAttributes, cells, start, &written);
}

static void render_selector(const process_candidate *candidates, size_t count,
        size_t selected, const wchar_t *dll_path) {
    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
    CONSOLE_SCREEN_BUFFER_INFO info;
    COORD home = {0, 0};
    size_t index;
    static SHORT previous_rows = 0;
    if (GetConsoleScreenBufferInfo(output, &info)) {
        /* logo(9) + header(3) + list(count) + margin */
        SHORT rows = (SHORT)(count + 16);
        clear_console_rows(output, rows > previous_rows ? rows : previous_rows);
        SetConsoleCursorPosition(output, home);
        previous_rows = rows;
    }
    print_logo();
    cc_print(CC_CYAN, L"  DLL: %ls\n\n", dll_path);
    cc_print(CC_YELLOW, L"  \u9009\u62e9\u6e38\u620f\u5ba2\u6237\u7aef (\u2191/\u2193 \u9009\u62e9, Enter \u6ce8\u5165, Esc \u9000\u51fa)\n\n");
    if (count == 0) {
        cc_print(CC_GRAY, L"  \u672a\u627e\u5230 %ls \u7a97\u53e3\uff0c\u7b49\u5f85\u4e2d...\n", TARGET_EXE);
    } else {
        for (index = 0; index < count; ++index) {
            wprintf(L"%lc [%5lu] %-30ls  %ls\n",
                    index == selected ? L'>' : L' ',
                    (unsigned long)candidates[index].process_id,
                    candidates[index].executable, candidates[index].title);
        }
    }
    fflush(stdout);
}

static DWORD select_process(const wchar_t *dll_path) {
    process_candidate candidates[MAX_CANDIDATES];
    size_t count = 0;
    size_t selected = 0;
    DWORD selected_process_id = 0;
    ULONGLONG next_refresh = 0;
    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
    CONSOLE_CURSOR_INFO original_cursor;
    CONSOLE_CURSOR_INFO hidden_cursor;
    int cursor_changed = 0;

    if (GetConsoleCursorInfo(output, &original_cursor)) {
        hidden_cursor = original_cursor;
        hidden_cursor.bVisible = FALSE;
        cursor_changed = SetConsoleCursorInfo(output, &hidden_cursor);
    }
    for (;;) {
        ULONGLONG now = GetTickCount64();
        if (now >= next_refresh) {
            DWORD previous_id = count == 0 ? 0 : candidates[selected].process_id;
            size_t index;
            count = enumerate_candidates(candidates, MAX_CANDIDATES);
            selected = 0;
            for (index = 0; index < count; ++index) {
                if (candidates[index].process_id == previous_id) {
                    selected = index;
                    break;
                }
            }
            render_selector(candidates, count, selected, dll_path);
            next_refresh = now + REFRESH_INTERVAL_MS;
        }
        if (_kbhit()) {
            int key = _getwch();
            if (key == 0 || key == 0xe0) {
                key = _getwch();
                if (key == 72 && count != 0) {
                    selected = selected == 0 ? count - 1 : selected - 1;
                    render_selector(candidates, count, selected, dll_path);
                } else if (key == 80 && count != 0) {
                    selected = (selected + 1) % count;
                    render_selector(candidates, count, selected, dll_path);
                }
            } else if (key == 13 && count != 0) {
                selected_process_id = candidates[selected].process_id;
                break;
            } else if (key == 27) {
                break;
            }
        }
        Sleep(25);
    }
    if (cursor_changed) SetConsoleCursorInfo(output, &original_cursor);
    wprintf(L"\n");
    return selected_process_id;
}

static uintptr_t remote_module_by_path(
        DWORD process_id, const wchar_t *module_path) {
    HANDLE snapshot;
    MODULEENTRY32W entry;
    uintptr_t result = 0;
    snapshot = CreateToolhelp32Snapshot(
            TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, process_id);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }
    memset(&entry, 0, sizeof(entry));
    entry.dwSize = sizeof(entry);
    if (Module32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szExePath, module_path) == 0) {
                result = (uintptr_t)entry.modBaseAddr;
                break;
            }
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

/* ------------------------------------------------------------------ */
/* Kernel driver injection (SSJJDrv.sys)                               */
/* The driver is loaded with NtLoadDriver (no SCM service record) and  */
/* unloaded immediately after injection to minimize exposure.          */
/* ------------------------------------------------------------------ */
typedef NTSTATUS (NTAPI *nt_load_driver_fn)(PUNICODE_STRING);
typedef NTSTATUS (NTAPI *nt_unload_driver_fn)(PUNICODE_STRING);

static const wchar_t *const g_driver_path = L"SSJJDrv.sys"; /* beside Vape.exe */

/* 默认用 kdmapper 方式（iqvw64e.sys provider + 手动映射）加载 SSJJDrv.sys；
 * --legacy-load 时回退到 NtLoadDriver（需签名/测试模式）。 */
static int g_legacy_load = 0;

/* Enable SeLoadDriverPrivilege so NtLoadDriver works from an elevated
 * user-mode process (required when the caller is not SYSTEM). */
static int enable_load_driver_privilege(void) {
    HANDLE token = NULL;
    TOKEN_PRIVILEGES tp;
    LUID luid;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES
            | TOKEN_QUERY, &token)) {
        return 0;
    }
    if (!LookupPrivilegeValueW(NULL, L"SeLoadDriverPrivilege", &luid)) {
        CloseHandle(token);
        return 0;
    }
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Luid = luid;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    if (!AdjustTokenPrivileges(token, FALSE, &tp, 0, NULL, NULL)) {
        CloseHandle(token);
        return 0;
    }
    CloseHandle(token);
    return 1;
}

/* Load SSJJDrv.sys. Default: kdmapper (vulnerable-driver provider +
 * manual map). With --legacy-load: NtLoadDriver service path. */
static int load_kernel_driver(void) {
    wchar_t image_path[MAX_PATH];

    /* The driver file sits next to Vape.exe */
    if (!GetModuleFileNameW(NULL, image_path, MAX_PATH)) return 0;
    {
        wchar_t *slash = wcsrchr(image_path, L'\\');
        if (slash == NULL) return 0;
        wcscpy_s(slash + 1, MAX_PATH - (size_t)(slash - image_path + 1),
                g_driver_path);
    }
    if (GetFileAttributesW(image_path) == INVALID_FILE_ATTRIBUTES) {
        return 0;
    }

    /* kdmapper 路径（默认）：provider 加载 + 手动映射 SSJJDrv.sys */
    if (!g_legacy_load) {
        return ssjj_loader_load_driver(image_path);
    }

    /* ---- legacy: NtLoadDriver（需签名 / 测试模式） ---- */
    {
        static const wchar_t *const key_path =
                L"SYSTEM\\CurrentControlSet\\Services\\" SSJJ_DRV_SERVICE_NAME;
        HKEY key = NULL;
        DWORD type = SERVICE_KERNEL_DRIVER;
        DWORD start = SERVICE_DEMAND_START;
        DWORD path_len;
        LONG status;
        NTSTATUS nt_status;
        UNICODE_STRING service_key;
        nt_load_driver_fn nt_load_driver;

        if (!enable_load_driver_privilege()) return 0;

        status = RegCreateKeyExW(HKEY_LOCAL_MACHINE, key_path, 0, NULL,
                REG_OPTION_NON_VOLATILE, KEY_SET_VALUE, NULL, &key, NULL);
        if (status != ERROR_SUCCESS) return 0;

        path_len = (DWORD)(wcslen(image_path) + 1) * sizeof(wchar_t);
        RegSetValueExW(key, L"ImagePath", 0, REG_EXPAND_SZ,
                (const BYTE *)image_path, path_len);
        RegSetValueExW(key, L"Type", 0, REG_DWORD,
                (const BYTE *)&type, sizeof(type));
        RegSetValueExW(key, L"Start", 0, REG_DWORD,
                (const BYTE *)&start, sizeof(start));
        RegCloseKey(key);

        nt_load_driver = (nt_load_driver_fn)GetProcAddress(
                GetModuleHandleW(L"ntdll.dll"), "NtLoadDriver");
        if (nt_load_driver == NULL) {
            RegDeleteKeyW(HKEY_LOCAL_MACHINE, key_path);
            return 0;
        }
        RtlInitUnicodeString(&service_key, SSJJ_DRV_SERVICE_KEY);
        nt_status = nt_load_driver(&service_key);
        if (nt_status != STATUS_SUCCESS &&
            nt_status != STATUS_IMAGE_ALREADY_LOADED) {
            RegDeleteKeyW(HKEY_LOCAL_MACHINE, key_path);
            return 0;
        }
    }
    return 1;
}

static void unload_kernel_driver(void) {
    /* kdmapper 路径：卸载映射驱动 + provider */
    if (!g_legacy_load) {
        ssjj_loader_unload_driver();
        return;
    }
    /* legacy 路径 */
    {
        static const wchar_t *const key_path =
                L"SYSTEM\\CurrentControlSet\\Services\\" SSJJ_DRV_SERVICE_NAME;
        UNICODE_STRING service_key;
        nt_unload_driver_fn nt_unload_driver;
        nt_unload_driver = (nt_unload_driver_fn)GetProcAddress(
                GetModuleHandleW(L"ntdll.dll"), "NtUnloadDriver");
        if (nt_unload_driver != NULL) {
            RtlInitUnicodeString(&service_key, SSJJ_DRV_SERVICE_KEY);
            nt_unload_driver(&service_key);
        }
        RegDeleteKeyW(HKEY_LOCAL_MACHINE, key_path);
    }
}

/* ------------------------------------------------------------------ */
/* Process protection (SSJJ_IOCTL_PROTECT)                             */
/* ------------------------------------------------------------------ */
/* Ask the driver to enable ObRegisterCallbacks-based handle protection
 * on the game process. After this, GameGuard's user-mode injection
 * (OpenProcess ALL_ACCESS -> VirtualAllocEx -> WriteProcessMemory ->
 * CreateRemoteThread) can no longer obtain VM_WRITE/CREATE_THREAD on
 * the game process. Our own PID is recorded as trusted, so the driver
 * still lets our Zw* operations through. Returns 1 if protection is
 * active, 0 otherwise. */
static int enable_process_protection(HANDLE device, DWORD game_pid) {
    SSJJ_PROTECT_REQ request;
    ULONG active = 0;
    DWORD returned = 0;

    memset(&request, 0, sizeof(request));
    request.GamePid = game_pid;
    request.TrustedPid = GetCurrentProcessId();
    request.Action = SSJJ_PROTECT_ENABLE;
    wcsncpy_s(request.ImageName, SSJJ_PROTECT_IMAGE_CHARS,
            TARGET_EXE, _TRUNCATE);

    if (!DeviceIoControl(device, SSJJ_IOCTL_PROTECT, &request,
            sizeof(request), &active, sizeof(active), &returned, NULL)) {
        return 0;
    }
    return (active == 1) ? 1 : 0;
}

/* Disable protection and unload the driver (clean exit path). */
static void disable_process_protection_and_unload(void) {
    HANDLE device = CreateFileW(L"\\\\.\\SSJJDrv",
            GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, NULL);
    if (device != INVALID_HANDLE_VALUE) {
        SSJJ_PROTECT_REQ request;
        ULONG active = 0;
        DWORD returned = 0;
        memset(&request, 0, sizeof(request));
        request.Action = SSJJ_PROTECT_DISABLE;
        DeviceIoControl(device, SSJJ_IOCTL_PROTECT, &request,
                sizeof(request), &active, sizeof(active), &returned, NULL);
        CloseHandle(device);
    }
    unload_kernel_driver();
}

/* Returns: 1 success, 2 already loaded, 0 failure. */
/* ------------------------------------------------------------------ */
/* VMX injection path (primary, default)                               */
/* Uses vmx_install() to map VMShellcode into kernel via VMLiteMapper, */
/* then injects via vmx_create_remote_thread() — fully kernel-level,   */
/* bypasses all GameGuard user-mode hooks.                             */
/* ------------------------------------------------------------------ */
static int inject_library_vmx(DWORD process_id, const wchar_t *dll_path) {
    int vmx_ret;
    int attempt;
    unsigned long long kernel32_base;
    unsigned long long load_library_addr;
    SIZE_T dll_path_size;
    unsigned long long remote_buf = 0;
    HMODULE local_kernel32;
    FARPROC local_llw;

    /* Step 1: Initialize VMX (maps shellcode into kernel) */
    vmx_ret = vmx_install(NULL, 0, NULL, 0);
    if (vmx_ret != VMX_OK) {
        fwprintf(stderr, L"[VMX] vmx_install failed: %d\n", vmx_ret);
        return 0;
    }
    fwprintf(stderr, L"[VMX] Kernel shellcode active.\n");

    /* Step 2: Set target process */
    vmx_ret = vmx_set_process(process_id);
    if (vmx_ret != VMX_OK) {
        fwprintf(stderr, L"[VMX] vmx_set_process(%lu) failed: %d\n",
            (unsigned long)process_id, vmx_ret);
        vmx_shutdown();
        return 0;
    }

    /* Step 3: Resolve kernel32!LoadLibraryW in target process via VMX */
    local_kernel32 = GetModuleHandleW(L"kernel32.dll");
    local_llw = local_kernel32 ? GetProcAddress(local_kernel32, "LoadLibraryW") : NULL;
    if (!local_kernel32 || !local_llw) {
        fwprintf(stderr, L"[VMX] Cannot resolve LoadLibraryW.\n");
        vmx_set_process(0);
        vmx_shutdown();
        return 0;
    }

    kernel32_base = vmx_get_module_base("kernel32.dll");
    if (!kernel32_base) {
        fwprintf(stderr, L"[VMX] Cannot find kernel32.dll in target process.\n");
        vmx_set_process(0);
        vmx_shutdown();
        return 0;
    }
    load_library_addr = kernel32_base +
        ((unsigned long long)local_llw - (unsigned long long)local_kernel32);

    /* Step 4: Allocate memory in target process via VMX (kernel-level, no OpenProcess) */
    dll_path_size = (wcslen(dll_path) + 1) * sizeof(wchar_t);
    remote_buf = vmx_alloc_mem((unsigned long long)dll_path_size, PAGE_READWRITE);
    if (!remote_buf) {
        fwprintf(stderr, L"[VMX] vmx_alloc_mem failed.\n");
        vmx_set_process(0);
        vmx_shutdown();
        return 0;
    }

    /* Step 5: Write the DLL path into target process via VMX */
    vmx_ret = vmx_write_mem(remote_buf, dll_path, (long)dll_path_size);
    if (vmx_ret != VMX_OK) {
        fwprintf(stderr, L"[VMX] vmx_write_mem failed: %d\n", vmx_ret);
        vmx_set_process(0);
        vmx_shutdown();
        return 0;
    }

    /* Step 6: Create remote thread via VMX kernel API */
    vmx_ret = vmx_create_remote_thread(load_library_addr, remote_buf);
    if (vmx_ret != VMX_OK) {
        fwprintf(stderr, L"[VMX] vmx_create_remote_thread failed: %d\n", vmx_ret);
        vmx_set_process(0);
        vmx_shutdown();
        return 0;
    }
    fwprintf(stderr, L"[VMX] Remote thread created in PID %lu.\n",
        (unsigned long)process_id);

    /* Step 7: Confirm the module is mapped */
    for (attempt = 0; attempt < 100; ++attempt) {
        if (remote_module_by_path(process_id, dll_path) != 0) break;
        Sleep(50);
    }
    if (remote_module_by_path(process_id, dll_path) == 0) {
        fwprintf(stderr, L"[VMX] LoadLibraryW returned but DLL not mapped.\n");
        vmx_set_process(0);
        vmx_shutdown();
        return 0;
    }

    /* Step 8: Clean up (VMX shellcode remains in kernel for future use) */
    vmx_set_process(0);
    return 1; /* success — VMX path, no process protection needed */
}

/* ------------------------------------------------------------------ */
/* inject_library() — try VMX first, fall back to SSJJDrv.sys         */
/* ------------------------------------------------------------------ */
static int inject_library(DWORD process_id, const wchar_t *dll_path) {
    HANDLE device = INVALID_HANDLE_VALUE;
    SSJJ_INJECT_REQ request;
    ULONG arch = 0;
    ULONG result = SSJJ_INJ_ERR_BADPID;
    DWORD returned = 0;
    BOOL ok;
    int attempt;

    if (remote_module_by_path(process_id, dll_path) != 0) {
        return 2;
    }

    /* ---- VMX path (default, unless --legacy-load) ---- */
    if (!g_legacy_load) {
        cc_print(CC_CYAN, L"[*] 尝试 VMX 内核注入路径...\n");
        int vmx_result = inject_library_vmx(process_id, dll_path);
        if (vmx_result != 0) {
            return vmx_result;
        }
        cc_print(CC_YELLOW, L"[!] VMX 路径失败，回退到 SSJJDrv.sys...\n");
    }

    /* ---- SSJJDrv.sys path (fallback / --legacy-load) ---- */

    /* 1. Load the kernel driver */
    if (!load_kernel_driver()) {
        fwprintf(stderr, L"Failed to load kernel driver %ls.\n",
                g_driver_path);
        return 0;
    }

    /* 2. Open the driver device */
    device = CreateFileW(L"\\\\.\\SSJJDrv",
            GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, NULL);
    if (device == INVALID_HANDLE_VALUE) {
        fwprintf(stderr, L"Failed to open SSJJDrv device.\n");
        goto cleanup;
    }

    /* 3. Verify the target is a native x64 process (driver-side check) */
    ok = DeviceIoControl(device, SSJJ_IOCTL_QUERY_X64, &process_id,
            sizeof(process_id), &arch, sizeof(arch), &returned, NULL);
    if (!ok || returned != sizeof(arch) || arch != 1) {
        fwprintf(stderr, L"Target process is not x64; injection refused.\n");
        goto cleanup;
    }

    /* 4. Resolve kernel32!LoadLibraryW offset */
    {
        HMODULE local_kernel = GetModuleHandleW(L"kernel32.dll");
        FARPROC local_load_library = local_kernel == NULL
                ? NULL : GetProcAddress(local_kernel, "LoadLibraryW");
        if (local_kernel == NULL || local_load_library == NULL) {
            fwprintf(stderr, L"Could not resolve kernel32!LoadLibraryW.\n");
            goto cleanup;
        }
        request.LoadLibraryOffset = (ULONGLONG)((uintptr_t)local_load_library
                - (uintptr_t)local_kernel);
    }
    request.ProcessId = process_id;
    wcscpy_s(request.DllPath, SSJJ_MAX_DLL_PATH, dll_path);

    /* 5. Ask the driver to inject */
    ok = DeviceIoControl(device, SSJJ_IOCTL_INJECT, &request, sizeof(request),
            &result, sizeof(result), &returned, NULL);
    if (!ok || returned != sizeof(result)) {
        fwprintf(stderr, L"Driver injection IOCTL failed.\n");
        goto cleanup;
    }
    if (result != SSJJ_INJ_OK) {
        fwprintf(stderr, L"Driver injection failed (code %lu).\n",
                (unsigned long)result);
        goto cleanup;
    }

    /* 6. Confirm the module is mapped */
    for (attempt = 0; attempt < 100; ++attempt) {
        if (remote_module_by_path(process_id, dll_path) != 0) {
            break;
        }
        Sleep(50);
    }
    if (remote_module_by_path(process_id, dll_path) == 0) {
        fwprintf(stderr, L"LoadLibraryW returned, but the DLL is not mapped. "
                L"Inspect the debugger output for bootstrap failure.\n");
        goto cleanup;
    }

    /* 7. No process protection (removed — replaced by VMX path above) */
    CloseHandle(device);
    device = INVALID_HANDLE_VALUE;
    unload_kernel_driver();
    return 1;

cleanup:
    if (device != INVALID_HANDLE_VALUE) CloseHandle(device);
    unload_kernel_driver();
    return 0;
}

static void usage(const wchar_t *program) {
    fwprintf(stderr,
            L"用法: %ls\n"
            L"       %ls --manual\n"
            L"       %ls <游戏pid> [SSJJNative.dll]\n"
            L"\n"
            L"默认（双击）：自动提权 → 扫描游戏进程 → 自动注入。\n"
            L"  --manual                 交互选择进程\n"
            L"  --legacy-load            用 NtLoadDriver 加载驱动（默认 kdmapper）\n"
            L"  <游戏pid> <dll>          脚本注入（开发模式）\n"
            L"  <dll>                    指定 DLL 的自动模式（开发模式）\n"
            L"  --help                   显示本帮助\n"
            L"\n"
            L"SSJJNative.dll 已内嵌于本程序。\n"
            L"注入后由 DLL 自动加载 Vape 载荷并启动。\n",
            program, program, program);
}

int wmain(int argc, wchar_t **argv) {
    wchar_t dll_path[MAX_PATH];
    wchar_t *end = NULL;
    unsigned long process_id = 0;
    int embedded = 0;
    int manual = 0;
    /* L4: 机器锁检查 - 调试器/虚拟机 -> "有锁机!" 退出 (VMProtect 虚拟化) */
    lock_check();

    /* 可选参数 --legacy-load：回退到 NtLoadDriver（需签名/测试模式）。
     * 默认用 kdmapper 方式（iqvw64e.sys provider + 手动映射）。 */
    for (int i = 1; i < argc; ++i) {
        if (wcscmp(argv[i], L"--legacy-load") == 0) {
            g_legacy_load = 1;
            for (int j = i; j < argc - 1; ++j) argv[j] = argv[j + 1];
            --argc;
            --i;
        }
    }

    int interactive;
    int injection_result;

    cc_init();
    /* scripted mode (<pid> <dll>) should not block on a key press */
    interactive = (argc != 3);

    if (argc >= 2 && (wcscmp(argv[1], L"--help") == 0
            || wcscmp(argv[1], L"-h") == 0 || wcscmp(argv[1], L"/?") == 0)) {
        usage(argv[0]);
        return 0;
    }
    if (argc < 1 || argc > 3) {
        usage(argv[0]);
        return 1;
    }
    if (argc == 3) {
        /* scripted: <pid> <dll> (explicit DLL = development mode) */
        process_id = wcstoul(argv[1], &end, 10);
        if (process_id == 0 || end == argv[1] || *end != L'\0') {
            cc_print(CC_RED, L"[-] 无效的进程 ID：%ls\n", argv[1]);
            return 1;
        }
        if (!absolute_existing_file(argv[2], dll_path, MAX_PATH)) {
            cc_print(CC_RED, L"[-] DLL 不存在：%ls\n", argv[2]);
            return 1;
        }
    } else if (argc == 2) {
        if (wcscmp(argv[1], L"--manual") == 0 || wcscmp(argv[1], L"-m") == 0) {
            manual = 1;
            if (!extract_embedded_dll(dll_path, MAX_PATH)) {
                cc_print(CC_RED, L"[-] 内嵌 SSJJNative.dll 资源缺失或无效。\n");
                return 1;
            }
            embedded = 1;
        } else {
            /* auto mode with explicit DLL = development mode */
            if (!absolute_existing_file(argv[1], dll_path, MAX_PATH)) {
                cc_print(CC_RED, L"[-] DLL 不存在：%ls\n", argv[1]);
                return 1;
            }
        }
    } else {
        /* double-click: auto mode with the embedded DLL */
        if (!extract_embedded_dll(dll_path, MAX_PATH)) {
            cc_print(CC_RED,
                    L"[-] 内嵌 SSJJNative.dll 资源 (RCDATA %d) 缺失或无效。\n",
                    SSJJ_NATIVE_RESOURCE_ID);
            cc_print(CC_RED,
                    L"[-] 请重新用 CMake 构建（注入器需链接 injector_payload.rc）。\n");
            return 1;
        }
        embedded = 1;
    }

    if (manual) {
        process_id = (unsigned long)select_process(dll_path);
        if (process_id == 0) {
            if (embedded) DeleteFileW(dll_path);
            return 1;
        }
    } else if (argc != 3) {
        process_id = (unsigned long)auto_check_process();
        if (process_id == 0) {
            cc_print(CC_YELLOW, L"[!] 未检测到游戏进程（%ls）\n", TARGET_EXE);
            cc_print(CC_YELLOW, L"[!] 请启动游戏并进入地图后加载...\n");
            if (interactive) press_any_key();
            if (embedded) DeleteFileW(dll_path);
            return 1;
        }
    }

    cc_print(CC_CYAN, L"[*] 原生 DLL 就绪：%ls\n", dll_path);
    cc_print(CC_CYAN, L"[*] 正在注入 (PID %lu) ...\n",
            (unsigned long)process_id);

    injection_result = inject_library((DWORD)process_id, dll_path);
    if (injection_result == 0) {
        cc_print(CC_RED, L"[-] 注入失败。\n");
        if (interactive) press_any_key();
        if (embedded) DeleteFileW(dll_path);
        return 5;
    }
    if (injection_result == 2) {
        cc_print(CC_YELLOW, L"[-] DLL 已在 PID %lu 中加载，跳过二次引导。\n",
                (unsigned long)process_id);
        if (interactive) press_any_key();
        if (embedded) DeleteFileW(dll_path);
        return 2;
    }
    cc_print(CC_GREEN, L"[+] 注入成功：Vape 已注入 PID %lu\n",
            (unsigned long)process_id);
    cc_print(CC_CYAN, L"[*] 游戏内按 F12 开关菜单，引导结果见 ssjj-native.log。\n");
    if (injection_result == 1) {
        /* VMX path or SSJJDrv legacy path succeeded */
        cc_print(CC_GREEN, L"[+] VMX 内核注入成功（内核 shellcode 持续活跃）\n");
        if (interactive) press_any_key();
    } else if (interactive) {
        press_any_key();
    }
    if (embedded) DeleteFileW(dll_path);
    return 0;
}
