# Architecture

## Components
1. **Collector service (`DfsMonitor.Service`)**
   - Periodic scheduler (`CollectionWorker`).
   - DFS-N collector:
     - discovers folder/targets using DFSN PowerShell cmdlets.
     - parses target priority class/rank and state ordering fields.
     - validates DNS + SMB reachability.
   - DFS-R collector:
     - abstraction-first implementation with TODO validation commands for WinRM/CIM + DFSR cmdlets.
     - captures service/member health, backlog proxy, and warning summaries.
   - Writes snapshots to UNC with local cache fallback.

2. **Web host (`DfsMonitor.Web`)**
   - Same-host API + UI.
   - Auth-required endpoints for config, status, exports.
   - JSON + CSV report endpoints.

3. **Shared library (`DfsMonitor.Shared`)**
   - Normalized configuration and snapshot models.
   - UNC storage with atomic write + versioned config backups.

## Data flow
- Load config from UNC (`config.json`) with local fallback cache.
- Collect in parallel with per-target isolation.
- Build snapshot and compute global health.
- Persist snapshot to UNC `status/YYYY/MM/DD/collector.json` (with local mirror fallback).

## Reliability features
- Atomic writes through temp file move.
- Versioned config backups (`config_yyyyMMddHHmmss.json`).
- Cycle-level exception handling (service does not crash on one bad namespace).
