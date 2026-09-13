using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public interface ITakeawayCounterService
{
    Task<string> GetNextPickupNumberAsync(string terminalId, CancellationToken cancellationToken = default);
    Task ResetDailySequencesAsync(CancellationToken cancellationToken = default);
}
