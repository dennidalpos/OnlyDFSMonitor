# DFS Monitor (.NET 8)

Centralized Windows Server 2022 monitoring for DFS Namespaces (DFS-N) and DFS Replication (DFS-R).

## Projects
- `src/DfsMonitor.Shared`: shared models, storage, runtime state, command queue.
- `src/DfsMonitor.Service`: Windows Worker service collector.
- `src/DfsMonitor.Web`: authenticated API + Razor pages UI.
- `tests/DfsMonitor.Tests`: unit + integration tests (storage/runtime/queue, orchestrator, Web API in-memory).

## Build
```bash
dotnet restore
dotnet build DfsMonitor.sln
dotnet test DfsMonitor.sln
```

## Publish release app
```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-interactive.ps1
```
Crea direttamente i pacchetti release in `publish/service` e `publish/web`.

## Run locally
```bash
dotnet run --project src/DfsMonitor.Service
dotnet run --project src/DfsMonitor.Web
```

## Implemented API
- `GET /api/health`
- `GET/PUT /api/config`
- `POST /api/collect/now` (queues file-based manual command consumed by service)
- `GET /api/service/status`
- `POST /api/service/start`
- `POST /api/service/stop`
- `GET /api/status/latest`
- `GET /api/status/namespaces/{id}`
- `GET /api/status/dfsr/{id}`
- `GET /api/report/latest.json`
- `GET /api/report/latest.csv`

## Script utili
```powershell
# Build non-interattiva
powershell -ExecutionPolicy Bypass -File scripts/clean-build.ps1
powershell -ExecutionPolicy Bypass -File scripts/clean-build.ps1 -RunTests

# Publish release diretto (senza prompt)
powershell -ExecutionPolicy Bypass -File scripts/build-interactive.ps1
```

## Authentication
Web/API supports two modes:
- `Negotiate` (default) via `Microsoft.AspNetCore.Authentication.Negotiate`
- `Jwt` placeholder mode (configurable `Auth:Jwt:*`) for integration with external IdP

## Permissions
Run service under domain service account/gMSA with:
- Read on DFS namespace/replication metadata.
- WinRM/PowerShell remoting rights to DFS member servers where needed.
- Read access to `DFS Replication` event log remotely.
- RW on config and status UNC shares.

See `docs/runbook.md` for full details.


## UI Configurazione servizio/web
Nella pagina `/config` è disponibile una sezione dedicata a:
- installazione del servizio Windows (nome servizio, display name, percorso exe);
- salvataggio dei parametri operativi del web server (`ASPNETCORE_URLS`, auth mode, JWT settings).

## Prompt di porting
- Prompt pronto all'uso: `docs/porting-app-prompt.md`
