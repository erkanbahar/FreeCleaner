param(
    [switch]$IncludePortableFlag
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projPath = Join-Path $repoRoot 'FreeCleaner\FreeCleaner.csproj'
$outDir = Join-Path $repoRoot 'artifacts\portable'

Write-Host "Publishing FreeCleaner Pro (Single-File Portable win-x64 .NET 10) -> $outDir" -ForegroundColor Cyan
dotnet publish $projPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:TreatWarningsAsErrors=true `
    -o $outDir

if ($IncludePortableFlag) {
    $flagPath = Join-Path $outDir 'portable.flag'
    "FreeCleaner Pro Portable Mode enabled" | Set-Content -Path $flagPath -Encoding UTF8
    Write-Host "Created portable.flag -> $flagPath" -ForegroundColor Yellow
}

$exePath = Join-Path $outDir 'FreeCleaner.exe'
if (Test-Path $exePath) {
    $sha256 = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "SHA-256 (FreeCleaner.exe): $sha256" -ForegroundColor Cyan
}

Write-Host "Portable build complete!" -ForegroundColor Green
