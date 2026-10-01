[CmdletBinding()]
param([switch]$PreparePythonTools)
$ErrorActionPreference = 'Stop'
$install = Join-Path $PSScriptRoot 'Install-ResearchTool.ps1'
foreach ($tool in 'python','frida','wireshark','usbpcap','usbview','protocol-cli','capture-analyzer') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $install -Tool $tool -DryRun
    if ($LASTEXITCODE -ne 0) { throw "Local payload validation failed: $tool" }
}
# Check the trust boundary in an isolated fake cache, never against real installers.
$temporary = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('OpenGG-tools-check-' + [Guid]::NewGuid())))
try {
    $scripts = New-Item -ItemType Directory -Path (Join-Path $temporary 'tools') -Force
    $cache = New-Item -ItemType Directory -Path (Join-Path $temporary 'tooling\installers') -Force
    Copy-Item -LiteralPath $install -Destination $scripts.FullName
    $fake = Join-Path $cache.FullName 'Wireshark-test-x64.exe'
    Set-Content -LiteralPath $fake -Value 'Never executable' -Encoding ASCII
    $manifest = @{wiresharkVersion='test'; files=@(@{file='Wireshark-test-x64.exe';sha256='BAD'})}
    $manifestPath = Join-Path $temporary 'tooling\manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    $ErrorActionPreference = 'Continue' # Expected stderr must be captured on Windows PowerShell 5.1.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts.FullName 'Install-ResearchTool.ps1') -Tool wireshark -DryRun 2>&1
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0 -or ($output | Out-String) -notmatch 'checksum mismatch') { throw 'Checksum rejection failed.' }
    $zip = Join-Path $cache.FullName 'python-test-embed-amd64.zip'
    Copy-Item -LiteralPath $fake -Destination $zip
    $manifest = @{pythonVersion='test';files=@(@{file='python-test-embed-amd64.zip';sha256=(Get-FileHash -LiteralPath $zip).Hash},@{file='python-wheels/../../../../escape.whl';sha256='BAD'})}
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    $ErrorActionPreference = 'Continue'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts.FullName 'Install-ResearchTool.ps1') -Tool python -DryRun 2>&1
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0 -or ($output | Out-String) -notmatch 'escapes the local cache') { throw 'Cache path rejection failed.' }
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $temporary.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected temporary directory.' }
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
Write-Output 'Seven install plans validated; corrupt hashes and cache traversal rejected. No system installer executed.'
if ($PreparePythonTools) {
    foreach ($tool in 'frida','protocol-cli','capture-analyzer') {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $install -Tool $tool
        if ($LASTEXITCODE -ne 0) { throw "Offline preparation failed: $tool" }
    }
    $root = Split-Path $PSScriptRoot -Parent
    $python = Join-Path $root 'tooling\runtime\python\python.exe'
    & $python -m pip check
    if ($LASTEXITCODE -ne 0) { throw 'Python dependency check failed.' }
    & $python -c "import hid,frida; print('Local hidapi and Frida imported:', frida.__version__)"
    if ($LASTEXITCODE -ne 0) { throw 'Local module import failed.' }
}
