using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// In-memory fake for <see cref="IPrinterConfigurationService"/>.
/// Initially empty; <see cref="RegisterPrinterAsync"/> appends printers to the in-memory list.
/// <see cref="SendTestPrintAsync"/> always returns a success result.
/// </summary>
public sealed class FakePrinterConfigurationService : IPrinterConfigurationService
{
    private readonly List<PrinterConfiguration> _printers = [];

    public Task<IReadOnlyList<PrinterConfiguration>> GetAllPrintersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PrinterConfiguration>>(_printers.AsReadOnly());

    public Task<PrinterConfiguration> RegisterPrinterAsync(PrinterRegistrationRequest request, CancellationToken ct = default)
    {
        var printer = new PrinterConfiguration
        {
            Id = UuidV7.NewGuid(),
            Name = request.Name,
            IpAddress = request.IpAddress,
            Port = request.Port,
            PaperWidthMm = request.PaperWidthMm,
            OpenCashDrawerOnReceipt = request.OpenCashDrawerOnReceipt,
            AssignedStationIds = request.AssignedStationIds,
            IsActive = true
        };
        _printers.Add(printer);
        return Task.FromResult(printer);
    }

    public Task<PrinterConfiguration> UpdatePrinterAsync(Guid printerId, PrinterRegistrationRequest request, bool isActive, CancellationToken ct = default)
    {
        var p = _printers.First(x => x.Id == printerId);
        p.Name = request.Name;
        p.IpAddress = request.IpAddress;
        p.Port = request.Port;
        p.PaperWidthMm = request.PaperWidthMm;
        p.OpenCashDrawerOnReceipt = request.OpenCashDrawerOnReceipt;
        p.AssignedStationIds = request.AssignedStationIds;
        p.IsActive = isActive;
        return Task.FromResult(p);
    }

    public Task<bool> DeletePrinterAsync(Guid printerId, CancellationToken ct = default)
    {
        var p = _printers.FirstOrDefault(x => x.Id == printerId);
        if (p is null) return Task.FromResult(false);
        _printers.Remove(p);
        return Task.FromResult(true);
    }

    public Task<TestPrintResult> SendTestPrintAsync(Guid printerId, CancellationToken ct = default)
        => Task.FromResult(new TestPrintResult(
            Success: true,
            Message: "Test OK (Simulation)",
            ResponseTime: TimeSpan.FromMilliseconds(50)));
}
