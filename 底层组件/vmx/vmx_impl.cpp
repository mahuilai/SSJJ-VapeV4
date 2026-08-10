/*
 * vmx_impl.cpp
 * VMX 内核通信接口实现 — 无授权验证版本
 *
 * 基于 VMXUsrCode（过检测项目），去除在线授权验证 + 服务器下载逻辑，
 * 改为从 Vape.exe 嵌入资源加载 VMShellcode 和 VMLiteMapper agent 驱动。
 *
 * 资源 ID 约定（在 injector_payload.rc.in 中定义）：
 *   RCDATA 421 = Vape.dll（原有）
 *   RCDATA 422 = SSJJNative.dll（原有，加密）
 *   RCDATA 423 = VMShellcode.bin（新增，内核 shellcode 二进制）
 *   RCDATA 424 = VMLiteMapper.sys（新增，agent 驱动二进制）
 */

#include <ws2tcpip.h>
#include <Windows.h>
#include <dbghelp.h>
#include <winhttp.h>
#include <string>
#include <fstream>
#include <sstream>
#include <iostream>
#include <xmmintrin.h>
#include <shlwapi.h>
#include <dwmapi.h>
#include <tlhelp32.h>

/* VMXUsrCode 头文件（按依赖顺序） */
#include "usrcode/superdef.h"
#include "usrcode/lazy_importer.hpp"
#include "usrcode/xorstr.hpp"
#include "usrcode/utils.hpp"
#include "usrcode/drvmanager.hpp"
#include "usrcode/shell.hpp"

/* 公共接口 */
#include "vmx_interface.h"

/* -----------------------------------------------------------------------
 * 嵌入资源 ID
 * ----------------------------------------------------------------------- */
#define VMX_SHELLCODE_RESOURCE_ID  423
#define VMX_MAPPER_RESOURCE_ID     424

/* -----------------------------------------------------------------------
 * 内部静态状态
 * ----------------------------------------------------------------------- */
static _Globals* s_Globals  = nullptr;
static bool      s_installed = false;

/* -----------------------------------------------------------------------
 * 辅助：从 Vape.exe 资源中读取二进制数据
 * 返回 LocalAlloc'd buffer（调用方负责 LocalFree），size 写入 out_size
 * ----------------------------------------------------------------------- */
static unsigned char* load_resource(int resource_id, int* out_size)
{
    HMODULE self = GetModuleHandleW(NULL);
    HRSRC   hres = FindResourceW(self, MAKEINTRESOURCEW(resource_id), RT_RCDATA);
    if (!hres) return nullptr;
    HGLOBAL hgl  = LoadResource(self, hres);
    if (!hgl) return nullptr;
    DWORD   sz   = SizeofResource(self, hres);
    void*   ptr  = LockResource(hgl);
    if (!ptr || sz == 0) return nullptr;

    unsigned char* buf = (unsigned char*)LocalAlloc(LPTR, sz);
    if (!buf) return nullptr;
    memcpy(buf, ptr, sz);
    *out_size = (int)sz;
    return buf;
}

/* -----------------------------------------------------------------------
 * 辅助：从嵌入资源写驱动文件到磁盘
 * ----------------------------------------------------------------------- */
static bool write_resource_to_file(int resource_id, const char* path)
{
    int size = 0;
    unsigned char* buf = load_resource(resource_id, &size);
    if (!buf) return false;
    bool ok = WriteFileFromBuffer(path, (char*)buf, size);
    LocalFree(buf);
    return ok;
}

/* -----------------------------------------------------------------------
 * 辅助：初始化 Globals 结构（不做任何网络验证）
 * ----------------------------------------------------------------------- */
static _Globals* alloc_globals_no_auth()
{
    _Globals* g = (_Globals*)LocalAlloc(LPTR, sizeof(_Globals));
    if (!g) return nullptr;
    /* source 标记为已初始化（原由 Cracker_PreVerifySecretKey 设置） */
    g->source = 0x91A;
    /* 获取 ADVAPI32!RegSetValueExA 地址（VMShell 内核通信入口） */
    g->VM.RegSetValueExA = (__int64)GetProcAddress(
        GetModuleHandleA("ADVAPI32.dll"), "RegSetValueExA");
    return g;
}

/* -----------------------------------------------------------------------
 * vmx_install()
 *
 * 精确还原 Cracker_Install() 逻辑，区别：
 *   - 不做授权验证
 *   - VMShellcode 二进制从资源 RCDATA 423 加载（原本从服务器 p1v3 下载）
 *   - VMLiteMapper agent 驱动从资源 RCDATA 424 加载（原本从服务器 p1v4 下载）
 * ----------------------------------------------------------------------- */
int vmx_install(
    const unsigned char* /*shellcode_data  — 已废弃，从资源加载*/,
    int                  /*shellcode_size  — 已废弃*/,
    const unsigned char* /*shellcode_mouse — 已废弃*/,
    int                  /*shellcode_mouse_size — 已废弃*/)
{
    if (s_installed && s_Globals) return VMX_OK;

    /* 加载必要 DLL */
    LoadAndImportDlls();

    if (!IsRunningAsAdmin())    return VMX_ERR_NOT_ADMIN;
    if (!ChangePrivilege(true)) return VMX_ERR_NO_PRIVILEGE;

    /* 初始化 Globals */
    s_Globals = alloc_globals_no_auth();
    if (!s_Globals) { ChangePrivilege(false); return VMX_ERR_INSTALL_FAILED; }

    /* 检查 shellcode 是否已在内核中活跃（幂等） */
    ULONG64 VerifyCode = VMShell::GetVerifyCode(s_Globals);
    if (VerifyCode) {
        s_Globals->VM.active = true;
        s_installed = true;
        VMShell::SetProcess(s_Globals, 0, false);
        return VMX_OK;
    }

    /* ----------------------------------------------------------------
     * 第一步：从磁盘读取 ntoskrnl.exe / win32kbase.sys / mouclass.sys
     * --------------------------------------------------------------- */
    PVOID FsRedirection = 0;
    LI_FN(Wow64DisableWow64FsRedirection)(&FsRedirection);

    char* System32Path = GetSystem32Path();

    LocalPtr<char*> filepath_buffer(MAX_PATH);

    /* ntoskrnl.exe */
    LI_FN(sprintf)(&filepath_buffer, xorstr_("%s\\ntoskrnl.exe"), System32Path);
    char* ntoskrnl_buf = 0; int ntoskrnl_sz = 0;
    if (!ReadFileToBuffer(&filepath_buffer, &ntoskrnl_buf, &ntoskrnl_sz))
        goto fail_early;

    /* win32kbase.sys */
    __stosb((PBYTE)&filepath_buffer, 0, MAX_PATH);
    LI_FN(sprintf)(&filepath_buffer, xorstr_("%s\\win32kbase.sys"), System32Path);
    char* win32kbase_buf = 0; int win32kbase_sz = 0;
    if (!ReadFileToBuffer(&filepath_buffer, &win32kbase_buf, &win32kbase_sz)) {
        SecureFree(ntoskrnl_buf);
        goto fail_early;
    }

    /* mouclass.sys（允许失败，鼠标 hook 为可选） */
    __stosb((PBYTE)&filepath_buffer, 0, MAX_PATH);
    LI_FN(sprintf)(&filepath_buffer, xorstr_("%s\\drivers\\mouclass.sys"), System32Path);
    char* mouclass_buf = 0; int mouclass_sz = 0;
    ReadFileToBuffer(&filepath_buffer, &mouclass_buf, &mouclass_sz);

    {
        /* ------------------------------------------------------------
         * 第二步：扫描 ntoskrnl，定位 VerifierRtlHashUnicodeString
         * ----------------------------------------------------------- */
        __int64 ntoskrnl_imagebase = Get64PEImageBase(ntoskrnl_buf);

        int MiGetPageTablePfnBuddyRaw = FileAddressToRVA(ntoskrnl_buf,
            FindPattern(ntoskrnl_buf, ntoskrnl_sz,
                xorstr_("\\x00\\x00\\xFF\\x03\\x48\\xD1\\xEA")));

        if (MiGetPageTablePfnBuddyRaw != 0) {
            char* tmp = (char*)SecureAlloc(100);
            if (tmp) {
                int offset = MiGetPageTablePfnBuddyRaw - 100;
                if (offset < 0) offset = 0;
                memcpy(tmp, ntoskrnl_buf + offset, 100);
                bool found = false;
                for (int i = 99; i >= 0; i--) {
                    if ((unsigned char)tmp[i] == 0x48 && (unsigned char)tmp[i+1] == 0x8B) {
                        MiGetPageTablePfnBuddyRaw = offset + i;
                        found = true;
                        break;
                    }
                }
                SecureFree(tmp);
                if (!found) MiGetPageTablePfnBuddyRaw = 0;
            } else {
                MiGetPageTablePfnBuddyRaw = 0;
            }
        }

        int aFA = FindPattern(ntoskrnl_buf, ntoskrnl_sz,
            xorstr_("00 00 52 74 6C 48 61 73 68 55 6E 69 63 6F 64 65 53 74 72 69 6E 67 00 00")) + 2;
        if (!aFA) {
            SecureFree(ntoskrnl_buf); SecureFree(win32kbase_buf);
            if (mouclass_buf) SecureFree(mouclass_buf);
            goto fail_early;
        }
        int aRVA    = FileAddressToRVA(ntoskrnl_buf, aFA);
        int aRefRVA = FindULONG64(ntoskrnl_buf, ntoskrnl_sz, ntoskrnl_imagebase + aRVA);
        int VerifierRtlHashUnicodeString_RVA =
            (int)(*(ULONG64*)(ntoskrnl_buf + aRefRVA + 8) - ntoskrnl_imagebase);

        /* ------------------------------------------------------------
         * 第三步：鼠标 hook（可选）
         * ----------------------------------------------------------- */
        char* MouseBin = nullptr; int MouseBin_sz = 0;
        int   SubRsp70RVA = 0;

        if (mouclass_buf) {
            int IoCode = FindPattern(mouclass_buf, mouclass_sz, xorstr_("B9 03 02 0F 00"));
            if (IoCode) {
                int leaFA = IoCode + 5;
                int dataptr = *(int*)(mouclass_buf + leaFA + 3);
                if (dataptr) {
                    int leaRVA = FileAddressToRVA(mouclass_buf, leaFA);
                    if (leaRVA) {
                        int cbRVA = leaRVA + 7 + dataptr;
                        if (cbRVA) {
                            int cbFA = RVAToFileAddress(mouclass_buf, cbRVA);
                            if (cbFA) {
                                int sub70 = FindPattern(
                                    mouclass_buf + cbFA, 100, xorstr_("48 83 EC 70"));
                                if (sub70) {
                                    int sub70FA = cbFA + sub70;
                                    SubRsp70RVA = FileAddressToRVA(mouclass_buf, sub70FA);
                                    int hdrLen = sub70FA - cbFA;
                                    MouseBin_sz = hdrLen + 14;
                                    MouseBin = (char*)SecureAlloc(MouseBin_sz);
                                    __movsb((PBYTE)MouseBin,
                                        (PBYTE)(mouclass_buf + cbFA), hdrLen);
                                    __movsb((PBYTE)(MouseBin + hdrLen),
                                        (PBYTE)xorstr_("\\xFF\\x25\\x00\\x00\\x00\\x00\\xCC\\xCC\\xCC\\xCC\\xCC\\xCC\\xCC\\xCC"),
                                        14);
                                }
                            }
                        }
                    }
                }
            }
            SecureFree(mouclass_buf);
        }

        /* ------------------------------------------------------------
         * 第四步：构建 ImportTable（PTABLE / TABLE 结构）
         * ----------------------------------------------------------- */
        PTABLE shellcode_table = (PTABLE)SecureAlloc(sizeof(TABLE));

        shellcode_table->MmIsAddressValid              = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmIsAddressValid"));
        shellcode_table->PsGetProcessWow64Process      = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("PsGetProcessWow64Process"));
        shellcode_table->PsGetProcessPeb               = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("PsGetProcessPeb"));
        shellcode_table->MmGetPhysicalMemoryRangesEx   = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmGetPhysicalMemoryRangesEx"));
        shellcode_table->ExAllocatePoolWithTag         = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ExAllocatePoolWithTag"));
        shellcode_table->ExFreePoolWithTag             = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ExFreePoolWithTag"));
        shellcode_table->IoGetCurrentProcess           = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("IoGetCurrentProcess"));
        shellcode_table->MmCopyMemory                  = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmCopyMemory"));
        shellcode_table->MmMapIoSpaceEx                = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmMapIoSpaceEx"));
        shellcode_table->MmUnmapIoSpace                = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmUnmapIoSpace"));
        shellcode_table->MmCopyVirtualMemory           = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmCopyVirtualMemory"));
        shellcode_table->PsGetProcessSectionBaseAddress= GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("PsGetProcessSectionBaseAddress"));
        shellcode_table->PsGetProcessId                = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("PsGetProcessId"));
        shellcode_table->RtlHashUnicodeString          = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("RtlHashUnicodeString"));
        shellcode_table->MiGetPageTablePfnBuddyRaw     = MiGetPageTablePfnBuddyRaw;
        shellcode_table->KeAcquireSpinLockAtDpcLevel   = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("KeAcquireSpinLockAtDpcLevel"));
        shellcode_table->KeReleaseSpinLockFromDpcLevel = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("KeReleaseSpinLockFromDpcLevel"));
        shellcode_table->IofCompleteRequest            = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("IofCompleteRequest"));
        shellcode_table->IoReleaseRemoveLockEx         = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("IoReleaseRemoveLockEx"));
        shellcode_table->ValidateHwnd                  = GetProcAddressCustomFromFile(win32kbase_buf, xorstr_("ValidateHwnd"));
        shellcode_table->KeInvalidateRangeAllCaches    = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("KeInvalidateRangeAllCaches"));
        shellcode_table->memmove                       = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("memmove"));
        shellcode_table->IoCreateFileEx                = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("IoCreateFileEx"));
        shellcode_table->ObReferenceObjectByHandleWithTag = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ObReferenceObjectByHandleWithTag"));
        shellcode_table->ObfDereferenceObject          = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ObfDereferenceObject"));
        shellcode_table->ObCloseHandle                 = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ObCloseHandle"));
        shellcode_table->ZwDeleteFile                  = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ZwDeleteFile"));
        shellcode_table->MmFlushImageSection           = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("MmFlushImageSection"));
        shellcode_table->IoFileObjectType              = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("IoFileObjectType"));
        shellcode_table->KeGetCurrentProcessorNumberEx = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("KeGetCurrentProcessorNumberEx"));
        shellcode_table->PsLookupProcessByProcessId    = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("PsLookupProcessByProcessId"));
        shellcode_table->RtlGetVersion                 = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("RtlGetVersion"));
        shellcode_table->ObOpenObjectByPointer         = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ObOpenObjectByPointer"));
        shellcode_table->ZwAllocateVirtualMemory       = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ZwAllocateVirtualMemory"));
        shellcode_table->ZwProtectVirtualMemory        = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ZwProtectVirtualMemory"));
        shellcode_table->ZwFreeVirtualMemory           = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ZwFreeVirtualMemory"));
        shellcode_table->ZwClose                       = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ZwClose"));
        shellcode_table->PsProcessType                 = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("PsProcessType"));
        shellcode_table->RtlCreateUserThread           = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("RtlCreateUserThread"));
        shellcode_table->ZwWaitForSingleObject         = GetProcAddressCustomFromFile(ntoskrnl_buf, xorstr_("ZwWaitForSingleObject"));

        SecureFree(ntoskrnl_buf);
        SecureFree(win32kbase_buf);

        /* ------------------------------------------------------------
         * 第五步：从嵌入资源加载 VMShellcode 二进制
         *         （原版：从服务器 p1v3 TCP 下载，VMXFILEX 头部 + shellcode）
         * ----------------------------------------------------------- */
        int shellcode_bin_sz = 0;
        unsigned char* shellcode_bin = load_resource(VMX_SHELLCODE_RESOURCE_ID, &shellcode_bin_sz);
        if (!shellcode_bin || shellcode_bin_sz == 0) {
            SecureFree(shellcode_table);
            if (MouseBin) SecureFree(MouseBin);
            goto fail_early;
        }

        /* ------------------------------------------------------------
         * 第六步：将数据写入注册表
         * ----------------------------------------------------------- */
        WriteVolatileBinaryToRegistry(
            xorstr_("SOFTWARE\\vmm_"), xorstr_("ImportTable"),
            (unsigned char*)shellcode_table, sizeof(TABLE));
        WriteVolatileBinaryToRegistry(
            xorstr_("SOFTWARE\\vmm_"), xorstr_("ShellcodeBinary"),
            shellcode_bin, shellcode_bin_sz);
        WriteVolatileQWORDToRegistry(
            xorstr_("SOFTWARE\\vmm_"), xorstr_("MousePtr"),
            (DWORD64)SubRsp70RVA);
        WriteVolatileBinaryToRegistry(
            xorstr_("SOFTWARE\\vmm_"), xorstr_("MouseBinary"),
            (unsigned char*)MouseBin, MouseBin ? MouseBin_sz : 0);
        WriteVolatileQWORDToRegistry(
            xorstr_("SOFTWARE\\vmm_"), xorstr_("FunctionPtr"),
            (DWORD64)VerifierRtlHashUnicodeString_RVA);

        SecureFree(shellcode_table);
        LocalFree(shellcode_bin);
        if (MouseBin) SecureFree(MouseBin);

        /* ------------------------------------------------------------
         * 第七步：从嵌入资源写出 VMLiteMapper agent 驱动并加载
         *         （原版：从服务器 p1v4/p1v6/p1v7 TCP 下载，VMXFILEX 头部 + sys）
         * ----------------------------------------------------------- */
        char* AgentDriverName = (char*)SecureAlloc(MAX_PATH);
        char* RandomStr = GenerateRandomString(8);
        LI_FN(lstrcatA)(AgentDriverName, RandomStr);
        SecureFree(RandomStr);

        char* AgentDriverPath = (char*)SecureAlloc(MAX_PATH);
        LI_FN(sprintf)(AgentDriverPath, xorstr_("%s\\drivers\\%s.sys"),
            System32Path, AgentDriverName);
        SecureFree(System32Path);

        bool wrote = write_resource_to_file(VMX_MAPPER_RESOURCE_ID, AgentDriverPath);
        if (!wrote) {
            SecureFree(AgentDriverName);
            SecureFree(AgentDriverPath);
            LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
            LocalFree(s_Globals); s_Globals = nullptr;
            ChangePrivilege(false);
            return VMX_ERR_INSTALL_FAILED;
        }

        if (!CreateServiceEx(AgentDriverPath, AgentDriverName)) {
            SecureFree(AgentDriverName);
            LI_FN(DeleteFileA)(AgentDriverPath);
            SecureFree(AgentDriverPath);
            LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
            LocalFree(s_Globals); s_Globals = nullptr;
            ChangePrivilege(false);
            return VMX_ERR_INSTALL_FAILED;
        }

        NTSTATUS agentStatus = StartServiceEx(AgentDriverName);

        /* VMLiteMapper agent 加载后立即返回 STATUS_UNSUCCESSFUL (0xC0000001)，正常 */
        if (agentStatus == 0xC00000A3) {
            /* STATUS_NO_SUCH_DEVICE: 签名验证失败 */
            DeleteServiceEx(AgentDriverName);
            SecureFree(AgentDriverName);
            LI_FN(DeleteFileA)(AgentDriverPath);
            SecureFree(AgentDriverPath);
            LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
            LocalFree(s_Globals); s_Globals = nullptr;
            ChangePrivilege(false);
            return VMX_ERR_INSTALL_FAILED;
        }
        if (agentStatus != 0xC0000001) {
            DeleteServiceEx(AgentDriverName);
            SecureFree(AgentDriverName);
            LI_FN(DeleteFileA)(AgentDriverPath);
            SecureFree(AgentDriverPath);
            LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
            LocalFree(s_Globals); s_Globals = nullptr;
            ChangePrivilege(false);
            return VMX_ERR_INSTALL_FAILED;
        }

        DeleteServiceEx(AgentDriverName);
        SecureFree(AgentDriverName);
        LI_FN(DeleteFileA)(AgentDriverPath);
        SecureFree(AgentDriverPath);
        LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);

        /* ------------------------------------------------------------
         * 第八步：验证 shellcode 活跃，设置心跳
         * ----------------------------------------------------------- */
        bool shellStatus = true;
        do {
            VerifyCode = VMShell::GetVerifyCode(s_Globals);
            shellStatus = shellStatus && VerifyCode;
            /* 不做心跳服务器验证，只做本地验证 */
            if (!shellStatus) break;
        } while (false);

        if (!shellStatus) {
            LocalFree(s_Globals); s_Globals = nullptr;
            ChangePrivilege(false);
            return VMX_ERR_INSTALL_FAILED;
        }

        s_Globals->VM.active  = true;
        s_Globals->VM.checkcode = VerifyCode;
        s_installed = true;
        VMShell::SetProcess(s_Globals, 0, false);
        return VMX_OK;
    }

fail_early:
    SecureFree(System32Path);
    LI_FN(Wow64RevertWow64FsRedirection)(FsRedirection);
    if (s_Globals) { LocalFree(s_Globals); s_Globals = nullptr; }
    ChangePrivilege(false);
    return VMX_ERR_INSTALL_FAILED;
}

/* -----------------------------------------------------------------------
 * vmx_set_process()
 * ----------------------------------------------------------------------- */
int vmx_set_process(unsigned long process_id)
{
    if (!s_installed || !s_Globals) return VMX_ERR_INSTALL_FAILED;
    bool ok = VMShell::SetProcess(s_Globals, process_id, false);
    return ok ? VMX_OK : VMX_ERR_SET_PROCESS_FAILED;
}

/* -----------------------------------------------------------------------
 * vmx_read_mem()
 * ----------------------------------------------------------------------- */
int vmx_read_mem(unsigned long long address, void *buffer, long size)
{
    if (!s_installed || !s_Globals) return VMX_ERR_INSTALL_FAILED;
    bool ok = VMShell::OperateVirtualMemory(
        s_Globals, (__int64)address, buffer, size, MemOperateType::Read);
    return ok ? VMX_OK : VMX_ERR_MEM_READ_FAILED;
}

/* -----------------------------------------------------------------------
 * vmx_write_mem()
 * ----------------------------------------------------------------------- */
int vmx_write_mem(unsigned long long address, const void *buffer, long size)
{
    if (!s_installed || !s_Globals) return VMX_ERR_INSTALL_FAILED;
    bool ok = VMShell::OperateVirtualMemory(
        s_Globals, (__int64)address, const_cast<void*>(buffer),
        size, MemOperateType::Write);
    return ok ? VMX_OK : VMX_ERR_MEM_WRITE_FAILED;
}

/* -----------------------------------------------------------------------
 * vmx_create_remote_thread()
 * ----------------------------------------------------------------------- */
int vmx_create_remote_thread(unsigned long long start_address,
                             unsigned long long start_parameter)
{
    if (!s_installed || !s_Globals) return VMX_ERR_INSTALL_FAILED;
    bool ok = VMShell::CreateRemoteThread(s_Globals, start_address, start_parameter);
    return ok ? VMX_OK : VMX_ERR_THREAD_FAILED;
}

/* -----------------------------------------------------------------------
 * vmx_get_module_base()
 * ----------------------------------------------------------------------- */
unsigned long long vmx_get_module_base(const char *module_name)
{
    if (!s_installed || !s_Globals) return 0;
    __int64 peb = VMShell::GetProcessEnvironmentBlock(s_Globals);
    if (!peb) return 0;
    return (unsigned long long)GetModuleAddress(
        s_Globals, module_name, peb,
        (__int64)VMShell::OperateVirtualMemory, true);
}

/* -----------------------------------------------------------------------
 * vmx_alloc_mem()
 * ----------------------------------------------------------------------- */
unsigned long long vmx_alloc_mem(unsigned long long size, unsigned long protect)
{
    if (!s_installed || !s_Globals) return 0;
    ULONG64 base = 0;
    ULONG64 sz   = (ULONG64)size;
    bool ok = VMShell::AllocateMemory(s_Globals, &base, &sz,
        MEM_COMMIT | MEM_RESERVE, (ULONG)protect);
    return ok ? (unsigned long long)base : 0;
}

/* -----------------------------------------------------------------------
 * vmx_is_installed()
 * ----------------------------------------------------------------------- */
int vmx_is_installed(void)
{
    return (s_installed && s_Globals) ? 1 : 0;
}

/* -----------------------------------------------------------------------
 * vmx_shutdown()
 * ----------------------------------------------------------------------- */
void vmx_shutdown(void)
{
    if (s_Globals) {
        VMShell::SetProcess(s_Globals, 0, false);
        __stosb((PBYTE)s_Globals, 0, sizeof(_Globals));
        LocalFree(s_Globals);
        s_Globals = nullptr;
    }
    s_installed = false;
    ChangePrivilege(false);
}
