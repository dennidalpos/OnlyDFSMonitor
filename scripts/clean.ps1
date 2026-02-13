$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'DfsMonitor.sln'

dotnet clean $solutionPath -c Debug
dotnet clean $solutionPath -c Release

Get-ChildItem -Path $repoRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin', 'obj') } |
    ForEach-Object {
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
