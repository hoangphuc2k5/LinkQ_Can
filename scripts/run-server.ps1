# Chạy Central Backend (máy SERVER).
param(
  [Parameter(Mandatory)] [string]$ApiKey,                                   # >= 16 ký tự, dùng chung với 2 máy client
  [string]$ConnectionString = 'Server=localhost\SQLEXPRESS;Database=WeighingDb;Trusted_Connection=True;TrustServerCertificate=True',
  [string]$Urls = 'http://0.0.0.0:5000'
)
$root = Split-Path $PSScriptRoot -Parent
$env:Auth__ApiKey = $ApiKey
$env:ConnectionStrings__Default = $ConnectionString
$env:Urls = $Urls
$env:Database__AutoMigrate = 'true'
dotnet run --project "$root\backend\Weighing.Server.Api" -c Release
