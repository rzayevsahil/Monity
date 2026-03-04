using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Monity.App.Helpers;
using Monity.Domain.Entities;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public sealed class AchievementService : IAchievementService
{
    private readonly IUsageRepository _repository;
    private readonly ITrayNotifier _trayNotifier;

    public AchievementService(IUsageRepository repository, ITrayNotifier trayNotifier)
    {
        _repository = repository;
        _trayNotifier = trayNotifier;
    }

    public async Task<IReadOnlyList<AchievementStatus>> GetAchievementsAsync(CancellationToken ct = default)
    {
        var definitions = await _repository.GetAchievementsAsync(ct);
        var progresses = await _repository.GetUserAchievementsAsync(ct);
        var progressMap = progresses.ToDictionary(p => p.AchievementKey);

        return definitions.Select(d => new AchievementStatus(
            d,
            progressMap.TryGetValue(d.Key, out var p) ? p : new UserAchievement
            {
                AchievementKey = d.Key,
                CurrentValue = 0,
                IsUnlocked = false,
                LastUpdatedAt = DateTime.MinValue
            }
        )).ToList();
    }

    public async Task CalculateAndNotifyAsync(CancellationToken ct = default)
    {
        var definitions = await _repository.GetAchievementsAsync(ct);
        var progresses = await _repository.GetUserAchievementsAsync(ct);
        var progressMap = progresses.ToDictionary(p => p.AchievementKey);

        foreach (var def in definitions)
        {
            var current = progressMap.TryGetValue(def.Key, out var p) ? p : new UserAchievement
            {
                AchievementKey = def.Key,
                CurrentValue = 0,
                IsUnlocked = false,
                LastUpdatedAt = DateTime.MinValue
            };

            if (current.IsUnlocked) continue;

            bool unlocked = false;
            int newValue = current.CurrentValue;

            switch (def.Key)
            {
                case "steady_hand":
                    newValue = await CalculateSteadyHandStreakAsync(ct);
                    unlocked = newValue >= def.GoalValue;
                    break;
                case "deep_focus":
                    newValue = (int)await _repository.GetDailyCategoryUsageSecondsAsync(DateTime.Today, "Geliştirme", ct) +
                               (int)await _repository.GetDailyCategoryUsageSecondsAsync(DateTime.Today, "Ofis", ct);
                    unlocked = newValue >= def.GoalValue;
                    break;
                case "early_bird":
                    newValue = await CalculateEarlyBirdStreakAsync(ct);
                    unlocked = newValue >= def.GoalValue;
                    break;
                case "night_owl":
                    newValue = await CalculateNightUsageSecondsAsync(ct);
                    unlocked = newValue >= def.GoalValue;
                    break;
                case "balanced_day":
                    var social = await _repository.GetDailyCategoryUsageSecondsAsync(DateTime.Today, "Sosyal", ct);
                    var ent = await _repository.GetDailyCategoryUsageSecondsAsync(DateTime.Today, "Eğlence", ct);
                    var total = await _repository.GetDailyUsageSecondsAsync(DateTime.Today, ct);
                    if (social + ent <= 3600 && total >= 14400) // Max 1h social, Min 4h total
                    {
                        newValue = 1;
                        unlocked = true;
                    }
                    break;
            }

            if (unlocked || newValue != current.CurrentValue)
            {
                current.CurrentValue = newValue;
                current.LastUpdatedAt = DateTime.Now;
                if (unlocked && !current.IsUnlocked)
                {
                    current.IsUnlocked = true;
                    current.UnlockedAt = DateTime.Now;
                    NotifyUnlock(def);
                }
                await _repository.UpsertUserAchievementAsync(current, ct);
            }
        }
    }

    private async Task<int> CalculateSteadyHandStreakAsync(CancellationToken ct)
    {
        int streak = 0;
        var limitsJson = await _repository.GetSettingAsync("daily_limits", ct) ?? "{}";
        var limits = JsonSerializer.Deserialize<Dictionary<string, long>>(limitsJson) ?? new();
        if (limits.Count == 0) return 0;

        for (int i = 0; i < 7; i++) // Check up to 7 days
        {
            var date = DateTime.Today.AddDays(-i);
            var usage = await _repository.GetDailyUsageAsync(date.ToString("yyyy-MM-dd"), true, null, null, ct);
            bool exceeded = false;
            foreach (var app in usage)
            {
                if (limits.TryGetValue(app.ProcessName, out var limit) && app.TotalSeconds > limit)
                {
                    exceeded = true;
                    break;
                }
            }
            if (!exceeded) streak++;
            else break;
        }
        return streak;
    }

    private async Task<int> CalculateEarlyBirdStreakAsync(CancellationToken ct)
    {
        int streak = 0;
        for (int i = 0; i < 7; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            var firstSession = await _repository.GetFirstSessionStartedAtAsync(date.ToString("yyyy-MM-dd"), ct);
            if (firstSession.HasValue && firstSession.Value.Hour < 8) streak++;
            else break;
        }
        return streak;
    }

    private async Task<int> CalculateNightUsageSecondsAsync(CancellationToken ct)
    {
        // This is a bit simplified, ideally we'd query sessions started after 21:00
        // For now let's use a repository method if available or iterate today's sessions
        // Actually, I don't have a direct "GetUsageAfterTimeAsync", so I might need one or just calculate from HourlyUsage
        var hourly = await _repository.GetHourlyUsageAsync(DateTime.Today.ToString("yyyy-MM-dd"), true, null, ct);
        return (int)hourly.Where(h => h.Hour >= 21).Sum(h => h.TotalSeconds);
    }

    private void NotifyUnlock(Achievement def)
    {
        var title = Strings.Get("Achievement_Unlocked_Title");
        var name = Strings.Get($"Achievement_{def.Key}_Title");
        _trayNotifier.ShowBalloonTip(title, name);
    }
}
