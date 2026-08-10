/*
 * vmx_interface.h
 * VMX 内核通信接口 — 无授权验证版本
 *
 * 对外暴露纯 C 接口，供 injector.c 调用。
 * 内部实现在 vmx_impl.cpp（C++ / VMXUsrCode）。
 */
#pragma once

#ifdef __cplusplus
extern "C" {
#endif

/* -----------------------------------------------------------------------
 * 返回码（与 StatusCode 枚举对应，但暴露为 int）
 * ----------------------------------------------------------------------- */
#define VMX_OK                          0
#define VMX_ERR_NOT_ADMIN               1
#define VMX_ERR_NO_PRIVILEGE            2
#define VMX_ERR_INSTALL_FAILED          3
#define VMX_ERR_SET_PROCESS_FAILED      4
#define VMX_ERR_MEM_READ_FAILED         5
#define VMX_ERR_MEM_WRITE_FAILED        6
#define VMX_ERR_THREAD_FAILED           7

/* -----------------------------------------------------------------------
 * vmx_install()
 *   映射 VMShellcode 进内核（通过 VMLiteMapper）。
 *   必须以管理员身份且已拥有 SeLoadDriverPrivilege 运行。
 *
 *   shellcode_data : VMShellcode.sys/.bin 的内存指针
 *   shellcode_size : 大小（字节）
 *   shellcode_mouse: 鼠标 shellcode 指针（可为 NULL）
 *   shellcode_mouse_size: 鼠标 shellcode 大小
 *
 *   成功返回 VMX_OK，失败返回对应错误码。
 *   成功后 g_vmx_key 即可用。
 * ----------------------------------------------------------------------- */
int vmx_install(
    const unsigned char *shellcode_data,
    int                  shellcode_size,
    const unsigned char *shellcode_mouse,
    int                  shellcode_mouse_size
);

/* -----------------------------------------------------------------------
 * vmx_set_process(process_id)
 *   设置目标进程，内核 shellcode 将对该进程进行内存操作。
 *   0 = 清除目标进程。
 * ----------------------------------------------------------------------- */
int vmx_set_process(unsigned long process_id);

/* -----------------------------------------------------------------------
 * vmx_read_mem / vmx_write_mem
 *   对目标进程内存的物理内存级读写（绕过所有用户态 hook）。
 *
 *   address: 目标进程虚拟地址
 *   buffer : 本地缓冲区
 *   size   : 字节数
 * ----------------------------------------------------------------------- */
int vmx_read_mem(unsigned long long address, void *buffer, long size);
int vmx_write_mem(unsigned long long address, const void *buffer, long size);

/* -----------------------------------------------------------------------
 * vmx_create_remote_thread(start_address, start_parameter)
 *   在目标进程中创建线程（内核级，绕过 GG 钩子）。
 *   通常用于调用 LoadLibraryW 完成 DLL 注入。
 * ----------------------------------------------------------------------- */
int vmx_create_remote_thread(unsigned long long start_address,
                             unsigned long long start_parameter);

/* -----------------------------------------------------------------------
 * vmx_get_module_base(module_name_utf8)
 *   获取目标进程中指定模块的基地址。
 * ----------------------------------------------------------------------- */
unsigned long long vmx_get_module_base(const char *module_name);

/* -----------------------------------------------------------------------
 * vmx_alloc_mem(size, protect)
 *   在目标进程中内核级分配内存（MEM_COMMIT|MEM_RESERVE）。
 *   protect: 页面保护属性（PAGE_READWRITE = 0x04, PAGE_EXECUTE_READ = 0x20 等）
 *   成功返回目标进程中分配的地址，失败返回 0。
 * ----------------------------------------------------------------------- */
unsigned long long vmx_alloc_mem(unsigned long long size, unsigned long protect);

/* -----------------------------------------------------------------------
 * vmx_is_installed()
 *   返回非 0 表示 VMX 已安装成功且 shellcode 在内核中活跃。
 * ----------------------------------------------------------------------- */
int vmx_is_installed(void);

/* -----------------------------------------------------------------------
 * vmx_shutdown()
 *   清理资源（调用后不可再使用 vmx_* API）。
 * ----------------------------------------------------------------------- */
void vmx_shutdown(void);

#ifdef __cplusplus
} /* extern "C" */
#endif
