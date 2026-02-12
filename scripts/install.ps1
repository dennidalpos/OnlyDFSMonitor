param(
  [string]$ServiceName = "DfsMonitor.Service",
  [string]$ServiceExe = "C:\DfsMonitor\DfsMonitor.Service.exe"
)

if (!(Test-Path $ServiceExe)) { throw "Service executable not found: $ServiceExe" }
sc.exe create $ServiceName binPath= "`"$ServiceExe`"" start= auto DisplayName= "DFS Monitor Service"
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/30000
Start-Service $ServiceName
Write-Host "Installed and started $ServiceName"
