using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Client.Maui.Contracts;

public record DiscoveredPeripheral(
    string DeviceName,
    string ServiceType,
    string IpAddress,
    int Port,
    string ModelHint);

public record DiscoveredMasterServer(
    string ServerName,
    string ServerUrl,
    string IpAddress,
    int Port,
    string Version,
    bool IsActive);

public interface ICrossPlatformDiscoveryService
{
    Task<IReadOnlyList<DiscoveredPeripheral>> DiscoverPeripheralsAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscoveredMasterServer>> DiscoverMasterServersAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
