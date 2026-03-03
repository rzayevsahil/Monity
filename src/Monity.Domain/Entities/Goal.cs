namespace Monity.Domain.Entities;

public enum GoalTargetType
{
    Category,
    App
}

public enum GoalLimitType
{
    Max,
    Min
}

public enum GoalFrequency
{
    Daily,
    Weekly,
    Monthly
}

public sealed class Goal
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public GoalTargetType TargetType { get; set; }
    public string TargetValue { get; set; } = string.Empty;
    public GoalLimitType LimitType { get; set; }
    public int LimitSeconds { get; set; }
    public GoalFrequency Frequency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
