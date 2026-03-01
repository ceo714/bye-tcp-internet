# Bye-TCP Internet - Скрипты установки и управления
# PowerShell скрипт для установки Windows службы

param(
    [Parameter(Mandatory = $false)]
    [switch]$Uninstall,
    
    [Parameter(Mandatory = $false)]
    [switch]$Start,
    
    [Parameter(Mandatory = $false)]
    [switch]$Stop,
    
    [Parameter(Mandatory = $false)]
    [switch]$Status
)

$serviceName = "ByeTcp"
$serviceDisplayName = "Bye-TCP Internet Optimizer"
$serviceDescription = "Адаптивный оптимизатор TCP/IP стека Windows в реальном времени"
$installPath = "C:\Program Files\ByeTcp"
$logPath = "$installPath\logs"
$configPath = "$installPath\config"
$backupPath = "$installPath\backups"

# Проверка прав администратора
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    Write-Host "❌ Требуется запуск от имени администратора" -ForegroundColor Red
    Write-Host "Запустите PowerShell как Administrator и повторите команду" -ForegroundColor Yellow
    exit 1
}

function Write-Logo {
    Write-Host @"
╔═══════════════════════════════════════════════════════════╗
║           Bye-TCP Internet Installer v0.1.0               ║
║     Адаптивный оптимизатор TCP/IP для Windows 10/11       ║
╚═══════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan
}

function Test-Requirements {
    Write-Host "🔍 Проверка системных требований..." -ForegroundColor Yellow
    
    # Проверка версии Windows
    $os = Get-WmiObject Win32_OperatingSystem
    $version = [Version]$os.Version
    $build = [int]$version.Build
    
    if ($build -lt 10240) {
        Write-Host "❌ Требуется Windows 10 или выше (Build 10240+)" -ForegroundColor Red
        return $false
    }
    
    Write-Host "  ✓ Windows $($os.Caption) (Build $build)" -ForegroundColor Green
    
    # Проверка .NET 8
    $dotnetVersion = $null
    try {
        $dotnetVersion = dotnet --version 2>$null
    } catch {}
    
    if ($dotnetVersion) {
        Write-Host "  ✓ .NET $dotnetVersion" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ .NET не найден. Приложение использует self-contained режим." -ForegroundColor Yellow
    }
    
    # Проверка WMI
    try {
        Get-WmiObject -Class Win32_Process -Query "SELECT * FROM Win32_Process WHERE ProcessId = $PID" -ErrorAction Stop | Out-Null
        Write-Host "  ✓ WMI доступен" -ForegroundColor Green
    } catch {
        Write-Host "  ❌ WMI недоступен" -ForegroundColor Red
        return $false
    }
    
    return $true
}

function Install-Service {
    Write-Host "`n📦 Установка службы..." -ForegroundColor Yellow
    
    # Создание директорий
    Write-Host "  Создание директорий..." -ForegroundColor Gray
    $directories = @($installPath, $logPath, $configPath, $backupPath)
    foreach ($dir in $directories) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            Write-Host "    ✓ $dir" -ForegroundColor Green
        } else {
            Write-Host "    → $dir (существует)" -ForegroundColor Gray
        }
    }
    
    # Копирование файлов
    Write-Host "  Копирование файлов приложения..." -ForegroundColor Gray
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $srcPath = if ($scriptDir) { $scriptDir } else { $PWD }
    
    # Определяем путь к бинарникам
    $binPath = if (Test-Path "$srcPath\bin\Release\net8.0-windows\win-x64") {
        "$srcPath\bin\Release\net8.0-windows\win-x64"
    } elseif (Test-Path "$srcPath\..\..\bin\Release\net8.0-windows\win-x64") {
        "$srcPath\..\..\bin\Release\net8.0-windows\win-x64"
    } else {
        $srcPath
    }
    
    if (Test-Path "$binPath\ByeTcp.Service.exe") {
        Copy-Item -Path "$binPath\*" -Destination $installPath -Recurse -Force
        Write-Host "    ✓ Файлы скопированы из $binPath" -ForegroundColor Green
    } else {
        Write-Host "    ⚠ Бинарники не найдены. Копируем из текущей директории..." -ForegroundColor Yellow
        Copy-Item -Path "$srcPath\*" -Destination $installPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    # Копирование конфигурации
    if (Test-Path "$srcPath\config") {
        Copy-Item -Path "$srcPath\config\*" -Destination $configPath -Force -ErrorAction SilentlyContinue
        Write-Host "    ✓ Конфигурация скопирована" -ForegroundColor Green
    }
    
    # Регистрация службы
    Write-Host "  Регистрация Windows службы..." -ForegroundColor Gray
    
    # Проверяем, существует ли уже служба
    $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    
    if ($existingService) {
        Write-Host "    → Служба уже существует. Удаляем..." -ForegroundColor Yellow
        sc.exe delete $serviceName 2>$null
        Start-Sleep -Seconds 1
    }
    
    # Создаем службу
    $exePath = "$installPath\ByeTcp.Service.exe"
    $createResult = sc.exe create $serviceName `
        binPath= "\"$exePath\"" `
        start= auto `
        DisplayName= "$serviceDisplayName"
    
    if ($LASTEXITCODE -eq 0 -or $createResult -like "*SUCCESS*") {
        Write-Host "    ✓ Служба зарегистрирована" -ForegroundColor Green
    } else {
        Write-Host "    ❌ Ошибка регистрации службы" -ForegroundColor Red
        Write-Host $createResult
        return $false
    }
    
    # Настраиваем описание службы
    sc.exe description $serviceName "$serviceDescription" 2>$null | Out-Null
    
    # Настройка прав доступа
    Write-Host "  Настройка прав доступа..." -ForegroundColor Gray
    icacls $installPath /grant Administrators:F /inheritance:r 2>$null | Out-Null
    icacls $logPath /grant Administrators:F /grant SYSTEM:F 2>$null | Out-Null
    Write-Host "    ✓ Права настроены" -ForegroundColor Green
    
    # Создание резервной копии текущих настроек TCP
    Write-Host "  Создание резервной копии настроек TCP/IP..." -ForegroundColor Gray
    $backupFile = "$backupPath\factory-defaults-$(Get-Date -Format 'yyyyMMdd-HHmmss').reg"
    reg export "HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" $backupFile /y 2>$null | Out-Null
    if (Test-Path $backupFile) {
        Write-Host "    ✓ Резервная копия: $backupFile" -ForegroundColor Green
    } else {
        Write-Host "    ⚠ Не удалось создать резервную копию" -ForegroundColor Yellow
    }
    
    return $true
}

function Uninstall-Service {
    Write-Host "`n🗑️ Удаление службы..." -ForegroundColor Yellow
    
    # Остановка службы
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Host "  Остановка службы..." -ForegroundColor Gray
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        
        Write-Host "  Удаление службы..." -ForegroundColor Gray
        sc.exe delete $serviceName 2>$null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    ✓ Служба удалена" -ForegroundColor Green
        } else {
            Write-Host "    ⚠ Служба не найдена или уже удалена" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  → Служба не найдена" -ForegroundColor Gray
    }
    
    # Удаление файлов
    $response = Read-Host "  Удалить файлы приложения из $installPath? (y/n)"
    if ($response -eq 'y' -or $response -eq 'Y') {
        if (Test-Path $installPath) {
            Remove-Item -Path $installPath -Recurse -Force
            Write-Host "    ✓ Файлы удалены" -ForegroundColor Green
        }
    }
    
    Write-Host "`n✅ Удаление завершено" -ForegroundColor Green
}

function Start-ServiceWrapper {
    Write-Host "`n▶️ Запуск службы..." -ForegroundColor Yellow
    Start-Service -Name $serviceName -ErrorAction Stop
    Write-Host "  ✓ Служба запущена" -ForegroundColor Green
}

function Stop-ServiceWrapper {
    Write-Host "`n⏹️ Остановка службы..." -ForegroundColor Yellow
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    Write-Host "  ✓ Служба остановлена" -ForegroundColor Green
}

function Show-ServiceStatus {
    Write-Host "`n📊 Статус службы:" -ForegroundColor Yellow
    
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    
    if ($service) {
        $statusColor = switch ($service.Status) {
            'Running' { 'Green' }
            'Stopped' { 'Red' }
            default { 'Yellow' }
        }
        
        Write-Host "  Имя:         $($service.Name)" -ForegroundColor Gray
        Write-Host "  Отображение: $($service.DisplayName)" -ForegroundColor Gray
        Write-Host "  Статус:      $($service.Status)" -ForegroundColor $statusColor
        Write-Host "  Режим:       $($service.StartType)" -ForegroundColor Gray
        
        # Последние логи
        $logFile = "$logPath\bye-tcp.log"
        if (Test-Path $logFile) {
            Write-Host "`n  Последние записи лога:" -ForegroundColor Gray
            Get-Content $logFile -Tail 5 | ForEach-Object {
                Write-Host "    $_" -ForegroundColor DarkGray
            }
        }
    } else {
        Write-Host "  ❌ Служба не установлена" -ForegroundColor Red
    }
}

# Основная логика
Write-Logo

if (-not (Test-Requirements)) {
    Write-Host "`n❌ Проверка требований не пройдена" -ForegroundColor Red
    exit 1
}

if ($Uninstall) {
    Uninstall-Service
    exit 0
}

if ($Start) {
    Start-ServiceWrapper
    exit 0
}

if ($Stop) {
    Stop-ServiceWrapper
    exit 0
}

if ($Status) {
    Show-ServiceStatus
    exit 0
}

# Установка по умолчанию
Write-Host "`n🚀 Начало установки..." -ForegroundColor Cyan

if (Install-Service) {
    Write-Host "`n✅ Установка успешно завершена!" -ForegroundColor Green
    
    $startService = Read-Host "`nЗапустить службу сейчас? (y/n)"
    if ($startService -eq 'y' -or $startService -eq 'Y') {
        Start-Service -Name $serviceName
        Write-Host "  ✓ Служба запущена" -ForegroundColor Green
    }
    
    Write-Host @"

📋 Полезные команды:

  # Проверка статуса
  .\install.ps1 -Status

  # Остановка службы
  .\install.ps1 -Stop

  # Запуск службы
  .\install.ps1 -Start

  # Удаление
  .\install.ps1 -Uninstall

  # Просмотр логов
  Get-Content "$logPath\bye-tcp.log" -Tail 50 -Wait

📁 Пути:
  Установка:  $installPath
  Логи:       $logPath
  Конфиг:     $configPath
  Бэкапы:     $backupPath

"@ -ForegroundColor Cyan
} else {
    Write-Host "`n❌ Установка не завершена из-за ошибок" -ForegroundColor Red
    exit 1
}
