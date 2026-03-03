using Monity.Domain.Entities;

namespace Monity.App.Services;

public interface IGoalParsingService
{
    Goal? ParseGoal(string input);
}
