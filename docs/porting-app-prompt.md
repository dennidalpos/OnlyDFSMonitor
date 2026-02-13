# Prompt Codex — Rebuild completo da zero (app desktop + servizio Windows)

Usa questo prompt in una nuova chat Codex per rifare `OnlyDFSMonitor` **da zero**, eliminando il codice e la documentazione attuali.

```text
Sei Codex (GPT-5.2-Codex), software engineering agent.

Mandato principale (obbligatorio):
- Elimina tutti i file legacy del progetto attuale e ricrea la soluzione da zero.
- Questo include codice, test, script e documentazione esistente.
- Non fare migrazione incrementale: fai un rebuild completo greenfield.
- Mantieni solo il nome repository (`OnlyDFSMonitor`) e l'obiettivo funzionale (monitoraggio DFS-N/DFS-R).

Obiettivo prodotto:
- Realizzare una app desktop Windows che gestisce configurazione e monitoraggio.
- La app deve poter installare, avviare, arrestare e verificare lo stato del servizio Windows direttamente da UI.
- Nessun HTTP server come componente primario di gestione (niente web UI/API come fulcro architetturale).

Target tecnico:
- .NET 8
- Windows Server 2022 / Windows 10+
- Architettura centrale senza agent sui DFS server monitorati.

Scelte architetturali richieste (all'inizio):
1) Scegli framework UI e motiva in modo netto (WPF, WinUI 3 o MAUI).
2) Definisci nuova solution structure (progetti, responsabilità, dipendenze).
3) Definisci strategia di persistenza configurazione/stato (file JSON locale + eventuale UNC).
4) Definisci security model per UI locale (es. controllo privilegi admin/local group).

Implementazione richiesta (greenfield):
1) Crea nuova solution e nuovi progetti coerenti con l'architettura scelta.
2) Implementa servizio Windows di collection DFS-N/DFS-R con:
   - scheduler periodico
   - collect-now
   - timeout/retry e isolamento errori per host/target
   - logging strutturato
3) Implementa app desktop con:
   - dashboard stato monitoraggio
   - configurazione completa
   - gestione servizio (Install/Start/Stop/Status)
   - trigger Collect Now
4) Implementa packaging/script di installazione puliti per ambiente Windows.
5) Ricrea da zero tutta la documentazione:
   - README
   - architettura
   - runbook operativo
   - troubleshooting

Regole di consegna:
- Fornisci commit piccoli e logici.
- Ogni commit deve compilare (quando possibile) e avere descrizione chiara.
- Evita dipendenze superflue.
- Evidenzia esplicitamente cosa è stato rimosso e cosa è stato ricreato.

Qualità e test minimi:
- Unit test per logica core.
- Test integrazione per flusso di configurazione + collect-now + persistenza stato.
- Esegui e riporta:
  - dotnet restore
  - dotnet build
  - dotnet test
- Se alcuni test non eseguibili in ambiente corrente, documenta limite e workaround.

Output atteso:
- Piano sintetico di rebuild totale (fasi + rischi + rollback).
- Patch reale completa (eliminazione vecchi file + nuova base progetto).
- Documentazione nuova completa.
- Elenco comandi eseguiti con esito.
```
