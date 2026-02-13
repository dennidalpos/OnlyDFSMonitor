# Architecture

## Piano sintetico rebuild totale
1. **Reset legacy**: rimozione codice web/API, script e test precedenti.
2. **Nuova base**: creazione solution con Core + Service + Desktop + CLI + Tests.
3. **Flussi critici**: scheduler periodico, collect-now, persistenza snapshot JSON.
4. **Consegna operativa**: script PowerShell e documentazione runbook/troubleshooting.

## Rischi principali
- Vincolo Windows-only per WPF/Service control.
- Cmdlet DFS richiedono permessi AD/WinRM/DFSR corretti.
- Condivisioni UNC possono essere intermittenti.

## Rollback
- rollback git a commit precedente;
- mantenere backup della cartella `C:\OnlyDFSMonitor` prima del deploy;
- reinstallazione servizio via script in `scripts/`.

## Data & persistence strategy
- JSON locale come fonte primaria (`config.json`, `latest.json`, `collect-now.json`).
- UNC opzionale come destinazione secondaria/mirror.
- Nessuna dipendenza da HTTP server per controllo operativo.

## Component interaction
- Desktop salva configurazione e crea collect-now command file.
- Service legge config, processa scheduler/collect-now, produce snapshot.
- CLI offre fallback headless per gestione servizio.
