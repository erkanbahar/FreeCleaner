param(
    [switch]$IncludePortableFlag,
    [switch]$ReadyToRun,
    [switch]$GenerateWingetManifests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projPath = Join-Path $repoRoot 'FreeCleaner\FreeCleaner.csproj'
$outDir = Join-Path $repoRoot 'artifacts\portable'

$publishArgs = @(
    'publish', $projPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:TreatWarningsAsErrors=true',
    '-o', $outDir
)

if ($ReadyToRun) {
    $publishArgs += '-p:PublishReadyToRun=true'
}

Write-Host "Publishing FreeCleaner Pro (Single-File Portable win-x64 .NET 10, ReadyToRun=$ReadyToRun) -> $outDir" -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if ($IncludePortableFlag) {
    $flagPath = Join-Path $outDir 'portable.flag'
    "FreeCleaner Pro Portable Mode enabled" | Set-Content -Path $flagPath -Encoding UTF8
    Write-Host "Created portable.flag -> $flagPath" -ForegroundColor Yellow
}

$exePath = Join-Path $outDir 'FreeCleaner.exe'
$sha256 = ''
if (Test-Path $exePath) {
    $sha256 = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "SHA-256 (FreeCleaner.exe): $sha256" -ForegroundColor Cyan
}

if ($GenerateWingetManifests) {
    [xml]$csprojXml = Get-Content -Path $projPath -Encoding UTF8
    $ver = $csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($ver)) { $ver = '4.7.0' }
    $tag = "v$ver"
    $wingetSourceDir = Join-Path $repoRoot 'installer\winget'
    $wingetOutDir = Join-Path $outDir 'winget'
    New-Item -ItemType Directory -Force -Path $wingetOutDir | Out-Null

    $shaUpper = if ([string]::IsNullOrWhiteSpace($sha256)) { 'REPLACE_WITH_RELEASE_SHA256' } else { $sha256.ToUpperInvariant() }
    foreach ($manifest in Get-ChildItem -Path $wingetSourceDir -Filter '*.yaml') {
        $content = (Get-Content -Path $manifest.FullName -Raw -Encoding UTF8) `
            -replace 'REPLACE_WITH_RELEASE_SHA256', $shaUpper `
            -replace '4\.7\.0', $ver `
            -replace 'v4\.7\.0', $tag
        Set-Content -Path (Join-Path $wingetOutDir $manifest.Name) -Value $content -Encoding UTF8 -NoNewline
    }
    Write-Host "Generated Winget manifests ($ver) -> $wingetOutDir" -ForegroundColor Yellow
}

Write-Host "Portable build complete!" -ForegroundColor Green
