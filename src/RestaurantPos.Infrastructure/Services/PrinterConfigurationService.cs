using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Infrastructure.Services;

public class PrinterConfigurationService : IPrinterConfigurationService
{
    private readonly AppDbContext _dbContext;
    private readonly IPrinterTransport _transport;
    private readonly IRestaurantSettingsService _settings;

    public PrinterConfigurationService(AppDbContext dbContext, IPrinterTransport transport, IRestaurantSettingsService settings)
    {
        _dbContext = dbContext;
        _transport = transport;
        _settings = settings;
    }

    public async Task<IReadOnlyList<PrinterConfiguration>> GetAllPrintersAsync(CancellationToken ct = default)
    {
        return await _dbContext.PrinterConfigurations
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PrinterConfiguration> RegisterPrinterAsync(PrinterRegistrationRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IpAddress);

        var printer = new PrinterConfiguration
        {
            Id = UuidV7.NewGuid(),
            Name = request.Name.Trim(),
            IpAddress = request.IpAddress.Trim(),
            Port = request.Port > 0 ? request.Port : 9100,
            PaperWidthMm = request.PaperWidthMm is 58 or 80 ? request.PaperWidthMm : 80,
            OpenCashDrawerOnReceipt = request.OpenCashDrawerOnReceipt,
            AssignedStationIds = request.AssignedStationIds ?? [],
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.PrinterConfigurations.Add(printer);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return printer;
    }

    public async Task<PrinterConfiguration> UpdatePrinterAsync(Guid printerId, PrinterRegistrationRequest request, bool isActive, CancellationToken ct = default)
    {
        var printer = await _dbContext.PrinterConfigurations.FindAsync([printerId], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Imprimante introuvable: {printerId}");

        printer.Name = request.Name.Trim();
        printer.IpAddress = request.IpAddress.Trim();
        printer.Port = request.Port > 0 ? request.Port : 9100;
        printer.PaperWidthMm = request.PaperWidthMm is 58 or 80 ? request.PaperWidthMm : 80;
        printer.OpenCashDrawerOnReceipt = request.OpenCashDrawerOnReceipt;
        printer.AssignedStationIds = request.AssignedStationIds ?? [];
        printer.IsActive = isActive;
        printer.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return printer;
    }

    public async Task<bool> DeletePrinterAsync(Guid printerId, CancellationToken ct = default)
    {
        var printer = await _dbContext.PrinterConfigurations.FindAsync([printerId], ct).ConfigureAwait(false);
        if (printer is null) return false;

        _dbContext.PrinterConfigurations.Remove(printer);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<TestPrintResult> SendTestPrintAsync(Guid printerId, CancellationToken ct = default)
    {
        var printer = await _dbContext.PrinterConfigurations.FindAsync([printerId], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Imprimante introuvable: {printerId}");

        var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
        var sw = Stopwatch.StartNew();

        try
        {
            var page = TicketDocumentBuilder.TestPage(printer, language, DateTimeOffset.UtcNow);
            await _transport.SendAsync(printer, page, printer.OpenCashDrawerOnReceipt, ct).ConfigureAwait(false);

            sw.Stop();
            return new TestPrintResult(true, Texts.T("messages.test_print_ok", ("name", printer.Name), ("ip", printer.IpAddress), ("port", printer.Port)), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestPrintResult(false, Texts.T("errors.test_print_failed", ("ip", printer.IpAddress), ("port", printer.Port), ("error", ex.Message)), sw.Elapsed);
        }
    }
}
