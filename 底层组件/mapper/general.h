/* ====================================================================
 * general.h - 精简公共头（基于 meme-rw 的 general.h 裁剪）
 *
 * 仅保留 provider（intel_driver）需要的依赖；
 * 移除 demo 相关的 magic/memory_helper/d3d11 等。
 * ==================================================================== */
#ifndef SSJJ_MAPPER_GENERAL_H
#define SSJJ_MAPPER_GENERAL_H

#include <Windows.h>
#include <winternl.h>
#include <winioctl.h>
#include <iostream>
#include <cstdio>
#include <fstream>
#include <string>
#include <vector>
#include <cstdint>

#pragma comment(lib, "ntdll.lib")

#include "provider/driver_resource.h"
#include "provider/service.h"
#include "provider/nt.h"
#include "provider/intel_driver.h"
#include "provider/utils.h"

#endif /* SSJJ_MAPPER_GENERAL_H */
