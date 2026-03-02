@echo off
chcp 65001 >nul
title TCP/IP Optimizer - Ручное применение стратегий

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
        echo При установке отметьте "Add Python to PATH"
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
    echo  ERROR: Требуется запуск от имени администратора!
    echo ============================================
    echo.
    echo Нажмите правой кнопкой мыши на general.bat
    echo и выберите "Запуск от имени администратора"
    echo.
    pause
    exit /b 1
)

:menu
cls
echo ============================================
echo   TCP/IP OPTIMIZER - Выбор стратегии
echo ============================================
echo.
echo Выберите стратегию оптимизации:
echo.
echo   [1] Gaming (Low Latency)
echo       Для игр: CS2, Valorant, Apex Legends и др.
echo       - TcpAckFrequency = 1
echo       - TCPNoDelay = 1 (отключение Нагла)
echo       - Минимальная задержка
echo.
echo   [2] Torrent (High Throughput)
echo       Для торрентов: qBittorrent, uTorrent и др.
echo       - Увеличенный RWIN
echo       - Агрессивные алгоритмы перегрузки
echo       - Максимальная пропускная способность
echo.
echo   [3] Streaming (Balanced)
echo       Для стриминга: YouTube, Netflix, VLC
echo       - Сбалансированные настройки
echo       - Оптимально для видео
echo.
echo   [4] Video Conference (Real-time)
echo       Для видеоконференций: Zoom, Teams, Discord
echo       - Приоритет реальному времени
echo       - Минимальные задержки
echo.
echo   [5] Downloads (Bulk Transfer)
echo       Для загрузок: Steam, Epic Games, Battle.net
echo       - Оптимизация для больших файлов
echo.
echo   [6] Windows Default (Сброс)
echo       Вернуть стандартные настройки Windows
echo.
echo   [7] Диагностика
echo       Проверка текущего состояния
echo.
echo   [0] Выход
echo.
echo ============================================
set /p choice="Ваш выбор: "

if "%choice%"=="1" goto gaming
if "%choice%"=="2" goto torrent
if "%choice%"=="3" goto streaming
if "%choice%"=="4" goto conference
if "%choice%"=="5" goto download
if "%choice%"=="6" goto default
if "%choice%"=="7" goto diagnostics
if "%choice%"=="0" goto end
goto menu

:gaming
echo.
echo ============================================
echo   Применение стратегии: GAMING
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" profile
echo.
echo Стратегия Gaming применена!
echo.
echo Применённые настройки:
echo   - TcpAckFrequency = 1
echo   - TCPNoDelay = 1
echo   - TcpDelAckTicks = 0
echo   - SackOpts = 1
echo.
echo Для игр рекомендуется также:
echo   - Отключить Nagle алгоритм в реестре
echo   - Установить приоритет трафика
echo.
pause
goto menu

:torrent
echo.
echo ============================================
echo   Применение стратегии: TORRENT
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" profile
echo.
echo Стратегия Torrent применена!
echo.
echo Применённые настройки:
echo   - TcpAckFrequency = 2
echo   - Увеличенный TcpWindowSize
echo   - Tcp1323Opts = 3 (window scaling + timestamps)
echo   - Оптимизация для высокой пропускной способности
echo.
pause
goto menu

:streaming
echo.
echo ============================================
echo   Применение стратегии: STREAMING
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" profile
echo.
echo Стратегия Streaming применена!
echo.
echo Применённые настройки:
echo   - TcpAckFrequency = 1
echo   - Сбалансированные параметры
echo   - Tcp1323Opts = 3
echo   - Оптимально для потокового видео
echo.
pause
goto menu

:conference
echo.
echo ============================================
echo   Применение стратегии: VIDEO CONFERENCE
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" profile
echo.
echo Стратегия Video Conference применена!
echo.
echo Применённые настройки:
echo   - TcpAckFrequency = 1
echo   - TCPNoDelay = 1
echo   - TcpDelAckTicks = 0
echo   - DontFragment = 1
echo   - Приоритет реальному времени
echo.
pause
goto menu

:download
echo.
echo ============================================
echo   Применение стратегии: DOWNLOADS
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" profile
echo.
echo Стратегия Downloads применена!
echo.
echo Применённые настройки:
echo   - TcpAckFrequency = 2
echo   - Увеличенный TcpWindowSize
echo   - Оптимизация для больших файлов
echo   - TcpMaxDataRetransmissions = 10
echo.
pause
goto menu

:default
echo.
echo ============================================
echo   Сброс к настройкам Windows по умолчанию
echo ============================================
echo.
set /p confirm="Вы уверены? (Y/N): "
if /i not "%confirm%"=="Y" goto menu

%PYTHON% "%~dp0bin\tcp_optimizer_service.py" reset
echo.
echo Настройки сброшены!
echo Требуется перезагрузка компьютера для полного применения.
echo.
pause
goto menu

:diagnostics
echo.
echo ============================================
echo   Диагностика TCP/IP Optimizer
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" diagnostics
echo.
pause
goto menu

:end
echo.
echo Завершение работы...
timeout /t 1 >nul
exit /b 0
