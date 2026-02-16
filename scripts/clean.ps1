param(
    [ValidateSet('Debug', 'Release', 'Both')]
    [string]$Configuration = 'Both',
    [switch]$RemovePackages,
    [switch]$KeepTestResults
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'DfsMonitor.sln'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet CLI non trovato nel PATH.'
}

$configurations = switch ($Configuration) {
    'Debug' { @('Debug') }
    'Release' { @('Release') }
    default { @('Debug', 'Release') }
}

foreach ($cfg in $configurations) {
    Write-Host "[OnlyDFSMonitor] dotnet clean -c $cfg"
    dotnet clean $solutionPath -c $cfg
}

$foldersToDelete = @('bin', 'obj')
if (-not $KeepTestResults) {
    $foldersToDelete += 'TestResults'
}

Get-ChildItem -Path $repoRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -in $foldersToDelete } |
    ForEach-Object {
        Write-Host "[OnlyDFSMonitor] Remove $($_.FullName)"
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

if ($RemovePackages) {
    $nugetCache = Join-Path $repoRoot '.nuget'
    if (Test-Path $nugetCache) {
        Write-Host "[OnlyDFSMonitor] Remove local package cache $nugetCache"
        Remove-Item -Path $nugetCache -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host '[OnlyDFSMonitor] Clean completata.'
