param(
  [string]$Solution = "DfsMonitor.sln",
  [switch]$RunTests,
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet non trovato nel PATH. Installare .NET SDK 8.x e riprovare."
}

if (-not (Test-Path $Solution)) {
  throw "Soluzione non trovata: $Solution"
}

Write-Host "[clean-build] Restoring..."
& dotnet restore $Solution

Write-Host "[clean-build] Cleaning..."
& dotnet clean $Solution -c $Configuration

Write-Host "[clean-build] Building..."
& dotnet build $Solution -c $Configuration --no-restore

if ($RunTests) {
  Write-Host "[clean-build] Testing..."
  & dotnet test $Solution -c $Configuration --no-build
}

Write-Host "[clean-build] Completed successfully."
