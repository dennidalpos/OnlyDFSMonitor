param(
  [string]$ServiceName = "DfsMonitor.Service"
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
  Write-Host "Service not found: $ServiceName"
  exit 0
}

if ($service.Status -ne "Stopped") {
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
}

sc.exe delete $ServiceName | Out-Null
Write-Host "Uninstalled $ServiceName"
