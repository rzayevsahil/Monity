using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Monity.App.Services;

public record InsightItem(string Message, string Icon, string Type);

public interface IInsightService
{
    Task<List<InsightItem>> GetInsightsAsync(DateTime date, CancellationToken ct = default);
}
