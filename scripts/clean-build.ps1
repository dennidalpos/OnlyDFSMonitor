param(
  [string]$Solution = "DfsMonitor.sln",
  [switch]$RunTests,
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Resolve-SolutionPath {
  param([string]$SolutionInput)

  if ([System.IO.Path]::IsPathRooted($SolutionInput) -and (Test-Path $SolutionInput)) {
    return (Resolve-Path $SolutionInput).Path
  }

  if (Test-Path $SolutionInput) {
    return (Resolve-Path $SolutionInput).Path
  }

  $repoRoot = Split-Path -Path $PSScriptRoot -Parent
  $fromRepoRoot = Join-Path $repoRoot $SolutionInput
  if (Test-Path $fromRepoRoot) {
    return (Resolve-Path $fromRepoRoot).Path
  }

  throw "Soluzione non trovata: $SolutionInput (cwd: $(Get-Location), repoRoot: $repoRoot)"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet non trovato nel PATH. Installare .NET SDK 8.x e riprovare."
}

$resolvedSolution = Resolve-SolutionPath $Solution

Write-Host "[clean-build] Solution: $resolvedSolution"
Write-Host "[clean-build] Restoring..."
& dotnet restore $resolvedSolution

Write-Host "[clean-build] Cleaning..."
& dotnet clean $resolvedSolution -c $Configuration

Write-Host "[clean-build] Building..."
& dotnet build $resolvedSolution -c $Configuration --no-restore

if ($RunTests) {
  Write-Host "[clean-build] Testing..."
  & dotnet test $resolvedSolution -c $Configuration --no-build
}

Write-Host "[clean-build] Completed successfully."
