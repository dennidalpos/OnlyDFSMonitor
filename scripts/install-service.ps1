$ErrorActionPreference = 'Stop'
$serviceName = 'OnlyDFSMonitorService'
$binaryPath = 'C:\OnlyDFSMonitor\OnlyDFSMonitor.Service.exe'
sc.exe create $serviceName binPath= "\"$binaryPath\"" start= auto
Write-Host "Service created: $serviceName"
