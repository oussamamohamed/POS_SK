// Contract: IPrinterConfigurationService & ITerminalLayoutService
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
    List<PreparationStation> AssignedStations
);

public record TestPrintResult(
    bool Success,
    string Message,
    TimeSpan ResponseTime
);

/// <summary>
/// Service contract for managing thermal network printers and production routing rules.
/// </summary>
public interface IPrinterConfigurationService
{
    Task<IReadOnlyList<PrinterConfiguration>> GetAllPrintersAsync(CancellationToken ct = default);
    Task<PrinterConfiguration> RegisterPrinterAsync(PrinterRegistrationRequest request, CancellationToken ct = default);
    Task<PrinterConfiguration> UpdatePrinterAsync(Guid printerId, PrinterRegistrationRequest request, bool isActive, CancellationToken ct = default);
    Task<bool> DeletePrinterAsync(Guid printerId, CancellationToken ct = default);
    Task<TestPrintResult> SendTestPrintAsync(Guid printerId, CancellationToken ct = default);
}

/// <summary>
/// Service contract for managing terminal screen layout preferences, quick keys, and category sequences.
/// </summary>
public interface ITerminalLayoutService
{
    Task<TerminalLayoutProfile> GetActiveProfileAsync(string terminalId, CancellationToken ct = default);
    Task<TerminalLayoutProfile> SaveProfileAsync(TerminalLayoutProfile profile, CancellationToken ct = default);
    Task<IReadOnlyList<TerminalLayoutProfile>> GetAllProfilesAsync(CancellationToken ct = default);
}
