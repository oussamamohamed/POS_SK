namespace RestaurantPos.Client.Maui.Contracts;

public interface ILocalSyncWorker
{
    Task<int> ProcessOutboxQueueAsync(CancellationToken cancellationToken = default);
}
