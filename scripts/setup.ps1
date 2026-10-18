# Kiểm tra công cụ và cài phụ thuộc. Chạy: powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($t in 'dotnet','node','npm') {
  if (-not (Get-Command $t -ErrorAction SilentlyContinue)) { throw "Thiếu '$t'. Cài .NET 8 SDK (https://dotnet.microsoft.com) và Node.js 20+ (https://nodejs.org)." }
}
Write-Host "dotnet $(dotnet --version) | node $(node --version)"
Push-Location "$root\backend"; dotnet restore; Pop-Location
Push-Location "$root\frontend\weighing-angular"; npm install; Pop-Location
Write-Host 'Xong. Máy SERVER cần thêm SQL Server (Express/Developer) - xem README.' -ForegroundColor Green
