using Microsoft.Extensions.Logging;

namespace ByeTcp.Infrastructure;

/// <summary>
/// Retry wrapper с exponential backoff для устойчивости к временным ошибкам
///
/// Возможности:
/// - Экспоненциальная задержка между попытками
/// - Логирование каждой попытки
/// - Фильтрация retryable exceptions
/// - Структурированное логирование с component, action, duration, result
/// </summary>
public static class RetryHelper
{
    /// <summary>
    /// Выполняет операцию с retry policy (3 попытки, exponential backoff)
    /// </summary>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ILogger logger,
        string component,
        string action,
        int maxRetries = 3,
        int baseDelaySeconds = 2,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                attempt++;
                var result = await operation(cancellationToken);

                var duration = DateTime.UtcNow - startTime;
                LogOperationResult(logger, component, action, duration, "Success", attempt);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Не логируем отмену как ошибку
                throw;
            }
            catch (Exception ex) when (IsRetryableException(ex))
            {
                lastException = ex;
                var delay = CalculateExponentialDelay(attempt, baseDelaySeconds);

                logger.LogWarning(
                    ex,
                    "⚠️ [{Component}] {Action} - попытка {Attempt}/{MaxRetries} не удалась: {Error}. Следующая попытка через {Delay}s",
                    component, action, attempt, maxRetries, ex.Message, delay);

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Неретраемая ошибка - логируем и пробрасываем
                var duration = DateTime.UtcNow - startTime;
                LogOperationResult(logger, component, action, duration, $"Failed: {ex.Message}", attempt);
                throw;
            }
        }

        // Все попытки исчерпаны
        var totalDuration = DateTime.UtcNow - startTime;
        LogOperationResult(logger, component, action, totalDuration, $"Failed after {maxRetries} attempts", attempt);

        throw new RetryExhaustedException(
            $"Operation '{action}' failed after {maxRetries} attempts",
            lastException);
    }

    /// <summary>
    /// Выполняет операцию с retry policy (без возвращаемого значения)
    /// </summary>
    public static async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        ILogger logger,
        string component,
        string action,
        int maxRetries = 3,
        int baseDelaySeconds = 2,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(
            async ct =>
            {
                await operation(ct);
                return true;
            },
            logger,
            component,
            action,
            maxRetries,
            baseDelaySeconds,
            cancellationToken);
    }

    /// <summary>
    /// Выполняет синхронную операцию с retry policy
    /// </summary>
    public static T ExecuteWithRetry<T>(
        Func<T> operation,
        ILogger logger,
        string component,
        string action,
        int maxRetries = 3,
        int baseDelaySeconds = 2,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                attempt++;
                var result = operation();

                var duration = DateTime.UtcNow - startTime;
                LogOperationResult(logger, component, action, duration, "Success", attempt);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRetryableException(ex))
            {
                lastException = ex;
                var delay = CalculateExponentialDelay(attempt, baseDelaySeconds);

                logger.LogWarning(
                    ex,
                    "⚠️ [{Component}] {Action} - попытка {Attempt}/{MaxRetries} не удалась: {Error}. Следующая попытка через {Delay}s",
                    component, action, attempt, maxRetries, ex.Message, delay);

                if (attempt < maxRetries)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(delay));
                }
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                LogOperationResult(logger, component, action, duration, $"Failed: {ex.Message}", attempt);
                throw;
            }
        }

        var totalDuration = DateTime.UtcNow - startTime;
        LogOperationResult(logger, component, action, totalDuration, $"Failed after {maxRetries} attempts", attempt);

        throw new RetryExhaustedException(
            $"Operation '{action}' failed after {maxRetries} attempts",
            lastException);
    }

    /// <summary>
    /// Вычисляет задержку с exponential backoff
    /// Формула: baseDelay * 2^(attempt-1)
    /// </summary>
    private static int CalculateExponentialDelay(int attempt, int baseDelaySeconds)
    {
        return baseDelaySeconds * (int)Math.Pow(2, attempt - 1);
    }

    /// <summary>
    /// Определяет, является ли ошибка ретраемой
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        return ex switch
        {
            // Временные ошибки сети
            System.Net.WebException => true,
            System.IO.IOException => true,
            System.Net.Http.HttpRequestException => true,
            System.Net.Sockets.SocketException => true,
            TimeoutException => true,
            
            // Временные ошибки Win32
            System.ComponentModel.Win32Exception win32Ex => 
                win32Ex.NativeErrorCode is 5 or 32 or 1225, // ERROR_ACCESS_DENIED, ERROR_SHARING_VIOLATION, ERROR_CONNECTION_UNAVAIL
                
            _ => false
        };
    }

    /// <summary>
    /// Логирует результат операции с полями: component, action, duration, result
    /// </summary>
    private static void LogOperationResult(
        ILogger logger,
        string component,
        string action,
        TimeSpan duration,
        string result,
        int attempts = 1)
    {
        logger.LogInformation(
            "[{Component}] {Action} completed in {Duration}ms with result: {Result} (attempts: {Attempts})",
            component,
            action,
            duration.TotalMilliseconds,
            result,
            attempts);
    }
}

/// <summary>
/// Исчерпание попыток retry
/// </summary>
public class RetryExhaustedException : Exception
{
    public RetryExhaustedException(string message) : base(message) { }
    public RetryExhaustedException(string message, Exception? innerException) 
        : base(message, innerException) { }
}

/// <summary>
/// Расширения для логирования с компонентом и действием
/// </summary>
public static class LoggerExtensions
{
    public static IDisposable BeginComponentScope(
        this ILogger logger,
        string component,
        string? action = null)
    {
        var scope = action != null
            ? new Dictionary<string, object?>
            {
                ["Component"] = component,
                ["Action"] = action
            }
            : new Dictionary<string, object?>
            {
                ["Component"] = component
            };

        return logger.BeginScope(scope);
    }

    public static void LogWithComponent(
        this ILogger logger,
        LogLevel level,
        string component,
        string action,
        string message,
        params object?[] args)
    {
        logger.Log(level, "[{Component}] {Action} " + message, 
            new object[] { component, action }.Concat(args).ToArray());
    }
}
