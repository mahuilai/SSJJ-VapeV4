# ====================================================================
# protect_payload.ps1 - 一键：ConfuserEx 混淆 Vape.dll -> 自动验证 -> 原生打包
#
# 流程:
#   1. 复制托管 Vape.dll (+依赖) 到 ConfuserEx work 目录
#   2. Confuser.CLI.exe 按 vape.crproj 配置混淆 (字符串加密+重命名+控制流)
#   3. verify2.exe 验证: 入口保留 + ldstr 已加密 (失败即中止)
#   4. build_native.ps1 用混淆后的 Vape.dll 重新打包 Vape.exe
#
# 用法:
#   .\protect_payload.ps1                          # 用默认托管 DLL
#   .\protect_payload.ps1 -ManagedDll "D:\x\Vape.dll"
# ====================================================================
param(
    [string]$ManagedDll = "$PSScriptRoot\..\bin\x64\Release\Vape.dll",
    [string]$ConfuserDir = "D:\加密\ConfuserEx",
    [string]$DepsDir     = "$PSScriptRoot\..\依赖"
)

$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host "=== [protect] 1/4 准备混淆输入 ===" -ForegroundColor Cyan
if (-not (Test-Path $ManagedDll)) { Write-Host "错误: 托管 DLL 不存在: $ManagedDll" -ForegroundColor Red; exit 1 }
New-Item -ItemType Directory -Force -Path "$ConfuserDir\work", "$ConfuserDir\out" | Out-Null
Copy-Item $ManagedDll "$ConfuserDir\work\Vape.dll" -Force
if (Test-Path $DepsDir) { Copy-Item "$DepsDir\*.dll" "$ConfuserDir\work\" -Force -ErrorAction SilentlyContinue }
Write-Host "  输入: $ManagedDll"

Write-Host "=== [protect] 2/4 ConfuserEx 混淆 ===" -ForegroundColor Cyan
Push-Location $ConfuserDir
try {
    & ".\Confuser.CLI.exe" -n "$ConfuserDir\vape.crproj"
    if ($LASTEXITCODE -ne 0) { Write-Host "ConfuserEx 失败" -ForegroundColor Red; exit $LASTEXITCODE }
} finally { Pop-Location }

Write-Host "=== [protect] 3/4 验证混淆结果 ===" -ForegroundColor Cyan
if (-not (Test-Path "$ConfuserDir\verify2.exe")) {
    Write-Host "  编译 verify2.exe ..."
    & $csc /nologo /r:"$ConfuserDir\dnlib.dll" /out:"$ConfuserDir\verify2.exe" "$ConfuserDir\verify2.cs" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "verify2 编译失败" -ForegroundColor Red; exit 1 }
}
& "$ConfuserDir\verify2.exe" "$ConfuserDir\out\Vape.dll"
if ($LASTEXITCODE -ne 0) {
    Write-Host "验证失败，中止打包 (先解决上面的 FAIL 再继续)" -ForegroundColor Red
    exit 1
}

Write-Host "=== [protect] 4/4 原生打包 (混淆后 Vape.dll) ===" -ForegroundColor Cyan
# 结束可能残留的 Vape.exe 进程, 避免 LNK1104 文件占用
Get-Process -Name 'Vape' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 500

& powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\build_native.ps1" -VapePayload "$ConfuserDir\out\Vape.dll"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== [protect] 5/5 VMProtect 加壳 (L4: 机器锁 + 虚拟化) ===" -ForegroundColor Cyan
$vmpDir   = "D:\加密\VMPprotect"
$dist     = Join-Path $PSScriptRoot "build\dist"
$injector = Join-Path $dist "Vape.exe"
$vmpOut   = Join-Path $dist "Vape_vmp.exe"
$proj     = Join-Path $PSScriptRoot "build\vape.vmp"

if (-not (Test-Path (Join-Path $vmpDir "VMProtect_Con.exe"))) {
    Write-Host "跳过 VMP: 未找到 $vmpDir\VMProtect_Con.exe (装 VMProtect 后可手动加壳)" -ForegroundColor Yellow
    exit 0
}

# 生成 .vmp 项目 (InputFile/OutputFile + lock_check 标记虚拟化 + 内置反调试反虚拟机)
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<VMProtect_Project>
  <Options>
    <InputFile>$injector</InputFile>
    <OutputFile>$vmpOut</OutputFile>
    <FileAlignment>0</FileAlignment>
    <Compression>1</Compression>
    <EncryptData>1</EncryptData>
    <StripDebugInfo>1</StripDebugInfo>
    <Protection>
      <Virtualization>1</Virtualization>
      <Mutation>1</Mutation>
      <Ultimate>0</Ultimate>
      <CheckDebugger>1</CheckDebugger>
      <CheckDebuggerDebug>1</CheckDebuggerDebug>
      <CheckEnvironment>1</CheckEnvironment>
    </Protection>
  </Options>
  <Files>
    <File>
      <Name>$injector</Name>
      <Flags>1</Flags>
      <Markers>
        <Marker>
          <Name>lock_check</Name>
          <Flags>1</Flags>
        </Marker>
      </Markers>
    </File>
  </Files>
</VMProtect_Project>
"@
[System.IO.File]::WriteAllText($proj, $xml, [System.Text.UTF8Encoding]::new($false))

Push-Location $vmpDir
try {
    & ".\VMProtect_Con.exe" $injector $vmpOut -pf $proj
    if ($LASTEXITCODE -ne 0) { Write-Host "VMProtect 加壳失败" -ForegroundColor Red; exit $LASTEXITCODE }
} finally { Pop-Location }

if (Test-Path $vmpOut) {
    Remove-Item $injector -Force
    Move-Item $vmpOut $injector -Force
    Write-Host "  VMP 完成: $injector ($([math]::Round((Get-Item $injector).Length/1MB,2)) MB)" -ForegroundColor Green
} else {
    Write-Host "VMP 输出缺失: $vmpOut" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[protect] 交付 = Vape.exe + VMProtect_Ext64.dll + VMProtectSDK64.dll (同在 dist)" -ForegroundColor Yellow
exit 0
