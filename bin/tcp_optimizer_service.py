#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
TCP/IP Optimizer Service for Windows 10/11
Автоматическая настройка параметров TCP/IP стека на основе активных процессов.
"""

import os
import sys
import time
import json
import ctypes
import logging
import threading
import winreg
import psutil
from datetime import datetime
from typing import Dict, Optional, List

# Проверка прав администратора
def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False

# Настройка логирования
LOG_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'logs')
os.makedirs(LOG_DIR, exist_ok=True)

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler(os.path.join(LOG_DIR, 'tcp_optimizer.log'), encoding='utf-8'),
        logging.StreamHandler()
    ]
)
logger = logging.getLogger(__name__)

# Путь к ключам реестра TCP/IP
TCP_PARAMS_KEY = r"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"
GLOBAL_TCP_KEY = r"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"

# Профили процессов и соответствующие настройки
PROCESS_PROFILES = {
    # Игры (низкая задержка)
    'gaming': {
        'processes': ['cs2.exe', 'csgo.exe', 'valorant.exe', 'overwatch.exe', 
                      'apex_legends.exe', 'fortnite.exe', 'minecraft.exe',
                      'leagueoflegends.exe', 'dota2.exe', 'rocketleague.exe'],
        'name': 'Gaming (Low Latency)',
        'settings': {
            'TcpAckFrequency': 1,
            'TCPNoDelay': 1,
            'TcpDelAckTicks': 0,
            'TcpMaxDupAcks': 2,
            'SackOpts': 1,
            'TcpMaxDataRetransmissions': 3,
            'InitialRtt': 300,
            'TcpInitialRtt': 300,
        }
    },
    # Торренты (максимальная пропускная способность)
    'torrent': {
        'processes': ['qbittorrent.exe', 'utorrent.exe', 'bittorrent.exe',
                      'deluge.exe', 'transmission.exe', 'vuze.exe'],
        'name': 'Torrent (High Throughput)',
        'settings': {
            'TcpAckFrequency': 2,
            'TCPNoDelay': 0,
            'TcpDelAckTicks': 0,
            'TcpMaxDupAcks': 6,
            'SackOpts': 1,
            'TcpMaxDataRetransmissions': 10,
            'TcpWindowSize': 64240,
            'GlobalMaxTcpWindowSize': 64240,
            'Tcp1323Opts': 3,
            'DefaultTTL': 64,
        }
    },
    # Стриминг видео
    'streaming': {
        'processes': ['vlc.exe', 'mpc-hc.exe', 'mpc-hc64.exe', 'potplayer.exe',
                      'chrome.exe', 'firefox.exe', 'msedge.exe', 'brave.exe',
                      'netflix.exe', 'spotify.exe'],
        'name': 'Streaming (Balanced)',
        'settings': {
            'TcpAckFrequency': 1,
            'TCPNoDelay': 0,
            'TcpDelAckTicks': 1,
            'TcpMaxDupAcks': 3,
            'SackOpts': 1,
            'TcpMaxDataRetransmissions': 5,
            'TcpWindowSize': 64240,
            'Tcp1323Opts': 3,
        }
    },
    # Видеоконференции
    'conference': {
        'processes': ['zoom.exe', 'teams.exe', 'skype.exe', 'discord.exe',
                      'webex.exe', 'meet.exe'],
        'name': 'Video Conference (Real-time)',
        'settings': {
            'TcpAckFrequency': 1,
            'TCPNoDelay': 1,
            'TcpDelAckTicks': 0,
            'TcpMaxDupAcks': 2,
            'SackOpts': 1,
            'TcpMaxDataRetransmissions': 3,
            'DontFragment': 1,
        }
    },
    # Загрузки файлов
    'download': {
        'processes': ['steam.exe', 'epicgameslauncher.exe', 'origin.exe',
                      'battle.net.exe', 'galaxy-client.exe'],
        'name': 'Downloads (Bulk Transfer)',
        'settings': {
            'TcpAckFrequency': 2,
            'TCPNoDelay': 0,
            'TcpDelAckTicks': 2,
            'TcpMaxDupAcks': 6,
            'SackOpts': 1,
            'TcpMaxDataRetransmissions': 10,
            'TcpWindowSize': 64240,
            'Tcp1323Opts': 3,
        }
    },
    # Стандартный профиль Windows
    'default': {
        'processes': [],
        'name': 'Windows Default',
        'settings': {}
    }
}

# Стандартные значения реестра для сброса
DEFAULT_REGISTRY_VALUES = {
    'TcpAckFrequency': 2,
    'TCPNoDelay': 0,
    'TcpDelAckTicks': 2,
    'TcpMaxDupAcks': 2,
    'SackOpts': 1,
    'TcpMaxDataRetransmissions': 5,
    'TcpWindowSize': 65535,
    'Tcp1323Opts': 0,
    'DefaultTTL': 128,
    'DontFragment': 0,
}


class TCPOptimizerService:
    """Служба оптимизации TCP/IP стека Windows."""
    
    SERVICE_NAME = "TCPOptimizerService"
    SERVICE_DISPLAY_NAME = "TCP/IP Optimizer Service"
    SERVICE_DESCRIPTION = "Автоматическая оптимизация параметров TCP/IP на основе активных процессов"
    
    def __init__(self):
        self.running = False
        self.current_profile = 'default'
        self.check_interval = 5  # секунды между проверками
        self.monitor_thread = None
        self.last_applied_settings = {}
        self.config_file = os.path.join(
            os.path.dirname(os.path.dirname(__file__)), 'config.json'
        )
        self.load_config()
    
    def load_config(self):
        """Загрузка конфигурации из файла."""
        if os.path.exists(self.config_file):
            try:
                with open(self.config_file, 'r', encoding='utf-8') as f:
                    config = json.load(f)
                    self.check_interval = config.get('check_interval', 5)
                    logger.info(f"Конфигурация загружена: интервал={self.check_interval}с")
            except Exception as e:
                logger.error(f"Ошибка загрузки конфигурации: {e}")
    
    def save_config(self):
        """Сохранение конфигурации в файл."""
        try:
            config = {'check_interval': self.check_interval}
            with open(self.config_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, indent=2)
            logger.info("Конфигурация сохранена")
        except Exception as e:
            logger.error(f"Ошибка сохранения конфигурации: {e}")
    
    def get_running_processes(self) -> List[str]:
        """Получение списка запущенных процессов."""
        try:
            return [p.name().lower() for p in psutil.process_iter(['name'])]
        except Exception as e:
            logger.error(f"Ошибка получения списка процессов: {e}")
            return []
    
    def detect_active_profile(self) -> str:
        """Определение активного профиля на основе запущенных процессов."""
        running = self.get_running_processes()
        
        # Приоритет профилей (от высшего к низшему)
        priority_order = ['gaming', 'conference', 'streaming', 'download', 'torrent']
        
        for profile_name in priority_order:
            profile = PROCESS_PROFILES.get(profile_name, {})
            for proc in profile.get('processes', []):
                if proc.lower() in running:
                    logger.info(f"Обнаружен процесс {proc}, активирован профиль: {profile_name}")
                    return profile_name
        
        return 'default'
    
    def get_network_interfaces(self) -> List[str]:
        """Получение GUID сетевых интерфейсов."""
        interfaces = []
        try:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, TCP_PARAMS_KEY) as key:
                i = 0
                while True:
                    try:
                        subkey_name = winreg.EnumKey(key, i)
                        # Пропускаем специальные ключи
                        if subkey_name not in ['Tcpip', 'Parameters']:
                            interfaces.append(subkey_name)
                        i += 1
                    except OSError:
                        break
        except Exception as e:
            logger.error(f"Ошибка получения сетевых интерфейсов: {e}")
        
        # Если не найдены интерфейсы, используем глобальные настройки
        if not interfaces:
            logger.warning("Сетевые интерфейсы не найдены, используем глобальные настройки")
        
        return interfaces
    
    def apply_registry_setting(self, key_path: str, value_name: str, value: int) -> bool:
        """Применение настройки реестра."""
        try:
            # Пробуем открыть ключ интерфейса
            try:
                key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, key_path, 0, 
                                    winreg.KEY_SET_VALUE)
            except FileNotFoundError:
                # Если ключ интерфейса не найден, используем глобальный
                key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, GLOBAL_TCP_KEY, 0,
                                    winreg.KEY_SET_VALUE)
            
            winreg.SetValueEx(key, value_name, 0, winreg.REG_DWORD, value)
            winreg.CloseKey(key)
            return True
        except Exception as e:
            logger.error(f"Ошибка применения {value_name}={value}: {e}")
            return False
    
    def apply_profile_settings(self, profile_name: str) -> bool:
        """Применение настроек профиля."""
        profile = PROCESS_PROFILES.get(profile_name)
        if not profile:
            logger.error(f"Профиль {profile_name} не найден")
            return False
        
        settings = profile.get('settings', {})
        if not settings:
            logger.info(f"Профиль {profile_name} не содержит настроек (стандартные)")
            return True
        
        interfaces = self.get_network_interfaces()
        applied = False
        
        # Применяем к глобальным настройкам
        for name, value in settings.items():
            if self.apply_registry_setting(GLOBAL_TCP_KEY, name, value):
                logger.info(f"Применено: {name}={value} (глобально)")
                applied = True
        
        # Применяем к каждому интерфейсу
        for iface in interfaces:
            iface_path = f"{TCP_PARAMS_KEY}\\{iface}"
            for name, value in settings.items():
                if self.apply_registry_setting(iface_path, name, value):
                    logger.debug(f"Применено: {name}={value} (интерфейс {iface})")
                    applied = True
        
        if applied:
            self.last_applied_settings = settings.copy()
            logger.info(f"Настройки профиля '{profile.get('name', profile_name)}' применены")
        
        return applied
    
    def reset_to_defaults(self) -> bool:
        """Сброс настроек к значениям по умолчанию."""
        logger.info("Сброс настроек TCP/IP к значениям по умолчанию")
        
        interfaces = self.get_network_interfaces()
        
        for name, value in DEFAULT_REGISTRY_VALUES.items():
            self.apply_registry_setting(GLOBAL_TCP_KEY, name, value)
            for iface in interfaces:
                iface_path = f"{TCP_PARAMS_KEY}\\{iface}"
                self.apply_registry_setting(iface_path, name, value)
        
        self.last_applied_settings = {}
        self.current_profile = 'default'
        logger.info("Настройки сброшены к значениям по умолчанию")
        return True
    
    def get_current_settings(self) -> Dict:
        """Получение текущих настроек реестра."""
        settings = {}
        try:
            key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, GLOBAL_TCP_KEY, 0,
                                winreg.KEY_READ)
            for name in DEFAULT_REGISTRY_VALUES.keys():
                try:
                    value, _ = winreg.QueryValueEx(key, name)
                    settings[name] = value
                except FileNotFoundError:
                    settings[name] = None
            winreg.CloseKey(key)
        except Exception as e:
            logger.error(f"Ошибка чтения настроек: {e}")
        
        return settings
    
    def monitor_loop(self):
        """Основной цикл мониторинга процессов."""
        logger.info("Запуск цикла мониторинга процессов")
        
        while self.running:
            try:
                active_profile = self.detect_active_profile()
                
                if active_profile != self.current_profile:
                    logger.info(f"Смена профиля: {self.current_profile} -> {active_profile}")
                    self.current_profile = active_profile
                    self.apply_profile_settings(active_profile)
                
                time.sleep(self.check_interval)
                
            except Exception as e:
                logger.error(f"Ошибка в цикле мониторинга: {e}")
                time.sleep(self.check_interval)
        
        logger.info("Цикл мониторинга остановлен")
    
    def start(self):
        """Запуск службы."""
        if not is_admin():
            logger.error("Требуется запуск от имени администратора!")
            print("ERROR: Требуется запуск от имени администратора!")
            return False
        
        self.running = True
        self.monitor_thread = threading.Thread(target=self.monitor_loop, daemon=True)
        self.monitor_thread.start()
        logger.info("Служба TCP/IP Optimizer запущена")
        return True
    
    def stop(self):
        """Остановка службы."""
        self.running = False
        if self.monitor_thread:
            self.monitor_thread.join(timeout=10)
        logger.info("Служба TCP/IP Optimizer остановлена")
    
    def get_status(self) -> Dict:
        """Получение статуса службы."""
        return {
            'running': self.running,
            'current_profile': self.current_profile,
            'profile_name': PROCESS_PROFILES.get(self.current_profile, {}).get('name', 'Unknown'),
            'check_interval': self.check_interval,
            'last_settings': self.last_applied_settings,
        }


# Интерфейс командной строки
def print_status(service: TCPOptimizerService):
    """Вывод статуса службы."""
    status = service.get_status()
    print("\n" + "="*50)
    print("СТАТУС СЛУЖБЫ TCP/IP OPTIMIZER")
    print("="*50)
    print(f"Работает: {'ДА' if status['running'] else 'НЕТ'}")
    print(f"Текущий профиль: {status['profile_name']} ({status['current_profile']})")
    print(f"Интервал проверки: {status['check_interval']} сек")
    
    if status['last_settings']:
        print("\nПрименённые настройки:")
        for name, value in status['last_settings'].items():
            print(f"  {name}: {value}")
    print("="*50 + "\n")


def run_diagnostics(service: TCPOptimizerService):
    """Запуск диагностики."""
    print("\n" + "="*50)
    print("ДИАГНОСТИКА TCP/IP OPTIMIZER")
    print("="*50)
    
    # Проверка прав администратора
    admin = is_admin()
    print(f"[{'OK' if admin else 'FAIL'}] Права администратора: {'Есть' if admin else 'Нет'}")
    
    # Проверка текущих настроек
    settings = service.get_current_settings()
    print(f"[OK] Чтение реестра: успешно")
    print(f"  Найдено параметров: {len([v for v in settings.values() if v is not None])}")
    
    # Проверка сетевых интерфейсов
    interfaces = service.get_network_interfaces()
    print(f"[{'OK' if interfaces else 'WARN'}] Сетевые интерфейсы: {len(interfaces)} найдено")
    
    # Проверка активных процессов
    processes = service.get_running_processes()
    active_profile = service.detect_active_profile()
    print(f"[OK] Активных процессов: {len(processes)}")
    print(f"[OK] Активный профиль: {PROCESS_PROFILES.get(active_profile, {}).get('name', 'Unknown')}")
    
    # Проверка логов
    log_file = os.path.join(LOG_DIR, 'tcp_optimizer.log')
    log_exists = os.path.exists(log_file)
    print(f"[{'OK' if log_exists else 'INFO'}] Файл логов: {'Существует' if log_exists else 'Не создан'}")
    
    print("="*50 + "\n")


def show_help():
    """Показ справки."""
    print("""
TCP/IP Optimizer Service - Управление

Использование:
    tcp_optimizer_service.py <команда>

Команды:
    start       - Запуск службы (мониторинг и авто-оптимизация)
    stop        - Остановка службы
    status      - Показать статус службы
    profile     - Показать текущий профиль и применить настройки вручную
    reset       - Сброс настроек к значениям по умолчанию
    diagnostics - Запуск диагностики
    help        - Показать эту справку

Профили оптимизации:
    gaming      - Игры (низкая задержка)
    torrent     - Торренты (максимальная пропускная способность)
    streaming   - Стриминг видео (сбалансированный)
    conference  - Видеоконференции (реальное время)
    download    - Загрузки файлов
    default     - Стандартные настройки Windows

Примеры:
    tcp_optimizer_service.py start
    tcp_optimizer_service.py diagnostics
    tcp_optimizer_service.py reset
""")


def main():
    """Точка входа."""
    service = TCPOptimizerService()
    
    if len(sys.argv) < 2:
        show_help()
        return
    
    command = sys.argv[1].lower()
    
    if command == 'start':
        print("Запуск службы TCP/IP Optimizer...")
        if service.start():
            print("Служба запущена. Нажмите Ctrl+C для остановки.")
            try:
                while service.running:
                    time.sleep(1)
            except KeyboardInterrupt:
                print("\nОстановка службы...")
                service.stop()
                print("Служба остановлена.")
    
    elif command == 'stop':
        print("Остановка службы...")
        service.stop()
        print("Служба остановлена.")
    
    elif command == 'status':
        print_status(service)
    
    elif command == 'profile':
        profile = service.detect_active_profile()
        print(f"\nОбнаруженный профиль: {PROCESS_PROFILES.get(profile, {}).get('name', 'Unknown')}")
        print("Применение настроек...")
        if service.apply_profile_settings(profile):
            print("Настройки применены успешно.")
        else:
            print("Ошибка применения настроек.")
    
    elif command == 'reset':
        print("Сброс настроек к значениям по умолчанию...")
        if service.reset_to_defaults():
            print("Настройки сброшены. Требуется перезагрузка для полного применения.")
        else:
            print("Ошибка сброса настроек.")
    
    elif command == 'diagnostics':
        run_diagnostics(service)
    
    elif command == 'help':
        show_help()
    
    else:
        print(f"Неизвестная команда: {command}")
        show_help()


if __name__ == '__main__':
    main()
