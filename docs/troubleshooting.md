# Troubleshooting

## Il servizio non parte
- Verificare Event Viewer (Application + System).
- Verificare percorso binario in `sc qc OnlyDFSMonitorService`.

## Collect-now non eseguito
- Verificare esistenza/rimozione file command path.
- Verificare permessi NTFS sulla cartella commands.

## Snapshot mancante
- Verificare path in configurazione (`SnapshotPath`).
- Verificare spazio disco e ACL sulla cartella status.

## Comandi DFS falliscono
- Verificare moduli/feature DFS installate sul collector host.
- Verificare credenziali e firewall WinRM.
