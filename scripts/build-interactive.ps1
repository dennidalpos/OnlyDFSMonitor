param(
  [string]$Runtime = "win-x64",
  [string]$OutputRoot = "publish"
)

$ErrorActionPreference = "Stop"

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host " DFS Monitor - Publish Release" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet non trovato nel PATH. Installare .NET SDK 8.x e rieseguire lo script."
}

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$publishRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $repoRoot $OutputRoot }
$serviceOutput = Join-Path $publishRoot "service"
$webOutput = Join-Path $publishRoot "web"

New-Item -ItemType Directory -Force -Path $serviceOutput | Out-Null
New-Item -ItemType Directory -Force -Path $webOutput | Out-Null

Write-Host "`n[step] Publish servizio (Release, $Runtime)" -ForegroundColor Green
& dotnet publish "$repoRoot/src/DfsMonitor.Service/DfsMonitor.Service.csproj" -c Release -r $Runtime --self-contained false -o $serviceOutput

Write-Host "`n[step] Publish web app (Release)" -ForegroundColor Green
& dotnet publish "$repoRoot/src/DfsMonitor.Web/DfsMonitor.Web.csproj" -c Release -o $webOutput

Write-Host "`nPublish completata." -ForegroundColor Cyan
Write-Host "- Service output: $serviceOutput"
Write-Host "- Web output: $webOutput"
