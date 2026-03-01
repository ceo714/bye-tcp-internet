# Bye-TCP Internet - Build Script
# PowerShell скрипт для сборки проекта

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory = $false)]
    [switch]$Clean,
    
    [Parameter(Mandatory = $false)]
    [switch]$Publish,
    
    [Parameter(Mandatory = $false)]
    [switch]$BuildNative
)

$ErrorActionPreference = "Stop"

function Write-Logo {
    Write-Host @"
╔═══════════════════════════════════════════════════════════╗
║           Bye-TCP Internet Build Script                   ║
╚═══════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan
}

function Test-Prerequisites {
    Write-Host "🔍 Проверка зависимостей..." -ForegroundColor Yellow
    
    # Проверка .NET SDK
    $dotnetVersion = dotnet --version 2>$null
    if (-not $dotnetVersion) {
        Write-Host "❌ .NET SDK не найден. Установите .NET 8 SDK" -ForegroundColor Red
        Write-Host "   https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Gray
        return $false
    }
    Write-Host "  ✓ .NET SDK $dotnetVersion" -ForegroundColor Green
    
    # Проверка MSBuild для native модуля
    if ($BuildNative) {
        $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null
        if ($msbuild) {
            Write-Host "  ✓ MSBuild найден" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ MSBuild не найден. Native модуль не будет собран." -ForegroundColor Yellow
            $script:BuildNative = $false
        }
    }
    
    return $true
}

function Invoke-Clean {
    Write-Host "`n🧹 Очистка..." -ForegroundColor Yellow
    
    $paths = @("bin", "obj", "src\bin", "src\obj")
    foreach ($path in $paths) {
        if (Test-Path $path) {
            Remove-Item -Path $path -Recurse -Force
            Write-Host "  ✓ Удалено: $path" -ForegroundColor Green
        }
    }
}

function Invoke-Build {
    Write-Host "`n🔨 Сборка проекта..." -ForegroundColor Yellow
    Write-Host "  Конфигурация: $Configuration" -ForegroundColor Gray
    
    # Сборка .NET проектов
    dotnet build ByeTcp.sln -c $Configuration -v q
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ .NET проекты собраны успешно" -ForegroundColor Green
    } else {
        Write-Host "  ❌ Ошибка сборки .NET проектов" -ForegroundColor Red
        return $false
    }
    
    # Сборка native модуля
    if ($BuildNative) {
        Write-Host "  Сборка native модуля..." -ForegroundColor Gray
        
        $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null
        
        if ($msbuild) {
            & $msbuild src\ByeTcp.Native\ByeTcp.Native.vcxproj `
                -p:Configuration=$Configuration `
                -p:Platform=x64 `
                -v:q
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  ✓ Native модуль собран успешно" -ForegroundColor Green
            } else {
                Write-Host "  ⚠ Ошибка сборки native модуля" -ForegroundColor Yellow
            }
        }
    }
    
    return $true
}

function Invoke-Publish {
    Write-Host "`n📦 Публикация..." -ForegroundColor Yellow
    
    $publishDir = "bin\publish\win-x64"
    
    dotnet publish src\ByeTcp.Service\ByeTcp.Service.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $publishDir `
        -v q
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Публикация завершена: $publishDir" -ForegroundColor Green
        
        # Копирование конфигурации
        if (Test-Path "config") {
            Copy-Item -Path "config\*" -Destination "$publishDir\config" -Force
            Write-Host "  ✓ Конфигурация скопирована" -ForegroundColor Green
        }
        
        # Копирование native модуля
        if (Test-Path "src\ByeTcp.Native\x64\$Configuration\ByeTcp.Native.dll") {
            Copy-Item -Path "src\ByeTcp.Native\x64\$Configuration\ByeTcp.Native.dll" -Destination $publishDir -Force
            Write-Host "  ✓ Native модуль скопирован" -ForegroundColor Green
        }
    } else {
        Write-Host "  ❌ Ошибка публикации" -ForegroundColor Red
        return $false
    }
    
    return $true
}

function Show-Summary {
    Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║                    Сборка завершена                       ║
╚═══════════════════════════════════════════════════════════╝

📁 Выходные файлы:
   bin\publish\win-x64\

📋 Следующие шаги:
   1. Проверьте файлы в папке публикации
   2. Запустите .\scripts\install.ps1 для установки
   3. Проверьте статус: .\scripts\install.ps1 -Status

"@ -ForegroundColor Green
}

# Основная логика
Write-Logo

if (-not (Test-Prerequisites)) {
    Write-Host "`n❌ Проверка зависимостей не пройдена" -ForegroundColor Red
    exit 1
}

if ($Clean) {
    Invoke-Clean
}

if (-not (Invoke-Build)) {
    Write-Host "`n❌ Сборка не завершена" -ForegroundColor Red
    exit 1
}

if ($Publish) {
    if (-not (Invoke-Publish)) {
        Write-Host "`n❌ Публикация не завершена" -ForegroundColor Red
        exit 1
    }
    Show-Summary
} else {
    Write-Host "`n✅ Сборка завершена успешно!" -ForegroundColor Green
    Write-Host "   Для публикации используйте: .\build.ps1 -Publish" -ForegroundColor Gray
}
