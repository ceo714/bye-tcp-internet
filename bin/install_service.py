#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Установка/удаление службы TCP/IP Optimizer Service.
Использует pywin32 для интеграции со службами Windows.
"""

import os
import sys
import ctypes
import winreg
import subprocess
import shutil

SERVICE_NAME = "TCPOptimizerService"
SERVICE_DISPLAY_NAME = "TCP/IP Optimizer Service"
SERVICE_DESCRIPTION = "Автоматическая оптимизация параметров TCP/IP на основе активных процессов"

# Проверка прав администратора
def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False

def get_python_path():
    """Получение пути к интерпретатору Python."""
    return sys.executable

def get_script_path():
    """Получение пути к скрипту службы."""
    return os.path.abspath(__file__.replace('install_service.py', 'tcp_optimizer_service.py'))

def check_pywin32():
    """Проверка наличия pywin32."""
    try:
        import win32service
        import win32serviceutil
        import servicemanager
        return True
    except ImportError:
        return False

def install_pywin32():
    """Установка pywin32."""
    print("Установка pywin32...")
    subprocess.check_call([sys.executable, "-m", "pip", "install", "pywin32", "--quiet"])
    # Запуск пост-установочного скрипта
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pywin32_postinstall", "-install"], 
                         shell=True)
    print("pywin32 установлен успешно.")

def install_service():
    """Установка службы Windows."""
    if not is_admin():
        print("ERROR: Требуется запуск от имени администратора!")
        print("Запустите этот скрипт правой кнопкой мыши -> Запуск от имени администратора")
        return False
    
    python_path = get_python_path()
    script_path = get_script_path()
    
    # Команда для запуска службы
    # Используем pythonw.exe для фонового запуска без консоли
    pythonw_path = python_path.replace('python.exe', 'pythonw.exe')
    if not os.path.exists(pythonw_path):
        pythonw_path = python_path
    
    service_cmd = f'"{pythonw_path}" "{script_path}" start'
    
    try:
        import win32service
        import win32serviceutil
        import servicemanager
        import win32event
    except ImportError:
        print("ERROR: Модуль pywin32 не найден.")
        print("Установите его командой: pip install pywin32")
        print("Или запустите: python install_service.py install-pywin32")
        return False
    
    try:
        # Удаляем службу если существует
        try:
            win32serviceutil.RemoveService(SERVICE_NAME)
            print(f"Старая версия службы удалена.")
        except:
            pass
        
        # Устанавливаем службу
        win32serviceutil.InstallService(
            SERVICE_NAME,
            SERVICE_DISPLAY_NAME,
            SERVICE_DESCRIPTION,
            exeArgs='start',
            startType=win32service.SERVICE_AUTO_START,
            bRunInteractive=0,
            exeName=pythonw_path,
            workingDirectory=os.path.dirname(script_path)
        )
        
        print(f"✓ Служба '{SERVICE_DISPLAY_NAME}' установлена успешно!")
        print(f"  Имя службы: {SERVICE_NAME}")
        print(f"  Тип запуска: Автоматически")
        
        # Запускаем службу
        try:
            win32serviceutil.StartService(SERVICE_NAME)
            print(f"✓ Служба запущена.")
        except Exception as e:
            print(f"⚠ Служба установлена, но не запущена: {e}")
            print(f"  Запустите вручную: net start {SERVICE_NAME}")
        
        return True
        
    except Exception as e:
        print(f"ERROR: Ошибка установки службы: {e}")
        return False

def remove_service():
    """Удаление службы Windows."""
    if not is_admin():
        print("ERROR: Требуется запуск от имени администратора!")
        return False
    
    try:
        import win32serviceutil
    except ImportError:
        print("ERROR: Модуль pywin32 не найден.")
        return False
    
    try:
        # Останавливаем службу
        try:
            win32serviceutil.StopService(SERVICE_NAME)
            print(f"✓ Служба остановлена.")
        except:
            pass
        
        # Удаляем службу
        win32serviceutil.RemoveService(SERVICE_NAME)
        print(f"✓ Служба '{SERVICE_NAME}' удалена успешно!")
        return True
        
    except Exception as e:
        print(f"ERROR: Ошибка удаления службы: {e}")
        return False

def check_service_status():
    """Проверка статуса службы."""
    try:
        import win32service
        import win32serviceutil
    except ImportError:
        print("ERROR: Модуль pywin32 не найден.")
        return None
    
    try:
        status = win32serviceutil.QueryServiceStatus(SERVICE_NAME)
        status_code = status[1]
        
        status_names = {
            win32service.SERVICE_STOPPED: "Остановлена",
            win32service.SERVICE_START_PENDING: "Запуск...",
            win32service.SERVICE_STOP_PENDING: "Остановка...",
            win32service.SERVICE_RUNNING: "Работает",
            win32service.SERVICE_CONTINUE_PENDING: "Продолжение...",
            win32service.SERVICE_PAUSE_PENDING: "Приостановка...",
            win32service.SERVICE_PAUSED: "Приостановлена",
        }
        
        return status_names.get(status_code, f"Неизвестно ({status_code})")
        
    except Exception:
        return "Не установлена"

def start_service():
    """Запуск службы."""
    if not is_admin():
        print("ERROR: Требуется запуск от имени администратора!")
        return False
    
    try:
        import win32serviceutil
        win32serviceutil.StartService(SERVICE_NAME)
        print(f"✓ Служба запущена.")
        return True
    except Exception as e:
        print(f"ERROR: Ошибка запуска службы: {e}")
        return False

def stop_service():
    """Остановка службы."""
    if not is_admin():
        print("ERROR: Требуется запуск от имени администратора!")
        return False
    
    try:
        import win32serviceutil
        win32serviceutil.StopService(SERVICE_NAME)
        print(f"✓ Служба остановлена.")
        return True
    except Exception as e:
        print(f"ERROR: Ошибка остановки службы: {e}")
        return False

def print_status():
    """Вывод статуса."""
    status = check_service_status()
    print(f"\nСтатус службы {SERVICE_NAME}: {status}\n")

def show_help():
    """Показ справки."""
    print(f"""
Установка/удаление службы {SERVICE_DISPLAY_NAME}

Использование (от имени администратора):
    install_service.py <команда>

Команды:
    install     - Установить службу
    remove      - Удалить службу
    start       - Запустить службу
    stop        - Остановить службу
    status      - Показать статус
    check       - Проверить наличие pywin32
    help        - Показать эту справку

Примеры:
    install_service.py install
    install_service.py status
    install_service.py remove
""")

def main():
    """Точка входа."""
    if len(sys.argv) < 2:
        show_help()
        return
    
    command = sys.argv[1].lower()
    
    if command == 'install':
        install_service()
    
    elif command == 'remove':
        remove_service()
    
    elif command == 'start':
        start_service()
    
    elif command == 'stop':
        stop_service()
    
    elif command == 'status':
        print_status()
    
    elif command == 'check':
        if check_pywin32():
            print("pywin32 установлен.")
        else:
            print("pywin32 НЕ найден.")
            print("Установите: pip install pywin32")
    
    elif command == 'install-pywin32':
        if is_admin():
            install_pywin32()
        else:
            print("ERROR: Требуется запуск от имени администратора!")
    
    elif command == 'help':
        show_help()
    
    else:
        print(f"Неизвестная команда: {command}")
        show_help()

if __name__ == '__main__':
    main()
