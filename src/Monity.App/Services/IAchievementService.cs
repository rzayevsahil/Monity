using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Monity.Domain.Entities;

namespace Monity.App.Services;

public interface IAchievementService
{
    Task<IReadOnlyList<AchievementStatus>> GetAchievementsAsync(CancellationToken ct = default);
    Task CalculateAndNotifyAsync(CancellationToken ct = default);
}

public record AchievementStatus(Achievement Achievement, UserAchievement Progress);
