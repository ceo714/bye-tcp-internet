using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Decision;

/// <summary>
/// Rule Engine на основе чистых функций
/// 
/// Принципы:
/// - Детерминированность (одинаковый input → одинаковый output)
/// - Отсутствие побочных эффектов (no I/O, no state mutation)
/// - Идемпотентность (повторный вызов = тот же результат)
/// </summary>
public sealed class PureRuleEngine : IRuleEngine
{
    private readonly ILogger<PureRuleEngine> _logger;

    public PureRuleEngine(ILogger<PureRuleEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Оценка правил и выбор профиля
    /// Чистая функция: входные данные → результат
    /// </summary>
    public RuleEvaluationResult Evaluate(
        EvaluationContext context,
        IReadOnlyList<Rule> rules,
        IReadOnlyDictionary<string, TcpProfile> profiles)
    {
        // Сортировка по приоритету (descending)
        var sortedRules = rules.OrderByDescending(r => r.Priority).ToList();
        
        _logger.LogDebug("🔍 Оценка {Count} правил. Активные процессы: {Processes}", 
            sortedRules.Count,
            string.Join(", ", context.RunningProcesses.Select(p => p.Name)));
        
        // Поиск первого подходящего правила
        foreach (var rule in sortedRules)
        {
            if (Matches(rule, context))
            {
                // Проверка доступности профиля
                if (!profiles.ContainsKey(rule.ProfileId))
                {
                    _logger.LogWarning("Правило {RuleId} ссылается на несуществующий профиль {ProfileId}", 
                        rule.Id, rule.ProfileId);
                    continue;
                }
                
                // Проверка, отличается ли профиль от текущего
                var shouldSwitch = context.CurrentProfileId != rule.ProfileId;
                
                var result = new RuleEvaluationResult
                {
                    SelectedProfileId = rule.ProfileId,
                    MatchingRuleId = rule.Id,
                    Priority = rule.Priority,
                    Reason = rule.Description ?? rule.Id,
                    ShouldSwitch = shouldSwitch,
                    Confidence = CalculateConfidence(rule, context)
                };
                
                if (shouldSwitch)
                {
                    _logger.LogInformation(
                        "🔄 Найдено правило: {RuleId} → профиль {ProfileId} (приоритет: {Priority})",
                        rule.Id, rule.ProfileId, rule.Priority);
                }
                
                return result;
            }
        }
        
        // Default profile если ничего не найдено
        var defaultResult = new RuleEvaluationResult
        {
            SelectedProfileId = "default",
            MatchingRuleId = null,
            Priority = 0,
            Reason = "No matching rules",
            ShouldSwitch = context.CurrentProfileId != "default",
            Confidence = EvaluationConfidence.Normal
        };
        
        if (defaultResult.ShouldSwitch)
        {
            _logger.LogDebug("Возврат к профилю по умолчанию (нет подходящих правил)");
        }
        
        return defaultResult;
    }

    /// <summary>
    /// Проверка соответствия правилу
    /// Чистая функция без побочных эффектов
    /// </summary>
    private static bool Matches(Rule rule, EvaluationContext context)
    {
        if (rule.Conditions == null)
            return true; // Правило без условий всегда подходит
        
        var conditions = rule.Conditions;
        
        // Проверка условия процесса
        if (conditions.Process != null)
        {
            if (!MatchesProcess(conditions.Process, context.RunningProcesses))
                return false;
        }
        
        // Проверка условия сети
        if (conditions.Network != null)
        {
            if (!MatchesNetwork(conditions.Network, context.NetworkMetrics))
                return false;
        }
        
        // Проверка условия времени
        if (conditions.Time != null)
        {
            if (!MatchesTime(conditions.Time, context.EvaluationTime))
                return false;
        }
        
        return true;
    }

    private static bool MatchesProcess(
        ProcessCondition condition,
        IReadOnlySet<ProcessInfo> runningProcesses)
    {
        var processName = condition.Name.ToLowerInvariant();
        
        // Ищем запущенный процесс с matching именем
        var processExists = runningProcesses.Any(p => 
            p.Name.ToLowerInvariant() == processName &&
            p.State == ProcessState.Running);
        
        return condition.State switch
        {
            ProcessState.Running => processExists,
            ProcessState.Exited => !processExists,
            _ => false
        };
    }

    private static bool MatchesNetwork(
        NetworkCondition condition,
        NetworkMetrics metrics)
    {
        // Проверка RTT
        if (condition.MinRttMs.HasValue && metrics.RttMs < condition.MinRttMs)
            return false;
        
        if (condition.MaxRttMs.HasValue && metrics.RttMs > condition.MaxRttMs)
            return false;
        
        // Проверка Packet Loss
        if (condition.MinPacketLossPercent.HasValue && 
            metrics.PacketLossPercent < condition.MinPacketLossPercent)
            return false;
        
        if (condition.MaxPacketLossPercent.HasValue && 
            metrics.PacketLossPercent > condition.MaxPacketLossPercent)
            return false;
        
        // Проверка Quality
        if (condition.MinQuality.HasValue && metrics.Quality < condition.MinQuality)
            return false;
        
        if (condition.MaxQuality.HasValue && metrics.Quality > condition.MaxQuality)
            return false;
        
        return true;
    }

    private static bool MatchesTime(
        TimeCondition condition,
        DateTime evaluationTime)
    {
        // Проверка дня недели
        if (condition.DaysOfWeek != null && 
            !condition.DaysOfWeek.Contains(evaluationTime.DayOfWeek))
            return false;
        
        // Проверка времени суток
        var timeOfDay = evaluationTime.TimeOfDay;
        
        if (condition.StartTime.HasValue)
        {
            var startTime = condition.StartTime.Value;
            var endTime = condition.EndTime ?? TimeSpan.FromDays(1);
            
            // Обработка overnight диапазонов (например, 22:00 - 06:00)
            if (startTime > endTime)
            {
                // Overnight: время должно быть >= start ИЛИ <= end
                if (timeOfDay < startTime && timeOfDay > endTime)
                    return false;
            }
            else
            {
                // Normal range: время должно быть между start и end
                if (timeOfDay < startTime || timeOfDay > endTime)
                    return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Вычисление уверенности в выборе правила
    /// </summary>
    private static EvaluationConfidence CalculateConfidence(Rule rule, EvaluationContext context)
    {
        // Высокая уверенность: высокий приоритет + multiple conditions match
        if (rule.Priority >= 100)
            return EvaluationConfidence.High;
        
        // Средняя уверенность: нормальный приоритет
        if (rule.Priority >= 50)
            return EvaluationConfidence.Normal;
        
        // Низкая уверенность: низкий приоритет
        return EvaluationConfidence.Low;
    }

    /// <summary>
    /// Валидация правил (статическая проверка)
    /// </summary>
    public ValidationResult ValidateRules(IReadOnlyList<Rule> rules)
    {
        var errors = new List<string>();
        
        // Проверка на дубликаты ID
        var duplicateIds = rules
            .GroupBy(r => r.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        
        foreach (var id in duplicateIds)
        {
            errors.Add($"Дубликат ID правила: {id}");
        }
        
        // Проверка на отрицательный приоритет
        foreach (var rule in rules.Where(r => r.Priority < 0))
        {
            errors.Add($"Отрицательный приоритет у правила {rule.Id}: {rule.Priority}");
        }
        
        // Проверка ссылок на профили
        var profileIds = rules.Select(r => r.ProfileId).ToHashSet();
        // Здесь мы не можем проверить существование профилей, это делается при загрузке
        
        // Проверка на конфликтующие правила (одинаковый приоритет + overlapping conditions)
        var rulesByPriority = rules.GroupBy(r => r.Priority);
        foreach (var group in rulesByPriority.Where(g => g.Count() > 1))
        {
            _logger.LogWarning(
                "Обнаружено {Count} правил с одинаковым приоритетом {Priority}", 
                group.Count(), group.Key);
        }
        
        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}
