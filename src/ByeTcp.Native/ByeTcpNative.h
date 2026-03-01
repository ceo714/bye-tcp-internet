/**
 * Bye-TCP Internet - Native Module
 * 
 * Низкоуровневый модуль для работы с Windows Filtering Platform (WFP)
 * и прямого доступа к TCP/IP настройкам через WinAPI.
 * 
 * Требования:
 * - Windows 10/11
 * - Administrator privileges
 * - Visual C++ Redistributable
 */

#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <fwpmu.h>
#include <fwpmtypes.h>
#include <ipexport.h>
#include <iphlpapi.h>
#include <string>
#include <vector>
#include <functional>

// Экспорт символов для DLL
#ifdef BYETCP_NATIVE_EXPORTS
    #define BYETCP_API __declspec(dllexport)
#else
    #define BYETCP_API __declspec(dllimport)
#endif

// C-style интерфейс для вызова из C#
extern "C" {

    /**
     * Инициализация WFP сессии
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int WfpInitialize();

    /**
     * Завершение WFP сессии
     */
    BYETCP_API void WfpCleanup();

    /**
     * Установка WFP callout для перехвата TCP соединений
     * @param engineGuid GUID движка фильтрации
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int WfpInstallCallout(const GUID* engineGuid);

    /**
     * Удаление WFP callout
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int WfpRemoveCallout();

    /**
     * Применение TCP опций к существующему соединению
     * @param socketHandle Дескриптор сокета
     * @param tcpNoDelay Включить TCP_NODELAY
     * @param keepAlive Включить SO_KEEPALIVE
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int TcpSetSocketOptions(SOCKET socketHandle, BOOL tcpNoDelay, BOOL keepAlive);

    /**
     * Получение информации о TCP соединении
     * @param localPort Локальный порт
     * @param remotePort Удаленный порт
     * @param infoBuffer Буфер для информации
     * @param bufferSize Размер буфера
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int TcpGetConnectionInfo(USHORT localPort, USHORT remotePort, void* infoBuffer, DWORD bufferSize);

    /**
     * Принудительная установка TCP ACK frequency через WFP
     * @param ackFrequency Частота ACK (1-8)
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int WfpSetTcpAckFrequency(UINT8 ackFrequency);

    /**
     * Мониторинг сетевых событий через WFP
     * @param callback Функция обратного вызова для событий
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int WfpStartMonitoring(void (*callback)(const char* eventType, const char* data));

    /**
     * Остановка мониторинга WFP
     */
    BYETCP_API void WfpStopMonitoring();

    /**
     * Получение списка активных TCP соединений
     * @param buffer Буфер для MIB_TCPTABLE_OWNER_PID
     * @param bufferSize Размер буфера
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int TcpGetActiveConnections(void* buffer, DWORD* bufferSize);

    /**
     * Получение процесса по PID
     * @param pid Идентификатор процесса
     * @param processNameBuffer Буфер для имени процесса
     * @param bufferSize Размер буфера
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int GetProcessNameByPid(DWORD pid, wchar_t* processNameBuffer, DWORD bufferSize);

    /**
     * Установка приоритета трафика для процесса
     * @param pid Идентификатор процесса
     * @param priority Приоритет (0-7, где 0 - highest)
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int SetTrafficPriorityForProcess(DWORD pid, UINT8 priority);

    /**
     * Блокировка/разблокировка трафика для приложения
     * @param applicationPath Путь к исполняемому файлу
     * @param block true для блокировки
     * @return 0 если успешно, код ошибки иначе
     */
    BYETCP_API int BlockApplicationTraffic(const wchar_t* applicationPath, BOOL block);

    /**
     * Получение версии DLL
     * @return Строка версии
     */
    BYETCP_API const char* GetVersion();
}

/**
 * Класс для управления WFP фильтрами
 */
class WfpFilterManager
{
public:
    WfpFilterManager();
    ~WfpFilterManager();

    bool Initialize();
    void Cleanup();

    bool InstallFilter(const GUID& filterKey, const wchar_t* name, const wchar_t* description);
    bool RemoveFilter(const GUID& filterKey);
    bool EnableFilter(const GUID& filterKey, bool enable);

private:
    HANDLE m_engineHandle;
    bool m_initialized;
};

/**
 * Класс для низкоуровневой оптимизации TCP
 */
class TcpOptimizerNative
{
public:
    static TcpOptimizerNative& Instance();

    bool SetTcpNoDelay(SOCKET socket, bool enable);
    bool SetTcpKeepAlive(SOCKET socket, bool enable, DWORD time, DWORD interval, DWORD count);
    bool SetReceiveBuffer(SOCKET socket, int size);
    bool SetSendBuffer(SOCKET socket, int size);
    bool DisableNagleAlgorithm(SOCKET socket);

private:
    TcpOptimizerNative() = default;
};

/**
 * Структура для передачи событий мониторинга
 */
struct WfpEvent
{
    enum class Type
    {
        ConnectionEstablished,
        ConnectionTerminated,
        PacketDropped,
        TrafficShaped,
        ProfileApplied
    };

    Type type;
    DWORD processId;
    std::wstring processName;
    UINT16 localPort;
    UINT16 remotePort;
    std::wstring remoteAddress;
    UINT64 timestamp;
};
