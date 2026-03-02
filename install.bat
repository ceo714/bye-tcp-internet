@echo off
chcp 65001 >nul
title TCP/IP Optimizer - Установка зависимостей

echo.
echo ============================================
echo   Установка зависимостей TCP/IP Optimizer
echo ============================================
echo.

:: Проверка наличия Python
where py >nul 2>&1
if %errorLevel% neq 0 (
    where python >nul 2>&1
    if %errorLevel% neq 0 (
        echo ERROR: Python не найден!
        echo.
        echo Установите Python 3.8 или выше:
        echo https://www.python.org/downloads/
        echo.
        pause
        exit /b 1
    )
    set PYTHON=python
) else (
    set PYTHON=py
)

echo Найден Python:
%PYTHON% --version
echo.

:: Обновление pip
echo Обновление pip...
%PYTHON% -m pip install --upgrade pip --quiet

:: Установка зависимостей
echo.
echo Установка зависимостей...
echo.
%PYTHON% -m pip install -r requirements.txt

if errorlevel 1 (
    echo.
    echo ERROR: Не удалось установить зависимости!
    echo Попробуйте установить вручную:
    echo   pip install pywin32 psutil
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Зависимости установлены успешно!
echo ============================================
echo.
echo Теперь вы можете запустить:
echo   - general.bat  для ручного применения стратегий
echo   - service.bat  для установки службы
echo.
echo Не забудьте запускать от имени администратора!
echo.
pause
