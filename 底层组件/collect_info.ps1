# ====================================================================
# collect_info.ps1 - 驱动加载失败诊断信息收集
# 用法：右键"使用 PowerShell 运行"，或
#       powershell -ExecutionPolicy Bypass -File .\collect_info.ps1
# 输出会同时打印到屏幕和 collect_report.txt
# ====================================================================
$ErrorActionPreference = 'SilentlyContinue'
$report = @()

$report += "=== 1. Windows 版本 ==="
$os = Get-CimInstance Win32_OperatingSystem
$report += "ProductName : $($os.Caption)"
$report += "Version     : $($os.Version)"
$report += "Build       : $($os.BuildNumber)"

$report += ""
$report += "=== 2. HVCI / Secure Boot / VBS ==="
$dg = Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard
$report += "HVCI SecurityServicesRunning : $($dg.SecurityServicesRunning)  (2=开启, 0/空=关闭)"
$report += "HVCI SecurityServicesConfigured: $($dg.SecurityServicesConfigured)"
try { $sb = Confirm-SecureBootUEFI } catch { $sb = "无法查询(非UEFI?)" }
$report += "SecureBoot : $sb"

$report += ""
$report += "=== 3. 易受攻击驱动程序阻止列表 ==="
$ci = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\CI\Config" -Name VulnerableDriverBlocklistEnable
$report += "VulnerableDriverBlocklistEnable : $($ci.VulnerableDriverBlocklistEnable)  (1=阻止列表开启->会拦 iqvw64e.sys, 0/空=关闭)"

$report += ""
$report += "=== 4. Defender 状态 ==="
$mp = Get-MpComputerStatus
$report += "RealTimeProtectionEnabled : $($mp.RealTimeProtectionEnabled)"
$report += "AntivirusEnabled          : $($mp.AntivirusEnabled)"
$report += "AMServiceEnabled          : $($mp.AMServiceEnabled)"
$report += "Defender 排除项:"
$report += "  $((Get-MpPreference).ExclusionPath -join ', ')"

$report += ""
$report += "=== 5. %TEMP% 残留 .sys 文件 ==="
$temps = Get-ChildItem $env:TEMP -Filter *.sys
if ($temps) { $temps | ForEach-Object { $report += "  $($_.Name)  $($_.Length) bytes  $($_.LastWriteTime)" } }
else { $report += "  无" }

$report += ""
$report += "=== 6. 可能的残留服务（随机名） ==="
$svc = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services" | Where-Object {
    $_.PSChildName -match '^[a-zA-Z]{10,30}$' -and
    (Get-ItemProperty $_.PSPath -Name ImagePath -ErrorAction SilentlyContinue).ImagePath -match 'Temp'
}
if ($svc) { $svc | ForEach-Object { $report += "  $($_.PSChildName)" } }
else { $report += "  无" }

$report += ""
$report += "=== 7. 驱动签名状态（IQVW64E 若在 TEMP 中） ==="
$found = Get-ChildItem $env:TEMP -Filter *.sys | Select-Object -First 5
foreach ($f in $found) {
    $sig = Get-AuthenticodeSignature $f.FullName
    $report += "  $($f.Name): Status=$($sig.Status)  Signer=$($sig.SignerCertificate.Subject)"
}

$report += ""
$report += "=== 8. 系统事件日志最近的内核/代码完整性错误 ==="
$ev = Get-WinEvent -FilterHashtable @{LogName='System'; Id=@(41,1001,6008)} -MaxEvents 5
if ($ev) { $ev | ForEach-Object { $report += "  [$($_.Id)] $($_.TimeCreated) $($_.Message.Split("`n")[0])" } }
else { $report += "  无" }

$report | ForEach-Object { Write-Host $_ }
$report | Out-File -FilePath "$PSScriptRoot\collect_report.txt" -Encoding UTF8
Write-Host ""
Write-Host "报告已保存到: $PSScriptRoot\collect_report.txt"
