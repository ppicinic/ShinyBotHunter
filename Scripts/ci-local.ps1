# The single definition of "CI passes". Run it from any cwd before asking for a merge:
#   powershell -File Scripts\ci-local.ps1
# It restores, builds and tests ShinyBotHunter.Tests, then runs npm ci, the TypeScript typecheck,
# eslint, the vitest tests and the production build in PokemonDashboard. It stops at the first failure, exits 1, and ends
# with one summary line. .github/workflows/build-and-test.yml calls this same script.
#
# ApiHawk is excluded deliberately: it targets net48 and builds against BizHawk assemblies that
# live in a private fork and are not committed, so it cannot build on a clean machine.
# Scripts/deploy-apihawk.ps1 is the manual path for it.

$ErrorActionPreference = "Stop"

$repoDir = Split-Path $PSScriptRoot -Parent
$testsProject = Join-Path $repoDir "ShinyBotHunter.Tests\ShinyBotHunter.Tests.csproj"
$dashboardDir = Join-Path $repoDir "PokemonDashboard"

$totalSteps = 8
$stepsDone = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Format-Elapsed {
    $stopwatch.Elapsed.ToString("mm\:ss")
}

# PowerShell 5.1 does not stop on a native command's non-zero exit even with
# ErrorActionPreference = Stop, so every step checks $LASTEXITCODE itself here.
function Invoke-Step([string]$name, [scriptblock]$command) {
    Write-Host ""
    Write-Host "=== ci-local: $name ===" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ci-local: FAIL at $name ($script:stepsDone/$totalSteps, $(Format-Elapsed))" -ForegroundColor Red
        exit 1
    }
    $script:stepsDone++
}

Invoke-Step "dotnet restore" { dotnet restore $testsProject }
Invoke-Step "dotnet build"   { dotnet build $testsProject --no-restore -c Release }
Invoke-Step "dotnet test"    { dotnet test $testsProject --no-build -c Release }

Push-Location $dashboardDir
try {
    Invoke-Step "npm ci"            { npm ci }
    Invoke-Step "npm run typecheck" { npm run typecheck }
    Invoke-Step "npm run lint"      { npm run lint }
    Invoke-Step "npm run test"      { npm run test }
    Invoke-Step "npm run build"     { npm run build }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "ci-local: PASS ($stepsDone/$totalSteps steps, $(Format-Elapsed))" -ForegroundColor Green
exit 0
