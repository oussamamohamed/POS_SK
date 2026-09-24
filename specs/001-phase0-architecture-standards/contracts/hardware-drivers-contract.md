# Contract: Hardware Drivers & Zero-Configuration Network Discovery

**Feature**: `001-phase0-architecture-standards`
**Protocols**: Raw TCP Sockets (port 9100), mDNS/DNS-SD (Bonjour)

## 1. ESC/POS Thermal Printer & Cash Drawer Driver Interface

```csharp
public interface IPrinterService
{
    ValueTask<PrinterOperationResult> PrintReceiptAsync(
        string ipAddress, 
        int port, 
        byte[] rawEscPosPayload, 
        CancellationToken cancellationToken = default);

    ValueTask<PrinterOperationResult> KickCashDrawerAsync(
        string ipAddress, 
        int port, 
        CancellationToken cancellationToken = default);

    ValueTask<PrinterStatus> CheckStatusAsync(
        string ipAddress, 
        int port, 
        CancellationToken cancellationToken = default);
}
```

### Standard Byte Command Sequences
- **Initialize Printer**: `[0x1B, 0x40]` (`ESC @`)
- **Cut Paper (Partial/Full)**: `[0x1D, 0x56, 0x42, 0x00]` (`GS V 66 0`)
- **Kick Cash Drawer (Pin 2, 24V)**: `[0x1B, 0x70, 0x00, 0x19, 0xFA]` (`ESC p 0 25 250`)
- **Select Code Page (CP437/PC858/UTF-8)**: `[0x1B, 0x74, 0x13]`

---

## 2. mDNS / Bonjour Zero-Configuration Discovery Contract

```csharp
public interface INetworkDiscoveryService
{
    IAsyncEnumerable<DiscoveredService> DiscoverServicesAsync(
        string serviceType, 
        TimeSpan timeout, 
        CancellationToken cancellationToken = default);
}

public record DiscoveredService(
    string InstanceName, 
    string ServiceType, 
    string HostName, 
    string IpAddress, 
    int Port, 
    IReadOnlyDictionary<string, string> Properties);
```

### Advertised Service Types
- `_pos-server._tcp.local.` (Central ASP.NET Core Server)
- `_printer._tcp.local.` (Network ESC/POS Thermal Printers)
