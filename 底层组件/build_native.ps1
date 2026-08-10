# ====================================================================
# build_native.ps1 - one-click build for SSJJInjector.exe + SSJJNative.dll
#
# Prerequisites:
#   - Visual Studio 2022 (Desktop development with C++), x64 toolchain
#   - CMake >= 3.21
#   - Managed payload built first:  Vape.csproj  (Release | x64)
#     -> bin\x64\Release\Vape.dll
#   - (Optional) WDK + signing cert for SSJJDrv.sys (kernel injector)
#
# Usage:
#   .\build_native.ps1
#   .\build_native.ps1 -VapePayload "D:\path\to\Vape.dll"
#   .\build_native.ps1 -BuildDriver -CertPfx "D:\cert\vape.pfx" -CertPassword xxx
# ====================================================================
param(
    [string]$VapePayload = "$PSScriptRoot\..\bin\x64\Release\Vape.dll",
    [switch]$BuildDriver,
    [string]$CertPfx = "",
    # CertPassword 以明文传给 signtool /p（签名工具要求明文，属脚本化签名惯例；
    # 若在共享机器上使用，建议改用 -CertThumbprint 避免密码落盘/入历史）
    [string]$CertPassword = "",
    [string]$CertThumbprint = "",
    # VMX 二进制路径（VMLiteMapper.sys + VMShellcode.sys）
    # 需要先用 Visual Studio 2022 + WDK 分别编译这两个项目
    [string]$VmxShellcode = "$PSScriptRoot\vmx\shellcode\x64\Release\VMShellcode.sys",
    [string]$VmxMapper    = "$PSScriptRoot\vmx\mapper\x64\Release\VMLiteMapper.sys"
)

$ErrorActionPreference = "Stop"

# --- 0. locate cmake (PATH first, then Visual Studio's bundled copy) -----
function Find-CMake {
    $cmd = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vs = & $vswhere -latest -products * -property installationPath
        $candidate = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$cmake = Find-CMake
if (-not $cmake) {
    Write-Host "[SSJJ] ERROR: cmake not found. Install CMake >= 3.21 or Visual Studio with CMake tools." -ForegroundColor Red
    exit 1
}
Write-Host "[SSJJ] CMake: $cmake" -ForegroundColor Cyan

# --- 1. payload check -------------------------------------------------
$resolvedPayload = (Resolve-Path -LiteralPath $VapePayload -ErrorAction SilentlyContinue)
if (-not $resolvedPayload) {
    Write-Host "[SSJJ] ERROR: payload not found: $VapePayload" -ForegroundColor Red
    Write-Host "[SSJJ] Build the managed project first:" -ForegroundColor Yellow
    Write-Host "        dotnet msbuild Vape.csproj -p:Configuration=Release -p:Platform=x64" -ForegroundColor Yellow
    exit 1
}
$VapePayload = $resolvedPayload.Path
Write-Host "[SSJJ] Payload: $VapePayload" -ForegroundColor Cyan

# --- 1b. VMX 二进制检查 (VMShellcode.sys + VMLiteMapper.sys) ---------------
Write-Host "[VMX] 检查 VMX 二进制文件..." -ForegroundColor Cyan
$vmxArgs = @()
if (Test-Path $VmxShellcode) {
    Write-Host "[VMX] VMShellcode: $VmxShellcode" -ForegroundColor Cyan
    $vmxArgs += "-DVMX_SHELLCODE_BIN=`"$VmxShellcode`""
} else {
    Write-Host "[VMX] WARNING: VMShellcode.sys 未找到： $VmxShellcode" -ForegroundColor Yellow
    Write-Host "[VMX] 请用 Visual Studio 2022 + WDK 打开 vmx\shellcode\VMShellcode.sln 并编译" -ForegroundColor Yellow
    Write-Host "[VMX] 使用占位文件，VMX 内核注入在运行时会失败" -ForegroundColor Yellow
}
if (Test-Path $VmxMapper) {
    Write-Host "[VMX] VMLiteMapper: $VmxMapper" -ForegroundColor Cyan
    $vmxArgs += "-DVMX_MAPPER_SYS=`"$VmxMapper`""
} else {
    Write-Host "[VMX] WARNING: VMLiteMapper.sys 未找到： $VmxMapper" -ForegroundColor Yellow
    Write-Host "[VMX] 请用 Visual Studio 2022 + WDK 打开 vmx\mapper\VMLiteMapper.sln 并编译" -ForegroundColor Yellow
    Write-Host "[VMX] 使用占位文件，VMX 内核注入在运行时会失败" -ForegroundColor Yellow
}

# --- 2. cmake configure ------------------------------------------------
$buildDir = Join-Path $PSScriptRoot "build"
Write-Host "[SSJJ] Configuring CMake..." -ForegroundColor Cyan
& $cmake -S $PSScriptRoot -B $buildDir -A x64 -DVAPE_PAYLOAD="$VapePayload" @vmxArgs
if ($LASTEXITCODE -ne 0) { Write-Host "[SSJJ] CMake configure failed" -ForegroundColor Red; exit $LASTEXITCODE }

# --- 3. build -----------------------------------------------------------
# Defender/杀软会短暂锁定新生成的 exe/dll 导致 LNK1104, 重试几次.
Write-Host "[SSJJ] Building Release..." -ForegroundColor Cyan
$buildOk = $false
for ($attempt = 1; $attempt -le 4; $attempt++) {
    & $cmake --build $buildDir --config Release
    if ($LASTEXITCODE -eq 0) { $buildOk = $true; break }
    Write-Host "[SSJJ] Build attempt $attempt failed (likely transient file lock); retrying..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
}
if (-not $buildOk) { Write-Host "[SSJJ] Build failed after retries" -ForegroundColor Red; exit 1 }

# --- 4. report ----------------------------------------------------------
# Locate outputs (multi-config generators add a <Config> subdirectory).
$injector = Get-ChildItem -Path $buildDir -Recurse -Filter "Vape.exe"       | Select-Object -First 1
$native   = Get-ChildItem -Path $buildDir -Recurse -Filter "SSJJNative.dll" | Select-Object -First 1

Write-Host ""
Write-Host "[SSJJ] Bundle ready:" -ForegroundColor Green
if ($injector) { Write-Host "  $($injector.FullName)  ($([math]::Round($injector.Length/1KB,1)) KB)" -ForegroundColor Cyan }
if ($native)   { Write-Host "  $($native.FullName)  ($([math]::Round($native.Length/1KB,1)) KB)"   -ForegroundColor Cyan }

Write-Host ""
Write-Host "[SSJJ] NOTE: Vape.exe (injector) is self-contained" -ForegroundColor Yellow
Write-Host "      (SSJJNative.dll is embedded inside it; no extra file to ship)." -ForegroundColor Yellow
Write-Host ""
Write-Host "[SSJJ] Usage (double-click = auto):" -ForegroundColor Yellow
Write-Host "  1. Start the game (SSJJ_BattleClient_Unity.exe), enter a scene." -ForegroundColor Yellow
Write-Host "  2. Double-click  $($injector.Name)" -ForegroundColor Yellow
Write-Host "     -> UAC prompt (auto-elevate) -> auto scan -> auto inject." -ForegroundColor Yellow
Write-Host "  3. F12 toggles the menu in-game." -ForegroundColor Yellow
Write-Host "  Advanced:  $($injector.Name) --manual   |   $($injector.Name) <pid> <dll>" -ForegroundColor Yellow

# --- 5. optional: kernel driver ----------------------------------------
# The injector now requires a signed SSJJDrv.sys next to Vape.exe.
# Build it with -BuildDriver (needs WDK + signing cert).
if ($BuildDriver) {
    Write-Host ""
    Write-Host "[SSJJ] Building kernel driver SSJJDrv.sys ..." -ForegroundColor Cyan
    $drvArgs = @()
    if ($CertPfx)        { $drvArgs += "-CertPfx", "`"$CertPfx`"" }
    if ($CertPassword)   { $drvArgs += "-CertPassword", "`"$CertPassword`"" }
    if ($CertThumbprint) { $drvArgs += "-CertThumbprint", "`"$CertThumbprint`"" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\build_driver.ps1" @drvArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[SSJJ] WARNING: driver build failed; Vape.exe needs SSJJDrv.sys beside it." -ForegroundColor Yellow
    } else {
        Write-Host "[SSJJ] Driver ready: $PSScriptRoot\dist\SSJJDrv.sys (signed)" -ForegroundColor Green
        Write-Host "      Keep SSJJDrv.sys next to Vape.exe when distributing." -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "[SSJJ] NOTE: SSJJDrv.sys NOT built (VMX 路径为默认，不需要 SSJJDrv.sys)." -ForegroundColor Yellow
    Write-Host "      SSJJDrv.sys 只在 --legacy-load 模式下需要。" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "[VMX] 下一步：编译 VMShellcode + VMLiteMapper：" -ForegroundColor Cyan
    Write-Host "  1. 用 Visual Studio 2022 + WDK 打开 vmx\shellcode\VMShellcode.sln，编译 x64|Release" -ForegroundColor Cyan
    Write-Host "  2. 用 Visual Studio 2022 + WDK 打开 vmx\mapper\VMLiteMapper.sln，编译 x64|Release" -ForegroundColor Cyan
    Write-Host "  3. 重新运行此脚本： .\build_native.ps1" -ForegroundColor Cyan
    Write-Host "     CMake 会自动检测并嵌入 VMShellcode.sys + VMLiteMapper.sys" -ForegroundColor Cyan
}
