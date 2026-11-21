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
    -o $outDir

Write-Host "Portable build complete!" -ForegroundColor Green
