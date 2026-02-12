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
- Runtime state e command queue file-based per collect-now
- API + UI con autenticazione Negotiate
- Collector DFS-N e DFS-R best effort via PowerShell
- Endpoint API principali e report JSON/CSV

Problemi/gap da completare con priorità:
1) Rendere `collect-now` realmente immediato (interrompere attesa del loop o usare signal/event).
2) DFS-R backlog basato su connessioni reali (non su pairing adiacente membri).
3) Migliorare semanticamente `ordering` DFS-N (non usare solo `State` come proxy).
4) Completare UI configurazione con tutti i campi (retry, thresholds, event sample, gruppi DFS-R/autodiscovery).
5) Aggiungere modalità auth alternativa (JWT placeholder configurabile) oltre a Negotiate.
6) Migliorare endpoint service start/stop con error handling esplicito su ambienti non-Windows.
7) Aggiungere test integrazione API/collector/orchestrazione.
8) Eseguire build/test su runner con dotnet installato e riportare risultati.

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
