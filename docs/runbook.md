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
- PowerShell: `powershell -ExecutionPolicy Bypass -File scripts/clean-build.ps1`
- PowerShell + test: `powershell -ExecutionPolicy Bypass -File scripts/clean-build.ps1 -RunTests`
- PowerShell interattiva guidata: `powershell -ExecutionPolicy Bypass -File scripts/build-interactive.ps1`
- Test suite includes in-memory Web/API integration coverage (JWT auth, collect-now queueing, status/report endpoints) under `tests/DfsMonitor.Tests/WebApiIntegrationTests.cs`.

## Required ports/firewall
- WinRM: TCP 5985 (HTTP) / 5986 (HTTPS)
- SMB: TCP 445 to namespace targets (reachability checks)
- LDAP/AD DS ports if DFSN discovery via AD is required
- HTTP/HTTPS port used by web app (Kestrel or behind IIS reverse proxy)

## Auth modes (Web/API)
- **Negotiate (default)**:
  - `Auth:Mode=Negotiate` (or omitted).
- **JWT placeholder mode**:
  - `Auth:Mode=Jwt`
  - `Auth:Jwt:Issuer=<issuer>`
  - `Auth:Jwt:Audience=<audience>`
  - `Auth:Jwt:SigningKey=<shared-secret>`
  - Use `GET /api/auth/jwt-placeholder` to validate effective JWT settings before integrating an identity provider.

## Operations
- Trigger immediate collection: `POST /api/collect/now`.
  - The service now wakes up on queue file changes (FileSystemWatcher) and does not wait for the full polling interval.
- Check service/runtime: `GET /api/service/status`.
- Start/stop service remotely from UI or API:
  - `POST /api/service/start`
  - `POST /api/service/stop`
  - Non-Windows hosts return an explicit `501` ProblemDetails response.

## Configuration UI
`/Config` now supports:
- Collection settings: polling/timeout/retry/parallelism/event sample count.
- Threshold settings: unreachable targets and backlog warn/critical values.
- Full storage settings: config UNC, status root, cache root, runtime state path, command queue path.
- DFS-R group controls: autodiscovery toggle + explicit group list.

## Troubleshooting
- Check service logs in `logs/service-*.log` and Windows Event Log source `DfsMonitor.Service`.
- If UNC unavailable, verify local cache under `LocalCacheRootPath` and pending sync files under `pending-sync`.
- Validate DFS commands on collector host:
  - `Get-DfsnFolder -Path "\\domain\\namespace\\*"`
  - `Get-DfsnFolderTarget -Path "\\domain\\namespace\\folder"`
  - `Get-DfsReplicationGroup`
  - `Get-DfsrMember -GroupName <group>`
  - `Get-DfsrConnection -GroupName <group>`
  - `Get-DfsrBacklog -GroupName <group> -SourceComputerName <src> -DestinationComputerName <dst>`
  - `Get-WinEvent -LogName 'DFS Replication' -ComputerName <member> -MaxEvents 20`
- Backlog note:
  - Primary mode uses real DFS-R connections (`Get-DfsrConnection`) for source/destination pairs.
  - If connection discovery fails, collector falls back to adjacency pairing for resilience.
