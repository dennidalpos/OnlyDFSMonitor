# OnlyDFSMonitor

Applicazione **desktop Windows** per monitoraggio centralizzato DFS Namespace (DFS-N) e DFS Replication (DFS-R), con servizio Windows **opzionale**.

> Obiettivo operativo: l'app deve funzionare anche se il servizio non è installato.

---

## 1) Architettura attuale

La soluzione è stata ricostruita in modalità greenfield e contiene solo i componenti necessari:

- `OnlyDFSMonitor.Core`
  - modelli di configurazione/snapshot;
  - persistenza JSON locale;
  - engine di collection;
  - utility per interrogare/gestire servizio Windows.
- `OnlyDFSMonitor.Desktop` (WPF)
  - UI di configurazione;
  - pulsanti Install/Start/Stop servizio;
  - `Collect Now` con fallback automatico:
    - se servizio installato → scrive comando file-based;
    - se servizio non installato → esegue collection locale immediata.
- `OnlyDFSMonitor.Service` (Worker Windows)
  - scheduler periodico;
  - ascolto file `collect-now`;
  - salvataggio snapshot JSON.
- `OnlyDFSMonitor.ServiceControl.Cli`
  - comandi headless per gestione servizio (`install/start/stop/status`).
- `OnlyDFSMonitor.Tests`
  - test unit e integrazione su persistenza/config/collection.

Nessun server HTTP è usato come control-plane principale.

---

## 2) Comportamento chiave richiesto

### 2.1 App funzionante senza servizio installato
La UI desktop non dipende dal servizio per eseguire una raccolta:
- `Collect Now (Auto)` prova prima a rilevare il servizio;
- se assente, esegue raccolta locale e salva direttamente lo snapshot.

### 2.2 DFS-R groups in auto-discovery
L'operatore inserisce il/i **namespace DFS-N** in configurazione.
I gruppi DFS-R vengono recuperati automaticamente dal collector host tramite:

```powershell
Get-DfsReplicationGroup | Select-Object -ExpandProperty GroupName
```

Se l'auto-discovery non restituisce dati, la raccolta DFS-R viene saltata senza bloccare l'esecuzione generale.

---

## 3) Persistenza file

Default locali:
- Config: `C:\OnlyDFSMonitor\config\config.json`
- Snapshot: `C:\OnlyDFSMonitor\status\latest.json`
- Command queue: `C:\OnlyDFSMonitor\commands\collect-now.json`

È disponibile anche `OptionalUncRoot` in configurazione per estensioni operative (mirror/condivisione).

---

## 4) Build e test

```bash
dotnet restore DfsMonitor.sln
dotnet build DfsMonitor.sln -c Release
dotnet test DfsMonitor.sln -c Release
```

---

## 5) Esecuzione

### Desktop
```bash
dotnet run --project src/OnlyDFSMonitor.Desktop
```

### Service (debug)
```bash
dotnet run --project src/OnlyDFSMonitor.Service
```

### CLI servizio
```bash
dotnet run --project src/OnlyDFSMonitor.ServiceControl.Cli -- status
```

---

## 6) Script disponibili

Cartella `scripts/` contiene solo script di build:
- `build.ps1` (restore + build + test in Release)

---

## 7) Permessi consigliati

- Esecuzione Desktop con utenza locale amministrativa quando si usano operazioni `sc.exe`.
- Account servizio con permessi DFS/WinRM coerenti con il dominio.
- ACL di scrittura sui path locali di config/status/commands.

---

## 8) Stato repository

Sono stati rimossi file legacy non più necessari (documentazione separata del vecchio impianto e script di build non indispensabili).
Questo README è la documentazione operativa principale aggiornata.
