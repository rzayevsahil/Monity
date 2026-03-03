using Monity.Domain.Entities;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Monity.App.Services;

public class GoalParsingService : IGoalParsingService
{
    public Goal? ParseGoal(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var goal = new Goal { Title = input };
        var trCulture = new CultureInfo("tr-TR");
        var lowerInput = input.ToLower(trCulture);

        // 1. Frequency
        if (Regex.IsMatch(lowerInput, @"hafta(lık|da)|weekly"))
            goal.Frequency = GoalFrequency.Weekly;
        else
            goal.Frequency = GoalFrequency.Daily; // Default

        // 2. Limit Type
        if (Regex.IsMatch(lowerInput, @"en fazla|en çok|max|maksimum|fazla|geçmesin"))
            goal.LimitType = GoalLimitType.Max;
        else if (Regex.IsMatch(lowerInput, @"en az|min|minimum|az"))
            goal.LimitType = GoalLimitType.Min;
        else
            goal.LimitType = GoalLimitType.Max; // Default

        // 3. Amount (Time)
        // Match numbers and units
        var amountMatch = Regex.Match(lowerInput, @"(\d+[\.,]?\d*)\s*(saat|sa|st|dakika|dk|dk\.|dak|h|m)");
        if (amountMatch.Success)
        {
            var valueStr = amountMatch.Groups[1].Value.Replace(",", ".");
            if (double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                var unit = amountMatch.Groups[2].Value;
                if (unit.StartsWith("s") || unit.StartsWith("h"))
                    goal.LimitSeconds = (int)(value * 3600);
                else
                    goal.LimitSeconds = (int)(value * 60);
            }
        }
        else
        {
            // Just a number?
            var numberMatch = Regex.Match(lowerInput, @"(\d+)");
            if (numberMatch.Success)
            {
                if (double.TryParse(numberMatch.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                    goal.LimitSeconds = (int)(value * 3600);
            }
            else
            {
                return null; // Value missing
            }
        }

        // 4. Target (Category, App, or Domain)
        // Check for specific Turkish "social media" phrase 
        if (lowerInput.Contains("sosyal medya") || lowerInput.Contains("sosyal ağlar"))
        {
            goal.TargetType = GoalTargetType.Category;
            goal.TargetValue = "Sosyal";
            return goal;
        }

        // Check defined categories
        foreach (var cat in AppCategories.All)
        {
            if (string.IsNullOrEmpty(cat)) continue;
            if (lowerInput.Contains(cat.ToLower(trCulture)))
            {
                goal.TargetType = GoalTargetType.Category;
                goal.TargetValue = cat;
                return goal;
            }
        }

        // Heuristic for app/domain: remove identified parts
        var stopWords = new[] { 
            "günlük", "günde", "haftalık", "haftada", 
            "en fazla", "en çok", "en az", "max", "maksimum", "min", "minimum", 
            "saat", "sa", "st", "dakika", "dk", "dk.", "dak", "geçmesin", "limit", "hedef" 
        };
        
        var clean = lowerInput;
        foreach (var word in stopWords)
            clean = Regex.Replace(clean, $@"\b{word}\b", "", RegexOptions.IgnoreCase);
        
        clean = Regex.Replace(clean, @"\d+[\.,]?\d*", "").Trim();
        
        if (!string.IsNullOrWhiteSpace(clean))
        {
            goal.TargetType = GoalTargetType.App;
            goal.TargetValue = clean;
            return goal;
        }

        return goal;
    }
}
