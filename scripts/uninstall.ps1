param([string]$ServiceName = "DfsMonitor.Service")
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Null
  Write-Host "Uninstalled $ServiceName"
} else {
  Write-Host "Service not found: $ServiceName"
}
