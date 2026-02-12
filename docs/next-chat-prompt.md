# Prompt per continuare in una nuova chat

Copia/incolla il testo seguente in una nuova chat per proseguire il lavoro in modo coerente:

```text
Sei Codex (GPT-5.2-Codex), software engineering agent.

Contesto progetto:
- Repository: OnlyDFSMonitor
- Target: Windows Server 2022, .NET 8
- Soluzione: DfsMonitor.Shared + DfsMonitor.Service + DfsMonitor.Web + tests
- Obiettivo: monitoraggio centralizzato DFS-N/DFS-R senza agent sui DFS server

Stato attuale (già implementato):
- Config/snapshot storage con fallback locale + lock file + versioning backup
- Runtime state + command queue file-based per collect-now
- collect-now immediato in worker tramite signal/event (watcher su queue)
- Collector DFS-N con ordering più semantico (stato + priority class + rank)
- Collector DFS-R backlog su connessioni reali (`Get-DfsrConnection`) con fallback resiliente
- API + UI principali, report JSON/CSV, configurazione estesa (retry/thresholds/event sample/storage paths/DFSR groups)
- Autenticazione Negotiate + modalità JWT placeholder configurabile
- Endpoint start/stop servizio con error handling esplicito su non-Windows
- Test integrazione orchestrator + build/test eseguiti su runner con dotnet

TODO rimanenti (priorità):
1) Aggiungere integration test API end-to-end (host web in-memory) con copertura auth, collect-now e endpoint status/report.
2) Rafforzare collector DFS-R per folder-level backlog (non solo group-level), con isolamento errori per connessione/member.
3) Migliorare robustezza cross-platform:
   - guard/suppress CA1416 in punti Windows-only con branch espliciti + test dedicati
   - evitare crash su config path UNC non disponibile in ambienti Linux/dev
4) Rendere più robusta la UI Config:
   - validazione campi client/server
   - preservare namespace IDs esistenti invece di rigenerarli sempre
   - UX per gestione gruppi DFS-R (add/remove dedicati)
5) Definire strategia operativa JWT reale (integrazione IdP, token issuance, claims/roles e policy auth).
6) Migliorare logging strutturato:
   - correlation id per ciclo collection / collect-now
   - metriche base (durata collector, success/fail per host, backlog unknown rate)
7) Documentazione operativa aggiuntiva:
   - runbook di migrazione da Negotiate a JWT
   - troubleshooting dedicato per WinRM/CIM timeout e permessi DFS cmdlets

Vincoli:
- Non installare agent sui DFS server.
- Preferire WinRM/CIM dove possibile.
- Robustezza: timeout/retry/per-host isolation.
- Mantenere logging strutturato e documentazione operativa.

Output richiesto in questa chat:
- Patch incrementale con commit piccoli
- Comandi di verifica eseguiti
- Aggiornamento docs/runbook
```
