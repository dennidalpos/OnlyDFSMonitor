# Runbook

## Installation (MSI-free)
1. Publish binaries on central monitoring server:
   - `dotnet publish src/DfsMonitor.Service -c Release -r win-x64 --self-contained false`
   - `dotnet publish src/DfsMonitor.Web -c Release`
2. Create service:
   - `powershell -ExecutionPolicy Bypass -File scripts/install.ps1 -ServiceExe C:\DfsMonitor\DfsMonitor.Service.exe`
3. Assign account:
   - `powershell -ExecutionPolicy Bypass -File scripts/set-service-account.ps1 -Account DOMAIN\gmsaDfsMonitor$`

## Build pulita
- Bash: `./scripts/clean-build.sh`
- Bash + test: `RUN_TESTS=1 ./scripts/clean-build.sh`
- PowerShell: `powershell -ExecutionPolicy Bypass -File scripts/clean-build.ps1`
- PowerShell + test: `powershell -ExecutionPolicy Bypass -File scripts/clean-build.ps1 -RunTests`

## Required ports/firewall
- WinRM: TCP 5985 (HTTP) / 5986 (HTTPS)
- SMB: TCP 445 to namespace targets (reachability checks)
- LDAP/AD DS ports if DFSN discovery via AD is required
- HTTP/HTTPS port used by web app (Kestrel or behind IIS reverse proxy)

## Operations
- Trigger immediate collection: `POST /api/collect/now`.
- Check service/runtime: `GET /api/service/status`.
- Start/stop service remotely from UI or API:
  - `POST /api/service/start`
  - `POST /api/service/stop`

## Troubleshooting
- Check service logs in `logs/service-*.log` and Windows Event Log source `DfsMonitor.Service`.
- If UNC unavailable, verify local cache under `LocalCacheRootPath` and pending sync files under `pending-sync`.
- Validate DFS commands on collector host:
  - `Get-DfsnFolder -Path "\\domain\\namespace\\*"`
  - `Get-DfsnFolderTarget -Path "\\domain\\namespace\\folder"`
  - `Get-DfsReplicationGroup`
  - `Get-DfsrMember -GroupName <group>`
  - `Get-DfsrBacklog -GroupName <group> -SourceComputerName <src> -DestinationComputerName <dst>`
  - `Get-WinEvent -LogName 'DFS Replication' -ComputerName <member> -MaxEvents 20`
- Backlog fallback note:
  - If backlog retrieval is blocked, API sets `BacklogState = Unknown` with details.
