/**
 * Bye-TCP Internet - DLL Entry Point
 */

#include "ByeTcpNative.h"

BOOL APIENTRY DllMain(HMODULE hModule,
                      DWORD  ul_reason_for_call,
                      LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        // Инициализация при загрузке DLL в процесс
        DisableThreadLibraryCalls(hModule);
        break;
        
    case DLL_THREAD_ATTACH:
        // Новый поток в процессе
        break;
        
    case DLL_THREAD_DETACH:
        // Поток завершен
        break;
        
    case DLL_PROCESS_DETACH:
        // Выгрузка DLL из процесса
        // Очищаем WFP сессию если активна
        WfpCleanup();
        break;
    }
    
    return TRUE;
}
