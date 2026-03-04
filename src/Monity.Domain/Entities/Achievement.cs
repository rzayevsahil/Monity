using System;

namespace Monity.Domain.Entities;

public class Achievement
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int GoalValue { get; set; }
    public bool IsActive { get; set; }
}
