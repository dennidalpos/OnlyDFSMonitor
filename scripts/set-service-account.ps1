param(
  [string]$ServiceName = "DfsMonitor.Service",
  [string]$Account = "DOMAIN\\gmsaDfsMonitor$",
  [string]$Password = ""
)
# For gMSA, password must be empty string.
sc.exe config $ServiceName obj= $Account password= $Password
Restart-Service $ServiceName
Write-Host "Updated service account for $ServiceName"
