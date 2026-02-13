param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'DfsMonitor.sln'

if (-not $NoRestore) {
    dotnet restore $solutionPath
}

dotnet build $solutionPath -c $Configuration --no-restore

if (-not $SkipTests) {
    dotnet test $solutionPath -c $Configuration --no-build --verbosity minimal
}
