# Runbook

## Installation (MSI-free)
1. Publish binaries on central monitoring server:
   - `dotnet publish src/DfsMonitor.Service -c Release -r win-x64 --self-contained false`
   - `dotnet publish src/DfsMonitor.Web -c Release`
2. Create service:
   - `powershell -ExecutionPolicy Bypass -File scripts/install.ps1 -ServiceExe C:\DfsMonitor\DfsMonitor.Service.exe`
3. Assign account:
   - `powershell -ExecutionPolicy Bypass -File scripts/set-service-account.ps1 -Account DOMAIN\gmsaDfsMonitor$`

## Required ports/firewall
- WinRM: TCP 5985 (HTTP) / 5986 (HTTPS)
- SMB: TCP 445 to namespace targets (reachability checks)
- LDAP/AD DS ports if DFSN discovery via AD is required
- HTTP/HTTPS port used by web app (Kestrel or behind IIS reverse proxy)

## Troubleshooting
- Check service logs in `logs/service-*.log` and Windows Event Log source `DfsMonitor.Service`.
- If UNC unavailable, verify local cache under configured `LocalCacheRootPath` and network share ACL.
- Validate DFS commands on collector host:
  - `Get-DfsnFolder -Path "\\domain\\namespace\\*"`
  - `Get-DfsnFolderTarget -Path "\\domain\\namespace\\folder"`
  - `Get-DfsReplicationGroup`
  - `Get-DfsrMember -GroupName <group>`
  - `Get-WinEvent -LogName 'DFS Replication' -ComputerName <member> -MaxEvents 20`
- Backlog fallback note:
  - If `Get-DfsrBacklog`/`dfsrdiag backlog` is blocked, API exposes `BacklogState = Unknown` with detail string.
