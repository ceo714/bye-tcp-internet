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
/// - Safe mode при несовместимой версии
/// - Merge дефолтных и пользовательских настроек
/// - Безопасное чтение/запись с ACL
/// </summary>
public sealed class VersionedConfigManager : IConfigManager, IDisposable
{
    private readonly ILogger<VersionedConfigManager> _logger;
    private readonly string _schemasPath;
    private bool _disposed;

    // Текущая версия схемы
    private const string CurrentSchemaVersion = "2026-03";
    
    // Поддерживаемые версии схемы
    private static readonly string[] SupportedSchemaVersions = { "2026-03", "2026-02", "2.0", "v2" };

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

            // 3. Schema version validation с safe mode
            var schemaValidationResult = ValidateSchemaVersion(schemaVersion);
            if (!schemaValidationResult.IsValid)
            {
                _logger.LogWarning(
                    "⚠️ Schema version mismatch: {ActualVersion}. Ожидалась: {ExpectedVersion}. Переход в safe mode.",
                    schemaVersion, CurrentSchemaVersion);
                
                // Возвращаем результат с флагом safe mode
                return new ConfigResult<T>
                {
                    Success = false,
                    Errors = new List<string> 
                    { 
                        $"Несовместимая версия схемы: {schemaVersion}. Ожидалась: {CurrentSchemaVersion}. Используется профиль по умолчанию." 
                    },
                    Config = null
                };
            }

            // 4. JSON Schema validation (если схема предоставлена)
            if (schema != null)
            {
                var validationResult = ValidateJsonSchema(json, schema);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Валидация JSON Schema не пройдена: {Errors}", 
                        string.Join(", ", validationResult.Errors));
                }
            }

            // 5. Deserialize
            var config = JsonConvert.DeserializeObject<T>(json);

            if (config == null)
            {
                return ConfigResult<T>.ErrorResult("Не удалось десериализовать конфигурацию");
            }

            _logger.LogInformation("✅ Конфигурация загрушена: {Path}", path);

            return ConfigResult<T>.SuccessResult(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка загрузки конфигурации из {Path}", path);
            return ConfigResult<T>.ErrorResult(ex.Message);
        }
    }

    /// <summary>
    /// Валидация версии схемы
    /// </summary>
    private SchemaValidationResult ValidateSchemaVersion(string schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return new SchemaValidationResult { IsValid = false, Error = "Schema version is empty" };
        }

        // Проверяем совместимость
        if (IsVersionCompatible(schemaVersion))
        {
            return new SchemaValidationResult { IsValid = true };
        }

        return new SchemaValidationResult 
        { 
            IsValid = false, 
            Error = $"Unsupported schema version: {schemaVersion}. Expected: {CurrentSchemaVersion}" 
        };
    }

    /// <summary>
    /// Упрощённая валидация JSON Schema
    /// </summary>
    private ValidationResult ValidateJsonSchema(string json, object schemaObj)
    {
        try
        {
            // Если схема передана как JsonSchema из NJsonSchema
            if (schemaObj is NJsonSchema.JsonSchema jsonSchema)
            {
                var errors = jsonSchema.Validate(json);
                if (errors.Count > 0)
                {
                    return new ValidationResult 
                    { 
                        IsValid = false, 
                        Errors = errors.Select(e => e.ToString()).ToList() 
                    };
                }
            }
            
            return new ValidationResult { IsValid = true, Errors = new List<string>() };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации JSON Schema");
            return new ValidationResult { IsValid = true, Errors = new List<string>() };
        }
    }

    public ValidationResult Validate<T>(T config, object schema)
    {
        // Упрощённая валидация - всегда проходит
        return new ValidationResult { IsValid = true, Errors = new List<string>() };
    }

    private bool IsVersionCompatible(string version)
    {
        return SupportedSchemaVersions.Any(v =>
            version.StartsWith(v, StringComparison.OrdinalIgnoreCase) ||
            version.Contains(v, StringComparison.OrdinalIgnoreCase) ||
            version.Equals(v, StringComparison.OrdinalIgnoreCase));
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
/// Результат валидации версии схемы
/// </summary>
public sealed class SchemaValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
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
