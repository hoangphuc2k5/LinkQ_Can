# Chạy Local Client Service (mỗi máy client một lần).
param(
  [Parameter(Mandatory)] [ValidateSet('Machine A','Machine B')] [string]$ClientId,
  [Parameter(Mandatory)] [string]$ServerUrl,                                # vd. http://192.168.1.10:5000
  [Parameter(Mandatory)] [string]$ApiKey
)
$root = Split-Path $PSScriptRoot -Parent
$env:Local__ClientId = $ClientId
$env:Local__ServerUrl = $ServerUrl
$env:Local__ServerApiKey = $ApiKey
$dataDir = Join-Path $env:LOCALAPPDATA 'WeighingSystem'
New-Item -ItemType Directory -Force $dataDir | Out-Null
$env:Local__DatabasePath = Join-Path $dataDir 'weighing-local.db'           # DB bền vững, tồn tại qua các lần khởi động
dotnet run --project "$root\backend\Weighing.Local.Api" -c Release
