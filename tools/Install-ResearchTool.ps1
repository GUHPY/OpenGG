[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('python','frida','wireshark','usbpcap','usbview','protocol-cli','capture-analyzer')]
    [string]$Tool,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = Split-Path $PSScriptRoot -Parent
$cache = [IO.Path]::GetFullPath((Join-Path $root 'tooling\installers'))
$manifestPath = Join-Path $root 'tooling\manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Run tools/Fetch-ResearchTools.ps1 to prepare the local installers.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
function Test-Payload([string]$name) {
    $record = @($manifest.files | Where-Object { $_.file -eq $name })
    if ($record.Count -ne 1) { throw "No unique installer manifest entry: $name" }
    $path = [IO.Path]::GetFullPath((Join-Path $cache $name))
    if (-not $path.StartsWith($cache + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Installer path escapes the local cache.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing local payload: $name" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record[0].sha256) { throw "Local payload checksum mismatch: $name" }
    return $path
}
function Initialize-Python {
    $archive = Test-Payload "python-$($manifest.pythonVersion)-embed-amd64.zip"
    $wheelRecords = @($manifest.files | Where-Object { $_.file -like 'python-wheels/*' })
    if (-not $wheelRecords.Count) { throw 'The offline Python wheels are missing.' }
    foreach ($item in $wheelRecords) { Test-Payload $item.file | Out-Null }
    $python = Join-Path $root 'tooling\runtime\python\python.exe'
    if ($DryRun) { Write-Host "Prepare local Python from $archive and $($wheelRecords.Count) verified wheels."; return $python }
    $runtime = Split-Path $python -Parent
    if (-not (Test-Path -LiteralPath $python)) { Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force }
    $paths = @(Get-ChildItem -LiteralPath $runtime -Filter 'python*._pth')
    if ($paths.Count -ne 1) { throw 'Unexpected embedded Python path configuration.' }
    $text = (Get-Content -LiteralPath $paths[0].FullName -Raw).Replace('#import site','import site')
    foreach ($entry in 'Lib/site-packages','../../../tools') {
        if ($text -notmatch ('(?m)^' + [regex]::Escape($entry) + '\s*$')) { $text += "`r`n$entry`r`n" }
    }
    Set-Content -LiteralPath $paths[0].FullName -Value $text -Encoding ASCII
    $site = Join-Path $runtime 'Lib\site-packages'
    New-Item -ItemType Directory -Path $site -Force | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $site 'pip'))) {
        $pip = @($wheelRecords | Where-Object { $_.file -like 'python-wheels/pip-*.whl' })
        if ($pip.Count -ne 1) { throw 'A unique pip wheel is required.' }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory((Test-Payload $pip[0].file),$site)
    }
    & $python --version | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Local Python did not start.' }
    return $python
}
if ($Tool -in 'python','frida','protocol-cli','capture-analyzer') {
    $python = Initialize-Python
    if (-not $DryRun -and $Tool -in 'frida','protocol-cli') {
        [string[]]$packages = if ($Tool -eq 'frida') { @("frida-tools==$($manifest.fridaToolsVersion)","frida==$($manifest.fridaVersion)","hidapi==$($manifest.hidapiVersion)") } else { @("hidapi==$($manifest.hidapiVersion)") }
        [string[]]$reinstall = if ($Tool -eq 'frida') { @('--force-reinstall') } else { @() }
        & $python -m pip install --no-index --no-warn-script-location --find-links (Join-Path $cache 'python-wheels') @reinstall @packages
        if ($LASTEXITCODE -ne 0) { throw 'Offline Python package installation failed.' }
    }
    Write-Output "Ready: $Tool (local to OpenGG; no system Python replacement)."
    exit 0
}
$filename = switch ($Tool) {
    'wireshark' { "Wireshark-$($manifest.wiresharkVersion)-x64.exe" }
    'usbpcap' { "USBPcapSetup-$($manifest.usbpcapVersion).exe" }
    'usbview' { 'usbview-sdk/winsdksetup.exe' }
}
$installer = Test-Payload $filename
if ($Tool -eq 'usbview') {
    $payload = @($manifest.files | Where-Object { $_.file -like 'usbview-sdk/*' })
    foreach ($file in $payload) { Test-Payload $file.file | Out-Null }
}
if ($DryRun) { Write-Output "Launch verified local installer: $installer"; exit 0 }
$arguments = if ($Tool -eq 'usbview') { @('/features','OptionId.WindowsDesktopDebuggers','/norestart') } else { @() }
# The installer is interactive: its own UI handles destination, elevation, license and driver choices.
if ($arguments.Count) { $process = Start-Process -FilePath $installer -ArgumentList $arguments -PassThru }
else { $process = Start-Process -FilePath $installer -PassThru }
$process.WaitForExit()
if ($process.ExitCode -notin 0,3010) { throw "Installer exited with code $($process.ExitCode)." }
if ($process.ExitCode -eq 3010) { Write-Output 'Installation finished. The installer requests a restart; OpenGG does not restart Windows.' }
else { Write-Output "Installer finished: $Tool." }
