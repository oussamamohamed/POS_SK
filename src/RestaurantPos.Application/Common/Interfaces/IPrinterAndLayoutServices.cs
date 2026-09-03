using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public record PrinterRegistrationRequest(
    string Name,
    string IpAddress,
    int Port,
    int PaperWidthMm,
    bool OpenCashDrawerOnReceipt,
    List<string> AssignedStationIds
);

public record TestPrintResult(
    bool Success,
    string Message,
    TimeSpan ResponseTime
);

public interface IPrinterConfigurationService
{
    Task<IReadOnlyList<PrinterConfiguration>> GetAllPrintersAsync(CancellationToken ct = default);
    Task<PrinterConfiguration> RegisterPrinterAsync(PrinterRegistrationRequest request, CancellationToken ct = default);
    Task<PrinterConfiguration> UpdatePrinterAsync(Guid printerId, PrinterRegistrationRequest request, bool isActive, CancellationToken ct = default);
    Task<bool> DeletePrinterAsync(Guid printerId, CancellationToken ct = default);
    Task<TestPrintResult> SendTestPrintAsync(Guid printerId, CancellationToken ct = default);
}

public interface ITerminalLayoutService
{
    Task<TerminalLayoutProfile> GetActiveProfileAsync(string terminalId, CancellationToken ct = default);
    Task<TerminalLayoutProfile> SaveProfileAsync(TerminalLayoutProfile profile, CancellationToken ct = default);
    Task<IReadOnlyList<TerminalLayoutProfile>> GetAllProfilesAsync(CancellationToken ct = default);
}
