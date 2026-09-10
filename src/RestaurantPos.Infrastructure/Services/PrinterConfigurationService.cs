using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class PrinterConfigurationService : IPrinterConfigurationService
{
    private readonly AppDbContext _dbContext;

    public PrinterConfigurationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var sw = Stopwatch.StartNew();

        try
        {
            byte[] testPayload = BuildTestPrintPayload(printer);

            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            await client.ConnectAsync(printer.IpAddress, printer.Port, cts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await stream.WriteAsync(testPayload, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            sw.Stop();
            return new TestPrintResult(true, $"Test d'impression réussi ({printer.Name} @ {printer.IpAddress}:{printer.Port})", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestPrintResult(false, $"Échec de connexion imprimante ({printer.IpAddress}:{printer.Port}): {ex.Message}", sw.Elapsed);
        }
    }

    private static byte[] BuildTestPrintPayload(PrinterConfiguration printer)
    {
        List<byte> bytes =
        [
            0x1B, 0x40, // ESC @ (Initialize printer)
            0x1B, 0x61, 0x01 // ESC a 1 (Center alignment)
        ];

        string text = $"\n=== TEST D'IMPRESSION POS ===\n{printer.Name}\nIP: {printer.IpAddress}:{printer.Port}\nLargeur: {printer.PaperWidthMm}mm\nDate: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n=============================\n\n\n";
        bytes.AddRange(Encoding.UTF8.GetBytes(text));

        if (printer.OpenCashDrawerOnReceipt)
        {
            bytes.AddRange([0x1B, 0x70, 0x00, 0x19, 0xFA]); // ESC p 0 25 250 (Drawer kick)
        }

        bytes.AddRange([0x1D, 0x56, 0x42, 0x00]); // GS V 66 0 (Cut paper)
        return [.. bytes];
    }
}
