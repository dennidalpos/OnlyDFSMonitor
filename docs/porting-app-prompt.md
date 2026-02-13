# Prompt Codex — Porting da HTTP server a app desktop con gestione servizio Windows

Usa questo prompt in una nuova chat Codex per guidare il porting del progetto `OnlyDFSMonitor`.

```text
Sei Codex (GPT-5.2-Codex), software engineering agent.

Contesto repository:
- Nome repo: OnlyDFSMonitor
- Stack attuale: .NET 8
- Progetti esistenti:
  - src/DfsMonitor.Shared (modelli, storage UNC/local cache, runtime state, command queue)
  - src/DfsMonitor.Service (Worker Service con collector DFS-N/DFS-R)
  - src/DfsMonitor.Web (Minimal API + Razor Pages per configurazione/stato/report/controllo servizio)
  - tests/DfsMonitor.Tests (unit + integration tests)

Situazione corrente da considerare nel porting:
- La logica di business e monitoraggio è nel Service + Shared.
- La Web espone endpoint API e UI per:
  - configurazione monitor
  - collect-now
  - status/report
  - start/stop/install servizio Windows
- Esiste già un endpoint installazione servizio (`/api/service/install`) e pagina `/Config` con form di installazione.

Obiettivo di porting:
- Sostituire la gestione via HTTP server con una app desktop Windows (UI locale), mantenendo il servizio Windows come engine di collection.
- L'app desktop deve permettere installazione/configurazione/avvio/arresto del servizio direttamente da UI.
- Il monitoraggio rimane centralizzato e senza agent sui DFS server.

Vincoli tecnici:
- Target primario: Windows Server 2022 / Windows 10+.
- Non rompere la logica esistente in DfsMonitor.Shared e DfsMonitor.Service: riuso massimo.
- Mantenere compatibilità config/snapshot già esistenti (json su UNC + cache locale).
- Evitare refactor “big bang”: procedere in step incrementali con commit piccoli.
- Non introdurre dipendenze non necessarie.

Decisione architetturale richiesta (prima di codificare):
1) Proponi opzione UI consigliata e motiva scelta tra:
   - WPF (.NET 8)
   - WinUI 3
   - .NET MAUI (Windows-first)
2) Definisci confini:
   - quali feature restano nel Service
   - quali feature passano nella Desktop App
   - cosa deprecare/eliminare in DfsMonitor.Web
3) Definisci strategia migrazione auth:
   - rimozione dipendenza da Negotiate/JWT lato web
   - eventuale protezione UI locale (ruoli Windows/local admin check)

Implementazione richiesta:
1) Crea nuovo progetto desktop (es. `src/DfsMonitor.Desktop`) con:
   - dashboard stato ultimo snapshot
   - pagina configurazione (stessi campi principali oggi presenti in `/Config`)
   - pulsanti: Install Service, Start, Stop, Collect Now, Save Config
2) Introduci un livello applicativo condiviso (se utile in Shared o nuovo progetto) per evitare duplicazione logica oggi in endpoint web.
3) Implementa service management robusto in desktop app:
   - installazione servizio (sc.exe/New-Service o approccio equivalente)
   - verifica prerequisiti/percorsi
   - start/stop/status
   - gestione errori dettagliata a schermo
4) Mantieni integrazione con command queue file-based per Collect Now.
5) Prevedi migrazione graduale:
   - Fase 1: Desktop + Service paralleli alla Web
   - Fase 2: feature parity
   - Fase 3: dismissione DfsMonitor.Web (opzionale, con flag/milestone)

Qualità e test:
- Aggiungi test unitari per la logica estratta dalla Web (view-model/services non-UI dove possibile).
- Aggiorna test integrazione dove ha senso.
- Esegui almeno:
  - dotnet restore
  - dotnet build DfsMonitor.sln
  - dotnet test DfsMonitor.sln
- Se la UI desktop non è testabile in CI Linux, separa logica testabile da shell UI.

Output atteso in risposta:
- Piano di migrazione in milestone (M1/M2/M3) con rischi e rollback.
- Patch incrementale reale (non solo pseudocodice), con file nuovi/modificati.
- Note operative aggiornate in docs/runbook.md.
- Elenco comandi eseguiti e relativo esito.
```

## Nota operativa
Se vuoi, posso anche generare una seconda variante del prompt focalizzata su **"prima POC rapida"** (scope ridotto in 1-2 giorni) invece del porting completo.
