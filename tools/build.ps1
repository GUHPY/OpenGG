[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [switch]$FrameworkDependent, [switch]$WithoutResearchTools)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$portableName = if ($WithoutResearchTools) { 'artifacts\portable-minimal' } else { 'artifacts\portable' }
$portable = Join-Path $root $portableName
if (-not $WithoutResearchTools) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\Test-ResearchTools.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Prepare the local installers with tools/Fetch-ResearchTools.ps1, or use -WithoutResearchTools.' }
}
& $Dotnet run --project (Join-Path $root 'tests\OpenGG.Checks\OpenGG.Checks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Offline C# checks failed.' }
& $Dotnet publish (Join-Path $root 'src\OpenGG.Desktop\OpenGG.Desktop.csproj') -c Release -r win-x64 --self-contained (-not $FrameworkDependent).ToString().ToLowerInvariant() -o $portable
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in 'README.md','LICENSE','THIRD_PARTY_NOTICES.md','OpenGG.gif','OpenGG Typography.svg','OpenGG Isotype.svg','OpenGG Full Logo.svg','OpenGG Secondary Logo.svg') { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $portable -Force }
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $portable -Recurse -Force
if (-not $WithoutResearchTools) { Copy-Item -LiteralPath (Join-Path $root 'tooling\installers') -Destination (Join-Path $portable 'tooling') -Recurse -Force }
$zipName = if ($WithoutResearchTools) { 'artifacts\OpenGG-0.1.0-win-x64-minimal.zip' } else { 'artifacts\OpenGG-0.1.0-win-x64.zip' }
$zip = Join-Path $root $zipName
$temporaryZip = $zip + '.tmp'
if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($temporaryZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $portable -File -Recurse) {
        $relative = $file.FullName.Substring($portable.Length+1).Replace('\','/')
        if ($relative.StartsWith('tooling/runtime/')) { continue } # Generated launchers refer to this machine's install path.
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
Write-Output "Portable app: $portable\OpenGG.exe"
