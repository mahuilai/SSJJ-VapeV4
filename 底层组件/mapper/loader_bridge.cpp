/* ====================================================================
 * loader_bridge.cpp - extern "C" 桥实现
 *
 * 组合 provider + mapper：
 *   ssjj_loader_load_driver   → intel_driver::Load() + MapDriver()
 *   ssjj_loader_unload_driver → UnmapDriver() + intel_driver::Unload()
 * ==================================================================== */
#include "general.h"
#include "mapper.h"
#include "loader_bridge.h"

static HANDLE g_device = INVALID_HANDLE_VALUE;
static SSJJ_MAPPED_DRIVER g_drv;
static int g_active = 0;

extern "C" int ssjj_loader_load_driver(const wchar_t* driver_path)
{
    if (g_active)
        return 1; /* 已加载，幂等 */

    /* 1. 加载 provider（iqvw64e.sys，随机驱动名 + 痕迹清理） */
    HANDLE device = intel_driver::Load();
    if (device == INVALID_HANDLE_VALUE) {
        printf("[loader] provider load failed\n");
        return 0;
    }

    /* 2. 手动映射 SSJJDrv.sys */
    SSJJ_MAPPED_DRIVER drv{};
    if (!ssjj_mapper::MapDriver(device, driver_path, drv)) {
        printf("[loader] MapDriver failed\n");
        intel_driver::Unload(device);
        return 0;
    }

    g_device = device;
    g_drv = drv;
    g_active = 1;
    printf("[loader] SSJJDrv.sys mapped (base=0x%llx)\n",
           (unsigned long long)drv.base);
    return 1;
}

extern "C" void ssjj_loader_unload_driver(void)
{
    if (!g_active)
        return;

    /* 1. 调 DriverUnload（SSJJProtectCleanup + 删设备/符号链接） */
    ssjj_mapper::UnmapDriver(g_device, g_drv);

    /* 2. 卸载 provider（停止服务 + 覆写/删除临时 .sys） */
    intel_driver::Unload(g_device);

    g_device = INVALID_HANDLE_VALUE;
    g_drv = {};
    g_active = 0;
}

extern "C" int ssjj_loader_is_active(void)
{
    return g_active;
}
