using Monity.Domain.Entities;

namespace Monity.App.Services;

public record GoalProgress(Goal Goal, long CurrentSeconds, double ProgressPercentage);

public interface IGoalService
{
    Task<IReadOnlyList<GoalProgress>> GetGoalProgressesAsync(DateTime? baseDate = null, CancellationToken ct = default);
    Task<int> AddGoalFromTextAsync(string text, CancellationToken ct = default);
    Task<int> AddGoalAsync(string title, GoalTargetType targetType, string targetValue, GoalLimitType limitType, int limitSeconds, GoalFrequency frequency, CancellationToken ct = default);
    Task DeleteGoalAsync(int goalId, CancellationToken ct = default);
}
