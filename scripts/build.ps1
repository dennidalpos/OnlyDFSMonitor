param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$NoRestore,
    [switch]$NoBuild,
    [string]$Framework,
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'DfsMonitor.sln'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet CLI non trovato nel PATH.'
}

$commonArgs = @('-c', $Configuration)
if ($Framework) { $commonArgs += @('-f', $Framework) }
if ($Runtime) { $commonArgs += @('-r', $Runtime) }

Write-Host "[OnlyDFSMonitor] Build avviata - Configuration=$Configuration Framework=$Framework Runtime=$Runtime"

if (-not $NoRestore) {
    Write-Host '[OnlyDFSMonitor] dotnet restore'
    dotnet restore $solutionPath
}

if (-not $NoBuild) {
    Write-Host '[OnlyDFSMonitor] dotnet build'
    $buildArgs = @($solutionPath) + $commonArgs
    if ($NoRestore) { $buildArgs += '--no-restore' }
    dotnet build @buildArgs
}

if (-not $SkipTests) {
    Write-Host '[OnlyDFSMonitor] dotnet test'
    $testArgs = @($solutionPath) + $commonArgs + @('--verbosity', 'minimal')
    if ($NoBuild) {
        $testArgs += '--no-build'
    }
    elseif ($NoRestore) {
        $testArgs += '--no-restore'
    }

    dotnet test @testArgs
}
else {
    Write-Host '[OnlyDFSMonitor] Test saltati (SkipTests).'
}

Write-Host '[OnlyDFSMonitor] Build completata con successo.'
