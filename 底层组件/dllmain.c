/* ====================================================================
 * SSJJNative.dll - Unity/Mono bootstrap payload.
 *
 * Injected by SSJJInjector.exe via CreateRemoteThread + LoadLibraryW.
 * On DLL_PROCESS_ATTACH a worker thread is started which:
 *   1. waits for the Mono runtime (mono-2.0-bdwgc.dll / mono.dll) to load
 *   2. attaches to the root domain (mono_thread_attach)
 *   3. materializes the embedded Vape.dll (RCDATA 421) into %TEMP%
 *   4. loads the assembly (in-memory preferred, on-disk fallback)
 *   5. locates Vape.Loader.Load (fallback t.u.i) and mono_runtime_invoke's it
 *
 * Exit codes of the bootstrap thread (also logged to ssjj-native.log):
 *    0 success        4 mono not loaded in 60s
 *    5 mono api init   6 root domain NULL
 *    7 thread attach   8 payload resource missing/invalid
 *    9 payload write  10 assembly load failed
 *   11 entry class    12 entry method
 *   13 runtime_invoke threw
 * ==================================================================== */
#include "mono_bridge.h"

#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#define VAPE_PAYLOAD_RESOURCE_ID 421

static HMODULE g_module = NULL;

/* ------------------------------------------------------------------ */
/* Logging (OutputDebugString only; file log compiled out by default   */
/* to avoid leaving ssjj-native.log traces for GameGuard to scan).     */
/* Define SSJJ_LOG_FILE at build time to re-enable file logging.       */
/* ------------------------------------------------------------------ */
static void ssjj_log(const wchar_t *format, ...) {
    wchar_t message[2048];
    wchar_t line[2304];
    SYSTEMTIME now;
    va_list arguments;

    va_start(arguments, format);
    _vsnwprintf_s(message, sizeof(message) / sizeof(message[0]),
            _TRUNCATE, format, arguments);
    va_end(arguments);
    GetLocalTime(&now);
    _snwprintf_s(line, sizeof(line) / sizeof(line[0]), _TRUNCATE,
            L"[%04u-%02u-%02u %02u:%02u:%02u.%03u] %ls\r\n",
            now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute,
            now.wSecond, now.wMilliseconds, message);
    OutputDebugStringW(line);

#ifdef SSJJ_LOG_FILE
    {
        wchar_t directory[MAX_PATH];
        wchar_t log_path[MAX_PATH];
        FILE *file = NULL;
        if (g_module == NULL
                || GetModuleFileNameW(g_module, directory, MAX_PATH) == 0) {
            return;
        }
        {
            wchar_t *separator = wcsrchr(directory, L'\\');
            if (separator == NULL) return;
            *separator = L'\0';
        }
        _snwprintf_s(log_path, sizeof(log_path) / sizeof(log_path[0]), _TRUNCATE,
                L"%ls\\ssjj-native.log", directory);
        if (_wfopen_s(&file, log_path, L"a, ccs=UTF-8") == 0 && file != NULL) {
            fputws(line, file);
            fclose(file);
        }
    }
#endif
}

/* ------------------------------------------------------------------ */
/* Materialize embedded Vape.dll (RCDATA 421) into %TEMP%\SSJJVape\    */
/* ------------------------------------------------------------------ */
static int materialize_embedded_payload(wchar_t *payload_path, size_t capacity) {
    HRSRC resource;
    HGLOBAL loaded_resource;
    const unsigned char *bytes;
    DWORD size;
    wchar_t temp_root[MAX_PATH];
    wchar_t temp_directory[MAX_PATH];
    HANDLE file = INVALID_HANDLE_VALUE;
    DWORD offset = 0;
    int result = 0;

    resource = FindResourceW(g_module,
            MAKEINTRESOURCEW(VAPE_PAYLOAD_RESOURCE_ID),
            MAKEINTRESOURCEW(10)); /* RT_RCDATA */
    if (resource == NULL) {
        ssjj_log(L"embedded payload resource %d is missing (error %lu)",
                VAPE_PAYLOAD_RESOURCE_ID, GetLastError());
        return 0;
    }
    size = SizeofResource(g_module, resource);
    loaded_resource = LoadResource(g_module, resource);
    bytes = loaded_resource == NULL ? NULL
            : (const unsigned char *)LockResource(loaded_resource);
    if (bytes == NULL || size < 4 || bytes[0] != 'M' || bytes[1] != 'Z') {
        ssjj_log(L"embedded payload is invalid (size=%lu, magic=%02x%02x)",
                size, bytes == NULL ? 0 : bytes[0], bytes == NULL ? 0 : bytes[1]);
        return 0;
    }
    if (GetTempPathW((DWORD)(sizeof(temp_root) / sizeof(temp_root[0])),
            temp_root) == 0) {
        ssjj_log(L"GetTempPathW failed: %lu", GetLastError());
        return 0;
    }
    _snwprintf_s(temp_directory,
            sizeof(temp_directory) / sizeof(temp_directory[0]), _TRUNCATE,
            L"%lsSSJJVape", temp_root);
    if (!CreateDirectoryW(temp_directory, NULL)
            && GetLastError() != ERROR_ALREADY_EXISTS) {
        ssjj_log(L"CreateDirectoryW failed: %lu", GetLastError());
        return 0;
    }
    /* Randomize payload filename: avoids predictable vape-<pid>.dll that
     * GameGuard could fingerprint on disk. */
    {
        DWORD tick = GetTickCount();
        DWORD seed = tick ^ GetCurrentProcessId();
        if (seed == 0) seed = 0x9E3779B9u;
        /* xorshift32 for a cheap non-deterministic name suffix */
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        if (_snwprintf_s(payload_path, capacity, _TRUNCATE,
                L"%ls\\tmp-%08lx.dll", temp_directory, seed) < 0) {
            ssjj_log(L"temporary payload path is too long");
            return 0;
        }
    }
    file = CreateFileW(payload_path, GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_DELETE, NULL, CREATE_ALWAYS,
            FILE_ATTRIBUTE_TEMPORARY, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        ssjj_log(L"CreateFileW for payload failed: %lu", GetLastError());
        return 0;
    }
    while (offset < size) {
        DWORD written = 0;
        DWORD remaining = size - offset;
        if (!WriteFile(file, bytes + offset, remaining, &written, NULL)
                || written == 0) {
            ssjj_log(L"WriteFile for payload failed: %lu", GetLastError());
            goto cleanup;
        }
        offset += written;
    }
    if (!FlushFileBuffers(file)) {
        ssjj_log(L"FlushFileBuffers failed: %lu", GetLastError());
        goto cleanup;
    }
    result = 1;

cleanup:
    CloseHandle(file);
    if (!result) {
        DeleteFileW(payload_path);
    } else {
        ssjj_log(L"materialized payload: %ls (%lu bytes)", payload_path, size);
    }
    return result;
}

/* ------------------------------------------------------------------ */
/* Wide -> UTF-8 conversion (for mono_domain_assembly_open fallback)   */
/* ------------------------------------------------------------------ */
static int wide_to_utf8(const wchar_t *wide, char *utf8, size_t capacity) {
    int length = WideCharToMultiByte(CP_UTF8, 0, wide, -1,
            utf8, (int)capacity, NULL, NULL);
    return length > 0;
}

/* ------------------------------------------------------------------ */
/* Bootstrap worker thread                                             */
/* ------------------------------------------------------------------ */
static DWORD WINAPI bootstrap_thread(LPVOID parameter) {
    HMODULE worker_module = (HMODULE)parameter;
    ssjj_mono_api api;
    MonoDomain *domain = NULL;
    MonoThread *thread = NULL;
    MonoAssembly *assembly = NULL;
    MonoImage *image = NULL;
    MonoClass *klass = NULL;
    MonoMethod *method = NULL;
    MonoObject *exception = NULL;
    wchar_t payload_path[MAX_PATH] = L"";
    char payload_utf8[MAX_PATH];
    DWORD exit_code = 0;
    int attempt;
    int in_memory = 0;

    Sleep(150); /* let the loader settle after LoadLibraryW */

    /* 1. wait for Mono runtime -------------------------------------- */
    api.module = NULL;
    for (attempt = 0; attempt < 600; ++attempt) {
        if (ssjj_find_mono_module(NULL, 0) != NULL) break;
        Sleep(100);
    }
    if (ssjj_find_mono_module(NULL, 0) == NULL) {
        ssjj_log(L"mono runtime not loaded within 60 seconds");
        exit_code = 4;
        goto done;
    }
    if (!ssjj_mono_api_init(&api, ssjj_find_mono_module(NULL, 0))) {
        ssjj_log(L"mono exports could not be resolved from %ls",
                api.module_path);
        exit_code = 5;
        goto done;
    }
    ssjj_log(L"mono runtime resolved: %ls", api.module_path);

    /* 2. attach to root domain --------------------------------------- */
    domain = api.mono_get_root_domain();
    if (domain == NULL) {
        ssjj_log(L"mono_get_root_domain() returned NULL");
        exit_code = 6;
        goto done;
    }
    thread = api.mono_thread_attach(domain);
    if (thread == NULL) {
        ssjj_log(L"mono_thread_attach() failed");
        exit_code = 7;
        goto done;
    }
    ssjj_log(L"attached to mono root domain");

    /* 3. materialize embedded Vape.dll ------------------------------- */
    if (!materialize_embedded_payload(payload_path, MAX_PATH)) {
        exit_code = 8; /* resource missing/invalid -> 8 */
        /* distinguish write failure: materialize returns 0 for both;
         * re-check resource presence to split codes 8/9 */
        if (FindResourceW(g_module, MAKEINTRESOURCEW(VAPE_PAYLOAD_RESOURCE_ID),
                MAKEINTRESOURCEW(10)) != NULL) {
            exit_code = 9;
        }
        goto done;
    }

    /* 4. load assembly: in-memory first, on-disk fallback ------------ */
    if (api.mono_image_open_from_data_with_name != NULL
            && api.mono_assembly_load_from_full != NULL) {
        MonoImageOpenStatus status = 0; /* MONO_IMAGE_OK */
        HRSRC resource = FindResourceW(g_module,
                MAKEINTRESOURCEW(VAPE_PAYLOAD_RESOURCE_ID),
                MAKEINTRESOURCEW(10));
        HGLOBAL loaded = resource == NULL ? NULL : LoadResource(g_module, resource);
        const unsigned char *bytes = loaded == NULL ? NULL
                : (const unsigned char *)LockResource(loaded);
        DWORD size = resource == NULL ? 0 : SizeofResource(g_module, resource);
        MonoImage *candidate = NULL;

        if (bytes != NULL && size > 0) {
            candidate = api.mono_image_open_from_data_with_name(
                    (char *)bytes, (guint32)size, TRUE, &status, "Vape.dll");
            if (candidate != NULL) {
                assembly = api.mono_assembly_load_from_full(
                        candidate, "Vape.dll", &status, FALSE);
                if (assembly != NULL) {
                    image = api.mono_assembly_get_image(assembly);
                    in_memory = 1;
                }
            }
        }
    }
    if (assembly == NULL) {
        /* fallback: load the materialized file from disk */
        if (wide_to_utf8(payload_path, payload_utf8, sizeof(payload_utf8))) {
            assembly = api.mono_domain_assembly_open(domain, payload_utf8);
            if (assembly != NULL) {
                image = api.mono_assembly_get_image(assembly);
            }
        }
    }
    if (assembly == NULL || image == NULL) {
        ssjj_log(L"assembly load failed (in-memory=%d, on-disk fallback tried)",
                in_memory);
        exit_code = 10;
        goto done;
    }
    ssjj_log(L"assembly loaded (%s): %hs", in_memory ? "memory" : "disk",
            api.mono_image_get_name != NULL
                    ? api.mono_image_get_name(image) : "unknown");

    /* 5. entry: Vape.Loader.Load, fallback t.u.i --------------------- */
    klass = api.mono_class_from_name(image, "Vape", "Loader");
    method = klass == NULL ? NULL
            : api.mono_class_get_method_from_name(klass, "Load", 0);
    if (method == NULL) {
        MonoClass *short_class = api.mono_class_from_name(image, "t", "u");
        method = short_class == NULL ? NULL
                : api.mono_class_get_method_from_name(short_class, "i", 0);
        if (method != NULL) {
            ssjj_log(L"entry resolved via short name t.u.i");
        }
    } else {
        ssjj_log(L"entry resolved: Vape.Loader.Load");
    }
    if (method == NULL) {
        ssjj_log(L"entry method not found (Vape.Loader.Load / t.u.i)");
        exit_code = klass == NULL && api.mono_class_from_name(image, "t", "u") == NULL
                ? 11 : 12;
        goto done;
    }

    /* 6. invoke entry ------------------------------------------------ */
    exception = NULL;
    api.mono_runtime_invoke(method, NULL, NULL, &exception);
    if (exception != NULL) {
        ssjj_log(L"mono_runtime_invoke raised a managed exception");
        exit_code = 13;
        goto done;
    }

    /* 7. pin the native module and finish ---------------------------- */
    {
        HMODULE pinned = NULL;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
                        | GET_MODULE_HANDLE_EX_FLAG_PIN,
                (LPCWSTR)(const void *)&g_module, &pinned)) {
            ssjj_log(L"GetModuleHandleExW(PIN) failed: %lu", GetLastError());
        }
    }
    ssjj_log(L"Vape.Loader.Load completed; injection is active");
    exit_code = 0;

done:
    /* Remove the materialized payload from disk once bootstrap succeeded
     * (in-memory load means the file is no longer needed). Avoids leaving
     * a stale DLL in %TEMP% for GameGuard to scan. */
    if (exit_code == 0 && payload_path[0] != L'\0') {
        DeleteFileW(payload_path);
        ssjj_log(L"cleaned up payload file");
    }
    if (exit_code != 0) {
        ssjj_log(L"bootstrap failed with code %lu", exit_code);
    }
    if (exit_code != 0 && worker_module != NULL) {
        /* nothing managed registered yet; safe to unload the native DLL */
        FreeLibraryAndExitThread(worker_module, exit_code);
    }
    return exit_code;
}

/* ------------------------------------------------------------------ */
/* DllMain                                                             */
/* ------------------------------------------------------------------ */
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    HANDLE thread;
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
        thread = CreateThread(NULL, 0, bootstrap_thread, instance, 0, NULL);
        if (thread != NULL) {
            CloseHandle(thread);
        }
    }
    return TRUE;
}
