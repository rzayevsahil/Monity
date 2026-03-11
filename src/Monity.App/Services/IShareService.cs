using System.Threading;
using System.Threading.Tasks;

namespace Monity.App.Services;

public interface IShareService
{
    /// <summary>
    /// Creates a share card (image + caption) for the given period.
    /// </summary>
    Task<ShareResult> CreateShareCardAsync(ShareContext context, CancellationToken ct = default);
}
