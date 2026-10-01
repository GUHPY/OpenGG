<#
.SYNOPSIS
Capture Windows USB ETW events while changing one vendor setting per marked step.
.DESCRIPTION
Requires administrator rights for a capture; -DryRun only prints the parsed steps.
UCX on the Windows 11 build 26200 investigated here emitted headers without useful
transfer payloads. An ETL file alone is not proof that report bytes were captured.
Traces may contain unrelated USB traffic. Keep raw traces local and publish only
the small device-specific records needed to reproduce a finding.
.EXAMPLE
powershell -File tools\Capture-UsbTrace.ps1 -DryRun "All keys 1.0 mm" "W 2.0 mm"
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Out = (Join-Path $env:USERPROFILE 'Desktop\opengg-usb'),
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Steps = @('Change one setting in the vendor app'),
    [switch]$DryRun
)
$Steps = @($Steps | ForEach-Object { $_.Trim().TrimEnd(',').Trim() } | Where-Object { $_ })
if ($DryRun) { Write-Output "Output: $Out"; $n=0; foreach ($step in $Steps) { $n++; Write-Output "Step $n of $($Steps.Count): $step" }; return }
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Start PowerShell as administrator for an actual capture, or use -DryRun.' }
$session='OpenGG-USB'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$etl=Join-Path $Out 'usb.etl'; $log=Join-Path $Out 'steps.txt'
logman start $session -ets -o $etl -bs 1024 -nb 128 640 -p Microsoft-Windows-USB-UCX 0x381C1 5 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not start ETW session (logman $LASTEXITCODE)." }
try {
    logman update trace $session -ets -p Microsoft-Windows-USB-USBXHCI 0x30101 5 | Out-Null
    logman update trace $session -ets -p Microsoft-Windows-USB-USBHUB3 0x38101 5 | Out-Null
    "start $([DateTime]::UtcNow.ToString('o'))" | Set-Content -LiteralPath $log -Encoding UTF8
    $n=0; foreach ($step in $Steps) { $n++; Read-Host "Step $n of $($Steps.Count): $step. Change the setting, then press Enter" | Out-Null; "step $n $([DateTime]::UtcNow.ToString('o')) $step" | Add-Content -LiteralPath $log -Encoding UTF8 }
}
finally { logman stop $session -ets | Out-Null; "end $([DateTime]::UtcNow.ToString('o'))" | Add-Content -LiteralPath $log -Encoding UTF8 }
Write-Output "Trace: $etl"
