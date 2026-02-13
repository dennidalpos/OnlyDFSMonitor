# Runbook operativo

## Installazione
1. Pubblica binari:
   - `dotnet publish src/OnlyDFSMonitor.Service -c Release -r win-x64`
   - `dotnet publish src/OnlyDFSMonitor.Desktop -c Release -r win-x64`
2. Installa servizio:
   - `powershell -ExecutionPolicy Bypass -File scripts/install-service.ps1`
3. Avvia servizio:
   - `powershell -ExecutionPolicy Bypass -File scripts/start-service.ps1`

## Operazioni standard
- Start: `sc.exe start OnlyDFSMonitorService`
- Stop: `sc.exe stop OnlyDFSMonitorService`
- Status: `sc.exe query OnlyDFSMonitorService`
- Collect now: creare file `collect-now.json` nel path command configurato.

## Permessi richiesti
- Utente servizio con privilegi lettura DFSN/DFSR.
- Accesso WinRM/PowerShell remoto ai server DFS monitorati.
- Permesso scrittura su path locali e (se usato) UNC.
