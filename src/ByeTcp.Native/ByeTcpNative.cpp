/**
 * Bye-TCP Internet - Native Module Implementation
 * 
 * Реализация низкоуровневых функций для работы с WFP и TCP/IP
 */

#include "ByeTcpNative.h"
#include <ws2tcpip.h>
#include <tlhelp32.h>
#include <sstream>
#include <iomanip>
#include <cstdio>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "fwpuclnt.lib")

// Глобальные переменные для WFP сессии
static HANDLE g_wfpEngineHandle = INVALID_HANDLE_VALUE;
static UINT64 g_wfpFilterId = 0;
static bool g_monitoring = false;
static void (*g_eventCallback)(const char*, const char*) = nullptr;

// Версия DLL
static const char* DLL_VERSION = "0.1.0";

//=============================================================================
// WFP Functions
//=============================================================================

extern "C" BYETCP_API int WfpInitialize()
{
    if (g_wfpEngineHandle != INVALID_HANDLE_VALUE)
    {
        return ERROR_ALREADY_INITIALIZED;
    }

    FWPM_SESSION0 session = {};
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    session.sessionName = const_cast<wchar_t*>(L"ByeTcp Session");
    session.description = const_cast<wchar_t*>(L"Bye-TCP Internet Optimizer WFP Session");

    DWORD result = FwpmEngineOpen0(
        nullptr,
        RPC_C_AUTHN_DEFAULT,
        nullptr,
        &session,
        &g_wfpEngineHandle
    );

    if (result != ERROR_SUCCESS)
    {
        g_wfpEngineHandle = INVALID_HANDLE_VALUE;
        return result;
    }

    return ERROR_SUCCESS;
}

extern "C" BYETCP_API void WfpCleanup()
{
    if (g_wfpEngineHandle != INVALID_HANDLE_VALUE)
    {
        FwpmEngineClose0(g_wfpEngineHandle);
        g_wfpEngineHandle = INVALID_HANDLE_VALUE;
    }
    g_wfpFilterId = 0;
    g_monitoring = false;
    g_eventCallback = nullptr;
}

extern "C" BYETCP_API int WfpInstallCallout(const GUID* engineGuid)
{
    if (g_wfpEngineHandle == INVALID_HANDLE_VALUE)
    {
        return ERROR_NOT_READY;
    }

    // Создаем подпроцесс для ALE connect layer
    FWPM_FILTER0 filter = {};
    filter.filterKey = *engineGuid;
    filter.displayData.name = const_cast<wchar_t*>(L"ByeTcp ALE Connect Filter");
    filter.displayData.description = const_cast<wchar_t*>(L"Filters TCP connections for optimization");
    filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
    filter.action.type = FWP_ACTION_PERMIT;
    filter.numFilterConditions = 0;

    DWORD result = FwpmFilterAdd0(
        g_wfpEngineHandle,
        &filter,
        nullptr,
        &g_wfpFilterId
    );

    return result;
}

extern "C" BYETCP_API int WfpRemoveCallout()
{
    if (g_wfpEngineHandle == INVALID_HANDLE_VALUE)
    {
        return ERROR_NOT_READY;
    }

    if (g_wfpFilterId != 0)
    {
        return FwpmFilterDeleteById0(g_wfpEngineHandle, g_wfpFilterId);
    }

    return ERROR_SUCCESS;
}

extern "C" BYETCP_API int WfpSetTcpAckFrequency(UINT8 ackFrequency)
{
    // Примечание: Прямая установка TcpAckFrequency через WFP невозможна
    // Этот параметр устанавливается только через реестр
    // Данная функция - заглушка для будущей реализации
    
    if (ackFrequency < 1 || ackFrequency > 8)
    {
        return ERROR_INVALID_PARAMETER;
    }

    // TODO: Реализация через kernel-mode драйвер
    return ERROR_NOT_SUPPORTED;
}

extern "C" BYETCP_API int WfpStartMonitoring(void (*callback)(const char* eventType, const char* data))
{
    if (g_wfpEngineHandle == INVALID_HANDLE_VALUE)
    {
        return ERROR_NOT_READY;
    }

    g_eventCallback = callback;
    g_monitoring = true;

    // Подписка на события через Event Subscription
    // Примечание: Полная реализация требует отдельного потока
    // для обработки WFP events

    return ERROR_SUCCESS;
}

extern "C" BYETCP_API void WfpStopMonitoring()
{
    g_monitoring = false;
    g_eventCallback = nullptr;
}

//=============================================================================
// TCP Socket Functions
//=============================================================================

extern "C" BYETCP_API int TcpSetSocketOptions(SOCKET socketHandle, BOOL tcpNoDelay, BOOL keepAlive)
{
    int result = 0;

    if (tcpNoDelay)
    {
        int flag = 1;
        result = setsockopt(socketHandle, IPPROTO_TCP, TCP_NODELAY, 
                           reinterpret_cast<const char*>(&flag), sizeof(flag));
        if (result == SOCKET_ERROR)
        {
            return WSAGetLastError();
        }
    }

    if (keepAlive)
    {
        int flag = 1;
        result = setsockopt(socketHandle, SOL_SOCKET, SO_KEEPALIVE,
                           reinterpret_cast<const char*>(&flag), sizeof(flag));
        if (result == SOCKET_ERROR)
        {
            return WSAGetLastError();
        }
    }

    return ERROR_SUCCESS;
}

extern "C" BYETCP_API int TcpGetConnectionInfo(USHORT localPort, USHORT remotePort, 
                                                void* infoBuffer, DWORD bufferSize)
{
    if (infoBuffer == nullptr || bufferSize == 0)
    {
        return ERROR_INVALID_PARAMETER;
    }

    // Получаем таблицу TCP соединений
    DWORD tableSize = 0;
    DWORD result = GetExtendedTcpTable(
        nullptr,
        &tableSize,
        FALSE,
        AF_INET,
        TCP_TABLE_OWNER_PID_ALL,
        0
    );

    if (result != ERROR_INSUFFICIENT_BUFFER)
    {
        return result;
    }

    std::vector<BYTE> tableBuffer(tableSize);
    result = GetExtendedTcpTable(
        tableBuffer.data(),
        &tableSize,
        FALSE,
        AF_INET,
        TCP_TABLE_OWNER_PID_ALL,
        0
    );

    if (result != ERROR_SUCCESS)
    {
        return result;
    }

    auto tcpTable = reinterpret_cast<MIB_TCPTABLE_OWNER_PID*>(tableBuffer.data());
    
    // Ищем нужное соединение
    for (DWORD i = 0; i < tcpTable->dwNumEntries; i++)
    {
        auto row = &tcpTable->table[i];
        USHORT local = ntohs(row->dwLocalPort);
        USHORT remote = ntohs(row->dwRemotePort);

        if (local == localPort && remote == remotePort)
        {
            // Копируем информацию в буфер
            if (bufferSize >= sizeof(MIB_TCPROW_OWNER_PID))
            {
                memcpy(infoBuffer, row, sizeof(MIB_TCPROW_OWNER_PID));
                return ERROR_SUCCESS;
            }
            return ERROR_INSUFFICIENT_BUFFER;
        }
    }

    return ERROR_NOT_FOUND;
}

//=============================================================================
// Process Functions
//=============================================================================

extern "C" BYETCP_API int TcpGetActiveConnections(void* buffer, DWORD* bufferSize)
{
    if (bufferSize == nullptr)
    {
        return ERROR_INVALID_PARAMETER;
    }

    DWORD tableSize = 0;
    DWORD result = GetExtendedTcpTable(
        nullptr,
        &tableSize,
        FALSE,
        AF_INET,
        TCP_TABLE_OWNER_PID_ALL,
        0
    );

    if (result != ERROR_INSUFFICIENT_BUFFER)
    {
        return result;
    }

    if (buffer == nullptr || *bufferSize < tableSize)
    {
        *bufferSize = tableSize;
        return ERROR_INSUFFICIENT_BUFFER;
    }

    return GetExtendedTcpTable(
        buffer,
        bufferSize,
        FALSE,
        AF_INET,
        TCP_TABLE_OWNER_PID_ALL,
        0
    );
}

extern "C" BYETCP_API int GetProcessNameByPid(DWORD pid, wchar_t* processNameBuffer, DWORD bufferSize)
{
    if (processNameBuffer == nullptr || bufferSize == 0)
    {
        return ERROR_INVALID_PARAMETER;
    }

    HANDLE hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnapshot == INVALID_HANDLE_VALUE)
    {
        return GetLastError();
    }

    PROCESSENTRY32W pe32 = {};
    pe32.dwSize = sizeof(PROCESSENTRY32W);

    int result = ERROR_NOT_FOUND;

    if (Process32FirstW(hSnapshot, &pe32))
    {
        do
        {
            if (pe32.th32ProcessID == pid)
            {
                wcsncpy_s(processNameBuffer, bufferSize, pe32.szExeFile, _TRUNCATE);
                result = ERROR_SUCCESS;
                break;
            }
        } while (Process32NextW(hSnapshot, &pe32));
    }

    CloseHandle(hSnapshot);
    return result;
}

extern "C" BYETCP_API int SetTrafficPriorityForProcess(DWORD pid, UINT8 priority)
{
    // Установка приоритета трафика через QoS
    // Требует Windows QoS API или WFP

    HANDLE hProcess = OpenProcess(PROCESS_SET_INFORMATION, FALSE, pid);
    if (hProcess == nullptr)
    {
        return GetLastError();
    }

    // Устанавливаем приоритет процесса
    DWORD priorityClass;
    switch (priority)
    {
        case 0:
        case 1:
            priorityClass = REALTIME_PRIORITY_CLASS;
            break;
        case 2:
        case 3:
            priorityClass = HIGH_PRIORITY_CLASS;
            break;
        case 4:
        case 5:
            priorityClass = ABOVE_NORMAL_PRIORITY_CLASS;
            break;
        default:
            priorityClass = NORMAL_PRIORITY_CLASS;
            break;
    }

    BOOL result = SetPriorityClass(hProcess, priorityClass);
    CloseHandle(hProcess);

    return result ? ERROR_SUCCESS : GetLastError();
}

extern "C" BYETCP_API int BlockApplicationTraffic(const wchar_t* applicationPath, BOOL block)
{
    if (applicationPath == nullptr)
    {
        return ERROR_INVALID_PARAMETER;
    }

    if (g_wfpEngineHandle == INVALID_HANDLE_VALUE)
    {
        int result = WfpInitialize();
        if (result != ERROR_SUCCESS)
        {
            return result;
        }
    }

    // Создаем фильтр для приложения
    FWPM_FILTER0 filter = {};
    filter.displayData.name = const_cast<wchar_t*>(block ? L"ByeTcp Block Filter" : L"ByeTcp Allow Filter");
    filter.displayData.description = const_cast<wchar_t*>(L"Application traffic control");
    filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
    filter.action.type = block ? FWP_ACTION_BLOCK : FWP_ACTION_PERMIT;
    filter.flags = FWPM_FILTER_FLAG_NONE;

    // Условие для пути приложения
    FWPM_FILTER_CONDITION0 condition = {};
    condition.fieldKey = FWPM_CONDITION_ALE_APP_ID;
    condition.matchType = FWP_MATCH_EQUAL;
    condition.conditionValue.type = FWP_BYTE_BLOB_TYPE;
    
    // Конвертируем путь в FWP_BYTE_BLOB
    FWP_BYTE_BLOB* appIdBlob = nullptr;
    HRESULT hr = FwpmGetAppIdFromFileName0(applicationPath, &appIdBlob);
    if (FAILED(hr))
    {
        return HRESULT_CODE(hr);
    }

    condition.conditionValue.byteBlob = *appIdBlob;
    filter.numFilterConditions = 1;
    filter.filterCondition = &condition;

    UINT64 filterId = 0;
    DWORD result = FwpmFilterAdd0(g_wfpEngineHandle, &filter, nullptr, &filterId);

    FwpmFreeMemory0(reinterpret_cast<void**>(&appIdBlob));

    return result;
}

//=============================================================================
// Utility Functions
//=============================================================================

extern "C" BYETCP_API const char* GetVersion()
{
    return DLL_VERSION;
}

//=============================================================================
// WfpFilterManager Implementation
//=============================================================================

WfpFilterManager::WfpFilterManager()
    : m_engineHandle(INVALID_HANDLE_VALUE)
    , m_initialized(false)
{
}

WfpFilterManager::~WfpFilterManager()
{
    Cleanup();
}

bool WfpFilterManager::Initialize()
{
    if (m_initialized)
    {
        return true;
    }

    FWPM_SESSION0 session = {};
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    session.sessionName = const_cast<wchar_t*>(L"ByeTcp Filter Manager");

    DWORD result = FwpmEngineOpen0(
        nullptr,
        RPC_C_AUTHN_DEFAULT,
        nullptr,
        &session,
        &m_engineHandle
    );

    m_initialized = (result == ERROR_SUCCESS);
    return m_initialized;
}

void WfpFilterManager::Cleanup()
{
    if (m_engineHandle != INVALID_HANDLE_VALUE)
    {
        FwpmEngineClose0(m_engineHandle);
        m_engineHandle = INVALID_HANDLE_VALUE;
    }
    m_initialized = false;
}

bool WfpFilterManager::InstallFilter(const GUID& filterKey, const wchar_t* name, const wchar_t* description)
{
    if (!m_initialized)
    {
        return false;
    }

    FWPM_FILTER0 filter = {};
    filter.filterKey = filterKey;
    filter.displayData.name = const_cast<wchar_t*>(name);
    filter.displayData.description = const_cast<wchar_t*>(description);
    filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
    filter.action.type = FWP_ACTION_PERMIT;

    UINT64 filterId = 0;
    DWORD result = FwpmFilterAdd0(m_engineHandle, &filter, nullptr, &filterId);

    return (result == ERROR_SUCCESS);
}

bool WfpFilterManager::RemoveFilter(const GUID& filterKey)
{
    if (!m_initialized)
    {
        return false;
    }

    DWORD result = FwpmFilterDeleteByKey0(m_engineHandle, &filterKey);
    return (result == ERROR_SUCCESS);
}

bool WfpFilterManager::EnableFilter(const GUID& filterKey, bool enable)
{
    if (!m_initialized)
    {
        return false;
    }

    DWORD result = FwpmFilterSetInfoByKey0(
        m_engineHandle,
        &filterKey,
        nullptr // Requires full filter info update
    );

    return (result == ERROR_SUCCESS);
}

//=============================================================================
// TcpOptimizerNative Implementation
//=============================================================================

TcpOptimizerNative& TcpOptimizerNative::Instance()
{
    static TcpOptimizerNative instance;
    return instance;
}

bool TcpOptimizerNative::SetTcpNoDelay(SOCKET socket, bool enable)
{
    int flag = enable ? 1 : 0;
    int result = setsockopt(socket, IPPROTO_TCP, TCP_NODELAY,
                           reinterpret_cast<const char*>(&flag), sizeof(flag));
    return (result == 0);
}

bool TcpOptimizerNative::SetTcpKeepAlive(SOCKET socket, bool enable, 
                                          DWORD time, DWORD interval, DWORD count)
{
    if (!enable)
    {
        int flag = 0;
        int result = setsockopt(socket, SOL_SOCKET, SO_KEEPALIVE,
                               reinterpret_cast<const char*>(&flag), sizeof(flag));
        return (result == 0);
    }

    // Настройка TCP KeepAlive с параметрами
    tcp_keepalive ka = {};
    ka.onoff = 1;
    ka.keepalivetime = time;
    ka.keepaliveinterval = interval;

    DWORD bytesReturned = 0;
    int result = WSAIoctl(socket, SIO_KEEPALIVE_VALS, &ka, sizeof(ka),
                         nullptr, 0, &bytesReturned, nullptr, nullptr);

    return (result == 0);
}

bool TcpOptimizerNative::SetReceiveBuffer(SOCKET socket, int size)
{
    int result = setsockopt(socket, SOL_SOCKET, SO_RCVBUF,
                           reinterpret_cast<const char*>(&size), sizeof(size));
    return (result == 0);
}

bool TcpOptimizerNative::SetSendBuffer(SOCKET socket, int size)
{
    int result = setsockopt(socket, SOL_SOCKET, SO_SNDBUF,
                           reinterpret_cast<const char*>(&size), sizeof(size));
    return (result == 0);
}

bool TcpOptimizerNative::DisableNagleAlgorithm(SOCKET socket)
{
    return SetTcpNoDelay(socket, true);
}
