@echo off
chcp 65001 >nul
title TCP/IP Optimizer Service - Управление службой

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
    echo  ERROR: Требуется запуск от имени администратора!
    echo ============================================
    echo.
    echo Нажмите правой кнопкой мыши на service.bat
    echo и выберите "Запуск от имени администратора"
    echo.
    pause
    exit /b 1
)

:menu
cls
echo ============================================
echo   TCP/IP OPTIMIZER SERVICE - Управление
echo ============================================
echo.
echo Выберите действие:
echo.
echo   [1] Установить службу (Install Service)
echo       Установка службы Windows для авто-оптимизации
echo.
echo   [2] Удалить службу (Remove Service)
echo       Полное удаление службы из системы
echo.
echo   [3] Запустить службу (Start Service)
echo       Запуск установленной службы
echo.
echo   [4] Остановить службу (Stop Service)
echo       Остановка работающей службы
echo.
echo   [5] Статус службы (Check Status)
echo       Проверка текущего состояния службы
echo.
echo   [6] Диагностика (Run Diagnostics)
echo       Проверка работоспособности системы
echo.
echo   [7] Просмотр логов (View Logs)
echo       Открыть файл логов службы
echo.
echo   [8] Перезапустить службу (Restart Service)
echo       Остановка и запуск службы
echo.
echo   [0] Выход
echo.
echo ============================================
set /p choice="Ваш выбор: "

if "%choice%"=="1" goto install
if "%choice%"=="2" goto remove
if "%choice%"=="3" goto start
if "%choice%"=="4" goto stop
if "%choice%"=="5" goto status
if "%choice%"=="6" goto diagnostics
if "%choice%"=="7" goto logs
if "%choice%"=="8" goto restart
if "%choice%"=="0" goto end
goto menu

:install
echo.
echo ============================================
echo   Установка службы TCP/IP Optimizer
echo ============================================
echo.
echo Проверка наличия pywin32...
%PYTHON% -c "import win32service" 2>nul
if errorlevel 1 (
    echo.
    echo Модуль pywin32 не найден.
    echo Установка pywin32...
    echo.
    %PYTHON% -m pip install pywin32 --quiet
    if errorlevel 1 (
        echo.
        echo ERROR: Не удалось установить pywin32!
        echo.
        echo Попробуйте установить вручную:
        echo   pip install pywin32
        echo.
        pause
        goto menu
    )
    echo.
    echo pywin32 установлен. Выполнение пост-установки...
    %PYTHON% -m pywin32_postinstall -install
)

echo.
echo Установка службы...
%PYTHON% "%~dp0bin\install_service.py" install
echo.
echo ============================================
echo   Служба установлена!
echo ============================================
echo.
echo Служба будет автоматически запускаться при загрузке Windows.
echo Для применения настроек в реальном времени служба должна работать.
echo.
pause
goto menu

:remove
echo.
echo ============================================
echo   Удаление службы TCP/IP Optimizer
echo ============================================
echo.
set /p confirm="Вы уверены, что хотите удалить службу? (Y/N): "
if /i not "%confirm%"=="Y" goto menu

echo.
%PYTHON% "%~dp0bin\install_service.py" remove
echo.
echo Служба удалена.
echo.
pause
goto menu

:start
echo.
echo ============================================
echo   Запуск службы
echo ============================================
echo.
%PYTHON% "%~dp0bin\install_service.py" start
echo.
pause
goto menu

:stop
echo.
echo ============================================
echo   Остановка службы
echo ============================================
echo.
%PYTHON% "%~dp0bin\install_service.py" stop
echo.
pause
goto menu

:status
echo.
echo ============================================
echo   Статус службы
echo ============================================
echo.
sc query TCPOptimizerService >nul 2>&1
if errorlevel 1 (
    echo Служба TCPOptimizerService НЕ установлена.
    echo.
    echo Установите службу через пункт меню [1].
) else (
    sc query TCPOptimizerService
    echo.
    echo ============================================
    echo   Дополнительная информация
    echo ============================================
    %PYTHON% "%~dp0bin\tcp_optimizer_service.py" status
)
echo.
pause
goto menu

:diagnostics
echo.
echo ============================================
echo   Диагностика системы
echo ============================================
echo.
%PYTHON% "%~dp0bin\tcp_optimizer_service.py" diagnostics
echo.
echo ============================================
echo   Проверка службы Windows
echo ============================================
echo.
sc query TCPOptimizerService 2>nul | find "STATE"
if errorlevel 1 (
    echo Служба не установлена или не запущена.
)
echo.
pause
goto menu

:logs
echo.
echo ============================================
echo   Просмотр логов
echo ============================================
echo.
set "logpath=%~dp0logs\tcp_optimizer.log"
if exist "%logpath%" (
    echo Файл логов: %logpath%
    echo.
    echo Последние 50 строк:
    echo --------------------------------------------
    powershell -Command "Get-Content '%logpath%' -Tail 50 -Encoding UTF8"
    echo --------------------------------------------
    echo.
    set /p open="Открыть файл логов в блокноте? (Y/N): "
    if /i "%open%"=="Y" (
        notepad "%logpath%"
    )
) else (
    echo Файл логов не найден.
    echo Логи создаются после первого запуска службы.
)
echo.
pause
goto menu

:restart
echo.
echo ============================================
echo   Перезапуск службы
echo ============================================
echo.
echo Остановка службы...
%PYTHON% "%~dp0bin\install_service.py" stop
timeout /t 2 >nul
echo Запуск службы...
%PYTHON% "%~dp0bin\install_service.py" start
echo.
echo Служба перезапущена.
echo.
pause
goto menu

:end
echo.
echo Завершение работы...
timeout /t 1 >nul
exit /b 0
