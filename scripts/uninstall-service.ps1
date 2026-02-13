$ErrorActionPreference = 'Stop'
sc.exe stop OnlyDFSMonitorService | Out-Null
sc.exe delete OnlyDFSMonitorService
