# OnlyDFSMonitor

Applicazione **desktop Windows** per il monitoraggio di DFS Namespace (DFS-N) e DFS Replication (DFS-R), con un servizio Windows **opzionale** per l'esecuzione periodica.

Il progetto è pensato per funzionare in due modalità:
- **Desktop-only (senza servizio installato)**: l'operatore avvia raccolte manuali dalla UI.
- **Desktop + Service**: il servizio esegue raccolte pianificate e gestisce i trigger `collect-now`.

---

## 1) Panoramica architetturale

La soluzione è composta da cinque progetti:

- `src/OnlyDFSMonitor.Core`
  - Modelli condivisi (configurazione e snapshot).
  - Persistenza JSON su filesystem locale.
  - Engine di collection.
  - Wrapper per comandi `sc.exe`.

- `src/OnlyDFSMonitor.Desktop` (WPF, `net8.0-windows`)
  - UI operativa per configurazione e comandi servizio.
  - Pulsante **Collect Now (Auto)** con fallback locale:
    - se il servizio è installato: crea il comando file-based;
    - se non è installato: esegue collection locale immediata.

- `src/OnlyDFSMonitor.Service` (Worker, `net8.0-windows`)
  - Loop periodico con polling configurabile.
  - Lettura trigger `collect-now`.
  - Salvataggio snapshot su file JSON.

- `src/OnlyDFSMonitor.ServiceControl.Cli`
  - Utility headless per `install/start/stop/status` del servizio.

- `tests/OnlyDFSMonitor.Tests`
  - Test unitari/integrativi su componenti core.

---

## 2) Requisiti

## 2.1 Ambiente di sviluppo
- Windows 10/11 o Windows Server recente.
- .NET SDK 8.x installato (`dotnet --info`).
- PowerShell 7+ consigliato per script (`pwsh`).

## 2.2 Ambiente runtime (operativo)
- Permessi coerenti con operazioni DFS e gestione servizio.
- Accesso in scrittura ai percorsi locali di config/status/commands.
- Se usato il servizio: account con privilegi adeguati in dominio.

---

## 3) Struttura repository

- `DfsMonitor.sln`
- `src/`
  - `OnlyDFSMonitor.Core/`
  - `OnlyDFSMonitor.Desktop/`
  - `OnlyDFSMonitor.Service/`
  - `OnlyDFSMonitor.ServiceControl.Cli/`
- `tests/OnlyDFSMonitor.Tests/`
- `scripts/`
  - `build.ps1`
  - `clean.ps1`

---

## 4) Configurazione e percorsi file

I default applicativi sono definiti in `StorageOptions`:

- Config: `C:\OnlyDFSMonitor\config\config.json`
- Snapshot: `C:\OnlyDFSMonitor\status\latest.json`
- Trigger collect-now: `C:\OnlyDFSMonitor\commands\collect-now.json`
- UNC opzionale: `\\fileserver\dfs-monitor`

### 4.1 Logica DFS-R

Se `DfsrGroups` non è valorizzato in configurazione, l'engine prova l'auto-discovery locale via PowerShell:

```powershell
Get-DfsReplicationGroup -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty GroupName
```

Se non trova gruppi validi, la parte DFS-R viene saltata senza bloccare la raccolta complessiva.

---

## 5) Build, clean e test

## 5.1 Workflow manuale con `dotnet`

```bash
dotnet restore DfsMonitor.sln
dotnet build DfsMonitor.sln -c Release
dotnet test DfsMonitor.sln -c Release
```

## 5.2 Script PowerShell

### `scripts/build.ps1`
Script parametrico per restore/build/test.

Parametri disponibili:
- `-Configuration` (`Debug` | `Release`, default `Release`)
- `-SkipTests` (salta test)
- `-NoRestore` (salta restore)

Esempi:

```powershell
pwsh ./scripts/build.ps1
pwsh ./scripts/build.ps1 -Configuration Debug
pwsh ./scripts/build.ps1 -SkipTests
pwsh ./scripts/build.ps1 -NoRestore -Configuration Release
```

### `scripts/clean.ps1`
Esegue:
1. `dotnet clean` in `Debug` e `Release` sulla solution;
2. rimozione ricorsiva cartelle `bin` e `obj` nel repository.

Esempio:

```powershell
pwsh ./scripts/clean.ps1
```

---

## 6) Esecuzione componenti

## 6.1 Desktop

```bash
dotnet run --project src/OnlyDFSMonitor.Desktop
```

Funzioni principali UI:
- salvataggio configurazione;
- install/start/stop servizio;
- collect-now automatico (service-aware).

## 6.2 Service (debug locale)

```bash
dotnet run --project src/OnlyDFSMonitor.Service
```

## 6.3 CLI controllo servizio

```bash
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- status
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- start
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- stop
```

---

## 7) Flusso operativo consigliato

1. Avviare Desktop e configurare namespace DFS-N.
2. Salvare configurazione.
3. Se necessario, installare/avviare il servizio dalla UI o dalla CLI.
4. Usare **Collect Now (Auto)**:
   - con servizio installato: enqueue comando su file;
   - senza servizio: esecuzione locale immediata.
5. Verificare snapshot generato nel path configurato.

---

## 8) Troubleshooting

- **Messaggio: servizio non installato**
  - Comportamento atteso: la Desktop app continua in local mode.

- **`sc.exe` restituisce codice errore su start/stop**
  - Verificare che il servizio esista (`status`) e che il nome sia `OnlyDFSMonitorService`.
  - Verificare privilegi amministrativi della sessione.

- **Nessun dato DFS-R raccolto**
  - Verificare disponibilità cmdlet DFS-R sull'host collector.
  - Verificare permessi e contesto dominio dell'utente/account servizio.

- **File JSON non scritto**
  - Verificare ACL dei path configurati (`config/status/commands`).
  - Controllare lock/antivirus sul file target.

---

## 9) Note di manutenzione

- Mantenere `README.md` come documentazione operativa principale del repository.
- Evitare documenti duplicati non allineati: in caso di aggiornamento processi, aggiornare questa guida come fonte unica.
