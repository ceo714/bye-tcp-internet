# Bye-TCP Internet - Cleanup Script
param(
    [switch]$All,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "Bye-TCP Internet - Cleanup"
Write-Host "=========================="
Write-Host ""

$foldersToDelete = @('bin', 'obj')
$publishPath = Join-Path $projectRoot 'publish'

$pathsToDelete = @()
foreach ($folder in $foldersToDelete) {
    $pathsToDelete += Get-ChildItem -Path $projectRoot -Recurse -Directory -Filter $folder -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
}

if ($All -and (Test-Path $publishPath)) {
    $pathsToDelete += $publishPath
}

if ($pathsToDelete.Count -eq 0) {
    Write-Host "Nothing to clean." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($pathsToDelete.Count) folders to delete:"
foreach ($path in $pathsToDelete) {
    if ($DryRun) {
        Write-Host "  [WILL DELETE] $path" -ForegroundColor Gray
    } else {
        Write-Host "  [DELETED] $path" -ForegroundColor DarkGray
        Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $DryRun) {
    Write-Host "`nCleanup complete!" -ForegroundColor Green
} else {
    Write-Host "`nThis is DryRun. Run without -DryRun to actually delete." -ForegroundColor Yellow
}
