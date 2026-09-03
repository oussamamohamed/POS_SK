# Contract: Cross-Platform mDNS & Peripheral Network Discovery

**Feature**: `002-cross-platform-frontend`
**Protocols**: mDNS (Multicast DNS) / DNS-SD (RFC 6762 / 6763)

## 1. Cross-Platform Discovery Service Contract

```csharp
namespace RestaurantPos.Client.Maui.Contracts;

public interface ICrossPlatformDiscoveryService
{
    Task<IReadOnlyList<DiscoveredPosPeripheral>> ScanSubnetForPeripheralsAsync(
        TimeSpan timeout, 
        CancellationToken cancellationToken = default);

    Task<DiscoveredServer?> FindCentralServerAsync(
        TimeSpan timeout, 
        CancellationToken cancellationToken = default);
}

public record DiscoveredPosPeripheral(
    string DeviceName,
    string ServiceType, // "_printer._tcp"
    string IpAddress,
    int Port,
    string ModelHint,
    bool IsConfigured);

public record DiscoveredServer(
    string ServerName,
    string HostAddress,
    int Port,
    string ApiBaseUrl,
    string SignalRHubUrl);
```

---

## 2. Platform Network Multicast Initialization

- **Android**: MUST acquire `WifiManager.MulticastLock` during discovery scans to allow UDP broadcast reception on standard Android Wi-Fi chips.
- **iOS**: Uses Apple Bonjour APIs / standard UDP multicast with `NSLocalNetworkUsageDescription` consent.
- **Windows**: Uses standard Windows Sockets (WinSock / `UdpClient`) on local subnet broadcast.
