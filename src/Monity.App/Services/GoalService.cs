using Monity.Domain.Entities;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public class GoalService : IGoalService
{
    private readonly IUsageRepository _repository;
    private readonly IGoalParsingService _parsingService;

    public GoalService(IUsageRepository repository, IGoalParsingService parsingService)
    {
        _repository = repository;
        _parsingService = parsingService;
    }

    public async Task<IReadOnlyList<GoalProgress>> GetGoalProgressesAsync(DateTime? baseDate = null, CancellationToken ct = default)
    {
        var goals = await _repository.GetGoalsAsync(ct);
        var activeGoals = goals.Where(g => g.IsActive).ToList();
        var results = new List<GoalProgress>();

        var referenceDate = baseDate ?? DateTime.Today;

        foreach (var goal in activeGoals)
        {
            DateTime startDate, endDate;
            if (goal.Frequency == GoalFrequency.Daily)
            {
                startDate = referenceDate;
                endDate = referenceDate;
            }
            else if (goal.Frequency == GoalFrequency.Weekly)
            {
                // Start of current week (Monday) based on referenceDate
                var diff = (7 + (referenceDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                startDate = referenceDate.AddDays(-1 * diff);
                endDate = referenceDate;
            }
            else // Monthly
            {
                startDate = new DateTime(referenceDate.Year, referenceDate.Month, 1);
                endDate = referenceDate;
            }

            var currentSeconds = await _repository.GetUsageSecondsForGoalAsync(goal, startDate, endDate, ct);
            var percentage = goal.LimitSeconds > 0 ? (double)currentSeconds / goal.LimitSeconds * 100 : 0;
            
            results.Add(new GoalProgress(goal, currentSeconds, percentage));
        }

        return results;
    }

    public async Task<int> AddGoalFromTextAsync(string text, CancellationToken ct = default)
    {
        var goal = _parsingService.ParseGoal(text);
        if (goal == null) return -1;

        // If title is just the text, we might want to make it cleaner, but let's stick with it.
        return await _repository.AddGoalAsync(goal, ct);
    }

    public async Task DeleteGoalAsync(int goalId, CancellationToken ct = default)
    {
        await _repository.DeleteGoalAsync(goalId, ct);
    }

    public async Task<int> AddGoalAsync(string title, GoalTargetType targetType, string targetValue, GoalLimitType limitType, int limitSeconds, GoalFrequency frequency, CancellationToken ct = default)
    {
        var goal = new Goal
        {
            Title = title,
            TargetType = targetType,
            TargetValue = targetValue,
            LimitType = limitType,
            LimitSeconds = limitSeconds,
            Frequency = frequency,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        return await _repository.AddGoalAsync(goal, ct);
    }
}
