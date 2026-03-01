# ╔═══════════════════════════════════════════════════════════╗
# ║     Bye-TCP Internet — Скрипт подготовки к публикации     ║
# ║  Создает чистую копию проекта без временных файлов        ║
# ╚═══════════════════════════════════════════════════════════╝

param(
    [string]$OutputPath = ".\release",
    [switch]$CreateZip   # Создать ZIP архив
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Bye-TCP Internet — Подготовка к публикации               ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Создаем выходную папку
if (Test-Path $OutputPath) {
    Remove-Item -Path $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath | Out-Null

Write-Host "📁 Копирование файлов..." -ForegroundColor Yellow

# Папки для копирования
$foldersToCopy = @(
    'src',
    'config',
    'schemas',
    'scripts',
    'docs'
)

# Файлы для копирования
$filesToCopy = @(
    '*.sln',
    '*.csproj',
    '*.vcxproj',
    '*.props',
    '*.targets',
    '*.md',
    '*.json',
    '*.resw',
    '*.xaml',
    'cleanup.ps1',
    'apply.ps1',
    'demo.ps1'
)

# Копируем папки
foreach ($folder in $foldersToCopy) {
    $src = Join-Path $projectRoot $folder
    $dst = Join-Path $OutputPath $folder
    
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dst -Recurse -Force
        Write-Host "  ✓ $folder" -ForegroundColor Green
    }
}

# Копируем файлы корневой папки
Get-ChildItem -Path $projectRoot -File | Where-Object {
    $filesToCopy -contains $_.Extension -or 
    $filesToCopy -contains $_.Name -or
    $_.Name -like '*.md' -or
    $_.Name -like '*.ps1'
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $OutputPath -Force
    Write-Host "  ✓ $($_.Name)" -ForegroundColor Green
}

# Исключаем временные папки
$excludePatterns = @('bin', 'obj', 'publish', '.vs', '.git', 'logs', 'backups', 'state', 'cache')
foreach ($pattern in $excludePatterns) {
    $path = Join-Path $OutputPath $pattern
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
        Write-Host "  ✗ Исключено: $pattern" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "📊 Размер релиза:" -ForegroundColor Yellow
$size = (Get-ChildItem -Path $OutputPath -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "   {0:N2} MB" -f $size -ForegroundColor Cyan

if ($CreateZip) {
    Write-Host ""
    Write-Host "📦 Создание ZIP архива..." -ForegroundColor Yellow
    
    $zipName = "ByeTcp-Internet-v2.0-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip"
    $zipPath = Join-Path $projectRoot $zipName
    
    Compress-Archive -Path $OutputPath -DestinationPath $zipPath -Force
    Write-Host "  ✓ $zipName" -ForegroundColor Green
}

Write-Host ""
Write-Host "✅ Подготовка завершена!" -ForegroundColor Green
Write-Host ""
Write-Host "📁 Выходная папка: $OutputPath" -ForegroundColor Cyan
Write-Host "📄 Инструкция: $OutputPath\README_PUBLISH.md" -ForegroundColor Cyan
