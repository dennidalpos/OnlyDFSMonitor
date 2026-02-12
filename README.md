# DFS Monitor (.NET 8)

Centralized Windows Server 2022 monitoring for DFS Namespaces and DFS Replication.

## Projects
- `src/DfsMonitor.Shared`: shared models, JSON config, UNC-aware storage.
- `src/DfsMonitor.Service`: Windows Worker service collector.
- `src/DfsMonitor.Web`: authenticated API + Razor pages UI.
- `tests/DfsMonitor.Tests`: config/status storage tests.

## Build
```bash
dotnet restore
dotnet build DfsMonitor.sln
dotnet test DfsMonitor.sln
```

## Run locally
```bash
dotnet run --project src/DfsMonitor.Service
dotnet run --project src/DfsMonitor.Web
```

## Authentication
Web/API uses Negotiate auth (`Microsoft.AspNetCore.Authentication.Negotiate`).
For non-domain dev, swap to JWT in `src/DfsMonitor.Web/Program.cs`.

## Permissions
Run service under domain service account/gMSA with:
- Read on DFS namespace/replication metadata.
- WinRM remote query rights to DFS member servers.
- Read access to `DFS Replication` event log remotely.
- RW on config and status UNC shares.

See `docs/runbook.md` for full details.
