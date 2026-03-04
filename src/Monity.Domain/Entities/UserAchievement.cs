using System;

namespace Monity.Domain.Entities;

public class UserAchievement
{
    public string AchievementKey { get; set; } = string.Empty;
    public int CurrentValue { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
