/* ====================================================================
 * mapper_selftest.cpp - 映射链路自测工具（独立于 Vape.exe）
 *
 * 用途：不注入游戏、不触发 GameGuard，纯验证：
 *   1. iqvw64e.sys provider 加载（R/W 原语）
 *   2. SSJJDrv.sys 手动映射 + DriverEntry（创建设备）
 *   3. \\.\SSJJDrv 设备可打开
 *   4. 卸载（DriverUnload → 释放内存 → provider 卸载）
 *
 * 用法：MapperSelfTest.exe <SSJJDrv.sys 绝对路径>
 * 退出码：0 = 全链路成功；1 = 任一步失败
 *
 * 建议：本机首次测试先跑本工具，确认映射链路稳定、不蓝屏，
 * 再跑完整 Vape.exe（注入 + 保护）。
 * ==================================================================== */
#include "general.h"
#include "mapper.h"

int wmain(int argc, wchar_t **argv)
{
    setvbuf(stdout, NULL, _IONBF, 0); /* 无缓冲：蓝屏时日志不丢 */

    if (argc < 2) {
        printf("用法: MapperSelfTest.exe <SSJJDrv.sys 绝对路径>\n");
        return 1;
    }

    printf("=== [1/5] 加载 provider (iqvw64e.sys) ===\n");
    HANDLE device = intel_driver::Load();
    if (device == INVALID_HANDLE_VALUE) {
        printf("[FAIL] provider 加载失败（检查 Defender 是否拦截 iqvw64e.sys）\n");
        return 1;
    }
    printf("[ OK ] provider 加载成功 (handle=%p, ntoskrnl=0x%llx)\n",
           device, (unsigned long long)intel_driver::ntoskrnlAddr);

    printf("\n=== [2/5] 手动映射 SSJJDrv.sys ===\n");
    SSJJ_MAPPED_DRIVER drv{};
    if (!ssjj_mapper::MapDriver(device, argv[1], drv)) {
        printf("[FAIL] 映射失败（见上方 [mapper] 输出）\n");
        intel_driver::Unload(device);
        return 1;
    }
    printf("[ OK ] 映射成功 base=0x%llx entry=0x%llx unload=0x%llx\n",
           (unsigned long long)drv.base,
           (unsigned long long)drv.entry_point,
           (unsigned long long)drv.unload_routine);

    printf("\n=== [3/5] 验证 \\\\.\\SSJJDrv 设备 ===\n");
    HANDLE dev = CreateFileW(L"\\\\.\\SSJJDrv", GENERIC_READ | GENERIC_WRITE,
            0, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (dev == INVALID_HANDLE_VALUE) {
        printf("[FAIL] 设备打开失败 error=%lu（DriverEntry 未成功创建设备）\n",
               GetLastError());
        ssjj_mapper::UnmapDriver(device, drv);
        intel_driver::Unload(device);
        return 1;
    }
    printf("[ OK ] 设备可打开\n");
    CloseHandle(dev);

    printf("\n=== [4/5] 按 Enter 触发卸载（DriverUnload + 释放内存）===\n");
    printf("（请观察系统是否蓝屏；若无异常说明卸载路径安全）\n");
    getchar();

    printf("\n=== [5/5] 卸载 ===\n");
    if (!ssjj_mapper::UnmapDriver(device, drv)) {
        printf("[WARN] 卸载映射驱动失败\n");
    }
    if (!intel_driver::Unload(device)) {
        printf("[WARN] 卸载 provider 失败\n");
    }
    printf("[ OK ] 全链路完成。若全程无蓝屏，映射器稳定，可跑完整 Vape.exe\n");
    return 0;
}
