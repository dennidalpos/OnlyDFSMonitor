param(
  [string]$ServiceName = "DfsMonitor.Service",
  [string]$Account = "DOMAIN\\gmsaDfsMonitor$",
  [string]$Password = ""
)

$ErrorActionPreference = "Stop"

if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
  throw "Service not found: $ServiceName"
}

# For gMSA, password must remain an empty string.
sc.exe config $ServiceName obj= $Account password= $Password | Out-Null
Restart-Service -Name $ServiceName

Write-Host "Updated service account for $ServiceName"
