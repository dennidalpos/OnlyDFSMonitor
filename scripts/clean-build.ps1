param(
  [string]$Solution = "DfsMonitor.sln",
  [switch]$RunTests
)

$ErrorActionPreference = "Stop"

Write-Host "[clean-build] Restoring..."
dotnet restore $Solution

Write-Host "[clean-build] Cleaning..."
dotnet clean $Solution -c Release

Write-Host "[clean-build] Building..."
dotnet build $Solution -c Release --no-restore

if ($RunTests) {
  Write-Host "[clean-build] Testing..."
  dotnet test $Solution -c Release --no-build
}

Write-Host "[clean-build] Completed successfully."
