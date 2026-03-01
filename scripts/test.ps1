# Bye-TCP Internet - Тестовый скрипт валидации
# PowerShell скрипт для проверки работоспособности службы

param(
    [Parameter(Mandatory = $false)]
    [switch]$Verbose
)

$serviceName = "ByeTcp"
$installPath = "C:\Program Files\ByeTcp"
$logPath = "$installPath\logs"

$testResults = @{
    Passed = 0
    Failed = 0
    Warnings = 0
}

function Write-TestHeader {
    Write-Host @"
╔═══════════════════════════════════════════════════════════╗
║           Bye-TCP Internet Validation Tests               ║
╚═══════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan
}

function Test-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Message = ""
    )
    
    if ($Passed) {
        Write-Host "  ✓ $Name" -ForegroundColor Green
        $testResults.Passed++
    } else {
        Write-Host "  ✗ $Name" -ForegroundColor Red
        if ($Message) {
            Write-Host "    → $Message" -ForegroundColor DarkGray
        }
        $testResults.Failed++
    }
}

function Test-Warning {
    param(
        [string]$Name,
        [string]$Message = ""
    )
    
    Write-Host "  ⚠ $Name" -ForegroundColor Yellow
    if ($Message) {
        Write-Host "    → $Message" -ForegroundColor DarkGray
    }
    $testResults.Warnings++
}

Write-TestHeader

# Тест 1: Проверка службы
Write-Host "`n📋 Тест 1: Проверка службы Windows" -ForegroundColor Yellow
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
Test-Result "Служба зарегистрирована" ($null -ne $service)

if ($service) {
    Test-Result "Служба запущена" ($service.Status -eq 'Running')
    Test-Result "Автозапуск" ($service.StartType -eq 'Auto')
}

# Тест 2: Проверка файлов
Write-Host "`n📋 Тест 2: Проверка файлов установки" -ForegroundColor Yellow
Test-Result "Директория установки" (Test-Path $installPath)
Test-Result "EXE файл службы" (Test-Path "$installPath\ByeTcp.Service.exe")
Test-Result "Директория логов" (Test-Path $logPath)
Test-Result "Директория конфигурации" (Test-Path "$installPath\config")
Test-Result "Директория бэкапов" (Test-Path "$installPath\backups")

# Тест 3: Проверка конфигурации
Write-Host "`n📋 Тест 3: Проверка конфигурации" -ForegroundColor Yellow
$configPath = "$installPath\config"

if (Test-Path "$configPath\rules.json") {
    try {
        $rules = Get-Content "$configPath\rules.json" | ConvertFrom-Json
        Test-Result "rules.json валиден" ($rules.rules.Count -gt 0)
        if ($Verbose) {
            Write-Host "    → Загружено $($rules.rules.Count) правил" -ForegroundColor Gray
        }
    } catch {
        Test-Result "rules.json валиден" $false "Ошибка парсинга JSON"
    }
} else {
    Test-Warning "rules.json не найден" "Используются встроенные правила"
}

if (Test-Path "$configPath\profiles.json") {
    try {
        $profiles = Get-Content "$configPath\profiles.json" | ConvertFrom-Json
        Test-Result "profiles.json валиден" ($profiles.profiles.Count -gt 0)
        if ($Verbose) {
            Write-Host "    → Загружено $($profiles.profiles.Count) профилей" -ForegroundColor Gray
        }
    } catch {
        Test-Result "profiles.json валиден" $false "Ошибка парсинга JSON"
    }
} else {
    Test-Warning "profiles.json не найден" "Используются встроенные профили"
}

# Тест 4: Проверка WMI
Write-Host "`n📋 Тест 4: Проверка WMI" -ForegroundColor Yellow
try {
    $process = Get-WmiObject -Class Win32_Process -Query "SELECT * FROM Win32_Process WHERE ProcessId = $PID" -ErrorAction Stop
    Test-Result "WMI доступен" $true
} catch {
    Test-Result "WMI доступен" $false $_.Exception.Message
}

# Тест 5: Проверка NetSh
Write-Host "`n📋 Тест 5: Проверка NetSh" -ForegroundColor Yellow
try {
    $tcpGlobal = netsh int tcp show global 2>$null
    Test-Result "NetSh доступен" ($null -ne $tcpGlobal)
    
    if ($Verbose -and $tcpGlobal) {
        Write-Host "  Текущие настройки TCP:" -ForegroundColor Gray
        $tcpGlobal | Select-Object -First 10 | ForEach-Object {
            Write-Host "    $_" -ForegroundColor DarkGray
        }
    }
} catch {
    Test-Result "NetSh доступен" $false $_.Exception.Message
}

# Тест 6: Проверка реестра
Write-Host "`n📋 Тест 6: Проверка реестра TCP/IP" -ForegroundColor Yellow
try {
    $tcpipKey = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -ErrorAction Stop
    Test-Result "Registry ключ TCP/IP доступен" $true
} catch {
    Test-Result "Registry ключ TCP/IP доступен" $false $_.Exception.Message
}

# Тест 7: Проверка сетевых интерфейсов
Write-Host "`n📋 Тест 7: Проверка сетевых интерфейсов" -ForegroundColor Yellow
try {
    $adapters = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' }
    Test-Result "Активные сетевые адаптеры" ($adapters.Count -gt 0)
    
    if ($Verbose) {
        foreach ($adapter in $adapters) {
            Write-Host "    → $($adapter.Name): $($adapter.InterfaceDescription)" -ForegroundColor Gray
        }
    }
} catch {
    Test-Warning "Не удалось получить сетевые адаптеры" $_.Exception.Message
}

# Тест 8: Проверка логов
Write-Host "`n📋 Тест 8: Проверка логов" -ForegroundColor Yellow
$logFile = "$logPath\bye-tcp.log"
if (Test-Path $logFile) {
    Test-Result "Файл лога существует" $true
    
    try {
        $lastLines = Get-Content $logFile -Tail 5
        Test-Result "Лог записывается" ($lastLines.Count -gt 0)
        
        if ($Verbose) {
            Write-Host "  Последние записи:" -ForegroundColor Gray
            $lastLines | ForEach-Object {
                Write-Host "    $_" -ForegroundColor DarkGray
            }
        }
    } catch {
        Test-Warning "Не удалось прочитать лог" $_.Exception.Message
    }
} else {
    Test-Warning "Файл лога не найден" "Служба еще не запускалась или логирование отключено"
}

# Тест 9: Проверка резервных копий
Write-Host "`n📋 Тест 9: Проверка резервных копий" -ForegroundColor Yellow
$backupPath = "$installPath\backups"
if (Test-Path $backupPath) {
    $backups = Get-ChildItem "$backupPath\*.reg" -ErrorAction SilentlyContinue
    Test-Result "Резервные копии существуют" ($backups.Count -gt 0)
    
    if ($Verbose -and $backups.Count -gt 0) {
        Write-Host "  Найдено $($backups.Count) резервных копий:" -ForegroundColor Gray
        $backups | ForEach-Object {
            Write-Host "    → $($_.Name)" -ForegroundColor Gray
        }
    }
} else {
    Test-Warning "Директория бэкапов не найдена"
}

# Итоги
Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║                      Результаты тестов                     ║
╚═══════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

Write-Host "  Пройдено:    $($testResults.Passed)" -ForegroundColor Green
Write-Host "  Не пройдено: $($testResults.Failed)" -ForegroundColor $(if ($testResults.Failed -gt 0) { "Red" } else { "Green" })
Write-Host "  Предупреждения: $($testResults.Warnings)" -ForegroundColor Yellow

if ($testResults.Failed -eq 0) {
    Write-Host "`n✅ Все тесты пройдены!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n❌ Некоторые тесты не пройдены" -ForegroundColor Red
    exit 1
}
