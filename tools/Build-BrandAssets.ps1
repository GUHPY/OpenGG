[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run this script with powershell.exe -STA -File tools/Build-BrandAssets.ps1.'
}
Add-Type -AssemblyName PresentationCore,WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$brand = Join-Path $root 'src\OpenGG.Desktop\Assets\Brand'
$docs = Join-Path $root 'docs\assets'
New-Item -ItemType Directory -Path $brand,$docs -Force | Out-Null
$invariant = [Globalization.CultureInfo]::InvariantCulture

# The four owner-supplied SVGs contain paths, rounded rectangles and circles only.
# WPF renders these native geometries without a browser or a new image dependency.
function Logo-Drawing([string]$file, [string]$ink) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create((Join-Path $root $file),$settings)
    $xml = [Xml.XmlDocument]::new()
    $xml.XmlResolver = $null
    try { $xml.Load($reader) } finally { $reader.Dispose() }
    $bounds = $xml.DocumentElement.GetAttribute('viewBox').Split(' ') | ForEach-Object { [double]::Parse($_,$invariant) }
    $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($ink))
    $drawing = [Windows.Media.DrawingGroup]::new()
    $context = $drawing.Open()
    try {
        foreach ($node in $xml.SelectNodes('//*[local-name()="path" or local-name()="rect" or local-name()="circle"]')) {
            switch ($node.LocalName) {
                'path' { $context.DrawGeometry($brush,$null,[Windows.Media.Geometry]::Parse('F1 ' + $node.GetAttribute('d'))) }
                'rect' {
                    $rect = [Windows.Rect]::new([double]::Parse($node.GetAttribute('x'),$invariant),[double]::Parse($node.GetAttribute('y'),$invariant),[double]::Parse($node.GetAttribute('width'),$invariant),[double]::Parse($node.GetAttribute('height'),$invariant))
                    $radius = if ($node.HasAttribute('rx')) { [double]::Parse($node.GetAttribute('rx'),$invariant) } else { 0 }
                    if ($node.GetAttribute('class') -eq 'cls-1') { $context.DrawRoundedRectangle($null,[Windows.Media.Pen]::new($brush,79.22),$rect,$radius,$radius) }
                    else { $context.DrawRoundedRectangle($brush,$null,$rect,$radius,$radius) }
                }
                'circle' {
                    $point = [Windows.Point]::new([double]::Parse($node.GetAttribute('cx'),$invariant),[double]::Parse($node.GetAttribute('cy'),$invariant))
                    $radius = [double]::Parse($node.GetAttribute('r'),$invariant)
                    $context.DrawEllipse($brush,$null,$point,$radius,$radius)
                }
            }
        }
    } finally { $context.Close() }
    $drawing.Freeze()
    [pscustomobject]@{Drawing=$drawing; Width=$bounds[2]; Height=$bounds[3]}
}
function Logo-Png($logo, [int]$width, [int]$height, [bool]$tile = $false) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $padding = if ($tile) { $width * .16 } else { 0 }
        if ($tile) { $context.DrawRoundedRectangle([Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#171719')),$null,[Windows.Rect]::new(0,0,$width,$height),$width*.2,$height*.2) }
        $scale = [Math]::Min(($width-2*$padding)/$logo.Width,($height-2*$padding)/$logo.Height)
        $context.PushTransform([Windows.Media.TranslateTransform]::new(($width-$logo.Width*$scale)/2,($height-$logo.Height*$scale)/2))
        $context.PushTransform([Windows.Media.ScaleTransform]::new($scale,$scale))
        $context.DrawDrawing($logo.Drawing)
        $context.Pop(); $context.Pop()
    } finally { $context.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}

$type = Logo-Drawing 'OpenGG Typography.svg' '#FFFFFF'
[IO.File]::WriteAllBytes((Join-Path $brand 'Typography.png'),(Logo-Png $type 900 203))
$iso = Logo-Drawing 'OpenGG Isotype.svg' '#FFFFFF'
[IO.File]::WriteAllBytes((Join-Path $brand 'OpenGG.png'),(Logo-Png $iso 512 512 $true))
foreach ($entry in @(@('OpenGG Full Logo.svg','opengg-full',1200),@('OpenGG Secondary Logo.svg','opengg-secondary',640))) {
    foreach ($variant in @(@('#111113','black'),@('#FFFFFF','white'))) {
        $logo = Logo-Drawing $entry[0] $variant[0]
        $width = [int]$entry[2]
        $height = [int][Math]::Ceiling($width*$logo.Height/$logo.Width)
        [IO.File]::WriteAllBytes((Join-Path $docs ($entry[1] + '-' + $variant[1] + '.png')),(Logo-Png $logo $width $height))
    }
}
$sizes = @(16,24,32,48,64,128,256)
$frames = @($sizes | ForEach-Object { ,(Logo-Png $iso $_ $_ $true) })
$stream = [IO.File]::Create((Join-Path $brand 'OpenGG.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Write-Output 'Built the white sidebar logo, seven-size Windows icon and light/dark README logos.'
