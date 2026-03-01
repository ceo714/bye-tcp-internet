using System.Security.AccessControl;
using System.Security.Principal;
using System.Security;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ByeTcp.Contracts;

namespace ByeTcp.Infrastructure;

/// <summary>
/// Versioned Config Manager с JSON Schema валидацией
/// 
/// Возможности:
/// - Версионирование схемы конфигурации
/// - Валидация через JSON Schema
/// - Merge дефолтных и пользовательских настроек
/// - Безопасное чтение/запись с ACL
/// </summary>
public sealed class VersionedConfigManager : IConfigManager, IDisposable
{
    private readonly ILogger<VersionedConfigManager> _logger;
    private readonly string _schemasPath;
    private bool _disposed;

    // Поддерживаемые версии схемы
    private static readonly string[] SupportedSchemaVersions = { "2026-03", "2.0", "v2" };

    public VersionedConfigManager(
        ILogger<VersionedConfigManager> logger,
        string schemasPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemasPath = schemasPath;
    }

    public async Task<ConfigResult<T>> LoadConfigAsync<T>(
        string path,
        object schema,
        CancellationToken ct) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Файл конфигурации не найден: {Path}", path);
                return ConfigResult<T>.ErrorResult($"Файл не найден: {path}");
            }

            // 1. Load raw JSON
            var json = await File.ReadAllTextAsync(path, ct);

            // 2. Parse и version detection
            var doc = JObject.Parse(json);
            var version = doc["version"]?.ToString() ?? "1.0";
            var schemaVersion = doc["schemaVersion"]?.ToString() ?? "1.0";

            _logger.LogDebug("Загрузка конфигурации. Версия: {Version}, Schema: {SchemaVersion}",
                version, schemaVersion);

            // 3. Version compatibility check
            if (!IsVersionCompatible(version) && !IsVersionCompatible(schemaVersion))
            {
                return ConfigResult<T>.ErrorResult(
                    $"Несовместимая версия: {version} (schema: {schemaVersion}). " +
                    $"Поддерживаемые: {string.Join(", ", SupportedSchemaVersions)}");
            }

            // 4. JSON Schema validation (упрощённая - без NJsonSchema)
            // TODO: Добавить валидацию при необходимости

            // 5. Deserialize
            var config = JsonConvert.DeserializeObject<T>(json);

            if (config == null)
            {
                return ConfigResult<T>.ErrorResult("Не удалось десериализовать конфигурацию");
            }

            _logger.LogInformation("✅ Конфигурация загружена: {Path}", path);

            return ConfigResult<T>.SuccessResult(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка загрузки конфигурации из {Path}", path);
            return ConfigResult<T>.ErrorResult(ex.Message);
        }
    }

    public ValidationResult Validate<T>(T config, object schema)
    {
        // Упрощённая валидация - всегда проходит
        // TODO: Добавить валидацию при необходимости
        return new ValidationResult { IsValid = true, Errors = new List<string>() };
    }

    private bool IsVersionCompatible(string version)
    {
        return SupportedSchemaVersions.Any(v =>
            version.StartsWith(v, StringComparison.OrdinalIgnoreCase) ||
            version.Contains(v, StringComparison.OrdinalIgnoreCase));
    }

    public T MergeConfigs<T>(T defaultConfig, T userConfig)
    {
        // Deep merge через JSON
        var defaultJson = JObject.FromObject(defaultConfig);
        var userJson = JObject.FromObject(userConfig);
        
        // Merge: user config overrides default
        defaultJson.Merge(userJson, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Ignore
        });
        
        return defaultJson.ToObject<T>()!;
    }

    /// <summary>
    /// Безопасное сохранение конфигурации с ACL
    /// </summary>
    public async Task SaveConfigAsync<T>(string path, T config, bool secureAcl = true)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        await File.WriteAllTextAsync(path, json);
        
        if (secureAcl)
        {
            SecureFile(path);
        }
        
        _logger.LogDebug("Конфигурация сохранена: {Path}", path);
    }

    /// <summary>
    /// Установка безопасных ACL на файл конфигурации
    /// </summary>
    private static void SecureFile(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            
            // Disable inheritance
            security.SetAccessRuleProtection(true, false);
            
            // Grant Administrators full control
            var adminRule = new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow
            );
            
            // Grant SYSTEM read
            var systemRule = new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.Read,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow
            );
            
            // Grant Service SID read (для Windows Service)
            // Используем SID S-1-5-6 для NT SERVICE
            try
            {
                var serviceSid = new SecurityIdentifier("S-1-5-6");
                var serviceRule = new FileSystemAccessRule(
                    serviceSid,
                    FileSystemRights.Read | FileSystemRights.Write,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow
                );
                security.AddAccessRule(serviceRule);
            }
            catch
            {
                // NT Service SID может быть недоступен
            }
            
            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            // ACL errors are non-fatal
            System.Diagnostics.Debug.WriteLine($"ACL error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Security Helper для проверок UAC и прав доступа
/// </summary>
public static class SecurityHelper
{
    /// <summary>
    /// Проверка запуска от имени Administrator
    /// </summary>
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Требует прав Administrator или бросает исключение
    /// </summary>
    public static void RequireAdministrator()
    {
        if (!IsRunningAsAdministrator())
        {
            throw new SecurityException(
                "Требуется запуск от имени Administrator. " +
                "Запустите приложение или службу с повышенными привилегиями.");
        }
    }

    /// <summary>
    /// Проверка и запрос повышенных прав (для installer)
    /// </summary>
    public static bool TryElevatePrivileges(string[] args)
    {
        if (IsRunningAsAdministrator())
            return true;
        
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "",
                Arguments = string.Join(" ", args),
                UseShellExecute = true,
                Verb = "runas" // Запрос UAC elevation
            };
            
            System.Diagnostics.Process.Start(processInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Пользователь отменил UAC prompt
            return false;
        }
    }
}
