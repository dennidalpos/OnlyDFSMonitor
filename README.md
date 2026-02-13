# OnlyDFSMonitor (.NET 8, Greenfield Desktop + Windows Service)

Questo repository è stato ricreato **da zero** in modalità greenfield.

## Scelta framework UI
**WPF** è stato scelto in modo netto perché:
- è stabile e nativo su Windows Server 2022 / Windows 10+;
- supporta facilmente tool amministrativi on-prem;
- riduce complessità rispetto a MAUI/WinUI 3 per un'app desktop enterprise classica.

## Nuova solution structure
- `src/OnlyDFSMonitor.Core`: modelli dominio, persistenza JSON, engine di collection, controllo servizio.
- `src/OnlyDFSMonitor.Service`: Windows Worker Service (scheduler + collect-now file trigger + persistenza snapshot).
- `src/OnlyDFSMonitor.Desktop`: UI WPF per dashboard/config/service control/collect-now.
- `src/OnlyDFSMonitor.ServiceControl.Cli`: utility CLI per install/start/stop/status del servizio.
- `tests/OnlyDFSMonitor.Tests`: unit e integration test di base.

## Security model (UI locale)
- esecuzione consigliata con utenza in gruppo locale amministratori;
- operazioni servizio demandate a `sc.exe` (richiede privilegi elevati);
- configurazione su filesystem locale e opzionale mirror UNC.

## Build & test
```bash
dotnet restore DfsMonitor.sln
dotnet build DfsMonitor.sln
dotnet test DfsMonitor.sln
```

## Avvio rapido
- Desktop: `dotnet run --project src/OnlyDFSMonitor.Desktop`
- Service (debug): `dotnet run --project src/OnlyDFSMonitor.Service`
- CLI service control: `dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- status`
