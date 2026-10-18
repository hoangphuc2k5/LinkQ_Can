param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location "$root\backend"
dotnet build Weighing.sln -c Release
if (-not $SkipTests) { dotnet test Weighing.sln -c Release --no-build }
Pop-Location
Push-Location "$root\frontend\weighing-angular"; npm run build; Pop-Location
