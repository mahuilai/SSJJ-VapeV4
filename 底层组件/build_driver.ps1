# ====================================================================
# build_driver.ps1 - build + sign SSJJDrv.sys (kernel injector driver)
#
# Prerequisites:
#   - Windows Driver Kit (WDK) for the km headers/libs
#     (Visual Studio 2022 Build Tools + WDK 10)
#   - A code-signing certificate for kernel drivers
#     (EV/attestation signed drivers load on Win10/11; test-signed
#      only with BCD test signing enabled)
#
# Usage:
#   .\build_driver.ps1                                   # build only
#   .\build_driver.ps1 -CertPfx "D:\cert\vape.pfx" -CertPassword "xxx"
#   .\build_driver.ps1 -CertThumbprint "AA..FF"
#
# Output: native\dist\SSJJDrv.sys  (copy beside Vape.exe)
# ====================================================================
param(
    [string]$CertPfx = "",
    [string]$CertPassword = "",
    [string]$CertThumbprint = "",
    [string]$Timestamp = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

# --- 0. locate Visual Studio x64 environment -------------------------
$vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) {
    $vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
        -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath 2>$null
    if ($vs) {
        $candidate = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
        if (Test-Path $candidate) { $vcvars = $candidate }
    }
}
if (-not (Test-Path $vcvars)) {
    Write-Host "[DRV] ERROR: vcvars64.bat not found. Install VS2022 Build Tools (C++)." -ForegroundColor Red
    exit 1
}

# --- 1. locate WDK km include -----------------------------------------
$kitsRoot = "C:\Program Files (x86)\Windows Kits\10"
$kmInclude = $null
$sdkVer = $null
if (Test-Path "$kitsRoot\Include") {
    Get-ChildItem "$kitsRoot\Include" -Directory | Sort-Object Name -Descending | ForEach-Object {
        if (-not $kmInclude -and (Test-Path "$($_.FullName)\km\wdm.h")) {
            $kmInclude = "$($_.FullName)\km"
            $sdkVer = $_.Name
        }
    }
}
if (-not $kmInclude) {
    Write-Host "[DRV] ERROR: WDK km headers not found under $kitsRoot\Include." -ForegroundColor Red
    Write-Host "[DRV] Install Windows Driver Kit (WDK) from the Windows SDK installer." -ForegroundColor Yellow
    exit 1
}
$shared = "$kitsRoot\Include\$sdkVer\shared"
$um     = "$kitsRoot\Include\$sdkVer\um"
$ucrt   = "$kitsRoot\Include\$sdkVer\ucrt"
$kmLib  = "$kitsRoot\Lib\$sdkVer\km\x64"
$umLib  = "$kitsRoot\Lib\$sdkVer\um\x64"
$ucrtLib= "$kitsRoot\Lib\$sdkVer\ucrt\x64"
Write-Host "[DRV] WDK: $kmInclude" -ForegroundColor Cyan

# --- 2. source layout --------------------------------------------------
$srcDir  = $PSScriptRoot
$outDir  = Join-Path $srcDir "dist"
$objDir  = Join-Path $srcDir "build\drv_obj"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

$sysPath = Join-Path $outDir "SSJJDrv.sys"

# --- 3. compile --------------------------------------------------------
Write-Host "[DRV] Compiling SSJJDrv.c ..." -ForegroundColor Cyan
$commonFlags = @(
    "/nologo", "/c", "/W3", "/O2", "/MT", "/Zp8", "/GS", "/Gy", "/Zl",
    "/kernel", "/utf-8",
    "/D_WIN64", "/D_AMD64_", "/DAMD64", "/DDRIVER",
    "/DNTDDI_VERSION=0x0A000007", "/DWINVER=0x0A00", "/D_WIN32_WINNT=0x0A00",
    "/I$srcDir\driver",
    "/I$kmInclude",
    "/I$shared",
    "/I$um",
    "/I$ucrt"
)
$compileCmd = $commonFlags + @("/Fo$objDir\SSJJDrv.obj", "$srcDir\driver\SSJJDrv.c")
cmd /c "`"$vcvars`" >nul 2>&1 && cl $compileCmd 2>&1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[DRV] Compile failed" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[DRV] Compiling SSJJProtect.c ..." -ForegroundColor Cyan
$protectCmd = $commonFlags + @("/Fo$objDir\SSJJProtect.obj", "$srcDir\driver\SSJJProtect.c")
cmd /c "`"$vcvars`" >nul 2>&1 && cl $protectCmd 2>&1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[DRV] Compile failed" -ForegroundColor Red
    exit $LASTEXITCODE
}

# --- 4. link -----------------------------------------------------------
Write-Host "[DRV] Linking SSJJDrv.sys ..." -ForegroundColor Cyan
$linkCmd = @(
    "/NOLOGO", "/OUT:$sysPath", "/DRIVER", "/SUBSYSTEM:NATIVE",
    "/ENTRY:DriverEntry", "/NODEFAULTLIB", "/SECTION:INIT,D", "/MACHINE:X64",
    "$objDir\SSJJDrv.obj",
    "$objDir\SSJJProtect.obj",
    "$kmLib\ntoskrnl.lib",
    "$kmLib\hal.lib",
    "$kmLib\wdmsec.lib",
    "$kmLib\BufferOverflowFastFailK.lib"
)
cmd /c "`"$vcvars`" >nul 2>&1 && link $linkCmd 2>&1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[DRV] Link failed" -ForegroundColor Red
    exit $LASTEXITCODE
}

# --- 5. sign -----------------------------------------------------------
$signtool = Get-ChildItem "$kitsRoot\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\" } | Select-Object -First 1
if (-not $signtool) {
    Write-Host "[DRV] signtool not found; driver is NOT signed." -ForegroundColor Yellow
} else {
    if ($CertThumbprint) {
        Write-Host "[DRV] Signing with certificate thumbprint $CertThumbprint ..." -ForegroundColor Cyan
        & $signtool.FullName sign /v /fd SHA256 /sha1 $CertThumbprint /tr $Timestamp /td SHA256 $sysPath
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[DRV] Signing failed" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } elseif ($CertPfx) {
        Write-Host "[DRV] Signing with $CertPfx ..." -ForegroundColor Cyan
        if ($CertPassword) {
            & $signtool.FullName sign /v /fd SHA256 /f $CertPfx /p $CertPassword /tr $Timestamp /td SHA256 $sysPath
        } else {
            & $signtool.FullName sign /v /fd SHA256 /f $CertPfx /tr $Timestamp /td SHA256 $sysPath
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[DRV] Signing failed" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } else {
        Write-Host "[DRV] No certificate specified; driver NOT signed." -ForegroundColor Yellow
        Write-Host "[DRV]   .\build_driver.ps1 -CertPfx D:\cert\vape.pfx -CertPassword xxx" -ForegroundColor Yellow
    }
}

# --- 6. report ----------------------------------------------------------
$size = [math]::Round((Get-Item $sysPath).Length / 1KB, 1)
Write-Host ""
Write-Host "[DRV] SSJJDrv.sys ready:" -ForegroundColor Green
Write-Host "  $sysPath  ($size KB)" -ForegroundColor Cyan
Write-Host ""
Write-Host "[DRV] Place SSJJDrv.sys next to Vape.exe (injector)." -ForegroundColor Yellow
Write-Host "[DRV] The injector loads it with NtLoadDriver, injects via ZwCreateThreadEx," -ForegroundColor Yellow
Write-Host "      then enables process protection (ObRegisterCallbacks) so GameGuard" -ForegroundColor Yellow
Write-Host "      cannot attach/inject into the game process afterwards." -ForegroundColor Yellow
