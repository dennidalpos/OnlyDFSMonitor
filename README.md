# OnlyDFSMonitor

OnlyDFSMonitor è una soluzione .NET per monitorare ambienti DFS Namespace (DFS-N) e DFS Replication (DFS-R) in modalità desktop, con supporto opzionale a un servizio Windows per la raccolta periodica.

La repository è organizzata in modo da coprire:
- configurazione operativa via UI;
- persistenza JSON di configurazione/snapshot;
- trigger di raccolta “collect-now” file-based;
- automazione build/clean/test tramite script PowerShell.

## Architettura della soluzione

### Progetti principali

- `src/OnlyDFSMonitor.Core`
  - modelli applicativi (`MonitorConfiguration`, `Snapshot`, `HealthState`);
  - engine di raccolta (`CollectionEngine`);
  - persistenza JSON (`JsonFileStore`);
  - integrazione con `sc.exe` tramite `ServiceManager`.

- `src/OnlyDFSMonitor.Desktop`
  - applicazione WPF per gestione operativa;
  - salvataggio/import/export configurazione;
  - import snapshot e filtri visuali basati su checkbox;
  - comandi di gestione servizio (`install/start/stop`) e collect-now con fallback locale.

- `src/OnlyDFSMonitor.Service`
  - worker background;
  - lettura configurazione persistita;
  - gestione trigger `collect-now`;
  - produzione snapshot periodico.

- `src/OnlyDFSMonitor.ServiceControl.Cli`
  - utility CLI per pilotare il servizio (`install`, `start`, `stop`, `status`).

- `tests/OnlyDFSMonitor.Tests`
  - test unitari su engine e persistenza;
  - test integrazione minimo sul ciclo collect-now.

## Requisiti

### Sviluppo
- .NET SDK 8.x (`dotnet --info`).
- PowerShell 7+ (`pwsh`) consigliato per script.
- Windows consigliato per esecuzione completa delle parti service/DFS.

### Runtime operativo
- ACL in scrittura sui path usati da config/snapshot/commands.
- Permessi coerenti con interrogazione DFS e gestione servizio.
- In caso di servizio Windows, account con privilegi adeguati.

## Configurazione applicativa

I path principali sono definiti in `StorageOptions`:
- `ConfigPath`: file JSON con configurazione monitor.
- `SnapshotPath`: file JSON dell’ultimo snapshot.
- `CommandPath`: trigger file `collect-now`.
- `OptionalUncRoot`: UNC opzionale per uso operativo.

Default:
- `C:\OnlyDFSMonitor\config\config.json`
- `C:\OnlyDFSMonitor\status\latest.json`
- `C:\OnlyDFSMonitor\commands\collect-now.json`
- `\\fileserver\dfs-monitor`

## Import / Export

### Configurazione

Dalla UI desktop sono disponibili due flussi espliciti:
- **Import Config**
  - apre un file JSON;
  - valida il payload con deserializzazione strict;
  - applica i valori importati ai controlli UI.

- **Export Config**
  - serializza la configurazione corrente della UI;
  - salva in un file scelto dall’operatore.

### Snapshot

- **Import Snapshot for Filtering**
  - carica uno snapshot JSON;
  - aggiorna la vista “Filtered Snapshot”;
  - applica i filtri delle checkbox di stato.

### Affidabilità I/O JSON

La persistenza usa write atomica:
1. scrittura su file temporaneo;
2. sostituzione del file target.

Per la lettura:
- `LoadAsync`: resiliente, usa fallback su file assente o JSON invalido.
- `LoadStrictAsync`: rigoroso, genera eccezione su file assente o payload non valido.

## Logica checkbox e filtri (Desktop)

### Checkbox di configurazione

- **Auto-discover DFS-R groups**
  - checked: i gruppi DFS-R vengono scoperti automaticamente dall’host collector;
  - unchecked: vengono usati solo i gruppi manuali inseriti nel textbox dedicato.

- **Include disabled namespaces during local collect**
  - checked: anche namespace marcati `[ ]` vengono inclusi nella raccolta locale;
  - unchecked: i namespace disabilitati vengono esclusi.

### Parsing namespace con flag enabled/disabled

Il textbox Namespace supporta linee nel formato:
- `[x] \\domain\dfs\Public` (abilitato)
- `[ ] \\domain\dfs\Legacy` (disabilitato)

Il parser normalizza righe vuote/spazi e converte in `NamespaceDefinition` coerenti.

### Checkbox filtri stato snapshot

Nella sezione filtri snapshot:
- `OK`
- `Warn`
- `Critical`
- `Unknown`

Ogni checkbox controlla la visibilità di namespace e backlog DFS-R con lo stato corrispondente nella vista “Filtered Snapshot”.

## DFS-R auto-discovery

Quando la configurazione non contiene gruppi DFS-R manuali, `CollectionEngine` tenta:
1. `pwsh`;
2. fallback a `powershell`.

Comando usato:

```powershell
Get-DfsReplicationGroup -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty GroupName |
  ConvertTo-Json -Depth 4
```

Se il comando fallisce o non produce output valido, la raccolta continua senza bloccare l’intero snapshot.

## Build, test e clean

## Build script (`scripts/build.ps1`)

Parametri supportati:
- `-Configuration` (`Debug` | `Release`, default `Release`)
- `-SkipTests`
- `-NoRestore`
- `-NoBuild`
- `-Framework <TFM>`
- `-Runtime <RID>`

Esempi:

```powershell
pwsh ./scripts/build.ps1
pwsh ./scripts/build.ps1 -Configuration Debug
pwsh ./scripts/build.ps1 -NoRestore -SkipTests
pwsh ./scripts/build.ps1 -Framework net8.0 -Runtime win-x64
```

## Clean script (`scripts/clean.ps1`)

Parametri supportati:
- `-Configuration` (`Debug` | `Release` | `Both`, default `Both`)
- `-RemovePackages` (rimuove cache locale `.nuget` nel repo)
- `-KeepTestResults` (mantiene cartelle `TestResults`)

Lo script esegue:
1. `dotnet clean` per le configurazioni richieste;
2. rimozione cartelle build artifacts (`bin`, `obj`, opzionalmente `TestResults`);
3. rimozione cache locale opzionale.

Esempi:

```powershell
pwsh ./scripts/clean.ps1
pwsh ./scripts/clean.ps1 -Configuration Release
pwsh ./scripts/clean.ps1 -KeepTestResults
pwsh ./scripts/clean.ps1 -RemovePackages
```

## Esecuzione locale

### Desktop

```bash
dotnet run --project src/OnlyDFSMonitor.Desktop
```

### Worker service (modalità debug)

```bash
dotnet run --project src/OnlyDFSMonitor.Service
```

### CLI controllo servizio

```bash
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- status
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- install
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- start
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- stop
```

## Troubleshooting operativo

- **Import config/snapshot fallisce**
  - verificare formato JSON valido;
  - verificare permessi lettura del file sorgente.

- **Export o Save config fallisce**
  - verificare ACL sulla cartella destinazione;
  - verificare eventuali lock sul file target.

- **Collect-now locale non produce snapshot**
  - verificare path snapshot e command;
  - verificare che i namespace non siano stati esclusi dal filtro disabled.

- **Nessun gruppo DFS-R rilevato**
  - verificare disponibilità cmdlet DFS-R sull’host;
  - verificare contesto di esecuzione e privilegi.

- **Comandi servizio con exit code non zero**
  - verificare esistenza servizio `OnlyDFSMonitorService`;
  - eseguire terminale con privilegi amministrativi.
