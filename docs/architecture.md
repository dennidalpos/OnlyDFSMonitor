# Architecture

## Components
1. **Collector service (`DfsMonitor.Service`)**
   - Periodic scheduler (`CollectionWorker`) with file-queued manual collect-now commands.
   - DFS-N collector:
     - discovers folder/targets using DFSN PowerShell cmdlets.
     - parses target priority class/rank and ordering fields.
     - validates DNS + SMB reachability with timeout and retries.
   - DFS-R collector:
     - discovers replication groups (`Get-DfsReplicationGroup`) or uses configured groups.
     - gathers members/service state (`Get-DfsrMember`, `Get-Service DFSR`).
     - gathers warning/error events (`Get-WinEvent` configurable sample size).
     - best-effort backlog (`Get-DfsrBacklog`) with unknown fallback when unavailable.
   - Writes snapshots to UNC with local pending-sync queue and automatic resync.

2. **Web host (`DfsMonitor.Web`)**
   - Same-host API + UI.
   - Auth-required endpoints for config, status, reports, collect-now, and service control.

3. **Shared library (`DfsMonitor.Shared`)**
   - Normalized configuration/snapshot models.
   - UNC storage with atomic write + versioned backups + file lock.
   - Runtime state store and file command queue.

## Data flow
- Load config from UNC (`config.json`) with local fallback cache.
- Service checks queued commands (`collect-now-*.json`) then runs collector.
- Collect in parallel with per-item isolation and retries.
- Build snapshot and compute global health.
- Persist locally and to UNC with pending-sync replay when UNC returns.
- Publish runtime state for UI/API service status.
