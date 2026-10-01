param([string]$Out = (Join-Path (Get-Location) 'opengg-hid-inventory.json'))
$ErrorActionPreference = 'Stop'
$app = Join-Path (Split-Path $PSScriptRoot -Parent) 'OpenGG.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Run this script from the portable OpenGG tools folder, or use OpenGG.exe --inspect --out inventory.json.' }
& $app --inspect --out $Out | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'HID inventory failed.' }
Write-Output $Out
