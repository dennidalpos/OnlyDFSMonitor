param(
  [string]$DefaultSolution = "DfsMonitor.sln"
)

$ErrorActionPreference = "Stop"

function Ask-YesNo {
  param(
    [string]$Prompt,
    [bool]$Default = $true
  )

  $suffix = if ($Default) { "[Y/n]" } else { "[y/N]" }
  while ($true) {
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }

    switch ($answer.Trim().ToLowerInvariant()) {
      "y" { return $true }
      "yes" { return $true }
      "n" { return $false }
      "no" { return $false }
      default { Write-Host "Valore non valido. Inserisci y/yes o n/no." -ForegroundColor Yellow }
    }
  }
}

function Ask-NonEmpty {
  param(
    [string]$Prompt,
    [string]$Default = ""
  )

  while ($true) {
    $value = Read-Host "$Prompt$(if ($Default) { " [$Default]" } else { "" })"
    if ([string]::IsNullOrWhiteSpace($value)) {
      if (-not [string]::IsNullOrWhiteSpace($Default)) { return $Default }
      Write-Host "Il valore non può essere vuoto." -ForegroundColor Yellow
      continue
    }

    return $value.Trim()
  }
}

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host " DFS Monitor - Build interattiva (.NET 8)" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
  Write-Host "dotnet non trovato nel PATH." -ForegroundColor Red
  if (Ask-YesNo "Vuoi aprire la pagina ufficiale di installazione .NET?" $true) {
    Start-Process "https://learn.microsoft.com/dotnet/core/install/windows"
  }
  throw "Installare .NET SDK 8.x e rieseguire lo script."
}

$solution = Ask-NonEmpty "Percorso soluzione" $DefaultSolution
if (-not (Test-Path $solution)) {
  throw "Soluzione non trovata: $solution"
}

$configuration = Ask-NonEmpty "Configurazione build (Debug/Release)" "Release"
$runRestore = Ask-YesNo "Eseguire restore?" $true
$runClean = Ask-YesNo "Eseguire clean?" $true
$runBuild = Ask-YesNo "Eseguire build?" $true
$runTests = Ask-YesNo "Eseguire test?" $true

Write-Host "\nRiepilogo:" -ForegroundColor Cyan
Write-Host "- Soluzione: $solution"
Write-Host "- Configurazione: $configuration"
Write-Host "- Restore: $runRestore"
Write-Host "- Clean: $runClean"
Write-Host "- Build: $runBuild"
Write-Host "- Test: $runTests"

if (-not (Ask-YesNo "Confermi esecuzione?" $true)) {
  Write-Host "Operazione annullata dall'utente." -ForegroundColor Yellow
  exit 0
}

if ($runRestore) {
  Write-Host "\n[step] dotnet restore $solution" -ForegroundColor Green
  dotnet restore $solution
}

if ($runClean) {
  Write-Host "\n[step] dotnet clean $solution -c $configuration" -ForegroundColor Green
  dotnet clean $solution -c $configuration
}

if ($runBuild) {
  $buildArgs = @("build", $solution, "-c", $configuration)
  if ($runRestore) { $buildArgs += "--no-restore" }
  Write-Host "\n[step] dotnet $($buildArgs -join ' ')" -ForegroundColor Green
  dotnet @buildArgs
}

if ($runTests) {
  $testArgs = @("test", $solution, "-c", $configuration)
  if ($runBuild) { $testArgs += "--no-build" }
  elseif ($runRestore) { $testArgs += "--no-restore" }

  Write-Host "\n[step] dotnet $($testArgs -join ' ')" -ForegroundColor Green
  dotnet @testArgs
}

Write-Host "\nBuild interattiva completata con successo." -ForegroundColor Cyan
