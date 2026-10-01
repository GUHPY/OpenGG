[CmdletBinding()]
param([switch]$SkipSdkLayout)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root 'tooling\installers'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$files = [Collections.Generic.List[object]]::new()
function Get-Package([string]$name, [string]$url, [string]$expected = '') {
    $path = Join-Path $cache $name
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Output "Downloading $name"
        Invoke-WebRequest -Uri $url -OutFile ($path + '.partial') -UseBasicParsing
        Move-Item -LiteralPath ($path + '.partial') -Destination $path -Force
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($expected -and $hash -ne $expected) { throw "Upstream checksum mismatch: $name" }
    $files.Add([ordered]@{ file=$name; url=$url; sha256=$hash; bytes=(Get-Item -LiteralPath $path).Length })
}
Get-Package 'python-3.14.8-amd64.exe' 'https://www.python.org/ftp/python/3.14.8/python-3.14.8-amd64.exe'
Get-Package 'python-3.14.8-embed-amd64.zip' 'https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip'
Get-Package 'Wireshark-4.6.9-x64.exe' 'https://2.na.dl.wireshark.org/win64/all-versions/Wireshark-4.6.9-x64.exe' 'BF9B5CE8A89F244C376A9B1A946276EAA06463DDE3E33069A34D7F102F5878CF'
Get-Package 'USBPcapSetup-1.5.4.0.exe' 'https://github.com/desowin/usbpcap/releases/download/1.5.4.0/USBPcapSetup-1.5.4.0.exe'
Get-Package 'usbpcap-src-incl-pdb-1.5.4.0.7z' 'https://github.com/desowin/usbpcap/releases/download/1.5.4.0/usbpcap-src-incl-pdb-1.5.4.0.7z'
Get-Package 'winsdksetup.exe' 'https://download.microsoft.com/download/46742ab5-6592-4968-a793-129e7f3bc55a/KIT_BUNDLE_WINDOWSSDK_MEDIACREATION/winsdksetup.exe'
foreach ($name in 'python-3.14.8-amd64.exe','Wireshark-4.6.9-x64.exe','winsdksetup.exe') {
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $cache $name)
    if ($signature.Status -ne 'Valid') { throw "Invalid installer signature: $name ($($signature.Status))" }
}
$wheels = Join-Path $cache 'python-wheels'
New-Item -ItemType Directory -Path $wheels -Force | Out-Null
& python -m pip wheel --index-url https://pypi.org/simple --wheel-dir $wheels frida-tools==14.10.4 frida==17.19.0 hidapi==0.15.0 pip==26.2.1
if ($LASTEXITCODE -ne 0) { throw 'Preparing offline Python wheels failed.' }
if (-not $SkipSdkLayout) {
    $layout = Join-Path $cache 'usbview-sdk'
    New-Item -ItemType Directory -Path $layout -Force | Out-Null
    $arguments = @('/layout', ('"' + $layout + '"'), '/features', 'OptionId.WindowsDesktopDebuggers', '/quiet', '/norestart')
    $setup = Start-Process -FilePath (Join-Path $cache 'winsdksetup.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $setup.WaitForExit()
    if ($setup.ExitCode -notin 0,3010) { throw "SDK layout failed: $($setup.ExitCode)" }
}
foreach ($directory in 'python-wheels','usbview-sdk') {
    $path = Join-Path $cache $directory
    if (-not (Test-Path -LiteralPath $path)) { continue }
    foreach ($item in Get-ChildItem -LiteralPath $path -File -Recurse) {
        $relative = $item.FullName.Substring($cache.Length+1).Replace('\','/')
        $source = if ($directory -eq 'python-wheels') { 'https://pypi.org/' } else { 'https://developer.microsoft.com/windows/downloads/windows-sdk/' }
        $files.Add([ordered]@{file=$relative; url=$source; sha256=(Get-FileHash -LiteralPath $item.FullName).Hash; bytes=$item.Length})
    }
}
$manifest = [ordered]@{ format=1; preparedUtc=[DateTimeOffset]::UtcNow.ToString('o'); architecture='win-x64'; pythonVersion='3.14.8'; fridaToolsVersion='14.10.4'; fridaVersion='17.19.0'; hidapiVersion='0.15.0'; pipVersion='26.2.1'; wiresharkVersion='4.6.9'; usbpcapVersion='1.5.4.0'; files=$files }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'tooling\manifest.json') -Encoding UTF8
Write-Output "Prepared $($files.Count) local installer/payload files in $cache"
