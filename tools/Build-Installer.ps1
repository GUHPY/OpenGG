[CmdletBinding()]
param(
    [switch]$Lite,
    [switch]$Full,
    [switch]$RebuildApp,
    [string]$Dotnet
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Locate .NET 10 SDK if rebuilding application
if (-not $Dotnet) {
    $userDotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
    if (Test-Path $userDotnet) { $Dotnet = $userDotnet }
    else { $Dotnet = 'dotnet' }
}

# Locate Inno Setup 6 compiler
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    'ISCC.exe'
)
$iscc = $null
foreach ($candidate in $isccCandidates) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        $iscc = $candidate
        break
    }
    if (Test-Path $candidate) {
        $iscc = $candidate
        break
    }
}

if (-not $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) not found. Please install Inno Setup 6."
}

Write-Host "Using Inno Setup compiler: $iscc" -ForegroundColor Cyan

# 1. Ensure installer wizard bitmaps exist
$assetsDir = Join-Path $root 'tools\installer-assets'
$wizardBmp = Join-Path $assetsDir 'wizard.bmp'
$wizardSmallBmp = Join-Path $assetsDir 'wizard-small.bmp'

if (-not (Test-Path $wizardBmp) -or -not (Test-Path $wizardSmallBmp)) {
    Write-Host "Generating installer brand bitmaps..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
    
    Add-Type -AssemblyName PresentationCore, WindowsBase
    $isoPath = Join-Path $root 'src\OpenGG.Desktop\Assets\Brand\OpenGG.png'
    $isoBytes = [IO.File]::ReadAllBytes($isoPath)
    $stream = [IO.MemoryStream]::new($isoBytes)
    $decoder = [Windows.Media.Imaging.PngBitmapDecoder]::new($stream, [Windows.Media.Imaging.BitmapCreateOptions]::None, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $isoFrame = $decoder.Frames[0]

    # Small 55x55 bitmap
    $visSmall = [Windows.Media.DrawingVisual]::new()
    $dcSmall = $visSmall.RenderOpen()
    $dcSmall.DrawImage($isoFrame, [Windows.Rect]::new(3, 3, 49, 49))
    $dcSmall.Close()
    $rtbSmall = [Windows.Media.Imaging.RenderTargetBitmap]::new(55, 55, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $rtbSmall.Render($visSmall)
    $bmpSmall = [Windows.Media.Imaging.BmpBitmapEncoder]::new()
    $bmpSmall.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtbSmall))
    $outSmall = [IO.File]::Create($wizardSmallBmp)
    $bmpSmall.Save($outSmall)
    $outSmall.Dispose()

    # Large 164x314 bitmap
    $visLarge = [Windows.Media.DrawingVisual]::new()
    $dcLarge = $visLarge.RenderOpen()
    $bgBrush = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromArgb(255, 14, 16, 20))
    $dcLarge.DrawRectangle($bgBrush, $null, [Windows.Rect]::new(0, 0, 164, 314))
    $dcLarge.DrawImage($isoFrame, [Windows.Rect]::new(32, 70, 100, 100))
    $dcLarge.Close()
    $rtbLarge = [Windows.Media.Imaging.RenderTargetBitmap]::new(164, 314, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $rtbLarge.Render($visLarge)
    $bmpLarge = [Windows.Media.Imaging.BmpBitmapEncoder]::new()
    $bmpLarge.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtbLarge))
    $outLarge = [IO.File]::Create($wizardBmp)
    $bmpLarge.Save($outLarge)
    $outLarge.Dispose()
    $stream.Dispose()
}

# 2. Rebuild app binaries if requested or if missing
$portable = Join-Path $root 'artifacts\portable'
$portableMinimal = Join-Path $root 'artifacts\portable-minimal'

if ($RebuildApp -or -not (Test-Path "$portable\OpenGG.exe")) {
    Write-Host "Publishing self-contained portable application..." -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\build.ps1') -Dotnet $Dotnet
    if ($LASTEXITCODE -ne 0) { throw "Build of portable package failed." }
}

if ($RebuildApp -or -not (Test-Path "$portableMinimal\OpenGG.exe")) {
    Write-Host "Publishing minimal portable application..." -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\build.ps1') -WithoutResearchTools -Dotnet $Dotnet
    if ($LASTEXITCODE -ne 0) { throw "Build of minimal package failed." }
}

$issFile = Join-Path $root 'tools\OpenGG.iss'
$buildLite = $Lite -or (-not $Full)
$buildFull = $Full -or (-not $Lite)

if ($buildLite) {
    Write-Host "`nCompiling OpenGG Setup (Lite - App Only)..." -ForegroundColor Green
    & $iscc /DMinimal=1 $issFile
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation of Lite installer failed." }
}

if ($buildFull) {
    Write-Host "`nCompiling OpenGG Setup (Full - With Offline Tools)..." -ForegroundColor Green
    & $iscc $issFile
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation of Full installer failed." }
}

Write-Host "`nGenerated Installers in artifacts/:" -ForegroundColor Green
Get-ChildItem (Join-Path $root 'artifacts\*Setup*.exe') | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    [PSCustomObject]@{
        Name = $_.Name
        SizeMB = [Math]::Round($_.Length / 1MB, 2)
        SHA256 = $hash
    }
} | Format-Table -AutoSize
