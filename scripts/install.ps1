param(
  [string]$ServiceName = "DfsMonitor.Service",
  [string]$ServiceExe = "C:\DfsMonitor\DfsMonitor.Service.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ServiceExe)) {
  throw "Service executable not found: $ServiceExe"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  throw "Service already exists: $ServiceName"
}

sc.exe create $ServiceName binPath= "`"$ServiceExe`"" start= auto DisplayName= "DFS Monitor Service" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
Start-Service -Name $ServiceName

Write-Host "Installed and started $ServiceName"
