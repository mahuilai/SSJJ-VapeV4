/* ====================================================================
 * mapper.cpp - 内核驱动手动映射器实现（kdmapper 风格，x64）
 *
 * 通用 PE 加载：读文件 → 内核分配 → 拷贝 → 重定位 → 导入解析 →
 * 构造 DRIVER_OBJECT → 调用 DriverEntry。
 *
 * 依赖 provider（intel_driver）：R/W 原语 + 内核导出解析 +
 * CallKernelFunction（NtAddAtom 短时 hook 执行内核函数）。
 * ==================================================================== */
#include "general.h"
#include "mapper.h"
#include <ntstatus.h>

namespace ssjj_mapper {

/* ------------------------------------------------------------------ */
/* 可执行非分页池分配                                                   */
/*                                                                      */
/* 注意：ExAllocatePoolWithTag(NonPagedPool) 在 Win10 1607+ 返回 NX     */
/* 内存，执行驱动代码会触发 ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY      */
/* 蓝屏。驱动代码页必须用 ExAllocatePool2(POOL_FLAG_NON_PAGED_EXECUTE) */
/* （Win10 2004+，Win11 必有）分配，ExFreePool 释放。                  */
/*                                                                      */
/* 关键：POOL_FLAG_NON_PAGED(0x40) 是 NX 标志，与                     */
/* POOL_FLAG_NON_PAGED_EXECUTE(0x20) 互斥！只传 0x20 才能拿到可执行    */
/* 内存；两个一起传（0x60）内核按 NX 语义分配 -> 执行驱动代码即 0x50。 */
/* ------------------------------------------------------------------ */
#define POOL_FLAG_NON_PAGED_EXECUTE  0x0000000000000020ULL

static uint64_t AllocatePoolExecute(HANDLE device, uint64_t size)
{
    static uint64_t exAllocatePool2 = intel_driver::GetKernelModuleExport(
            device, intel_driver::ntoskrnlAddr, "ExAllocatePool2");
    if (!exAllocatePool2) {
        printf("[mapper] ExAllocatePool2 not available\n");
        return 0;
    }
    uint64_t pool = 0;
    if (!intel_driver::CallKernelFunction(device, &pool, exAllocatePool2,
            (uint32_t)POOL_FLAG_NON_PAGED_EXECUTE,
            (SIZE_T)size, (uint32_t)'ApcA') || !pool) {
        printf("[mapper] ExAllocatePool2(EXECUTE) failed\n");
        return 0;
    }
    return pool;
}

/* ------------------------------------------------------------------ */
/* 读取驱动文件到内存                                                   */
/* ------------------------------------------------------------------ */
static bool ReadPeFile(const std::wstring& path, std::vector<uint8_t>& out)
{
    std::ifstream f(path.c_str(), std::ios::binary);
    if (!f.is_open())
        return false;
    f.seekg(0, std::ios::end);
    std::streamoff sz = f.tellg();
    f.seekg(0, std::ios::beg);
    if (sz <= 0 || sz > 64 * 1024 * 1024)
        return false;
    out.resize(static_cast<size_t>(sz));
    f.read(reinterpret_cast<char*>(out.data()), sz);
    return !f.fail();
}

/* ------------------------------------------------------------------ */
/* 在内核中解析一个导入函数：优先 ntoskrnl，失败回退 hal               */
/* ------------------------------------------------------------------ */
static uint64_t ResolveImport(HANDLE device, const std::string& name)
{
    uint64_t addr = intel_driver::GetKernelModuleExport(
            device, intel_driver::ntoskrnlAddr, name);
    if (addr)
        return addr;

    /* 回退：hal.dll 导出（极少见，SSJJDrv 不应需要，兜底而已） */
    static uint64_t hal_base = 0;
    if (hal_base == 0)
        hal_base = utils::GetKernelModuleAddress("hal.dll");
    if (hal_base)
        addr = intel_driver::GetKernelModuleExport(device, hal_base, name);
    return addr;
}

/* ------------------------------------------------------------------ */
/* 主映射流程                                                          */
/* ------------------------------------------------------------------ */
bool MapDriver(HANDLE device, const std::wstring& driver_path,
               SSJJ_MAPPED_DRIVER& out)
{
    std::vector<uint8_t> pe;
    if (!ReadPeFile(driver_path, pe))
        return false;

    if (pe.size() < sizeof(IMAGE_DOS_HEADER))
        return false;
    PIMAGE_DOS_HEADER dos = reinterpret_cast<PIMAGE_DOS_HEADER>(pe.data());
    if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        return false;
    if (dos->e_lfanew <= 0 ||
        dos->e_lfanew + sizeof(IMAGE_NT_HEADERS64) > pe.size())
        return false;

    PIMAGE_NT_HEADERS64 nt = reinterpret_cast<PIMAGE_NT_HEADERS64>(
            pe.data() + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC)
        return false;

    const uint32_t image_size = nt->OptionalHeader.SizeOfImage;
    const uint64_t entry_point =
            nt->OptionalHeader.AddressOfEntryPoint;
    if (image_size == 0)
        return false;

    /* ---- 1. 分配内核内存（可执行非分页池） ---- */
    uint64_t mapped_base = AllocatePoolExecute(device, image_size);
    if (!mapped_base)
        return false;

    /* ---- 2. 拷贝 headers ---- */
    uint32_t header_size = nt->OptionalHeader.SizeOfHeaders;
    if (header_size > pe.size())
        header_size = static_cast<uint32_t>(pe.size());
    if (!intel_driver::WriteMemory(device, mapped_base, pe.data(),
                                   header_size)) {
        intel_driver::FreePool(device, mapped_base);
        return false;
    }

    /* ---- 3. 拷贝 sections ---- */
    PIMAGE_SECTION_HEADER sec = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++sec) {
        if (sec->SizeOfRawData == 0)
            continue;
        if (sec->PointerToRawData + sec->SizeOfRawData > pe.size())
            continue;
        if (!intel_driver::WriteMemory(device,
                mapped_base + sec->VirtualAddress,
                pe.data() + sec->PointerToRawData,
                sec->SizeOfRawData)) {
            intel_driver::FreePool(device, mapped_base);
            return false;
        }
    }

    /* ---- 4. 重定位 ---- */
    {
        uint64_t delta = mapped_base - nt->OptionalHeader.ImageBase;
        if (delta != 0) {
            IMAGE_DATA_DIRECTORY reloc = nt->OptionalHeader
                    .DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC];
            if (reloc.VirtualAddress != 0 && reloc.Size > 0) {
                uint64_t cur = mapped_base + reloc.VirtualAddress;
                uint64_t end = cur + reloc.Size;
                while (cur + sizeof(IMAGE_BASE_RELOCATION) <= end) {
                    IMAGE_BASE_RELOCATION block{};
                    if (!intel_driver::ReadMemory(device, cur, &block,
                                                  sizeof(block)))
                        break;
                    if (block.SizeOfBlock < sizeof(IMAGE_BASE_RELOCATION))
                        break;
                    int count = static_cast<int>(
                            block.SizeOfBlock - sizeof(IMAGE_BASE_RELOCATION))
                            / static_cast<int>(sizeof(WORD));
                    if (count <= 0) {
                        cur += block.SizeOfBlock;
                        continue;
                    }
                    std::vector<WORD> entries(static_cast<size_t>(count));
                    if (!intel_driver::ReadMemory(device,
                            cur + sizeof(IMAGE_BASE_RELOCATION),
                            entries.data(), entries.size() * sizeof(WORD)))
                        break;
                    for (int k = 0; k < count; ++k) {
                        WORD type = static_cast<WORD>(entries[k] >> 12);
                        WORD off  = static_cast<WORD>(entries[k] & 0xFFF);
                        if (type == IMAGE_REL_BASED_DIR64) {
                            uint64_t field = mapped_base +
                                    block.VirtualAddress + off;
                            /* 防御：拒绝越界字段（.reloc 损坏时防止
                             * 写坏 pool 外内核内存 → 蓝屏） */
                            if (field < mapped_base ||
                                field + sizeof(uint64_t) >
                                        mapped_base + image_size) {
                                continue;
                            }
                            uint64_t val = 0;
                            if (intel_driver::ReadMemory(device, field, &val,
                                                         sizeof(val))) {
                                val += delta;
                                intel_driver::WriteMemory(device, field, &val,
                                                          sizeof(val));
                            }
                        }
                    }
                    cur += block.SizeOfBlock;
                }
            }
        }
    }

    /* ---- 5. 导入解析（kernel import table） ---- */
    {
        IMAGE_DATA_DIRECTORY imp = nt->OptionalHeader
                .DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (imp.VirtualAddress != 0 && imp.Size > 0) {
            uint64_t cur = mapped_base + imp.VirtualAddress;
            uint64_t end = cur + imp.Size;
            while (cur + sizeof(IMAGE_IMPORT_DESCRIPTOR) <= end) {
                IMAGE_IMPORT_DESCRIPTOR desc{};
                if (!intel_driver::ReadMemory(device, cur, &desc,
                                              sizeof(desc)))
                    break;
                if (desc.OriginalFirstThunk == 0 && desc.FirstThunk == 0)
                    break; /* 描述符数组结束 */

                uint64_t thunk_rva = desc.OriginalFirstThunk
                        ? desc.OriginalFirstThunk : desc.FirstThunk;
                uint64_t iat_rva = desc.FirstThunk;
                uint64_t thunk = mapped_base + thunk_rva;
                uint64_t iat = mapped_base + iat_rva;

                for (;;) {
                    uint64_t thunk_val = 0;
                    if (!intel_driver::ReadMemory(device, thunk, &thunk_val,
                                                  sizeof(thunk_val)))
                        break;
                    if (thunk_val == 0)
                        break;

                    uint64_t func_addr = 0;
                    if (!(thunk_val & IMAGE_ORDINAL_FLAG64)) {
                        /* 按名称导入：hint(2) + name */
                        uint64_t name_addr = mapped_base +
                                (thunk_val & 0x7FFFFFFF) + 2;
                        char name[256] = {};
                        if (!intel_driver::ReadMemory(device, name_addr, name,
                                sizeof(name) - 1))
                            break;
                        func_addr = ResolveImport(device, std::string(name));
                    } else {
                        /* 按序号导入：无法按名解析，失败 */
                        func_addr = 0;
                    }

                    if (func_addr == 0) {
                        printf("[mapper] failed to resolve import at IAT "
                               "0x%llx\n", (unsigned long long)iat);
                        intel_driver::FreePool(device, mapped_base);
                        return false;
                    }

                    intel_driver::WriteMemory(device, iat, &func_addr,
                                              sizeof(func_addr));
                    thunk += sizeof(uint64_t);
                    iat += sizeof(uint64_t);
                }
                cur += sizeof(IMAGE_IMPORT_DESCRIPTOR);
            }
        }
    }

    /* ---- 6. 构造 PDRIVER_OBJECT + DRIVER_EXTENSION ---- */
    uint64_t drv_obj = intel_driver::AllocatePool(device,
            nt::POOL_TYPE::NonPagedPool,
            SSJJ_DRIVER_OBJECT_SIZE + SSJJ_DRIVER_EXTENSION_SIZE);
    if (!drv_obj) {
        intel_driver::FreePool(device, mapped_base);
        return false;
    }

    SSJJ_DRIVER_OBJECT dobj{};
    dobj.Type = IO_TYPE_DRIVER;
    dobj.Size = SSJJ_DRIVER_OBJECT_SIZE;
    dobj.DriverStart = reinterpret_cast<PVOID>(mapped_base);
    dobj.DriverSize = image_size;
    dobj.DriverExtension = reinterpret_cast<PVOID>(
            drv_obj + SSJJ_DRIVER_OBJECT_SIZE);
    dobj.DriverInit = reinterpret_cast<PVOID>(mapped_base + entry_point);
    dobj.DriverUnload = nullptr; /* 由 DriverEntry 自行设置 */

    SSJJ_DRIVER_EXTENSION dext{};
    dext.DriverObject = reinterpret_cast<PVOID>(drv_obj);
    dext.Count = 1;

    if (!intel_driver::WriteMemory(device, drv_obj, &dobj, sizeof(dobj)) ||
        !intel_driver::WriteMemory(device,
                drv_obj + SSJJ_DRIVER_OBJECT_SIZE, &dext, sizeof(dext))) {
        intel_driver::FreePool(device, drv_obj);
        intel_driver::FreePool(device, mapped_base);
        return false;
    }

    /* ---- 7. 构造 registry path 参数（SSJJDrv 不使用，留空） ---- */
    uint64_t reg_path = intel_driver::AllocatePool(device,
            nt::POOL_TYPE::NonPagedPool, sizeof(UNICODE_STRING));
    if (!reg_path) {
        intel_driver::FreePool(device, drv_obj);
        intel_driver::FreePool(device, mapped_base);
        return false;
    }
    UNICODE_STRING us{};
    intel_driver::WriteMemory(device, reg_path, &us, sizeof(us));

    /* ---- 8. 调用 DriverEntry(driver_object, registry_path) ---- */
    NTSTATUS entry_status = STATUS_UNSUCCESSFUL;
    bool ok = intel_driver::CallKernelFunction(device, &entry_status,
            mapped_base + entry_point,
            reinterpret_cast<PVOID>(drv_obj),
            reinterpret_cast<PVOID>(reg_path));

    intel_driver::FreePool(device, reg_path);

    if (!ok || !NT_SUCCESS(entry_status)) {
        printf("[mapper] DriverEntry failed: ok=%d status=0x%08X\n",
               ok ? 1 : 0, static_cast<unsigned>(entry_status));
        intel_driver::FreePool(device, drv_obj);
        intel_driver::FreePool(device, mapped_base);
        return false;
    }

    /* 读回 DriverUnload（DriverEntry 已设置） */
    SSJJ_DRIVER_OBJECT final_obj{};
    intel_driver::ReadMemory(device, drv_obj, &final_obj, sizeof(final_obj));

    out.base = mapped_base;
    out.driver_object = drv_obj;
    out.entry_point = mapped_base + entry_point;
    out.unload_routine = reinterpret_cast<uint64_t>(final_obj.DriverUnload);
    out.image_size = image_size;

    printf("[mapper] mapped %ls -> base=0x%llx entry=0x%llx "
           "unload=0x%llx\n",
           driver_path.c_str(),
           (unsigned long long)out.base,
           (unsigned long long)out.entry_point,
           (unsigned long long)out.unload_routine);
    return true;
}

/* ------------------------------------------------------------------ */
/* 卸载：DriverUnload（注销保护+删设备）→ DRIVER_OBJECT → 代码页       */
/* ------------------------------------------------------------------ */
bool UnmapDriver(HANDLE device, const SSJJ_MAPPED_DRIVER& drv)
{
    if (!device || !drv.base || !drv.driver_object)
        return false;

    /* 读回最新 DriverUnload（可能被 DriverEntry 覆盖过） */
    SSJJ_DRIVER_OBJECT obj{};
    intel_driver::ReadMemory(device, drv.driver_object, &obj, sizeof(obj));
    uint64_t unload = reinterpret_cast<uint64_t>(obj.DriverUnload);

    if (unload) {
        /* SSJJDrvUnload(DriverObject)：注销 Ob 回调 + 删设备 + 删符号链接 */
        intel_driver::CallKernelFunction<void>(device, nullptr, unload,
                reinterpret_cast<PVOID>(drv.driver_object));
    }

    intel_driver::FreePool(device, drv.driver_object);
    intel_driver::FreePool(device, drv.base);
    return true;
}

} /* namespace ssjj_mapper */
