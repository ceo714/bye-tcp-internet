@echo off
chcp 65001 >nul
title TCP/IP Optimizer - Быстрый запуск

:: Переход в директорию скрипта
cd /d "%~dp0"

:: Проверка наличия Python
where py >nul 2>&1
if %errorLevel% neq 0 (
    where python >nul 2>&1
    if %errorLevel% neq 0 (
        echo.
        echo ============================================
        echo  ERROR: Python не найден!
        echo ============================================
        echo.
        echo Установите Python 3.8+ с https://www.python.org/downloads/
        echo.
        pause
        exit /b 1
    )
    set PYTHON=python
) else (
    set PYTHON=py
)

:: Проверка прав администратора
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo ============================================
    echo  Требуется запуск от имени администратора!
    echo ============================================
    echo.
    echo Перезапуск с правами администратора...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo ============================================
echo   TCP/IP OPTIMIZER - Быстрый запуск
echo ============================================
echo.

:: Проверка зависимостей
echo Проверка зависимостей...
%PYTHON% -c "import win32service" 2>nul
if errorlevel 1 (
    echo.
    echo Зависимости не установлены.
    echo Хотите установить сейчас? (Y/N)
    set /p install="> "
    if /i "%install%"=="Y" (
        call install.bat
    ) else (
        echo.
        echo Без зависимостей работа невозможна.
        pause
        exit /b 1
    )
)

echo.
echo ============================================
echo   Выберите режим работы:
echo ============================================
echo.
echo   [1] Ручное применение стратегий (general.bat)
echo       Интерактивный выбор профиля оптимизации
echo.
echo   [2] Установка службы (service.bat)
echo       Автоматическая оптимизация в фоне
echo.
echo   [3] Диагностика
echo       Проверка текущего состояния
echo.
echo   [4] Сброс настроек
echo       Вернуть стандартные настройки Windows
echo.
echo   [0] Выход
echo.
set /p choice="Ваш выбор: "

if "%choice%"=="1" (
    start "" "general.bat"
    exit /b
)
if "%choice%"=="2" (
    start "" "service.bat"
    exit /b
)
if "%choice%"=="3" (
    %PYTHON% bin\tcp_optimizer_service.py diagnostics
    pause
    goto :EOF
)
if "%choice%"=="4" (
    %PYTHON% bin\tcp_optimizer_service.py reset
    echo.
    echo Настройки сброшены. Требуется перезагрузка.
    pause
    goto :EOF
)
if "%choice%"=="0" (
    exit /b
)

echo.
echo Неверный выбор.
pause
