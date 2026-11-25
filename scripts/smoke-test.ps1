<#
.SYNOPSIS
    Runs the FreeCleaner UI smoke test: opens the app, visits every section in Turkish and English,
    then prints the report. Exit code 0 = passed, 1 = failed.

.DESCRIPTION
    The test never deletes anything: every dialog is answered "No"/"Cancel" automatically, and settings,
    history and logs are written to a temporary copy of the data folder (<ReportDirectory>\data), so the
    real settings are never changed. Sections still load their read-only lists (processes, services, ports ...).
    Report: <ReportDirectory>\smoke-report.txt ; with -Screenshots also <ReportDirectory>\screens\*.png

.EXAMPLE
    .\scripts\smoke-test.ps1                 # Debug build, report in %TEMP%\FreeCleaner-smoke

.EXAMPLE
    .\scripts\smoke-test.ps1 -Screenshots    # also saves a PNG of every section in both languages
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $ReportDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'FreeCleaner-smoke'),
    [switch] $Screenshots,
    [switch] $NoBuild,
    [int] $TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projPath = Join-Path $repoRoot 'FreeCleaner\FreeCleaner.csproj'

if (-not $NoBuild) {
    & dotnet build $projPath -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

$exe = Join-Path $repoRoot "FreeCleaner\bin\$Configuration\net10.0-windows\FreeCleaner.exe"
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

if (Test-Path $ReportDirectory) { Remove-Item $ReportDirectory -Recurse -Force }
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null

$arguments = @('--smoke-test', '--smoke-report', "`"$ReportDirectory`"")
if ($Screenshots) { $arguments += '--smoke-screenshots' }

Write-Host "==> $exe $($arguments -join ' ')" -ForegroundColor Cyan
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
$null = $process.Handle   # keeps the handle so ExitCode is available after exit

if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    $process.Kill()
    throw "Smoke test did not finish within $TimeoutMinutes minutes."
}

$report = Join-Path $ReportDirectory 'smoke-report.txt'
if (Test-Path $report) { Get-Content $report -Encoding UTF8 | Out-Host } else { Write-Warning "No report at $report" }

if ($process.ExitCode -ne 0) {
    Write-Host "UI smoke test FAILED (exit code $($process.ExitCode))." -ForegroundColor Red
    exit $process.ExitCode
}
Write-Host 'UI smoke test passed.' -ForegroundColor Green
