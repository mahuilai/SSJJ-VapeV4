/* ====================================================================
 * loader_bridge.h - 给 injector.c（C）的 extern "C" 加载接口
 *
 * 封装：provider（iqvw64e.sys）加载 → 手动映射 SSJJDrv.sys → 卸载。
 * ==================================================================== */
#ifndef SSJJ_LOADER_BRIDGE_H
#define SSJJ_LOADER_BRIDGE_H

#ifdef __cplusplus
extern "C" {
#endif

/* 通过 kdmapper 方式（iqvw64e.sys provider + 手动映射）加载 SSJJDrv.sys。
 * driver_path: SSJJDrv.sys 绝对路径。
 * 返回 1 成功 / 0 失败。成功后 \\.\SSJJDrv 设备可用（IOCTL 照常）。 */
int ssjj_loader_load_driver(const wchar_t* driver_path);

/* 卸载映射的驱动（调用 DriverUnload 注销保护/删设备）+ 卸载 provider。 */
void ssjj_loader_unload_driver(void);

/* 是否已通过 mapper 加载（幂等判断）。 */
int ssjj_loader_is_active(void);

#ifdef __cplusplus
}
#endif

#endif /* SSJJ_LOADER_BRIDGE_H */
